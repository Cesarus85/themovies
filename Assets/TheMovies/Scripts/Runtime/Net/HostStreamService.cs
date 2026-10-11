using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TheMovies.Core.Network;
using TheMovies.Core.Streaming;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>
    /// Streams the host's movie file to participants who don't have their own copy. The file is sent
    /// unchanged in blocks over the existing Netcode connection (so it also works through Relay without
    /// port forwarding); on the receiving side a local HTTP server feeds the blocks to the video player,
    /// which can then seek freely. The normal sync keeps everyone at the same position.
    /// The host reads the blocks on a worker thread, so a movie on a NAS can be passed on without stalling frames.
    /// </summary>
    public sealed class HostStreamService : MonoBehaviour
    {
        const string k_RequestMessage = "TheMovies.StreamRequest";
        const string k_DataMessage = "TheMovies.StreamData";
        const string k_SubtitleRequestMessage = "TheMovies.SubtitleRequest";
        const string k_SubtitleDataMessage = "TheMovies.SubtitleData";
        const int k_MaxSubtitleBytes = 2 * 1024 * 1024;
        const int k_MaxInFlight = 6;
        const double k_RetryAfter = 10.0;

        public static HostStreamService Instance { get; private set; }

        /// <summary>Host side: opens the file of a media version (not yet connected), or returns null if it must not be served.</summary>
        public static Func<int, IRandomAccessSource> HostSourceProvider;

        /// <summary>Host side: returns the subtitle file (.srt bytes) for a media version, or null.</summary>
        public static Func<int, byte[]> HostSubtitleProvider;

        /// <summary>Client side: called with the host's subtitle file (null if it has none).</summary>
        Action<int, byte[]> m_SubtitleCallback;
        NetworkManager m_Network;
        bool m_Registered;

        // Host
        sealed class HostedFile
        {
            public int Version;
            public IRandomAccessSource Source;
            public bool Opened;
            public bool Failed;
            public DateTime RetryAfter;
        }

        HostedFile m_Hosted;
        readonly object m_ReadLock = new object();
        readonly Queue<(HostedFile file, ulong client, int index)> m_ReadQueue = new Queue<(HostedFile, ulong, int)>();
        readonly ConcurrentQueue<(ulong client, int version, int index, byte[] data)> m_ReadDone = new ConcurrentQueue<(ulong, int, int, byte[])>();
        Thread m_Reader;
        bool m_ReaderStop;

        // Client
        LocalStreamServer m_Server;
        RemoteBlockCache m_Cache;
        int m_CacheVersion = -1;
        long m_LastBytes;
        float m_RateSampleAt;

        /// <summary>Smoothed download rate of the current stream in bytes per second.</summary>
        public float ReceiveRate { get; private set; }

        public bool IsStreaming => m_Cache != null && !m_Cache.IsClosed;

        public static HostStreamService Create()
        {
            var go = new GameObject("Host Stream Service");
            return go.AddComponent<HostStreamService>();
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            StopStream();
            CloseFile();
            lock (m_ReadLock)
            {
                m_ReaderStop = true;
                Monitor.PulseAll(m_ReadLock);
            }
            m_Server?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (m_Network == null)
                m_Network = NetworkManager.Singleton;
            if (m_Network == null) return;

            bool listening = m_Network.IsListening && m_Network.CustomMessagingManager != null;
            if (listening && !m_Registered)
            {
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(k_RequestMessage, OnRequest);
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(k_DataMessage, OnData);
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(k_SubtitleRequestMessage, OnSubtitleRequest);
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(k_SubtitleDataMessage, OnSubtitleData);
                m_Registered = true;
            }
            else if (!listening && m_Registered)
            {
                // Session ended; the messaging manager is gone with it.
                m_Registered = false;
                StopStream();
                CloseFile();
                while (m_ReadDone.TryDequeue(out _)) { }
            }

            if (m_Registered && m_Cache != null && !m_Network.IsServer && m_Network.IsConnectedClient)
                SendRequests();
            if (m_Registered && m_Network.IsServer)
                SendReadBlocks();

            UpdateRate();
        }

        // ---------------- Client ----------------

        /// <summary>Starts (or reuses) the stream of a media version and returns the URL for the video player.</summary>
        public string PrepareStream(int mediaVersion, string fileName, long size)
        {
            if (m_Cache != null && m_CacheVersion == mediaVersion && !m_Cache.IsClosed)
                return CurrentUrl;

            StopStream();
            if (m_Server == null)
                m_Server = new LocalStreamServer();

            m_Cache = new RemoteBlockCache(size, RemoteBlockCache.DefaultBlockSize, capacityBlocks: 320, readAheadBlocks: 120);
            m_CacheVersion = mediaVersion;
            m_LastBytes = 0;
            ReceiveRate = 0;
            string ext = SafeExtension(fileName);
            CurrentUrl = m_Server.Serve($"/media/{mediaVersion}/movie{ext}", m_Cache, LocalStreamServer.ContentTypeFor(fileName));
            return CurrentUrl;
        }

        public string CurrentUrl { get; private set; }

        public void StopStream()
        {
            m_Cache?.Close();
            m_Cache = null;
            m_CacheVersion = -1;
            CurrentUrl = null;
            m_Server?.StopServing();
        }

        /// <summary>Seconds of video the read-ahead window can hold at most (estimated from the average bitrate).</summary>
        public float ReadAheadSeconds(double duration)
        {
            var cache = m_Cache;
            if (cache == null || duration <= 0) return float.MaxValue;
            return (float)((double)cache.ReadAheadBlocks * cache.BlockSize / (cache.TotalSize / duration));
        }

        /// <summary>Seconds of video cached ahead of where the player reads (estimated from the average bitrate).</summary>
        public float BufferedSeconds(double duration)
        {
            var cache = m_Cache;
            if (cache == null || duration <= 0) return float.MaxValue;
            long ahead = cache.BufferedAheadOfReader();
            return ahead == long.MaxValue ? float.MaxValue : (float)(ahead / (cache.TotalSize / duration));
        }

        void SendRequests()
        {
            var requests = m_Cache.TakeRequests(Time.realtimeSinceStartupAsDouble, k_MaxInFlight, k_RetryAfter);
            foreach (int index in requests)
            {
                using (var writer = new FastBufferWriter(8, Allocator.Temp))
                {
                    writer.WriteValueSafe(m_CacheVersion);
                    writer.WriteValueSafe(index);
                    m_Network.CustomMessagingManager.SendNamedMessage(k_RequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        void OnData(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int version);
            reader.ReadValueSafe(out int index);
            reader.ReadValueSafe(out int length);
            if (m_Cache == null || version != m_CacheVersion)
                return;
            if (length < 0)
            {
                m_Cache.OnBlockFailed(index);
                return;
            }
            var data = new byte[length];
            reader.ReadBytesSafe(ref data, length);
            m_Cache.OnBlockReceived(index, data);
        }

        void UpdateRate()
        {
            if (Time.unscaledTime < m_RateSampleAt) return;
            m_RateSampleAt = Time.unscaledTime + 1f;
            long bytes = m_Cache?.BytesReceived ?? 0;
            float rate = Mathf.Max(0, bytes - m_LastBytes);
            m_LastBytes = bytes;
            ReceiveRate = Mathf.Lerp(ReceiveRate, rate, 0.5f);
        }

        /// <summary>Asks the host for the subtitles of a media version; <paramref name="callback"/> gets the bytes or null.</summary>
        public void RequestSubtitles(int mediaVersion, Action<int, byte[]> callback)
        {
            if (!m_Registered || m_Network == null || m_Network.IsServer || !m_Network.IsConnectedClient) return;
            m_SubtitleCallback = callback;
            using (var writer = new FastBufferWriter(4, Allocator.Temp))
            {
                writer.WriteValueSafe(mediaVersion);
                m_Network.CustomMessagingManager.SendNamedMessage(k_SubtitleRequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        void OnSubtitleData(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int version);
            reader.ReadValueSafe(out int length);
            byte[] data = null;
            if (length > 0 && length <= k_MaxSubtitleBytes)
            {
                data = new byte[length];
                reader.ReadBytesSafe(ref data, length);
            }
            m_SubtitleCallback?.Invoke(version, data);
        }

        void OnSubtitleRequest(ulong sender, FastBufferReader reader)
        {
            if (!m_Network.IsServer) return;
            reader.ReadValueSafe(out int version);
            byte[] data = null;
            try
            {
                data = HostSubtitleProvider?.Invoke(version);
                if (data != null && data.Length > k_MaxSubtitleBytes)
                    data = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not read subtitles for streaming: {e.Message}");
            }

            int length = data?.Length ?? 0;
            using (var writer = new FastBufferWriter(8 + length, Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(length);
                if (length > 0)
                    writer.WriteBytesSafe(data, length);
                m_Network.CustomMessagingManager.SendNamedMessage(k_SubtitleDataMessage, sender, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        // ---------------- Host ----------------

        void OnRequest(ulong sender, FastBufferReader reader)
        {
            if (!m_Network.IsServer) return;
            reader.ReadValueSafe(out int version);
            reader.ReadValueSafe(out int index);

            if (m_Hosted == null || m_Hosted.Version != version)
            {
                CloseFile();
                IRandomAccessSource source = null;
                try
                {
                    source = HostSourceProvider?.Invoke(version);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TheMovies] Could not open the movie for streaming: {e.Message}");
                }
                m_Hosted = new HostedFile { Version = version, Source = source, Failed = source == null };
            }
            if (m_Hosted.Failed)
            {
                SendBlock(sender, version, index, null);
                return;
            }

            lock (m_ReadLock)
            {
                m_ReadQueue.Enqueue((m_Hosted, sender, index));
                Monitor.Pulse(m_ReadLock);
            }
            if (m_Reader == null)
            {
                m_Reader = new Thread(ReadLoop) { IsBackground = true, Name = "TheMovies host stream reader" };
                m_Reader.Start();
            }
        }

        /// <summary>Worker thread: reads requested blocks from the hosted file.</summary>
        void ReadLoop()
        {
            while (true)
            {
                (HostedFile file, ulong client, int index) job;
                lock (m_ReadLock)
                {
                    while (m_ReadQueue.Count == 0 && !m_ReaderStop)
                        Monitor.Wait(m_ReadLock);
                    if (m_ReaderStop) return;
                    job = m_ReadQueue.Dequeue();
                }

                byte[] block = null;
                lock (job.file)
                {
                    var source = job.file.Source;
                    bool waiting = DateTime.UtcNow < job.file.RetryAfter;
                    for (int attempt = 0; attempt < 2 && !waiting && source != null && !job.file.Failed; attempt++)
                    {
                        try
                        {
                            if (!job.file.Opened)
                            {
                                source.Open();
                                job.file.Opened = true;
                            }
                            block = SourceReader.ReadBlock(source, job.index, RemoteBlockCache.DefaultBlockSize);
                            break;
                        }
                        catch (Exception e)
                        {
                            // Reconnect once (NAS connections time out while paused).
                            Debug.LogWarning($"[TheMovies] Could not read stream block {job.index}: {e.Message}");
                            try { source.Dispose(); } catch (Exception) { }
                            job.file.Opened = false;
                        }
                    }
                    if (block == null && !waiting)
                        job.file.RetryAfter = DateTime.UtcNow.AddSeconds(3);
                }
                m_ReadDone.Enqueue((job.client, job.file.Version, job.index, block));
            }
        }

        void SendReadBlocks()
        {
            while (m_ReadDone.TryDequeue(out var done))
                SendBlock(done.client, done.version, done.index, done.data);
        }

        void SendBlock(ulong client, int version, int index, byte[] block)
        {
            if (!m_Network.ConnectedClients.ContainsKey(client)) return;
            int length = block?.Length ?? -1;
            using (var writer = new FastBufferWriter(12 + Math.Max(0, length), Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(index);
                writer.WriteValueSafe(length);
                if (block != null)
                    writer.WriteBytesSafe(block, length);
                m_Network.CustomMessagingManager.SendNamedMessage(k_DataMessage, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        void CloseFile()
        {
            var hosted = m_Hosted;
            m_Hosted = null;
            if (hosted?.Source == null) return;
            hosted.Failed = true;
            // Let a running read finish first; dispose off the main thread so a slow NAS can't stall a frame.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (hosted)
                {
                    try { hosted.Source.Dispose(); }
                    catch (Exception) { }
                }
            });
        }

        static string SafeExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            foreach (char c in ext)
                if (!(char.IsLetterOrDigit(c) || c == '.'))
                    return ".mp4";
            return ext.Length > 1 && ext.Length <= 6 ? ext : ".mp4";
        }
    }
}
