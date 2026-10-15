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
        const string k_DepthAi = "tm.depthAi";
        const string k_Brightness = "tm.brightness";
        const string k_Contrast = "tm.contrast";
        const string k_Saturation = "tm.saturation";
        const string k_DepthSteps = "tm.depthSteps";
        const string k_SubtitleSize = "tm.subtitleSize";
        const string k_SubtitlePlacement = "tm.subtitlePlacement";
        const string k_PushToTalk = "tm.pushToTalk";
        const string k_Ducking = "tm.ducking";
        const string k_Room = "tm.room";
        const string k_RealHands = "tm.realHands";
        const string k_LightMode = "tm.lightMode";
        const string k_HouseLight = "tm.houseLight";

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

        /// <summary>2D→3D may use the AI depth model.</summary>
        public bool DepthAi;

        /// <summary>Picture of the screen (1 = unchanged).</summary>
        public float Brightness, Contrast, Saturation;

        /// <summary>3D depth for 3D movies (0 = as mastered, + farther, − nearer).</summary>
        public int DepthSteps;

        public float SubtitleSize;
        public int SubtitlePlacement;

        /// <summary>Voice chat: microphone only while a grip button is held.</summary>
        public bool PushToTalk;

        /// <summary>Movie sound while others talk: 0 = unchanged, 1 = a bit quieter, 2 = much quieter.</summary>
        public int Ducking;

        /// <summary>Room: 0 = cinema, 1 = living room (per viewer).</summary>
        public int Room;

        /// <summary>Real hands and controllers through passthrough instead of models.</summary>
        public bool RealHands;

        /// <summary>House lights: Auto (dim while playing), On, Off or Manual (<see cref="HouseLight"/>), as TheMovies.Video.LightMode.</summary>
        public int LightMode;

        /// <summary>Manual house light level, 0 (dark) … 1 (fully on).</summary>
        public float HouseLight;

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
                DepthAi = PlayerPrefs.GetInt(k_DepthAi, 1) == 1,
                Brightness = PlayerPrefs.GetFloat(k_Brightness, 1f),
                Contrast = PlayerPrefs.GetFloat(k_Contrast, 1f),
                Saturation = PlayerPrefs.GetFloat(k_Saturation, 1f),
                DepthSteps = PlayerPrefs.GetInt(k_DepthSteps, 0),
                SubtitleSize = PlayerPrefs.GetFloat(k_SubtitleSize, 1f),
                SubtitlePlacement = PlayerPrefs.GetInt(k_SubtitlePlacement, 0),
                PushToTalk = PlayerPrefs.GetInt(k_PushToTalk, 0) == 1,
                Ducking = PlayerPrefs.GetInt(k_Ducking, 1),
                Room = PlayerPrefs.GetInt(k_Room, 0),
                RealHands = PlayerPrefs.GetInt(k_RealHands, 1) == 1,
                LightMode = PlayerPrefs.GetInt(k_LightMode, 0),
                HouseLight = PlayerPrefs.GetFloat(k_HouseLight, 0.5f),
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
            PlayerPrefs.SetInt(k_DepthAi, DepthAi ? 1 : 0);
            PlayerPrefs.SetFloat(k_Brightness, Brightness);
            PlayerPrefs.SetFloat(k_Contrast, Contrast);
            PlayerPrefs.SetFloat(k_Saturation, Saturation);
            PlayerPrefs.SetInt(k_DepthSteps, DepthSteps);
            PlayerPrefs.SetFloat(k_SubtitleSize, SubtitleSize);
            PlayerPrefs.SetInt(k_SubtitlePlacement, SubtitlePlacement);
            PlayerPrefs.SetInt(k_PushToTalk, PushToTalk ? 1 : 0);
            PlayerPrefs.SetInt(k_Ducking, Ducking);
            PlayerPrefs.SetInt(k_Room, Room);
            PlayerPrefs.SetInt(k_RealHands, RealHands ? 1 : 0);
            PlayerPrefs.SetInt(k_LightMode, LightMode);
            PlayerPrefs.SetFloat(k_HouseLight, HouseLight);
            PlayerPrefs.Save();
        }
    }
}
