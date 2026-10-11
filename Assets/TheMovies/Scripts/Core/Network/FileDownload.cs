using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TheMovies.Core.Network
{
    /// <summary>
    /// Copies a file from a NAS (any <see cref="IRandomAccessSource"/>) to local storage in large chunks.
    /// Writes to "name.part" and renames when complete; an interrupted copy continues where it stopped.
    /// Connection errors are retried with a fresh connection.
    /// </summary>
    public sealed class FileDownload
    {
        public const int ChunkSize = 4 * 1024 * 1024;
        const int MaxRetries = 6;

        readonly Func<IRandomAccessSource> m_Factory;
        readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
        long m_Done;

        public FileDownload(Func<IRandomAccessSource> factory, string targetPath, long totalSize)
        {
            m_Factory = factory;
            TargetPath = targetPath;
            Total = totalSize;
        }

        public string TargetPath { get; }
        public string PartPath => TargetPath + ".part";
        public long Total { get; private set; }
        public long Done => Interlocked.Read(ref m_Done);
        public float Progress => Total > 0 ? (float)Math.Min(1.0, (double)Done / Total) : 0f;

        /// <summary>Bytes per second over the last few seconds.</summary>
        public double Rate { get; private set; }

        /// <summary>Seconds left at the current rate, or -1 if unknown.</summary>
        public double SecondsLeft => Rate > 1 ? (Total - Done) / Rate : -1;

        public bool Finished { get; private set; }
        public bool Canceled => m_Cancel.IsCancellationRequested;
        public string Error { get; private set; }

        public void Cancel() => m_Cancel.Cancel();

        public Task Start() => Task.Run(Run);

        void Run()
        {
            IRandomAccessSource source = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TargetPath) ?? ".");
                if (File.Exists(TargetPath) && new FileInfo(TargetPath).Length == Total)
                {
                    Interlocked.Exchange(ref m_Done, Total);
                    Finished = true;
                    return;
                }

                long offset = File.Exists(PartPath) ? new FileInfo(PartPath).Length : 0;
                if (offset > Total) offset = 0;
                Interlocked.Exchange(ref m_Done, offset);
                var buffer = new byte[ChunkSize];
                var clock = Stopwatch.StartNew();
                double rateFrom = 0;
                long rateBytes = offset;
                int retries = 0;

                using (var output = new FileStream(PartPath, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
                {
                    output.SetLength(offset);
                    output.Seek(offset, SeekOrigin.Begin);
                    while (offset < Total)
                    {
                        m_Cancel.Token.ThrowIfCancellationRequested();
                        try
                        {
                            if (source == null)
                            {
                                source = m_Factory();
                                source.Open();
                                if (source.Length != Total)
                                {
                                    if (offset > 0) throw new IOException("Die Datei hat sich auf dem NAS geändert.");
                                    Total = source.Length;
                                }
                            }
                            int want = (int)Math.Min(buffer.Length, Total - offset);
                            int read = SourceReader.ReadFully(source, offset, buffer, 0, want);
                            if (read <= 0) throw new IOException("Die Datei endet früher als erwartet.");
                            output.Write(buffer, 0, read);
                            offset += read;
                            Interlocked.Exchange(ref m_Done, offset);
                            retries = 0;
                        }
                        catch (Exception e) when (!(e is OperationCanceledException) && retries < MaxRetries && !m_Cancel.IsCancellationRequested)
                        {
                            retries++;
                            try { source?.Dispose(); }
                            catch (Exception) { }
                            source = null;
                            Thread.Sleep(500 * retries);
                        }

                        double now = clock.Elapsed.TotalSeconds;
                        if (now - rateFrom >= 1.0)
                        {
                            double rate = (offset - rateBytes) / (now - rateFrom);
                            Rate = Rate <= 0 ? rate : Rate * 0.6 + rate * 0.4;
                            rateFrom = now;
                            rateBytes = offset;
                        }
                    }
                    output.Flush(true);
                }

                if (File.Exists(TargetPath)) File.Delete(TargetPath);
                File.Move(PartPath, TargetPath);
                Finished = true;
            }
            catch (OperationCanceledException)
            {
                Error = "Abgebrochen.";
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            finally
            {
                try { source?.Dispose(); }
                catch (Exception) { }
            }
        }

        /// <summary>A file name that is valid on Android and Windows (keeps umlauts and spaces).</summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "film.mp4";
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < 32 || "\\/:*?\"<>|".IndexOf(chars[i]) >= 0)
                    chars[i] = '_';
            string result = new string(chars).TrimEnd('.', ' ');
            return result.Length == 0 ? "film.mp4" : result;
        }
    }
}
