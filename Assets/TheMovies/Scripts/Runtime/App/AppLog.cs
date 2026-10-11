using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using TheMovies.Core;
using UnityEngine;

namespace TheMovies.App
{
    /// <summary>
    /// The app's own log: everything Unity logs (also from other threads) goes into a ring buffer for the menu
    /// page "Protokoll" and into Android/data/com.themovies.cinema/files/themovies-log.txt. The log of the
    /// previous run is kept as themovies-log-previous.txt, so a crash or freeze can be looked at after a restart.
    /// </summary>
    public static class AppLog
    {
        const long k_MaxFileBytes = 2 * 1024 * 1024;

        public static readonly LogBuffer Buffer = new LogBuffer(3000);
        public static readonly LogBuffer Previous = new LogBuffer(3000);

        static readonly ConcurrentQueue<string> s_Pending = new ConcurrentQueue<string>();
        static readonly AutoResetEvent s_Signal = new AutoResetEvent(false);
        static string s_Path;
        static bool s_Started;

        public static string FilePath => s_Path;

        public static void Start()
        {
            if (s_Started) return;
            s_Started = true;
            try
            {
                string dir = Application.persistentDataPath;
                s_Path = Path.Combine(dir, "themovies-log.txt");
                string previous = Path.Combine(dir, "themovies-log-previous.txt");
                if (File.Exists(s_Path))
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(s_Path, previous);
                    LoadPrevious(previous);
                }
                new Thread(WriteLoop) { IsBackground = true, Name = "TheMovies log writer" }.Start();
            }
            catch (Exception e)
            {
                s_Path = null;
                Debug.LogWarning("[TheMovies] Log file unavailable: " + e.Message);
            }

            Application.logMessageReceivedThreaded += OnLog;
            Debug.Log($"[TheMovies] Start {Application.version} · {SystemInfo.deviceModel} · {SystemInfo.operatingSystem} · " +
                      $"{SystemInfo.graphicsDeviceType} · Unity {Application.unityVersion}");
        }

        static void LoadPrevious(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - 3000);
                for (int i = start; i < lines.Length; i++)
                {
                    string line = lines[i];
                    var level = line.Length > 9 && line[9] == 'F' ? LogLevel.Error : line.Length > 9 && line[9] == 'W' ? LogLevel.Warning : LogLevel.Info;
                    Previous.Add(level, line.Length > 11 ? line.Substring(11) : line, DateTime.MinValue);
                }
            }
            catch (Exception)
            {
                // Unreadable: ignore
            }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            var level = type == LogType.Log ? LogLevel.Info : type == LogType.Warning ? LogLevel.Warning : LogLevel.Error;
            string text = condition;
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && !string.IsNullOrEmpty(stackTrace))
                text += "\n" + stackTrace;
            var now = DateTime.Now;
            Buffer.Add(level, text, now);
            if (s_Path == null) return;
            var entry = new LogBuffer.Entry { Time = now, Level = level, Text = text.Replace("\n", "\n           ") };
            s_Pending.Enqueue(entry.ToString());
            s_Signal.Set();
        }

        static void WriteLoop()
        {
            var sb = new StringBuilder();
            while (true)
            {
                s_Signal.WaitOne(1000);
                sb.Clear();
                while (s_Pending.TryDequeue(out string line)) sb.Append(line).Append('\n');
                if (sb.Length == 0) continue;
                try
                {
                    var info = new FileInfo(s_Path);
                    if (info.Exists && info.Length > k_MaxFileBytes)
                    {
                        // Keep the second half so the beginning of a problem is not lost entirely.
                        string text = File.ReadAllText(s_Path);
                        File.WriteAllText(s_Path, "…\n" + text.Substring(text.Length / 2));
                    }
                    File.AppendAllText(s_Path, sb.ToString());
                }
                catch (Exception)
                {
                    // Storage full or similar: keep running without the file
                }
            }
        }
    }
}
