using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheMovies.Core;
using TheMovies.Core.Network;
using TheMovies.Video;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>
    /// Loads movie posters for the tile view (from media servers, from next to the movie on the NAS or on this
    /// device), scales them down on the GPU (with mipmaps, so they stay calm in VR) and keeps the last ones.
    /// </summary>
    public sealed class PosterCache : MonoBehaviour
    {
        const int k_MaxTextures = 90;
        const int k_PosterHeight = 384;
        const int k_MaxLocalBytes = 8 * 1024 * 1024;

        static PosterCache s_Instance;

        readonly Dictionary<string, Texture> m_Textures = new Dictionary<string, Texture>();
        readonly LinkedList<string> m_Order = new LinkedList<string>();
        readonly HashSet<string> m_Pending = new HashSet<string>();
        readonly HashSet<string> m_Missing = new HashSet<string>();
        readonly Queue<Action> m_MainThread = new Queue<Action>();

        /// <summary>A poster finished loading (or turned out to be missing): the menu redraws its tiles.</summary>
        public event Action Updated;

        public static PosterCache Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("Poster Cache");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<PosterCache>();
                }
                return s_Instance;
            }
        }

        // ---------------- Keys ----------------

        public static string KeyFor(NetworkItem item, NetworkSource source) =>
            source == null || item == null ? null : source.Key + "|" + (item.IsFolder ? "dir:" + item.Location : item.Location);

        public static string KeyFor(MediaEntry entry)
        {
            if (entry == null) return null;
            if (entry.IsNetwork) return entry.SourceId + "|" + entry.Location;
            return entry.IsUrl ? null : "local|" + entry.Location;
        }

        // ---------------- Lookup ----------------

        /// <summary>The poster if loaded; starts loading it otherwise (for an item of the source being browsed).</summary>
        public Texture Get(NetworkItem item, NetworkLibrary library)
        {
            var source = library?.Current;
            string key = KeyFor(item, source);
            if (key == null) return null;
            if (TryGet(key, out var texture) || m_Pending.Contains(key) || m_Missing.Contains(key)) return texture;

            bool worthAsking = item.Poster != string.Empty &&
                               (item.Poster != null || item.Nfo != null ||
                                (item.IsFolder && (source.Kind == NetworkSourceKind.Smb || source.Kind == NetworkSourceKind.WebDav)));
            if (!worthAsking)
            {
                m_Missing.Add(key);
                return null;
            }
            m_Pending.Add(key);
            library.LoadArtwork(item, data => Finish(key, data));
            return null;
        }

        /// <summary>The poster of a remembered movie (recently watched), loading it if needed.</summary>
        public Texture Get(MediaEntry entry)
        {
            string key = KeyFor(entry);
            if (key == null) return null;
            if (TryGet(key, out var texture) || m_Pending.Contains(key) || m_Missing.Contains(key)) return texture;
            m_Pending.Add(key);
            if (entry.IsNetwork)
            {
                var library = NetworkLibrary.Instance;
                if (library == null) Finish(key, null);
                else library.LoadPoster(entry, data => Finish(key, data));
                return null;
            }
            string path = entry.Location;
            string poster = entry.Poster;
            Task.Run(() =>
            {
                byte[] data = null;
                try
                {
                    data = ReadLocalPoster(path, poster);
                }
                catch (Exception e)
                {
                    Debug.Log($"[TheMovies] No poster for {path}: {e.Message}");
                }
                lock (m_MainThread) m_MainThread.Enqueue(() => Finish(key, data));
            });
            return null;
        }

        /// <summary>A poster next to a movie on this device ("Film.jpg", "Film-poster.jpg", "poster.jpg" …).</summary>
        static byte[] ReadLocalPoster(string moviePath, string known)
        {
            string picture = !string.IsNullOrEmpty(known) && File.Exists(known) ? known : null;
            if (picture == null)
            {
                string folder = Path.GetDirectoryName(moviePath);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                var items = new List<NetworkItem>();
                foreach (string file in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    if (Artwork.IsImageFile(name) || MediaMatcher.IsVideoFile(name))
                        items.Add(new NetworkItem { Name = name, Location = file });
                }
                Artwork.AttachSidecars(items);
                string movieName = Path.GetFileName(moviePath);
                picture = items.Find(i => string.Equals(i.Name, movieName, StringComparison.OrdinalIgnoreCase))?.Poster;
            }
            if (picture == null || new FileInfo(picture).Length > k_MaxLocalBytes) return null;
            return File.ReadAllBytes(picture);
        }

        bool TryGet(string key, out Texture texture)
        {
            if (!m_Textures.TryGetValue(key, out texture)) return false;
            m_Order.Remove(key);
            m_Order.AddFirst(key);
            return true;
        }

        // ---------------- Decoding ----------------

        void Update()
        {
            while (true)
            {
                Action action;
                lock (m_MainThread)
                {
                    if (m_MainThread.Count == 0) break;
                    action = m_MainThread.Dequeue();
                }
                action();
            }
        }

        void Finish(string key, byte[] data)
        {
            m_Pending.Remove(key);
            var texture = data != null ? Decode(data) : null;
            if (texture == null)
            {
                m_Missing.Add(key);
            }
            else
            {
                m_Textures[key] = texture;
                m_Order.AddFirst(key);
                while (m_Order.Count > k_MaxTextures)
                {
                    string oldest = m_Order.Last.Value;
                    m_Order.RemoveLast();
                    if (m_Textures.TryGetValue(oldest, out var old)) Destroy(old);
                    m_Textures.Remove(oldest);
                }
            }
            Updated?.Invoke();
        }

        /// <summary>JPEG/PNG → small texture with mipmaps (halving steps keep the downscaling smooth).</summary>
        static Texture Decode(byte[] data)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(data, false) || source.width < 8 || source.height < 8)
            {
                Destroy(source);
                return null;
            }
            int height = Mathf.Min(k_PosterHeight, source.height);
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * (height / (float)source.height)));

            Texture current = source;
            RenderTexture temporary = null;
            while (current.height > height * 2)
            {
                var half = RenderTexture.GetTemporary(Mathf.Max(width, current.width / 2), current.height / 2, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                half.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, half);
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                temporary = half;
                current = half;
            }

            var result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 2,
                name = "Poster",
            };
            result.Create();
            Graphics.Blit(current, result);
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            Destroy(source);
            return result;
        }

        /// <summary>Forgets that posters were missing (after a source was reloaded).</summary>
        public void RetryMissing() => m_Missing.Clear();

        void OnDestroy()
        {
            foreach (var t in m_Textures.Values) Destroy(t);
            m_Textures.Clear();
            if (s_Instance == this) s_Instance = null;
        }
    }
}
