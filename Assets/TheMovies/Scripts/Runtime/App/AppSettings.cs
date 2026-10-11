using UnityEngine;

namespace TheMovies.App
{
    /// <summary>Per-device preferences, stored in PlayerPrefs.</summary>
    public sealed class AppSettings
    {
        const string k_Name = "tm.name";
        const string k_ScreenSize = "tm.screenSize";
        const string k_Volume = "tm.volume";
        const string k_Muted = "tm.muted";
        const string k_LastRoom = "tm.lastRoom";
        const string k_LastHost = "tm.lastHost";
        const string k_LightsOnWhenPaused = "tm.lightsPaused";
        const string k_Height = "tm.height";
        const string k_Subtitles = "tm.subtitles";
        const string k_MixedReality = "tm.mixedReality";
        const string k_AudioDelay = "tm.audioDelay";
        const string k_Convert3D = "tm.convert3d";
        const string k_ScreenGlow = "tm.screenGlow";

        public string Name;
        public int ScreenSize;
        public float Volume;
        public bool MicMuted;
        public string LastRoomCode;
        public string LastLanHost;
        public bool LightsOnWhenPaused;
        public float HeightOffset;
        public bool Subtitles;
        public bool MixedReality;
        public int AudioDelayMs;

        /// <summary>2D→3D: 0 = off, 1 = light, 2 = strong.</summary>
        public int Convert3D;

        /// <summary>Light around the screen in mixed reality: 0 = off, 1 = soft, 2 = strong.</summary>
        public int ScreenGlow;

        public static AppSettings Load()
        {
            return new AppSettings
            {
                Name = PlayerPrefs.GetString(k_Name, "Gast " + Random.Range(100, 1000)),
                ScreenSize = PlayerPrefs.GetInt(k_ScreenSize, 2),
                Volume = PlayerPrefs.GetFloat(k_Volume, 0.8f),
                MicMuted = PlayerPrefs.GetInt(k_Muted, 0) == 1,
                LastRoomCode = PlayerPrefs.GetString(k_LastRoom, string.Empty),
                LastLanHost = PlayerPrefs.GetString(k_LastHost, "192.168."),
                LightsOnWhenPaused = PlayerPrefs.GetInt(k_LightsOnWhenPaused, 1) == 1,
                HeightOffset = PlayerPrefs.GetFloat(k_Height, 0f),
                Subtitles = PlayerPrefs.GetInt(k_Subtitles, 1) == 1,
                MixedReality = PlayerPrefs.GetInt(k_MixedReality, 0) == 1,
                AudioDelayMs = PlayerPrefs.GetInt(k_AudioDelay, 0),
                Convert3D = PlayerPrefs.GetInt(k_Convert3D, 0),
                ScreenGlow = PlayerPrefs.GetInt(k_ScreenGlow, 1),
            };
        }

        public void Save()
        {
            PlayerPrefs.SetString(k_Name, Name ?? string.Empty);
            PlayerPrefs.SetInt(k_ScreenSize, ScreenSize);
            PlayerPrefs.SetFloat(k_Volume, Volume);
            PlayerPrefs.SetInt(k_Muted, MicMuted ? 1 : 0);
            PlayerPrefs.SetString(k_LastRoom, LastRoomCode ?? string.Empty);
            PlayerPrefs.SetString(k_LastHost, LastLanHost ?? string.Empty);
            PlayerPrefs.SetInt(k_LightsOnWhenPaused, LightsOnWhenPaused ? 1 : 0);
            PlayerPrefs.SetFloat(k_Height, HeightOffset);
            PlayerPrefs.SetInt(k_Subtitles, Subtitles ? 1 : 0);
            PlayerPrefs.SetInt(k_MixedReality, MixedReality ? 1 : 0);
            PlayerPrefs.SetInt(k_AudioDelay, AudioDelayMs);
            PlayerPrefs.SetInt(k_Convert3D, Convert3D);
            PlayerPrefs.SetInt(k_ScreenGlow, ScreenGlow);
            PlayerPrefs.Save();
        }
    }
}
