using System;
using System.IO;
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
    /// </summary>
    public sealed class HostStreamService : MonoBehaviour
    {
        const string k_RequestMessage = "TheMovies.StreamRequest";
        const string k_DataMessage = "TheMovies.StreamData";
        const int k_MaxInFlight = 6;
        const double k_RetryAfter = 10.0;

        public static HostStreamService Instance { get; private set; }

        /// <summary>Host side: returns the local file path for a media version, or null if it must not be served.</summary>
        public static Func<int, string> HostFileProvider;

        NetworkManager m_Network;
        bool m_Registered;

        // Host
        FileStream m_File;
        string m_FilePath;

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
                m_Registered = true;
            }
            else if (!listening && m_Registered)
            {
                // Session ended; the messaging manager is gone with it.
                m_Registered = false;
                StopStream();
                CloseFile();
            }

            if (m_Registered && m_Cache != null && !m_Network.IsServer && m_Network.IsConnectedClient)
                SendRequests();

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

            m_Cache = new RemoteBlockCache(size, RemoteBlockCache.DefaultBlockSize, capacityBlocks: 384, readAheadBlocks: 40);
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

        /// <summary>Seconds of video buffered ahead of the given position (estimated from the average bitrate).</summary>
        public float BufferedSeconds(double mediaTime, double duration)
        {
            var cache = m_Cache;
            if (cache == null || duration <= 0) return 0;
            double bytesPerSecond = cache.TotalSize / duration;
            long position = (long)(cache.TotalSize * Math.Min(1.0, Math.Max(0.0, mediaTime / duration)));
            int index = cache.BlockIndexOf(position);
            long ahead = (long)cache.ContiguousBlocksFrom(index) * cache.BlockSize - (position - (long)index * cache.BlockSize);
            if (index + cache.ContiguousBlocksFrom(index) >= cache.BlockCount)
                return float.MaxValue; // everything up to the end is there
            return (float)(Math.Max(0, ahead) / bytesPerSecond);
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

        // ---------------- Host ----------------

        void OnRequest(ulong sender, FastBufferReader reader)
        {
            if (!m_Network.IsServer) return;
            reader.ReadValueSafe(out int version);
            reader.ReadValueSafe(out int index);

            byte[] block = ReadBlock(version, index);
            int length = block?.Length ?? -1;
            using (var writer = new FastBufferWriter(12 + Math.Max(0, length), Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(index);
                writer.WriteValueSafe(length);
                if (block != null)
                    writer.WriteBytesSafe(block, length);
                m_Network.CustomMessagingManager.SendNamedMessage(k_DataMessage, sender, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        byte[] ReadBlock(int version, int index)
        {
            string path = HostFileProvider?.Invoke(version);
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (m_File == null || m_FilePath != path)
                {
                    CloseFile();
                    m_File = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
                    m_FilePath = path;
                }
                long offset = (long)index * RemoteBlockCache.DefaultBlockSize;
                if (index < 0 || offset >= m_File.Length) return null;
                int length = (int)Math.Min(RemoteBlockCache.DefaultBlockSize, m_File.Length - offset);
                var block = new byte[length];
                m_File.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < length)
                {
                    int n = m_File.Read(block, read, length - read);
                    if (n <= 0) return null;
                    read += n;
                }
                return block;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not read stream block {index}: {e.Message}");
                CloseFile();
                return null;
            }
        }

        void CloseFile()
        {
            m_File?.Dispose();
            m_File = null;
            m_FilePath = null;
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
