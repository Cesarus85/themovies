using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheMovies.Core;
using UnityEngine;

namespace TheMovies.Video
{
    /// <summary>
    /// Finds playable movies on the device: video files in the usual folders plus the stream URLs
    /// listed in "urls.txt" in the app's own Movies folder.
    /// </summary>
    public sealed class MediaLibrary
    {
        public const string PlaylistFileName = "urls.txt";
        const int k_MaxEntries = 2000;
        const int k_MaxDepth = 3;

        List<MediaEntry> m_Entries = new List<MediaEntry>();
        Task m_Scan;

        public event Action Changed;

        public IReadOnlyList<MediaEntry> Entries => m_Entries;
        public bool IsScanning => m_Scan != null && !m_Scan.IsCompleted;

        /// <summary>The app's own folder. Always readable, no permission needed; reachable via USB under Android/data/&lt;app&gt;/files/Movies.</summary>
        public static string AppMoviesFolder => Path.Combine(Application.persistentDataPath, "Movies");

        public static IEnumerable<string> SearchFolders()
        {
            yield return AppMoviesFolder;
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return "/sdcard/Movies";
            yield return "/sdcard/Download";
            yield return "/sdcard/Downloads";
            yield return "/sdcard/Videos";
            yield return "/sdcard/Oculus/Movies";
#else
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!string.IsNullOrEmpty(videos)) yield return videos;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, "Movies");
                yield return Path.Combine(home, "Videos");
                yield return Path.Combine(home, "Downloads");
            }
#endif
        }

        public static void EnsureAppFolder()
        {
            try
            {
                Directory.CreateDirectory(AppMoviesFolder);
                string playlist = Path.Combine(AppMoviesFolder, PlaylistFileName);
                if (!File.Exists(playlist))
                {
                    File.WriteAllText(playlist,
                        "# Stream-Adressen für The Movies, eine pro Zeile:\n" +
                        "#   https://server/film.mp4\n" +
                        "#   Titel | https://server/film.mp4\n" +
                        "#   Titel | https://server/film-3d.mp4 | sbs     (Formate: 2d, sbs, fsbs, ou, fou)\n");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not create {AppMoviesFolder}: {e.Message}");
            }
        }

        /// <summary>Rescans in the background; <see cref="Changed"/> fires on the main thread when done.</summary>
        public Task Refresh()
        {
            if (IsScanning) return m_Scan;
            var folders = new List<string>(SearchFolders());
            m_Scan = RefreshAsync(folders);
            return m_Scan;
        }

        async Task RefreshAsync(List<string> folders)
        {
            List<MediaEntry> found;
            try
            {
                found = await Task.Run(() => Scan(folders));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Media scan failed: {e.Message}");
                found = new List<MediaEntry>();
            }
            m_Entries = found;
            Changed?.Invoke();
        }

        static List<MediaEntry> Scan(List<string> folders)
        {
            var result = new List<MediaEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders)
            {
                string playlist = Path.Combine(folder, PlaylistFileName);
                if (File.Exists(playlist))
                {
                    try
                    {
                        foreach (var e in PlaylistParser.Parse(File.ReadAllText(playlist)))
                            if (seen.Add(e.Location))
                                result.Add(e);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[TheMovies] Could not read {playlist}: {e.Message}");
                    }
                }
            }

            foreach (string folder in folders)
                ScanFolder(folder, 0, result, seen);

            result.Sort((a, b) =>
            {
                if (a.IsUrl != b.IsUrl) return a.IsUrl ? 1 : -1;
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        static void ScanFolder(string folder, int depth, List<MediaEntry> result, HashSet<string> seen)
        {
            if (depth > k_MaxDepth || result.Count >= k_MaxEntries) return;
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (!MediaMatcher.IsVideoFile(file)) continue;
                    string full = Path.GetFullPath(file);
                    if (!seen.Add(full)) continue;
                    long size = 0;
                    try { size = new FileInfo(file).Length; } catch (IOException) { }
                    result.Add(new MediaEntry
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        Location = full,
                        Size = size,
                    });
                    if (result.Count >= k_MaxEntries) return;
                }
                foreach (string dir in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                    ScanFolder(dir, depth + 1, result, seen);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // No permission (yet) for this folder.
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Finds the local copy of a movie someone else picked.</summary>
        public MediaEntry FindMatch(string matchKey, bool isUrl, long size)
        {
            if (isUrl)
                return new MediaEntry { Title = matchKey, Location = matchKey, IsUrl = true };
            return MediaMatcher.FindBest(matchKey, size, m_Entries);
        }
    }
}
