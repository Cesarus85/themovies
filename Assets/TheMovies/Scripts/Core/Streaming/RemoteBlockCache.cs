using System;
using System.Collections.Generic;
using System.Threading;

namespace TheMovies.Core.Streaming
{
    /// <summary>
    /// In-memory cache for a file that lives on another device and is fetched in fixed-size blocks.
    /// Reader threads (the local HTTP server) block in <see cref="WaitForBlock"/>; the main thread asks
    /// <see cref="TakeRequests"/> which blocks to fetch next and delivers them via <see cref="OnBlockReceived"/>.
    /// Blocks a reader is waiting for come first, then read-ahead from the latest read position.
    /// </summary>
    public sealed class RemoteBlockCache : IBlockSource
    {
        public const int DefaultBlockSize = 256 * 1024;

        readonly object m_Lock = new object();
        readonly Dictionary<int, byte[]> m_Blocks = new Dictionary<int, byte[]>();
        readonly LinkedList<int> m_Lru = new LinkedList<int>();
        readonly Dictionary<int, LinkedListNode<int>> m_LruNodes = new Dictionary<int, LinkedListNode<int>>();
        readonly SortedSet<int> m_Wanted = new SortedSet<int>();
        readonly Dictionary<int, double> m_InFlight = new Dictionary<int, double>();
        readonly int m_CapacityBlocks;
        bool m_Closed;
        int m_ReadPosition;
        long m_BytesReceived;

        public RemoteBlockCache(long totalSize, int blockSize = DefaultBlockSize, int capacityBlocks = 384, int readAheadBlocks = 32)
        {
            if (totalSize <= 0) throw new ArgumentOutOfRangeException(nameof(totalSize));
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
            TotalSize = totalSize;
            BlockSize = blockSize;
            BlockCount = (int)((totalSize + blockSize - 1) / blockSize);
            m_CapacityBlocks = Math.Max(capacityBlocks, readAheadBlocks + 8);
            ReadAheadBlocks = readAheadBlocks;
        }

        public long TotalSize { get; }
        public int BlockSize { get; }
        public int BlockCount { get; }
        public int ReadAheadBlocks { get; set; }

        public long BytesReceived
        {
            get { lock (m_Lock) return m_BytesReceived; }
        }

        /// <summary>Block index the most recent reader asked for.</summary>
        public int ReadPosition
        {
            get { lock (m_Lock) return m_ReadPosition; }
        }

        public bool IsClosed
        {
            get { lock (m_Lock) return m_Closed; }
        }

        public int InFlightCount
        {
            get { lock (m_Lock) return m_InFlight.Count; }
        }

        public int BlockIndexOf(long byteOffset) => (int)Math.Max(0, Math.Min(BlockCount - 1, byteOffset / BlockSize));

        public int ExpectedLength(int index)
        {
            if (index < 0 || index >= BlockCount) return 0;
            long start = (long)index * BlockSize;
            return (int)Math.Min(BlockSize, TotalSize - start);
        }

        /// <summary>Blocks the calling thread until the block is available, the timeout passes or the cache is closed.</summary>
        /// <returns>The block data, or null on timeout/close.</returns>
        public byte[] WaitForBlock(int index, int timeoutMs)
        {
            if (index < 0 || index >= BlockCount) return null;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            lock (m_Lock)
            {
                m_ReadPosition = index;
                while (true)
                {
                    if (m_Closed) return null;
                    if (m_Blocks.TryGetValue(index, out var data))
                    {
                        Touch(index);
                        m_Wanted.Remove(index);
                        return data;
                    }
                    m_Wanted.Add(index);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        m_Wanted.Remove(index);
                        return null;
                    }
                    Monitor.Wait(m_Lock, remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250));
                }
            }
        }

        public bool HasBlock(int index)
        {
            lock (m_Lock) return m_Blocks.ContainsKey(index);
        }

        /// <summary>
        /// Picks the blocks to request now and marks them as in flight.
        /// Requests that did not answer within <paramref name="retryAfter"/> seconds are sent again.
        /// </summary>
        public List<int> TakeRequests(double now, int maxInFlight, double retryAfter)
        {
            var result = new List<int>();
            lock (m_Lock)
            {
                if (m_Closed) return result;

                // Forget requests that never got an answer.
                List<int> expired = null;
                foreach (var kv in m_InFlight)
                    if (now - kv.Value > retryAfter)
                        (expired ??= new List<int>()).Add(kv.Key);
                if (expired != null)
                    foreach (int i in expired)
                        m_InFlight.Remove(i);

                foreach (int index in m_Wanted)
                {
                    if (m_InFlight.Count + result.Count >= maxInFlight) break;
                    if (!m_Blocks.ContainsKey(index) && !m_InFlight.ContainsKey(index))
                        result.Add(index);
                }

                int end = Math.Min(BlockCount, m_ReadPosition + 1 + ReadAheadBlocks);
                for (int index = m_ReadPosition; index < end; index++)
                {
                    if (m_InFlight.Count + result.Count >= maxInFlight) break;
                    if (!m_Blocks.ContainsKey(index) && !m_InFlight.ContainsKey(index) && !result.Contains(index))
                        result.Add(index);
                }

                foreach (int index in result)
                    m_InFlight[index] = now;
            }
            return result;
        }

        /// <returns>False if the data does not fit the block (wrong length or index).</returns>
        public bool OnBlockReceived(int index, byte[] data)
        {
            if (data == null || index < 0 || index >= BlockCount || data.Length != ExpectedLength(index))
            {
                OnBlockFailed(index);
                return false;
            }
            lock (m_Lock)
            {
                m_InFlight.Remove(index);
                if (m_Closed) return false;
                if (!m_Blocks.ContainsKey(index))
                {
                    m_Blocks[index] = data;
                    m_BytesReceived += data.Length;
                    Touch(index);
                    Evict();
                }
                Monitor.PulseAll(m_Lock);
            }
            return true;
        }

        public void OnBlockFailed(int index)
        {
            lock (m_Lock)
            {
                m_InFlight.Remove(index);
            }
        }

        /// <summary>
        /// Bytes cached in one piece from the block the reader asked for last, i.e. what the player can still read
        /// without waiting; long.MaxValue if that reaches the end of the file.
        /// </summary>
        public long BufferedAheadOfReader()
        {
            lock (m_Lock)
            {
                int count = 0;
                while (m_ReadPosition + count < BlockCount && m_Blocks.ContainsKey(m_ReadPosition + count))
                    count++;
                if (m_ReadPosition + count >= BlockCount) return long.MaxValue;
                return (long)count * BlockSize;
            }
        }

        /// <summary>Number of consecutive cached blocks starting at <paramref name="index"/>.</summary>
        public int ContiguousBlocksFrom(int index)
        {
            lock (m_Lock)
            {
                int count = 0;
                for (int i = Math.Max(0, index); i < BlockCount && m_Blocks.ContainsKey(i); i++)
                    count++;
                return count;
            }
        }

        /// <summary>Wakes up all waiting readers; they get null. Used when the movie changes or the session ends.</summary>
        public void Close()
        {
            lock (m_Lock)
            {
                m_Closed = true;
                m_Blocks.Clear();
                m_Lru.Clear();
                m_LruNodes.Clear();
                m_Wanted.Clear();
                m_InFlight.Clear();
                Monitor.PulseAll(m_Lock);
            }
        }

        void Touch(int index)
        {
            if (m_LruNodes.TryGetValue(index, out var node))
            {
                m_Lru.Remove(node);
                m_Lru.AddLast(node);
            }
            else
            {
                m_LruNodes[index] = m_Lru.AddLast(index);
            }
        }

        void Evict()
        {
            // Keep the window around the read position; drop the least recently used blocks elsewhere first.
            int keepFrom = m_ReadPosition - 2;
            int keepTo = m_ReadPosition + ReadAheadBlocks;
            var node = m_Lru.First;
            while (m_Blocks.Count > m_CapacityBlocks && node != null)
            {
                var next = node.Next;
                int index = node.Value;
                if (index < keepFrom || index > keepTo)
                    Remove(node);
                node = next;
            }
            while (m_Blocks.Count > m_CapacityBlocks && m_Lru.First != null)
                Remove(m_Lru.First);
        }

        void Remove(LinkedListNode<int> node)
        {
            m_Blocks.Remove(node.Value);
            m_LruNodes.Remove(node.Value);
            m_Lru.Remove(node);
        }
    }
}
