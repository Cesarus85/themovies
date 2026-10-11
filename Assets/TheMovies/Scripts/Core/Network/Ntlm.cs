using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TheMovies.Core.Network
{
    /// <summary>
    /// NTLMv2 authentication (as used by SMB file shares): builds the NEGOTIATE and AUTHENTICATE messages and
    /// derives the session key. MD4 and RC4 are implemented here because .NET Standard does not ship them.
    /// </summary>
    public sealed class NtlmAuthenticator
    {
        const uint NegotiateUnicode = 0x00000001;
        const uint RequestTarget = 0x00000004;
        const uint NegotiateSign = 0x00000010;
        const uint NegotiateNtlm = 0x00000200;
        const uint NegotiateAlwaysSign = 0x00008000;
        const uint NegotiateExtendedSessionSecurity = 0x00080000;
        const uint NegotiateTargetInfo = 0x00800000;
        const uint NegotiateVersion = 0x02000000;
        const uint Negotiate128 = 0x20000000;
        const uint NegotiateKeyExchange = 0x40000000;
        const uint Negotiate56 = 0x80000000;

        const ushort AvEol = 0;
        const ushort AvFlags = 6;
        const ushort AvTimestamp = 7;

        static readonly byte[] k_Signature = { (byte)'N', (byte)'T', (byte)'L', (byte)'M', (byte)'S', (byte)'S', (byte)'P', 0 };

        readonly string m_User;
        readonly string m_Password;
        readonly string m_Domain;
        readonly Func<byte[]> m_Random;
        byte[] m_Negotiate;

        /// <summary>Key for signing SMB messages after authentication.</summary>
        public byte[] SessionKey { get; private set; }

        public bool IsAnonymous => string.IsNullOrEmpty(m_User);

        /// <param name="random">Source of 8/16 byte random values (injectable for tests).</param>
        public NtlmAuthenticator(string user, string password, string domain = "", Func<int, byte[]> random = null)
        {
            m_User = user ?? string.Empty;
            m_Password = password ?? string.Empty;
            m_Domain = domain ?? string.Empty;
            var rng = random ?? RandomBytes;
            m_Random = () => rng(16);
        }

        static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        public byte[] CreateNegotiate()
        {
            uint flags = NegotiateUnicode | RequestTarget | NegotiateSign | NegotiateNtlm | NegotiateAlwaysSign |
                         NegotiateExtendedSessionSecurity | NegotiateTargetInfo | NegotiateVersion | Negotiate128 |
                         NegotiateKeyExchange | Negotiate56;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(k_Signature);
            w.Write(1u);
            w.Write(flags);
            WriteField(w, 0, 0); // domain
            WriteField(w, 0, 0); // workstation
            WriteVersion(w);
            m_Negotiate = ms.ToArray();
            return m_Negotiate;
        }

        public byte[] CreateAuthenticate(byte[] challengeMessage)
        {
            if (challengeMessage == null || challengeMessage.Length < 48 || !StartsWithSignature(challengeMessage) ||
                BitConverter.ToUInt32(challengeMessage, 8) != 2)
                throw new InvalidDataException("Ungültige NTLM-Challenge vom Server.");

            uint serverFlags = BitConverter.ToUInt32(challengeMessage, 20);
            var serverChallenge = new byte[8];
            Array.Copy(challengeMessage, 24, serverChallenge, 0, 8);
            byte[] targetInfo = ReadField(challengeMessage, 40);

            uint flags = serverFlags & (NegotiateUnicode | NegotiateSign | NegotiateNtlm | NegotiateAlwaysSign |
                                        NegotiateExtendedSessionSecurity | NegotiateTargetInfo | NegotiateVersion |
                                        Negotiate128 | NegotiateKeyExchange | Negotiate56);
            flags |= NegotiateUnicode | NegotiateNtlm;

            byte[] lmResponse, ntResponse, encryptedKey;
            bool hasMic = false;
            if (IsAnonymous)
            {
                lmResponse = new byte[1];
                ntResponse = new byte[0];
                encryptedKey = new byte[0];
                SessionKey = new byte[16];
                flags &= ~NegotiateKeyExchange;
            }
            else
            {
                byte[] ntOwf = NtowfV2(m_Password, m_User, m_Domain);
                byte[] clientChallenge = new byte[8];
                Array.Copy(m_Random(), clientChallenge, 8);

                long timestamp = FindTimestamp(targetInfo, out bool serverSentTimestamp);
                if (!serverSentTimestamp)
                    timestamp = DateTime.UtcNow.ToFileTimeUtc();
                hasMic = serverSentTimestamp;
                byte[] info = hasMic ? AddMicFlag(targetInfo) : targetInfo;

                var temp = new MemoryStream();
                var tw = new BinaryWriter(temp);
                tw.Write((byte)1);
                tw.Write((byte)1);
                tw.Write((ushort)0);
                tw.Write(0u);
                tw.Write(timestamp);
                tw.Write(clientChallenge);
                tw.Write(0u);
                tw.Write(info);
                tw.Write(0u);
                byte[] tempBytes = temp.ToArray();

                byte[] proof = HmacMd5(ntOwf, Concat(serverChallenge, tempBytes));
                ntResponse = Concat(proof, tempBytes);
                // With a timestamp from the server, the LM response must be all zeros (MS-NLMP 3.1.5.1.2).
                lmResponse = hasMic ? new byte[24] : Concat(HmacMd5(ntOwf, Concat(serverChallenge, clientChallenge)), clientChallenge);

                byte[] baseKey = HmacMd5(ntOwf, proof);
                if ((flags & NegotiateKeyExchange) != 0)
                {
                    SessionKey = m_Random();
                    encryptedKey = Rc4(baseKey, SessionKey);
                }
                else
                {
                    SessionKey = baseKey;
                    encryptedKey = new byte[0];
                }
            }

            byte[] domain = Encoding.Unicode.GetBytes(m_Domain);
            byte[] user = Encoding.Unicode.GetBytes(m_User);
            byte[] workstation = Encoding.Unicode.GetBytes("THEMOVIES");

            const int headerSize = 88; // up to and including the MIC
            int offset = headerSize;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(k_Signature);
            w.Write(3u);
            WriteField(w, lmResponse.Length, offset); offset += lmResponse.Length;
            WriteField(w, ntResponse.Length, offset); offset += ntResponse.Length;
            WriteField(w, domain.Length, offset); offset += domain.Length;
            WriteField(w, user.Length, offset); offset += user.Length;
            WriteField(w, workstation.Length, offset); offset += workstation.Length;
            WriteField(w, encryptedKey.Length, offset);
            w.Write(flags);
            WriteVersion(w);
            w.Write(new byte[16]); // MIC, filled in below
            w.Write(lmResponse);
            w.Write(ntResponse);
            w.Write(domain);
            w.Write(user);
            w.Write(workstation);
            w.Write(encryptedKey);
            byte[] message = ms.ToArray();

            if (hasMic)
            {
                byte[] mic = HmacMd5(SessionKey, Concat(Concat(m_Negotiate ?? new byte[0], challengeMessage), message));
                Array.Copy(mic, 0, message, 72, 16);
            }
            return message;
        }

        // ---------------- Crypto helpers ----------------

        public static byte[] NtowfV2(string password, string user, string domain)
        {
            byte[] ntHash = Md4.Hash(Encoding.Unicode.GetBytes(password ?? string.Empty));
            return HmacMd5(ntHash, Encoding.Unicode.GetBytes((user ?? string.Empty).ToUpperInvariant() + (domain ?? string.Empty)));
        }

        public static byte[] HmacMd5(byte[] key, byte[] data)
        {
            using (var h = new HMACMD5(key))
                return h.ComputeHash(data);
        }

        public static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
            }
            var output = new byte[data.Length];
            for (int k = 0, i = 0, j = 0; k < data.Length; k++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
                output[k] = (byte)(data[k] ^ s[(s[i] + s[j]) & 0xFF]);
            }
            return output;
        }

        static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        static bool StartsWithSignature(byte[] data)
        {
            for (int i = 0; i < k_Signature.Length; i++)
                if (data[i] != k_Signature[i]) return false;
            return true;
        }

        static void WriteField(BinaryWriter w, int length, int offset)
        {
            w.Write((ushort)length);
            w.Write((ushort)length);
            w.Write((uint)offset);
        }

        static void WriteVersion(BinaryWriter w)
        {
            // Windows 10 build 19041, NTLM revision 15
            w.Write((byte)10);
            w.Write((byte)0);
            w.Write((ushort)19041);
            w.Write(new byte[] { 0, 0, 0, 15 });
        }

        static byte[] ReadField(byte[] message, int fieldOffset)
        {
            int length = BitConverter.ToUInt16(message, fieldOffset);
            int offset = (int)BitConverter.ToUInt32(message, fieldOffset + 4);
            if (length == 0 || offset + length > message.Length) return new byte[0];
            var r = new byte[length];
            Array.Copy(message, offset, r, 0, length);
            return r;
        }

        static long FindTimestamp(byte[] targetInfo, out bool found)
        {
            found = false;
            for (int i = 0; i + 4 <= targetInfo.Length;)
            {
                ushort id = BitConverter.ToUInt16(targetInfo, i);
                ushort len = BitConverter.ToUInt16(targetInfo, i + 2);
                if (id == AvEol) break;
                if (id == AvTimestamp && len == 8 && i + 12 <= targetInfo.Length)
                {
                    found = true;
                    return BitConverter.ToInt64(targetInfo, i + 4);
                }
                i += 4 + len;
            }
            return 0;
        }

        /// <summary>Adds/sets MsvAvFlags = 0x2 ("MIC present") in the target info.</summary>
        static byte[] AddMicFlag(byte[] targetInfo)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            bool flagsWritten = false;
            for (int i = 0; i + 4 <= targetInfo.Length;)
            {
                ushort id = BitConverter.ToUInt16(targetInfo, i);
                ushort len = BitConverter.ToUInt16(targetInfo, i + 2);
                if (id == AvEol) break;
                if (id == AvFlags && len == 4)
                {
                    w.Write(id);
                    w.Write(len);
                    w.Write(BitConverter.ToUInt32(targetInfo, i + 4) | 2u);
                    flagsWritten = true;
                }
                else
                {
                    w.Write(targetInfo, i, 4 + len);
                }
                i += 4 + len;
            }
            if (!flagsWritten)
            {
                w.Write(AvFlags);
                w.Write((ushort)4);
                w.Write(2u);
            }
            w.Write(AvEol);
            w.Write((ushort)0);
            return ms.ToArray();
        }
    }

    /// <summary>MD4 (RFC 1320), needed for the NT password hash.</summary>
    public static class Md4
    {
        public static byte[] Hash(byte[] input)
        {
            uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
            long bitLength = (long)input.Length * 8;
            int padded = ((input.Length + 8) / 64 + 1) * 64;
            var msg = new byte[padded];
            Array.Copy(input, msg, input.Length);
            msg[input.Length] = 0x80;
            BitConverter.GetBytes(bitLength).CopyTo(msg, padded - 8);

            var x = new uint[16];
            for (int block = 0; block < padded; block += 64)
            {
                for (int i = 0; i < 16; i++) x[i] = BitConverter.ToUInt32(msg, block + i * 4);
                uint aa = a, bb = b, cc = c, dd = d;

                uint F(uint p, uint q, uint r) => (p & q) | (~p & r);
                uint G(uint p, uint q, uint r) => (p & q) | (p & r) | (q & r);
                uint H(uint p, uint q, uint r) => p ^ q ^ r;
                uint Rot(uint v, int s) => (v << s) | (v >> (32 - s));

                int[] r1 = { 3, 7, 11, 19 };
                for (int i = 0; i < 16; i++)
                {
                    int s = r1[i % 4];
                    switch (i % 4)
                    {
                        case 0: a = Rot(a + F(b, c, d) + x[i], s); break;
                        case 1: d = Rot(d + F(a, b, c) + x[i], s); break;
                        case 2: c = Rot(c + F(d, a, b) + x[i], s); break;
                        default: b = Rot(b + F(c, d, a) + x[i], s); break;
                    }
                }
                int[] order2 = { 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15 };
                int[] r2 = { 3, 5, 9, 13 };
                for (int i = 0; i < 16; i++)
                {
                    int k = order2[i], s = r2[i % 4];
                    switch (i % 4)
                    {
                        case 0: a = Rot(a + G(b, c, d) + x[k] + 0x5a827999, s); break;
                        case 1: d = Rot(d + G(a, b, c) + x[k] + 0x5a827999, s); break;
                        case 2: c = Rot(c + G(d, a, b) + x[k] + 0x5a827999, s); break;
                        default: b = Rot(b + G(c, d, a) + x[k] + 0x5a827999, s); break;
                    }
                }
                int[] order3 = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };
                int[] r3 = { 3, 9, 11, 15 };
                for (int i = 0; i < 16; i++)
                {
                    int k = order3[i], s = r3[i % 4];
                    switch (i % 4)
                    {
                        case 0: a = Rot(a + H(b, c, d) + x[k] + 0x6ed9eba1, s); break;
                        case 1: d = Rot(d + H(a, b, c) + x[k] + 0x6ed9eba1, s); break;
                        case 2: c = Rot(c + H(d, a, b) + x[k] + 0x6ed9eba1, s); break;
                        default: b = Rot(b + H(c, d, a) + x[k] + 0x6ed9eba1, s); break;
                    }
                }
                a += aa;
                b += bb;
                c += cc;
                d += dd;
            }

            var result = new byte[16];
            BitConverter.GetBytes(a).CopyTo(result, 0);
            BitConverter.GetBytes(b).CopyTo(result, 4);
            BitConverter.GetBytes(c).CopyTo(result, 8);
            BitConverter.GetBytes(d).CopyTo(result, 12);
            return result;
        }
    }
}
