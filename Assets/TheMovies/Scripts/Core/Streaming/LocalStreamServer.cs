using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TheMovies.Core.Streaming
{
    /// <summary>Result of parsing an HTTP Range header against a file length.</summary>
    public enum RangeResult
    {
        /// <summary>No (or unsupported) Range header: send the whole file.</summary>
        Full,

        /// <summary>A valid single byte range.</summary>
        Partial,

        /// <summary>The range lies outside the file (HTTP 416).</summary>
        Unsatisfiable,
    }

    public static class HttpRange
    {
        /// <summary>Parses "bytes=a-b", "bytes=a-" and "bytes=-n". <paramref name="end"/> is inclusive.</summary>
        public static RangeResult Parse(string header, long length, out long start, out long end)
        {
            start = 0;
            end = length - 1;
            if (string.IsNullOrWhiteSpace(header))
                return RangeResult.Full;

            header = header.Trim();
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return RangeResult.Full;

            string spec = header.Substring(6).Trim();
            if (spec.IndexOf(',') >= 0)
                spec = spec.Substring(0, spec.IndexOf(',')).Trim(); // multiple ranges: serve the first only
            int dash = spec.IndexOf('-');
            if (dash < 0)
                return RangeResult.Full;

            string a = spec.Substring(0, dash).Trim();
            string b = spec.Substring(dash + 1).Trim();

            if (a.Length == 0)
            {
                // Suffix range: the last n bytes
                if (!long.TryParse(b, out long suffix) || suffix <= 0)
                    return RangeResult.Unsatisfiable;
                start = Math.Max(0, length - suffix);
                end = length - 1;
                return RangeResult.Partial;
            }

            if (!long.TryParse(a, out start) || start < 0)
                return RangeResult.Full;
            if (start >= length)
                return RangeResult.Unsatisfiable;

            if (b.Length == 0)
            {
                end = length - 1;
            }
            else
            {
                if (!long.TryParse(b, out end) || end < start)
                    return RangeResult.Full;
                end = Math.Min(end, length - 1);
            }
            return RangeResult.Partial;
        }
    }

    /// <summary>
    /// Minimal HTTP/1.1 server on 127.0.0.1 that serves one remote file out of a <see cref="RemoteBlockCache"/>,
    /// with byte range support so a video player can seek. Each connection is handled on its own thread
    /// and answers with "Connection: close".
    /// </summary>
    public sealed class LocalStreamServer : IDisposable
    {
        public const int BlockWaitTimeoutMs = 30000;

        readonly TcpListener m_Listener;
        readonly Thread m_AcceptThread;
        readonly object m_Lock = new object();
        volatile bool m_Disposed;
        string m_Path;
        RemoteBlockCache m_Cache;
        string m_ContentType = "application/octet-stream";

        public LocalStreamServer()
        {
            m_Listener = new TcpListener(IPAddress.Loopback, 0);
            m_Listener.Start();
            Port = ((IPEndPoint)m_Listener.LocalEndpoint).Port;
            m_AcceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "TheMovies stream server" };
            m_AcceptThread.Start();
        }

        public int Port { get; }

        /// <summary>Number of HTTP requests answered so far (for diagnostics).</summary>
        public int RequestCount;

        /// <summary>Serves <paramref name="cache"/> under <paramref name="path"/> (e.g. "/media/3/movie.mp4"). Returns the URL.</summary>
        public string Serve(string path, RemoteBlockCache cache, string contentType)
        {
            lock (m_Lock)
            {
                m_Path = path;
                m_Cache = cache;
                m_ContentType = contentType ?? "application/octet-stream";
            }
            return $"http://127.0.0.1:{Port}{path}";
        }

        public void StopServing()
        {
            lock (m_Lock)
            {
                m_Path = null;
                m_Cache = null;
            }
        }

        public static string ContentTypeFor(string fileName)
        {
            switch (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant())
            {
                case ".mp4":
                case ".m4v":
                    return "video/mp4";
                case ".mkv":
                    return "video/x-matroska";
                case ".webm":
                    return "video/webm";
                case ".mov":
                    return "video/quicktime";
                case ".ts":
                    return "video/mp2t";
                default:
                    return "application/octet-stream";
            }
        }

        public void Dispose()
        {
            m_Disposed = true;
            try { m_Listener.Stop(); }
            catch (SocketException) { }
        }

        void AcceptLoop()
        {
            while (!m_Disposed)
            {
                TcpClient client;
                try
                {
                    client = m_Listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (m_Disposed) return;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    client.SendTimeout = BlockWaitTimeoutMs;
                    var stream = client.GetStream();
                    if (!TryReadRequest(stream, out string method, out string path, out string range))
                        return;
                    Interlocked.Increment(ref RequestCount);

                    string servedPath;
                    RemoteBlockCache cache;
                    string contentType;
                    lock (m_Lock)
                    {
                        servedPath = m_Path;
                        cache = m_Cache;
                        contentType = m_ContentType;
                    }

                    if (cache == null || servedPath == null || !string.Equals(StripQuery(path), servedPath, StringComparison.Ordinal))
                    {
                        WriteHead(stream, "404 Not Found", 0, null, null);
                        return;
                    }
                    if (method != "GET" && method != "HEAD")
                    {
                        WriteHead(stream, "405 Method Not Allowed", 0, null, null);
                        return;
                    }

                    long length = cache.TotalSize;
                    var result = HttpRange.Parse(range, length, out long start, out long end);
                    if (result == RangeResult.Unsatisfiable)
                    {
                        WriteHead(stream, "416 Range Not Satisfiable", 0, null, $"Content-Range: bytes */{length}\r\n");
                        return;
                    }

                    bool partial = result == RangeResult.Partial;
                    long count = end - start + 1;
                    WriteHead(stream, partial ? "206 Partial Content" : "200 OK", count, contentType,
                        partial ? $"Content-Range: bytes {start}-{end}/{length}\r\n" : null);
                    if (method == "HEAD")
                        return;

                    WriteBody(stream, cache, start, end);
                }
                catch (IOException)
                {
                    // Player closed the connection (normal when it seeks).
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        static void WriteBody(Stream stream, RemoteBlockCache cache, long start, long end)
        {
            long position = start;
            while (position <= end)
            {
                int index = cache.BlockIndexOf(position);
                byte[] block = cache.WaitForBlock(index, BlockWaitTimeoutMs);
                if (block == null)
                    return; // timeout or movie changed
                long blockStart = (long)index * cache.BlockSize;
                int offset = (int)(position - blockStart);
                int count = (int)Math.Min(block.Length - offset, end - position + 1);
                if (count <= 0)
                    return;
                stream.Write(block, offset, count);
                position += count;
            }
            stream.Flush();
        }

        static string StripQuery(string path)
        {
            int q = path.IndexOf('?');
            return q >= 0 ? path.Substring(0, q) : path;
        }

        static void WriteHead(Stream stream, string status, long contentLength, string contentType, string extra)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
            if (contentType != null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            if (extra != null) sb.Append(extra);
            sb.Append("Connection: close\r\n\r\n");
            byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Reads the request line and the headers we care about (up to 16 KB).</summary>
        static bool TryReadRequest(Stream stream, out string method, out string path, out string range)
        {
            method = path = range = null;
            var buffer = new byte[16 * 1024];
            int length = 0;
            while (true)
            {
                if (length == buffer.Length) return false;
                int read = stream.Read(buffer, length, buffer.Length - length);
                if (read <= 0) return false;
                length += read;
                int headerEnd = IndexOfHeaderEnd(buffer, length);
                if (headerEnd >= 0)
                {
                    string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 2) return false;
                    method = requestLine[0].ToUpperInvariant();
                    path = Uri.UnescapeDataString(requestLine[1]);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0 && lines[i].Substring(0, colon).Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
                            range = lines[i].Substring(colon + 1).Trim();
                    }
                    return true;
                }
            }
        }

        static int IndexOfHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 3; i < length; i++)
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                    return i - 3;
            return -1;
        }
    }
}
