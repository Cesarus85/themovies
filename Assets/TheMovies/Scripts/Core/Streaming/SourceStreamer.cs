using System;
using System.Diagnostics;
using System.Threading;
using TheMovies.Core.Network;

namespace TheMovies.Core.Streaming
{
    /// <summary>
    /// Feeds a <see cref="RemoteBlockCache"/> from an <see cref="IRandomAccessSource"/> (NAS file) on a background
    /// thread, so <see cref="LocalStreamServer"/> can serve it to the video player. Fetches the blocks the player
    /// waits for first, then reads ahead; reconnects after errors.
    /// </summary>
    public sealed class SourceStreamer : IBlockSource, IDisposable
    {
        readonly Func<IRandomAccessSource> m_Factory;
        readonly RemoteBlockCache m_Cache;
        readonly Thread m_Thread;
        volatile bool m_Stopped;
        volatile IRandomAccessSource m_Source;
        volatile string m_Error;

        public SourceStreamer(Func<IRandomAccessSource> factory, long size, int capacityBlocks = 160, int readAheadBlocks = 48)
        {
            m_Factory = factory;
            m_Cache = new RemoteBlockCache(size, RemoteBlockCache.DefaultBlockSize, capacityBlocks, readAheadBlocks);
            m_Thread = new Thread(Run) { IsBackground = true, Name = "TheMovies source streamer" };
            m_Thread.Start();
        }

        public long TotalSize => m_Cache.TotalSize;
        public int BlockSize => m_Cache.BlockSize;
        public int BlockIndexOf(long byteOffset) => m_Cache.BlockIndexOf(byteOffset);
        public byte[] WaitForBlock(int index, int timeoutMs) => m_Cache.WaitForBlock(index, timeoutMs);

        /// <summary>Last connection or read error, null while everything works.</summary>
        public string Error => m_Error;

        public long BytesReceived => m_Cache.BytesReceived;

        /// <summary>Number of consecutive cached blocks from the given byte offset.</summary>
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
            // Closing the connection also wakes the thread if it is blocked in a read.
            try { m_Source?.Dispose(); }
            catch (Exception) { }
        }

        void Run()
        {
            var clock = Stopwatch.StartNew();
            int failures = 0;
            while (!m_Stopped)
            {
                var requests = m_Cache.TakeRequests(clock.Elapsed.TotalSeconds, 1, 120);
                if (requests.Count == 0)
                {
                    Thread.Sleep(8);
                    continue;
                }

                int index = requests[0];
                try
                {
                    var source = m_Source;
                    if (source == null)
                    {
                        source = m_Factory();
                        m_Source = source;
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
                    try { m_Source?.Dispose(); }
                    catch (Exception) { }
                    m_Source = null;
                    Thread.Sleep(Math.Min(5000, 300 * failures));
                }
            }
            try { m_Source?.Dispose(); }
            catch (Exception) { }
            m_Source = null;
        }
    }
}
