using UnityEditor;

namespace TheMovies.EditorTools
{
    /// <summary>Build options of the own video player (menu "The Movies").</summary>
    public static class PlayerBuildOptions
    {
        const string k_FfmpegMenu = "The Movies/Player: AC3-, DTS- und TrueHD-Ton (FFmpeg)";
        const string k_FfmpegKey = "TheMovies.FfmpegAudio";

        /// <summary>
        /// Include Jellyfin's FFmpeg audio decoder (org.jellyfin.media3:media3-ffmpeg-decoder): movies with AC3,
        /// E-AC3, DTS or TrueHD sound play with sound. Switch off if Gradle can't download it.
        /// </summary>
        public static bool FfmpegAudio
        {
            get => EditorPrefs.GetBool(k_FfmpegKey, true);
            set => EditorPrefs.SetBool(k_FfmpegKey, value);
        }

        [MenuItem(k_FfmpegMenu, priority = 33)]
        static void ToggleFfmpeg() => FfmpegAudio = !FfmpegAudio;

        [MenuItem(k_FfmpegMenu, true)]
        static bool ToggleFfmpegValidate()
        {
            Menu.SetChecked(k_FfmpegMenu, FfmpegAudio);
            return true;
        }
    }
}
