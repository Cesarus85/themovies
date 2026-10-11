using System;
using System.Collections;
using System.IO;
using System.Linq;
using TheMovies.Core;
using TheMovies.UI;
using TheMovies.World;
using UnityEngine;
using UnityEngine.Networking;

namespace TheMovies.Video
{
    /// <summary>
    /// Shows .srt subtitles in the lower part of the screen. Each viewer can switch them on or off.
    /// Sources: a sidecar file next to the movie ("Film.srt", "Film.de.srt"), the same URL with .srt for
    /// streams, or the host's subtitle file when the movie is streamed from the host.
    /// </summary>
    public sealed class SubtitleService : MonoBehaviour
    {
        const int k_MaxLineChars = 42;
        const int k_MaxBytes = 2 * 1024 * 1024;

        public static SubtitleService Instance { get; private set; }

        ScreenController m_Screen;
        Func<double> m_TimeProvider;
        SubtitleTrack m_Track;
        TextMesh m_Text;
        MeshRenderer m_Background;
        string m_Shown;
        int m_LoadToken;

        public bool Enabled { get; set; } = true;
        public bool HasTrack => m_Track != null && m_Track.Count > 0;

        /// <summary>Name of the loaded subtitle source for the menu.</summary>
        public string TrackName { get; private set; } = string.Empty;

        public static SubtitleService Create(ScreenController screen, Func<double> timeProvider)
        {
            var go = new GameObject("Subtitles");
            go.transform.SetParent(screen.transform, false);
            var service = go.AddComponent<SubtitleService>();
            service.m_Screen = screen;
            service.m_TimeProvider = timeProvider;
            service.Build();
            return service;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Build()
        {
            m_Background = UIFactory.CreateQuad(transform, "Background", new Vector3(0, 0, 0.01f), Vector2.one,
                new Color(0f, 0f, 0f, 0.55f), UIFactory.PanelMaterial);
            m_Text = UIFactory.CreateText(transform, string.Empty, Vector3.zero, 1f, TextAnchor.MiddleCenter, Color.white);
            m_Background.gameObject.SetActive(false);
            m_Text.gameObject.SetActive(false);
        }

        public void Clear()
        {
            m_LoadToken++;
            m_Track = null;
            TrackName = string.Empty;
            Show(null);
        }

        public void LoadForFile(string videoPath)
        {
            Clear();
            string path = FindSidecar(videoPath);
            if (path == null) return;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > k_MaxBytes) return;
                SetTrack(File.ReadAllBytes(path), Path.GetFileName(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not read subtitles {path}: {e.Message}");
            }
        }

        /// <summary>Tries "movie.srt" next to a stream URL.</summary>
        public void LoadForUrl(string url)
        {
            Clear();
            string srtUrl = SubtitleUrlFor(url);
            if (srtUrl != null)
                StartCoroutine(Download(srtUrl, m_LoadToken));
        }

        public void SetTrack(byte[] data, string name)
        {
            if (data == null || data.Length == 0) return;
            var track = SrtParser.Parse(SrtParser.Decode(data));
            if (track.Count == 0) return;
            m_Track = track;
            TrackName = name ?? string.Empty;
            m_Shown = null;
        }

        IEnumerator Download(string url, int token)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (token != m_LoadToken) yield break;
                if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.data != null &&
                    request.downloadHandler.data.Length <= k_MaxBytes)
                    SetTrack(request.downloadHandler.data, Path.GetFileName(new Uri(url).AbsolutePath));
            }
        }

        void LateUpdate()
        {
            string text = null;
            if (Enabled && m_Track != null && m_TimeProvider != null)
                text = m_Track.TextAt(m_TimeProvider());
            if (text != m_Shown)
                Show(text);
        }

        void Show(string text)
        {
            m_Shown = text;
            bool visible = !string.IsNullOrEmpty(text);
            m_Text.gameObject.SetActive(visible);
            m_Background.gameObject.SetActive(visible);
            if (!visible) return;

            string wrapped = SrtParser.Wrap(text, k_MaxLineChars);
            string[] lines = wrapped.Split('\n');
            int longest = lines.Max(l => l.Length);

            // Size relative to the picture: about 5 % of its height per line, in the lower tenth.
            Vector2 screen = m_Screen.Size;
            float lineHeight = Mathf.Clamp(screen.y * 0.05f, 0.08f, 0.5f);
            m_Text.characterSize = lineHeight / (m_Text.fontSize * 0.1f);
            m_Text.text = wrapped;

            float width = longest * lineHeight * 0.52f + lineHeight;
            float height = lines.Length * lineHeight * 1.12f + lineHeight * 0.4f;
            float centerY = screen.y * 0.06f + height * 0.5f;
            transform.localPosition = new Vector3(0, centerY, -0.12f);
            m_Background.transform.localScale = new Vector3(width, height, 1f);
        }

        /// <summary>Finds "Movie.srt", or "Movie.de.srt" etc. (German preferred) next to the video.</summary>
        public static string FindSidecar(string videoPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(videoPath);
                string name = Path.GetFileNameWithoutExtension(videoPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) || !Directory.Exists(dir)) return null;
                string exact = Path.Combine(dir, name + ".srt");
                if (File.Exists(exact)) return exact;
                var candidates = Directory.GetFiles(dir, "*.srt")
                    .Where(f => Path.GetFileName(f).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => GermanRank(Path.GetFileName(f)))
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return candidates.Count > 0 ? candidates[0] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static int GermanRank(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            if (lower.Contains(".forced.")) return 3;
            if (lower.Contains(".de.") || lower.Contains(".deu.") || lower.Contains(".ger.") || lower.Contains("german")) return 0;
            if (lower.Contains(".en.") || lower.Contains(".eng.")) return 1;
            return 2;
        }

        static string SubtitleUrlFor(string url)
        {
            try
            {
                var uri = new UriBuilder(url);
                string path = uri.Path;
                int dot = path.LastIndexOf('.');
                int slash = path.LastIndexOf('/');
                if (dot <= slash) return null;
                uri.Path = path.Substring(0, dot) + ".srt";
                return uri.Uri.ToString();
            }
            catch (UriFormatException)
            {
                return null;
            }
        }
    }
}
