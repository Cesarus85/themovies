using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TheMovies.Core.Network
{
    /// <summary>A file that can be read at arbitrary offsets: local, on an SMB share or behind an HTTP server.</summary>
    public interface IRandomAccessSource : IDisposable
    {
        /// <summary>File size in bytes; valid after <see cref="Open"/>.</summary>
        long Length { get; }

        /// <summary>Connects and opens the file (blocking).</summary>
        void Open();

        /// <summary>Reads up to <paramref name="count"/> bytes; returns 0 at the end of the file.</summary>
        int Read(long offset, byte[] buffer, int index, int count);
    }

    /// <summary>A source whose connection can be cut from another thread (unblocks a running read).</summary>
    public interface IAbortable
    {
        void Abort();
    }

    public static class SourceReader
    {
        /// <summary>Reads exactly <paramref name="count"/> bytes (less only at the end of the file).</summary>
        public static int ReadFully(IRandomAccessSource source, long offset, byte[] buffer, int index, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = source.Read(offset + total, buffer, index + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        /// <summary>Reads one block of a file split into <paramref name="blockSize"/> byte blocks, or null past the end.</summary>
        public static byte[] ReadBlock(IRandomAccessSource source, int index, int blockSize)
        {
            long offset = (long)index * blockSize;
            if (index < 0 || offset >= source.Length) return null;
            int length = (int)Math.Min(blockSize, source.Length - offset);
            var block = new byte[length];
            if (ReadFully(source, offset, block, 0, length) != length)
                throw new IOException("Datei endet früher als erwartet.");
            return block;
        }
    }

    public sealed class LocalFileSource : IRandomAccessSource
    {
        readonly string m_Path;
        FileStream m_File;

        public LocalFileSource(string path)
        {
            m_Path = path;
        }

        public long Length => m_File?.Length ?? 0;

        public void Open()
        {
            m_File = new FileStream(m_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        }

        public int Read(long offset, byte[] buffer, int index, int count)
        {
            if (m_File == null) Open();
            m_File.Seek(offset, SeekOrigin.Begin);
            return m_File.Read(buffer, index, count);
        }

        public void Dispose()
        {
            m_File?.Dispose();
            m_File = null;
        }
    }

    public sealed class SmbFileSource : IRandomAccessSource, IAbortable
    {
        readonly SmbAddress m_Address;
        readonly string m_User;
        readonly string m_Password;
        readonly string m_Domain;
        Smb2Client m_Client;
        uint m_Tree;
        byte[] m_FileId;

        public SmbFileSource(SmbAddress address, string user, string password, string domain = "")
        {
            m_Address = address;
            m_User = user;
            m_Password = password;
            m_Domain = domain;
        }

        public long Length { get; private set; }

        public void Open()
        {
            Dispose();
            if (string.IsNullOrEmpty(m_Address.Share) || string.IsNullOrEmpty(m_Address.Path))
                throw new IOException("Keine Datei angegeben.");
            var client = new Smb2Client(m_Address.Host, m_Address.Port);
            try
            {
                client.Connect();
                client.Login(m_User, m_Password, m_Domain);
                m_Tree = client.TreeConnect(m_Address.Share);
                m_FileId = client.Open(m_Tree, m_Address.Path, false, out long size);
                Length = size;
                m_Client = client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public int Read(long offset, byte[] buffer, int index, int count)
        {
            if (m_Client == null) Open();
            if (offset >= Length) return 0;
            return m_Client.Read(m_Tree, m_FileId, offset, buffer, index, count);
        }

        public void Abort()
        {
            m_Client?.Abort();
        }

        public void Dispose()
        {
            var client = m_Client;
            m_Client = null;
            if (client == null) return;
            try
            {
                if (m_FileId != null && client.IsConnected) client.Close(m_Tree, m_FileId);
            }
            catch (Exception)
            {
                // Connection already gone
            }
            client.Dispose();
        }
    }

    /// <summary>Reads a file from an HTTP(S) server with Range requests (WebDAV, DLNA).</summary>
    public sealed class HttpRangeSource : IRandomAccessSource, IAbortable
    {
        readonly Uri m_Uri;
        readonly string m_User;
        readonly string m_Password;
        HttpClient m_Client;

        public HttpRangeSource(string url, string user = null, string password = null)
        {
            m_Uri = new Uri(url);
            m_User = user;
            m_Password = password;
        }

        public long Length { get; private set; }

        public void Open()
        {
            m_Client ??= NetHttp.CreateClient(m_Uri, m_User, m_Password);
            using (var request = new HttpRequestMessage(HttpMethod.Get, m_Uri))
            {
                request.Headers.Range = new RangeHeaderValue(0, 0);
                using (var response = m_Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    NetHttp.ThrowIfFailed(response);
                    if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.Length == null)
                        throw new IOException("Der Server unterstützt keine Bereichsanfragen (Spulen nicht möglich).");
                    Length = response.Content.Headers.ContentRange.Length.Value;
                }
            }
        }

        public int Read(long offset, byte[] buffer, int index, int count)
        {
            if (m_Client == null) Open();
            if (offset >= Length || count <= 0) return 0;
            long end = Math.Min(Length - 1, offset + count - 1);
            using (var request = new HttpRequestMessage(HttpMethod.Get, m_Uri))
            {
                request.Headers.Range = new RangeHeaderValue(offset, end);
                using (var response = m_Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    NetHttp.ThrowIfFailed(response);
                    if (response.StatusCode != HttpStatusCode.PartialContent)
                        throw new IOException("Der Server unterstützt keine Bereichsanfragen (Spulen nicht möglich).");
                    var from = response.Content.Headers.ContentRange?.From;
                    if (from.HasValue && from.Value != offset)
                        throw new IOException("Der Server hat einen falschen Bereich geliefert.");
                    using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    {
                        int wanted = (int)(end - offset + 1);
                        int total = 0;
                        while (total < wanted)
                        {
                            int n = stream.Read(buffer, index + total, wanted - total);
                            if (n <= 0) break;
                            total += n;
                        }
                        return total;
                    }
                }
            }
        }

        public void Dispose()
        {
            m_Client?.Dispose();
            m_Client = null;
        }

        public void Abort()
        {
            try { m_Client?.CancelPendingRequests(); }
            catch (Exception) { }
        }
    }

    public static class NetHttp
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// HTTP client with optional login. Self-signed certificates are accepted for hosts in the local network
        /// (NAS devices rarely have an official certificate); public hosts are validated normally.
        /// </summary>
        public static HttpClient CreateClient(Uri uri, string user, string password)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(user))
            {
                handler.Credentials = new NetworkCredential(user, password ?? string.Empty);
                handler.PreAuthenticate = true;
            }
            if (uri.Scheme == Uri.UriSchemeHttps && NetAddress.IsLocalHost(uri.Host))
            {
                try
                {
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (NotImplementedException)
                {
                }
            }
            var client = new HttpClient(handler) { Timeout = Timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TheMovies/1.0");
            return client;
        }

        public static void ThrowIfFailed(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            switch ((int)response.StatusCode)
            {
                case 401: throw new IOException("Anmeldung abgelehnt (Benutzer oder Passwort falsch).");
                case 403: throw new IOException("Zugriff verweigert.");
                case 404: throw new IOException("Nicht gefunden.");
                case 405: throw new IOException("Der Server unterstützt WebDAV an dieser Adresse nicht.");
                default: throw new IOException($"HTTP-Fehler {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
    }
}
