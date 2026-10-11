using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace TheMovies.Core.Network
{
    public sealed class SmbException : IOException
    {
        public uint Status { get; }

        public SmbException(uint status, string message) : base(message)
        {
            Status = status;
        }

        public static string Describe(uint status)
        {
            switch (status)
            {
                case Smb2Client.StatusLogonFailure: return "Anmeldung abgelehnt (Benutzer oder Passwort falsch).";
                case Smb2Client.StatusAccessDenied: return "Zugriff verweigert.";
                case Smb2Client.StatusBadNetworkName: return "Freigabe nicht gefunden.";
                case Smb2Client.StatusObjectNameNotFound:
                case Smb2Client.StatusObjectPathNotFound: return "Datei oder Ordner nicht gefunden.";
                case Smb2Client.StatusAccountDisabled: return "Konto ist deaktiviert.";
                case Smb2Client.StatusPasswordExpired: return "Passwort ist abgelaufen.";
                case Smb2Client.StatusNotSupported: return "Vom Server nicht unterstützt.";
                case Smb2Client.StatusLogonTypeNotGranted: return "Anmeldung von diesem Gerät nicht erlaubt.";
                default: return $"SMB-Fehler 0x{status:X8}.";
            }
        }
    }

    /// <summary>One entry of a directory or share listing.</summary>
    public sealed class SmbEntry
    {
        public string Name;
        public bool IsDirectory;
        public bool IsHidden;
        public long Size;
    }

    /// <summary>
    /// Minimal SMB 2/3 client for reading files from a NAS or Windows share: NTLMv2 login, message signing,
    /// share listing, directory listing and reading. Encryption (SMB 3 "encrypt data") and SMB 3.1.1 are not
    /// supported; servers fall back to 3.0.2 or older. Not thread-safe: one request at a time.
    /// </summary>
    public sealed class Smb2Client : IDisposable
    {
        public const int DefaultPort = 445;

        public const uint StatusSuccess = 0x00000000;
        public const uint StatusPending = 0x00000103;
        public const uint StatusBufferOverflow = 0x80000005;
        public const uint StatusNoMoreFiles = 0x80000006;
        public const uint StatusEndOfFile = 0xC0000011;
        public const uint StatusMoreProcessingRequired = 0xC0000016;
        public const uint StatusAccessDenied = 0xC0000022;
        public const uint StatusObjectNameNotFound = 0xC0000034;
        public const uint StatusObjectPathNotFound = 0xC000003A;
        public const uint StatusLogonFailure = 0xC000006D;
        public const uint StatusAccountDisabled = 0xC0000072;
        public const uint StatusPasswordExpired = 0xC0000071;
        public const uint StatusNotSupported = 0xC00000BB;
        public const uint StatusBadNetworkName = 0xC00000CC;
        public const uint StatusLogonTypeNotGranted = 0xC000015B;

        const ushort CmdNegotiate = 0x00;
        const ushort CmdSessionSetup = 0x01;
        const ushort CmdLogoff = 0x02;
        const ushort CmdTreeConnect = 0x03;
        const ushort CmdCreate = 0x05;
        const ushort CmdClose = 0x06;
        const ushort CmdRead = 0x08;
        const ushort CmdIoctl = 0x0B;
        const ushort CmdQueryDirectory = 0x0E;

        const uint FlagResponse = 0x1;
        const uint FlagAsync = 0x2;
        const uint FlagSigned = 0x8;

        public const ushort Dialect202 = 0x0202;
        public const ushort Dialect210 = 0x0210;
        public const ushort Dialect300 = 0x0300;
        public const ushort Dialect302 = 0x0302;

        const int HeaderSize = 64;
        const int MaxCreditsRequested = 256;

        readonly string m_Host;
        readonly int m_Port;
        readonly int m_TimeoutMs;
        TcpClient m_Tcp;
        Stream m_Stream;
        ulong m_MessageId;
        ulong m_SessionId;
        int m_Credits;
        bool m_Sign;
        byte[] m_SigningKey;
        bool m_SupportsMultiCredit;
        readonly byte[] m_ClientGuid = Guid.NewGuid().ToByteArray();

        public ushort Dialect { get; private set; }
        public int MaxReadSize { get; private set; } = 65536;
        public bool ServerRequiresSigning { get; private set; }
        public bool IsGuest { get; private set; }
        public bool IsConnected => m_Tcp != null && m_Tcp.Connected;
        public string Host => m_Host;

        public Smb2Client(string host, int port = DefaultPort, int timeoutMs = 15000)
        {
            m_Host = host;
            m_Port = port <= 0 ? DefaultPort : port;
            m_TimeoutMs = timeoutMs;
        }

        public void Dispose()
        {
            try
            {
                if (m_SessionId != 0 && IsConnected)
                    Send(CmdLogoff, new byte[] { 4, 0, 0, 0 }, 0);
            }
            catch (Exception)
            {
                // Best effort
            }
            m_Stream?.Dispose();
            m_Tcp?.Dispose();
            m_Stream = null;
            m_Tcp = null;
        }

        // ---------------- Connection ----------------

        /// <summary>Opens the TCP connection and negotiates the dialect.</summary>
        public void Connect()
        {
            var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = m_TimeoutMs, SendTimeout = m_TimeoutMs };
            var connect = tcp.ConnectAsync(m_Host, m_Port);
            bool finished;
            try
            {
                finished = connect.Wait(m_TimeoutMs);
            }
            catch (AggregateException)
            {
                finished = true; // faulted, handled below
            }
            if (!finished)
            {
                tcp.Dispose();
                throw new IOException($"Keine Verbindung zu {m_Host} (Zeitüberschreitung).");
            }
            if (connect.IsFaulted)
            {
                tcp.Dispose();
                var inner = connect.Exception?.GetBaseException();
                string reason = inner is SocketException se && se.SocketErrorCode == SocketError.ConnectionRefused
                    ? "Port " + m_Port + " ist zu – SMB am Gerät aktiviert?"
                    : inner?.Message;
                throw new IOException($"Keine Verbindung zu {m_Host}: {reason}", inner);
            }
            m_Tcp = tcp;
            m_Stream = tcp.GetStream();
            m_MessageId = 0;
            m_Credits = 1;
            Negotiate();
        }

        void Negotiate()
        {
            ushort[] dialects = { Dialect202, Dialect210, Dialect300, Dialect302 };
            var w = new Writer();
            w.U16(36);
            w.U16((ushort)dialects.Length);
            w.U16(1); // signing enabled
            w.U16(0);
            w.U32(0); // capabilities
            w.Bytes(m_ClientGuid);
            w.U64(0);
            foreach (ushort d in dialects) w.U16(d);

            var r = Transact(CmdNegotiate, w.ToArray(), 0);
            r.ThrowIfError("Verhandlung");
            byte[] b = r.Body;
            ushort securityMode = U16(b, 2);
            Dialect = U16(b, 4);
            if (Dialect == 0x02FF || Dialect < Dialect202 || Dialect > Dialect302)
                throw new IOException($"Server bietet keinen unterstützten SMB-Dialekt an (0x{Dialect:X4}).");
            ServerRequiresSigning = (securityMode & 0x2) != 0;
            MaxReadSize = (int)Math.Min(8u * 1024 * 1024, Math.Max(65536u, U32(b, 32)));
            m_SupportsMultiCredit = Dialect >= Dialect210;
            if (!m_SupportsMultiCredit) MaxReadSize = 65536;
        }

        /// <summary>Logs in with NTLMv2. An empty user name logs in anonymously.</summary>
        public void Login(string user, string password, string domain = "")
        {
            ParseUser(ref user, ref domain);
            var ntlm = new NtlmAuthenticator(user, password, domain);

            var first = Transact(CmdSessionSetup, SessionSetupBody(ntlm.CreateNegotiate()), 0);
            if (first.Status != StatusMoreProcessingRequired)
                first.ThrowIfError("Anmeldung");
            m_SessionId = first.SessionId;
            byte[] challenge = SecurityBuffer(first.Body, U16(first.Body, 4), U16(first.Body, 6));

            byte[] authenticate = ntlm.CreateAuthenticate(challenge);
            var second = Transact(CmdSessionSetup, SessionSetupBody(authenticate), 0);
            second.ThrowIfError("Anmeldung");

            ushort sessionFlags = U16(second.Body, 2);
            IsGuest = (sessionFlags & 0x3) != 0;
            if ((sessionFlags & 0x4) != 0)
                throw new SmbException(StatusNotSupported, "Die Freigabe verlangt SMB-Verschlüsselung; bitte am NAS auf \"optional\" stellen.");

            if (!IsGuest && !ntlm.IsAnonymous && ServerRequiresSigning)
            {
                byte[] sessionKey = new byte[16];
                Array.Copy(ntlm.SessionKey, sessionKey, Math.Min(16, ntlm.SessionKey.Length));
                m_SigningKey = Dialect >= Dialect300 ? Smb3Kdf(sessionKey, "SMB2AESCMAC\0", "SmbSign\0") : sessionKey;
                m_Sign = true;
            }
        }

        /// <summary>Accepts "DOMAIN\user" and "user@domain".</summary>
        static void ParseUser(ref string user, ref string domain)
        {
            user = user ?? string.Empty;
            int slash = user.IndexOf('\\');
            if (slash > 0)
            {
                domain = user.Substring(0, slash);
                user = user.Substring(slash + 1);
            }
        }

        byte[] SessionSetupBody(byte[] token)
        {
            var w = new Writer();
            w.U16(25);
            w.U8(0); // flags
            w.U8(1); // signing enabled
            w.U32(0); // capabilities
            w.U32(0); // channel
            w.U16(HeaderSize + 24);
            w.U16((ushort)token.Length);
            w.U64(0); // previous session
            w.Bytes(token);
            return w.ToArray();
        }

        /// <returns>The tree id of the share.</returns>
        public uint TreeConnect(string share)
        {
            byte[] path = Encoding.Unicode.GetBytes($@"\\{m_Host}\{share}");
            var w = new Writer();
            w.U16(9);
            w.U16(0);
            w.U16(HeaderSize + 8);
            w.U16((ushort)path.Length);
            w.Bytes(path);
            var r = Transact(CmdTreeConnect, w.ToArray(), 0);
            r.ThrowIfError($"Freigabe \"{share}\"");
            uint shareFlags = U32(r.Body, 4);
            if ((shareFlags & 0x8000) != 0)
                throw new SmbException(StatusNotSupported, $"Die Freigabe \"{share}\" verlangt SMB-Verschlüsselung; bitte am NAS auf \"optional\" stellen.");
            return r.TreeId;
        }

        // ---------------- Files ----------------

        /// <summary>Opens a file (or a directory) relative to the share root. Returns the 16 byte file id.</summary>
        public byte[] Open(uint tree, string path, bool directory, out long size)
        {
            byte[] name = Encoding.Unicode.GetBytes(NormalizePath(path));
            var w = new Writer();
            w.U16(57);
            w.U8(0); // security flags
            w.U8(0); // no oplock
            w.U32(2); // impersonation
            w.U64(0);
            w.U64(0);
            w.U32(directory ? 0x00100081u : 0x00120089u); // list dir / read data + attributes + sync
            w.U32(0); // attributes
            w.U32(7); // share read/write/delete
            w.U32(1); // FILE_OPEN
            w.U32(directory ? 0x00000001u : 0x00000040u); // directory / non-directory
            w.U16(HeaderSize + 56);
            w.U16((ushort)name.Length);
            w.U32(0);
            w.U32(0);
            w.Bytes(name.Length > 0 ? name : new byte[1]);

            var r = Transact(CmdCreate, w.ToArray(), tree);
            r.ThrowIfError(string.IsNullOrEmpty(path) ? "Freigabe öffnen" : $"\"{path}\"");
            size = (long)U64(r.Body, 48);
            var fileId = new byte[16];
            Array.Copy(r.Body, 64, fileId, 0, 16);
            return fileId;
        }

        public void Close(uint tree, byte[] fileId)
        {
            var w = new Writer();
            w.U16(24);
            w.U16(0);
            w.U32(0);
            w.Bytes(fileId);
            Transact(CmdClose, w.ToArray(), tree);
        }

        /// <summary>Reads up to <paramref name="count"/> bytes (limited by the server's max read size). Returns 0 at the end of the file.</summary>
        public int Read(uint tree, byte[] fileId, long offset, byte[] buffer, int index, int count)
        {
            count = Math.Min(count, MaxReadSize);
            ushort charge = 1;
            if (m_SupportsMultiCredit)
            {
                charge = (ushort)Math.Max(1, (count + 65535) / 65536);
                if (charge > m_Credits && m_Credits > 0)
                {
                    charge = (ushort)m_Credits;
                    count = Math.Min(count, charge * 65536);
                }
            }

            var w = new Writer();
            w.U16(49);
            w.U8(0x50); // padding: data right after the response header
            w.U8(0);
            w.U32((uint)count);
            w.U64((ulong)offset);
            w.Bytes(fileId);
            w.U32(0); // minimum count
            w.U32(0); // channel
            w.U32(0); // remaining
            w.U16(0);
            w.U16(0);
            w.U8(0);

            var r = Transact(CmdRead, w.ToArray(), tree, charge);
            if (r.Status == StatusEndOfFile) return 0;
            r.ThrowIfError("Lesen");
            int dataOffset = r.Body[2] - HeaderSize;
            int length = (int)U32(r.Body, 4);
            if (length > count || dataOffset < 0 || dataOffset + length > r.Body.Length)
                throw new IOException("Ungültige Leseantwort vom Server.");
            Buffer.BlockCopy(r.Body, dataOffset, buffer, index, length);
            return length;
        }

        /// <summary>Lists a directory relative to the share root ("" = root). "." and ".." are skipped.</summary>
        public List<SmbEntry> ListDirectory(uint tree, string path)
        {
            byte[] dir = Open(tree, path, true, out _);
            var result = new List<SmbEntry>();
            try
            {
                bool first = true;
                while (true)
                {
                    byte[] pattern = Encoding.Unicode.GetBytes("*");
                    var w = new Writer();
                    w.U16(33);
                    w.U8(0x01); // FileDirectoryInformation
                    w.U8(first ? (byte)0x01 : (byte)0x00); // restart scans
                    w.U32(0);
                    w.Bytes(dir);
                    w.U16(HeaderSize + 32);
                    w.U16((ushort)pattern.Length);
                    w.U32(65536);
                    w.Bytes(pattern);
                    first = false;

                    var r = Transact(CmdQueryDirectory, w.ToArray(), tree);
                    if (r.Status == StatusNoMoreFiles) break;
                    r.ThrowIfError("Ordner lesen");
                    int offset = U16(r.Body, 2) - HeaderSize;
                    int length = (int)U32(r.Body, 4);
                    ParseDirectory(r.Body, offset, length, result);
                    if (result.Count > 20000) break;
                }
            }
            finally
            {
                Close(tree, dir);
            }
            return result;
        }

        static void ParseDirectory(byte[] data, int start, int length, List<SmbEntry> result)
        {
            int end = Math.Min(data.Length, start + length);
            int pos = start;
            while (pos >= 0 && pos + 64 <= end)
            {
                uint next = U32(data, pos);
                long eof = (long)U64(data, pos + 40);
                uint attributes = U32(data, pos + 56);
                int nameLength = (int)U32(data, pos + 60);
                if (pos + 64 + nameLength > end) break;
                string name = Encoding.Unicode.GetString(data, pos + 64, nameLength);
                if (name != "." && name != "..")
                {
                    result.Add(new SmbEntry
                    {
                        Name = name,
                        IsDirectory = (attributes & 0x10) != 0,
                        IsHidden = (attributes & 0x2) != 0 || name.StartsWith(".", StringComparison.Ordinal),
                        Size = eof,
                    });
                }
                if (next == 0) break;
                pos += (int)next;
            }
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('/', '\\').Trim('\\');
        }

        // ---------------- Share listing (DCE/RPC srvsvc NetrShareEnum over IPC$) ----------------

        /// <summary>Lists the disk shares of the server (hidden "$" shares are skipped).</summary>
        public List<string> ListShares()
        {
            uint tree = TreeConnect("IPC$");
            var w = new Writer();
            byte[] pipe;
            {
                byte[] name = Encoding.Unicode.GetBytes("srvsvc");
                w.U16(57);
                w.U8(0);
                w.U8(0);
                w.U32(2);
                w.U64(0);
                w.U64(0);
                w.U32(0x0012019F); // read/write
                w.U32(0);
                w.U32(7);
                w.U32(1);
                w.U32(0);
                w.U16(HeaderSize + 56);
                w.U16((ushort)name.Length);
                w.U32(0);
                w.U32(0);
                w.Bytes(name);
                var r = Transact(CmdCreate, w.ToArray(), tree);
                r.ThrowIfError("Freigabeliste");
                pipe = new byte[16];
                Array.Copy(r.Body, 64, pipe, 0, 16);
            }

            try
            {
                byte[] bindAck = PipeTransceive(tree, pipe, Rpc.Bind(1));
                if (bindAck.Length < 24 || bindAck[2] != 12)
                    throw new IOException("Server lehnt die Freigabeliste ab.");

                byte[] response = PipeTransceive(tree, pipe, Rpc.ShareEnumRequest(2, m_Host));
                var stub = new MemoryStream();
                int frag = 0;
                while (true)
                {
                    if (response.Length < 24 || response[2] != 2)
                    {
                        if (response.Length >= 28 && response[2] == 3)
                            throw new SmbException(StatusAccessDenied, $"Freigabeliste abgelehnt (Fehler 0x{U32(response, 24):X8}).");
                        throw new IOException("Ungültige Antwort auf die Freigabeliste.");
                    }
                    int fragLength = U16(response, 8);
                    stub.Write(response, 24, Math.Min(response.Length, fragLength) - 24);
                    if ((response[3] & 0x02) != 0 || ++frag > 64) break; // last fragment
                    response = ReadPipe(tree, pipe);
                }
                return Rpc.ParseShareEnum(stub.ToArray());
            }
            finally
            {
                Close(tree, pipe);
            }
        }

        byte[] PipeTransceive(uint tree, byte[] pipe, byte[] input)
        {
            var w = new Writer();
            w.U16(57);
            w.U16(0);
            w.U32(0x0011C017); // FSCTL_PIPE_TRANSCEIVE
            w.Bytes(pipe);
            w.U32(HeaderSize + 56);
            w.U32((uint)input.Length);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.U32(65536);
            w.U32(1); // is FSCTL
            w.U32(0);
            w.Bytes(input);
            var r = Transact(CmdIoctl, w.ToArray(), tree);
            if (r.Status != StatusBufferOverflow)
                r.ThrowIfError("Freigabeliste");
            int offset = (int)U32(r.Body, 32) - HeaderSize;
            int count = (int)U32(r.Body, 36);
            if (offset < 0 || offset + count > r.Body.Length) throw new IOException("Ungültige Pipe-Antwort.");
            var output = new byte[count];
            Array.Copy(r.Body, offset, output, 0, count);
            return output;
        }

        byte[] ReadPipe(uint tree, byte[] pipe)
        {
            var buffer = new byte[65536];
            int n = Read(tree, pipe, 0, buffer, 0, buffer.Length);
            var result = new byte[n];
            Array.Copy(buffer, result, n);
            return result;
        }

        // ---------------- Transport ----------------

        sealed class Response
        {
            public uint Status;
            public ulong SessionId;
            public uint TreeId;
            public byte[] Body;

            public void ThrowIfError(string what)
            {
                if (Status != StatusSuccess)
                    throw new SmbException(Status, $"{what}: {SmbException.Describe(Status)}");
            }
        }

        Response Transact(ushort command, byte[] body, uint treeId, ushort creditCharge = 1)
        {
            ulong id = Send(command, body, treeId, creditCharge);
            while (true)
            {
                byte[] message = ReceiveMessage();
                if (message.Length < HeaderSize || message[0] != 0xFE || message[1] != 'S' || message[2] != 'M' || message[3] != 'B')
                {
                    if (message.Length >= 4 && message[0] == 0xFD)
                        throw new SmbException(StatusNotSupported, "Der Server verschlüsselt die Verbindung (nicht unterstützt).");
                    throw new IOException("Ungültige SMB-Antwort.");
                }
                uint status = U32(message, 8);
                uint flags = U32(message, 16);
                ulong messageId = U64(message, 24);
                m_Credits += U16(message, 14);
                if (messageId != id) continue; // oplock break or stray message
                if (status == StatusPending && (flags & FlagAsync) != 0) continue; // interim response, the real one follows

                var payload = new byte[message.Length - HeaderSize];
                Buffer.BlockCopy(message, HeaderSize, payload, 0, payload.Length);
                return new Response
                {
                    Status = status,
                    SessionId = U64(message, 40),
                    TreeId = (flags & FlagAsync) != 0 ? 0 : U32(message, 36),
                    Body = payload,
                };
            }
        }

        ulong Send(ushort command, byte[] body, uint treeId, ushort creditCharge = 1)
        {
            if (m_Stream == null) throw new IOException("Nicht verbunden.");
            ulong id = m_MessageId;
            ushort charge = m_SupportsMultiCredit ? Math.Max((ushort)1, creditCharge) : (ushort)0;
            m_MessageId += Math.Max(1u, charge);
            m_Credits -= Math.Max(1, (int)charge);
            ushort request = (ushort)Math.Max(1, Math.Min(MaxCreditsRequested, MaxCreditsRequested - m_Credits));

            var msg = new byte[HeaderSize + body.Length];
            msg[0] = 0xFE; msg[1] = (byte)'S'; msg[2] = (byte)'M'; msg[3] = (byte)'B';
            Put16(msg, 4, HeaderSize);
            Put16(msg, 6, charge);
            Put16(msg, 12, command);
            Put16(msg, 14, request);
            bool sign = m_Sign && command != CmdNegotiate && command != CmdSessionSetup;
            Put32(msg, 16, sign ? FlagSigned : 0);
            Put64(msg, 24, id);
            Put32(msg, 32, 0xFEFF); // process id
            Put32(msg, 36, treeId);
            Put64(msg, 40, m_SessionId);
            Buffer.BlockCopy(body, 0, msg, HeaderSize, body.Length);

            if (sign)
            {
                byte[] signature = Dialect >= Dialect300 ? AesCmac(m_SigningKey, msg) : HmacSha256(m_SigningKey, msg);
                Buffer.BlockCopy(signature, 0, msg, 48, 16);
            }

            var frame = new byte[4 + msg.Length];
            frame[1] = (byte)(msg.Length >> 16);
            frame[2] = (byte)(msg.Length >> 8);
            frame[3] = (byte)msg.Length;
            Buffer.BlockCopy(msg, 0, frame, 4, msg.Length);
            m_Stream.Write(frame, 0, frame.Length);
            return id;
        }

        byte[] ReceiveMessage()
        {
            var head = new byte[4];
            ReadExactly(head, 4);
            int length = (head[1] << 16) | (head[2] << 8) | head[3];
            if (length <= 0 || length > 16 * 1024 * 1024) throw new IOException("Ungültige SMB-Nachrichtenlänge.");
            var message = new byte[length];
            ReadExactly(message, length);
            return message;
        }

        void ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = m_Stream.Read(buffer, read, count - read);
                if (n <= 0) throw new IOException("Verbindung zum Server getrennt.");
                read += n;
            }
        }

        static byte[] SecurityBuffer(byte[] body, int offset, int length)
        {
            offset -= HeaderSize;
            if (offset < 0 || offset + length > body.Length) throw new IOException("Ungültige Anmeldeantwort.");
            var r = new byte[length];
            Array.Copy(body, offset, r, 0, length);
            return r;
        }

        // ---------------- Crypto ----------------

        static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using (var h = new HMACSHA256(key))
            {
                var full = h.ComputeHash(data);
                var r = new byte[16];
                Array.Copy(full, r, 16);
                return r;
            }
        }

        /// <summary>SP800-108 counter mode KDF with HMAC-SHA256, 128 bit output (MS-SMB2 3.1.4.2).</summary>
        public static byte[] Smb3Kdf(byte[] key, string label, string context)
        {
            var w = new MemoryStream();
            w.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
            byte[] l = Encoding.ASCII.GetBytes(label);
            w.Write(l, 0, l.Length);
            w.WriteByte(0);
            byte[] c = Encoding.ASCII.GetBytes(context);
            w.Write(c, 0, c.Length);
            w.Write(new byte[] { 0, 0, 0, 128 }, 0, 4);
            using (var h = new HMACSHA256(key))
            {
                var full = h.ComputeHash(w.ToArray());
                var r = new byte[16];
                Array.Copy(full, r, 16);
                return r;
            }
        }

        /// <summary>AES-128-CMAC (RFC 4493).</summary>
        public static byte[] AesCmac(byte[] key, byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (var enc = aes.CreateEncryptor())
                {
                    var zero = new byte[16];
                    var l = new byte[16];
                    enc.TransformBlock(zero, 0, 16, l, 0);
                    byte[] k1 = ShiftLeft(l);
                    byte[] k2 = ShiftLeft(k1);

                    int n = Math.Max(1, (data.Length + 15) / 16);
                    bool complete = data.Length > 0 && data.Length % 16 == 0;
                    var last = new byte[16];
                    int lastStart = (n - 1) * 16;
                    for (int i = 0; i < 16; i++)
                    {
                        int idx = lastStart + i;
                        byte b = idx < data.Length ? data[idx] : (idx == data.Length ? (byte)0x80 : (byte)0);
                        last[i] = (byte)(b ^ (complete ? k1[i] : k2[i]));
                    }

                    var x = new byte[16];
                    var y = new byte[16];
                    for (int block = 0; block < n - 1; block++)
                    {
                        for (int i = 0; i < 16; i++) y[i] = (byte)(x[i] ^ data[block * 16 + i]);
                        enc.TransformBlock(y, 0, 16, x, 0);
                    }
                    for (int i = 0; i < 16; i++) y[i] = (byte)(x[i] ^ last[i]);
                    var t = new byte[16];
                    enc.TransformBlock(y, 0, 16, t, 0);
                    return t;
                }
            }
        }

        static byte[] ShiftLeft(byte[] b)
        {
            var r = new byte[16];
            for (int i = 0; i < 15; i++)
                r[i] = (byte)((b[i] << 1) | (b[i + 1] >> 7));
            r[15] = (byte)(b[15] << 1);
            if ((b[0] & 0x80) != 0) r[15] ^= 0x87;
            return r;
        }

        // ---------------- Little endian helpers ----------------

        internal static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        internal static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        internal static ulong U64(byte[] b, int o) => U32(b, o) | ((ulong)U32(b, o + 4) << 32);
        static void Put16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        static void Put32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        static void Put64(byte[] b, int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

        internal sealed class Writer
        {
            readonly MemoryStream m_Stream = new MemoryStream();
            public int Length => (int)m_Stream.Length;
            public void U8(byte v) => m_Stream.WriteByte(v);
            public void U16(int v) { m_Stream.WriteByte((byte)v); m_Stream.WriteByte((byte)(v >> 8)); }
            public void U32(uint v) { for (int i = 0; i < 4; i++) m_Stream.WriteByte((byte)(v >> (8 * i))); }
            public void U64(ulong v) { for (int i = 0; i < 8; i++) m_Stream.WriteByte((byte)(v >> (8 * i))); }
            public void Bytes(byte[] b) => m_Stream.Write(b, 0, b.Length);
            public void Align4() { while (m_Stream.Length % 4 != 0) m_Stream.WriteByte(0); }
            public byte[] ToArray() => m_Stream.ToArray();
            public void Patch16(int offset, int v)
            {
                long p = m_Stream.Position;
                m_Stream.Position = offset;
                U16(v);
                m_Stream.Position = p;
            }
        }

        /// <summary>The few DCE/RPC pieces needed for NetrShareEnum (MS-SRVS 3.1.4.8).</summary>
        static class Rpc
        {
            static readonly byte[] k_SrvsvcUuid = new Guid("4b324fc8-1670-01d3-1278-5a47bf6ee188").ToByteArray();
            static readonly byte[] k_NdrUuid = new Guid("8a885d04-1ceb-11c9-9fe8-08002b104860").ToByteArray();

            static void Header(Writer w, byte type, uint callId)
            {
                w.U8(5); w.U8(0); w.U8(type); w.U8(3); // v5.0, first+last fragment
                w.U32(0x10); // little endian, ASCII, IEEE
                w.U16(0); // frag length, patched later
                w.U16(0); // auth length
                w.U32(callId);
            }

            public static byte[] Bind(uint callId)
            {
                var w = new Writer();
                Header(w, 11, callId);
                w.U16(4280); w.U16(4280); w.U32(0);
                w.U8(1); w.U8(0); w.U16(0); // one context
                w.U16(0); w.U8(1); w.U8(0); // context 0, one transfer syntax
                w.Bytes(k_SrvsvcUuid); w.U16(3); w.U16(0);
                w.Bytes(k_NdrUuid); w.U32(2);
                w.Patch16(8, w.Length);
                return w.ToArray();
            }

            public static byte[] ShareEnumRequest(uint callId, string server)
            {
                var stub = new Writer();
                // ServerName: unique pointer to a conformant varying string
                string name = @"\\" + server + "\0";
                stub.U32(0x00020000);
                stub.U32((uint)name.Length); stub.U32(0); stub.U32((uint)name.Length);
                stub.Bytes(Encoding.Unicode.GetBytes(name));
                stub.Align4();
                // InfoStruct: level 1, union switch 1, pointer to an empty container
                stub.U32(1); stub.U32(1); stub.U32(0x00020004);
                stub.U32(0); stub.U32(0);
                stub.U32(0xFFFFFFFF); // preferred maximum length
                stub.U32(0x00020008); stub.U32(0); // resume handle
                byte[] s = stub.ToArray();

                var w = new Writer();
                Header(w, 0, callId);
                w.U32((uint)s.Length); // alloc hint
                w.U16(0); // context
                w.U16(15); // opnum NetrShareEnum
                w.Bytes(s);
                w.Patch16(8, w.Length);
                return w.ToArray();
            }

            public static List<string> ParseShareEnum(byte[] s)
            {
                var result = new List<string>();
                int p = 0;
                uint U() { uint v = U32(s, p); p += 4; return v; }
                U(); // level
                U(); // switch
                if (U() == 0) return result; // container pointer
                uint count = U();
                if (U() == 0 || count == 0) return result; // array pointer
                uint max = U();
                count = Math.Min(count, Math.Min(max, 4096u));
                var names = new uint[count];
                var types = new uint[count];
                var remarks = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    names[i] = U();
                    types[i] = U();
                    remarks[i] = U();
                }
                string ReadString()
                {
                    U(); U(); // max count, offset
                    int actual = (int)U();
                    string v = Encoding.Unicode.GetString(s, p, actual * 2).TrimEnd('\0');
                    p += actual * 2;
                    p = (p + 3) & ~3;
                    return v;
                }
                for (int i = 0; i < count; i++)
                {
                    string shareName = names[i] != 0 ? ReadString() : string.Empty;
                    if (remarks[i] != 0) ReadString();
                    bool disk = (types[i] & 0x0FFFFFFF) == 0;
                    bool special = (types[i] & 0x80000000) != 0;
                    if (disk && !special && shareName.Length > 0 && !shareName.EndsWith("$", StringComparison.Ordinal))
                        result.Add(shareName);
                }
                result.Sort(StringComparer.OrdinalIgnoreCase);
                return result;
            }
        }
    }
}
