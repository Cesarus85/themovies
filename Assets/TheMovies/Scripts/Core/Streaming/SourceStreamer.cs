using System;
using System.Diagnostics;
using System.Threading;
using TheMovies.Core.Network;

namespace TheMovies.Core.Streaming
{
    /// <summary>
    /// Feeds a <see cref="RemoteBlockCache"/> from an <see cref="IRandomAccessSource"/> (NAS file) on background
    /// threads, so <see cref="LocalStreamServer"/> can serve it to the video player. Several workers, each with its
    /// own connection, fetch in parallel (hides the network latency of single reads). Blocks the player waits
    /// for come first, then read-ahead; connections are re-opened after errors.
    /// </summary>
    public sealed class SourceStreamer : IBlockSource, IDisposable
    {
        public const int DefaultWorkers = 3;

        readonly Func<IRandomAccessSource> m_Factory;
        readonly RemoteBlockCache m_Cache;
        readonly object m_TakeLock = new object();
        readonly IRandomAccessSource[] m_Sources;
        readonly Stopwatch m_Clock = Stopwatch.StartNew();
        volatile bool m_Stopped;
        volatile string m_Error;

        /// <param name="readAheadBlocks">How far ahead of the player to fetch (blocks of 256 KB).</param>
        public SourceStreamer(Func<IRandomAccessSource> factory, long size, int capacityBlocks = 160, int readAheadBlocks = 48,
            int workers = DefaultWorkers)
        {
            m_Factory = factory;
            m_Cache = new RemoteBlockCache(size, RemoteBlockCache.DefaultBlockSize, capacityBlocks, readAheadBlocks);
            m_Sources = new IRandomAccessSource[Math.Max(1, workers)];
            for (int i = 0; i < m_Sources.Length; i++)
            {
                int worker = i;
                new Thread(() => Run(worker)) { IsBackground = true, Name = "TheMovies source streamer " + i }.Start();
            }
        }

        public long TotalSize => m_Cache.TotalSize;
        public int BlockSize => m_Cache.BlockSize;
        public int BlockIndexOf(long byteOffset) => m_Cache.BlockIndexOf(byteOffset);
        public byte[] WaitForBlock(int index, int timeoutMs) => m_Cache.WaitForBlock(index, timeoutMs);

        /// <summary>Last connection or read error, null while everything works.</summary>
        public string Error => m_Error;

        public long BytesReceived => m_Cache.BytesReceived;

        /// <summary>How many bytes the streamer keeps ready ahead of the player at most.</summary>
        public long ReadAheadBytes => (long)m_Cache.ReadAheadBlocks * m_Cache.BlockSize;

        /// <summary>Number of consecutive cached bytes from the given byte offset (long.MaxValue if that reaches the end).</summary>
        public long BufferedBytesFrom(long offset)
        {
            int index = m_Cache.BlockIndexOf(offset);
            int blocks = m_Cache.ContiguousBlocksFrom(index);
            if (index + blocks >= m_Cache.BlockCount) return long.MaxValue;
            return Math.Max(0, (long)blocks * m_Cache.BlockSize - (offset - (long)index * m_Cache.BlockSize));
        }

        public void Dispose()
        {
            m_Stopped = true;
            m_Cache.Close();
            // Closing the connections also wakes workers blocked in a read.
            for (int i = 0; i < m_Sources.Length; i++)
            {
                try { m_Sources[i]?.Dispose(); }
                catch (Exception) { }
            }
        }

        /// <summary>Takes exactly one block to fetch (or -1).</summary>
        int TakeOne()
        {
            lock (m_TakeLock)
            {
                var requests = m_Cache.TakeRequests(m_Clock.Elapsed.TotalSeconds, m_Cache.InFlightCount + 1, 120);
                if (requests.Count == 0) return -1;
                for (int i = 1; i < requests.Count; i++)
                    m_Cache.OnBlockFailed(requests[i]); // hand extra ones back
                return requests[0];
            }
        }

        void Run(int worker)
        {
            int failures = 0;
            while (!m_Stopped)
            {
                int index = TakeOne();
                if (index < 0)
                {
                    Thread.Sleep(6);
                    continue;
                }

                try
                {
                    var source = m_Sources[worker];
                    if (source == null)
                    {
                        source = m_Factory();
                        m_Sources[worker] = source;
                        source.Open();
                        if (source.Length != m_Cache.TotalSize)
                            throw new System.IO.IOException("Die Datei hat sich auf dem Server geändert.");
                    }
                    byte[] block = SourceReader.ReadBlock(source, index, m_Cache.BlockSize);
                    if (m_Stopped) break;
                    m_Cache.OnBlockReceived(index, block);
                    failures = 0;
                    m_Error = null;
                }
                catch (Exception e)
                {
                    m_Cache.OnBlockFailed(index);
                    if (m_Stopped) break;
                    failures++;
                    m_Error = e.Message;
                    try { m_Sources[worker]?.Dispose(); }
                    catch (Exception) { }
                    m_Sources[worker] = null;
                    Thread.Sleep(Math.Min(5000, 300 * failures));
                }
            }
            try { m_Sources[worker]?.Dispose(); }
            catch (Exception) { }
            m_Sources[worker] = null;
        }
    }
}
