using System;
using System.Threading.Tasks;
using UnityEngine;
#if THEMOVIES_VIVOX
using Unity.Services.Vivox;
#endif

namespace TheMovies.Net
{
    /// <summary>
    /// Optional voice chat via Unity Vivox. Compiled in only when the Vivox package is installed
    /// (define THEMOVIES_VIVOX comes from the assembly definition). Everybody in a cinema shares one channel.
    /// </summary>
    public static class VoiceChat
    {
        static string s_Channel;
        static bool s_Busy;
        static bool s_Muted;

        public static event Action Changed;

        public static bool Compiled
        {
            get
            {
#if THEMOVIES_VIVOX
                return true;
#else
                return false;
#endif
            }
        }

        public static bool InChannel => s_Channel != null;
        public static bool Muted => s_Muted;
        public static string Status { get; private set; } = Compiled ? "Sprachchat bereit" : "Sprachchat nicht installiert";

        public static async Task Join(string channel, string displayName)
        {
            if (!Compiled || string.IsNullOrEmpty(channel) || s_Busy || s_Channel == channel) return;
            s_Busy = true;
            try
            {
                if (s_Channel != null)
                    await LeaveInternal();
                if (!await SessionManager.TrySignIn())
                {
                    SetStatus("Sprachchat: Unity Services nicht verfügbar");
                    return;
                }
#if THEMOVIES_VIVOX
                await VivoxService.Instance.InitializeAsync();
                if (!VivoxService.Instance.IsLoggedIn)
                    await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = displayName });
                await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                s_Channel = channel;
                ApplyMute();
                SetStatus("Sprachchat verbunden");
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Voice chat failed: {e.Message}");
                SetStatus("Sprachchat fehlgeschlagen");
            }
            finally
            {
                s_Busy = false;
            }
        }

        public static async Task Leave()
        {
            if (!Compiled || s_Channel == null || s_Busy) return;
            s_Busy = true;
            try
            {
                await LeaveInternal();
                SetStatus("Sprachchat getrennt");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Leaving voice chat failed: {e.Message}");
            }
            finally
            {
                s_Busy = false;
            }
        }

        static async Task LeaveInternal()
        {
            s_Channel = null;
#if THEMOVIES_VIVOX
            await VivoxService.Instance.LeaveAllChannelsAsync();
#else
            await Task.Yield();
#endif
        }

        public static void SetMuted(bool muted)
        {
            s_Muted = muted;
            ApplyMute();
            Changed?.Invoke();
        }

        static void ApplyMute()
        {
#if THEMOVIES_VIVOX
            if (s_Channel == null) return;
            try
            {
                if (s_Muted) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Mute failed: {e.Message}");
            }
#endif
        }

        /// <summary>True if the participant with this Unity Services player id is talking right now.</summary>
        public static bool IsSpeaking(string playerId)
        {
#if THEMOVIES_VIVOX
            if (s_Channel == null || string.IsNullOrEmpty(playerId)) return false;
            try
            {
                if (VivoxService.Instance.ActiveChannels.TryGetValue(s_Channel, out var participants))
                {
                    foreach (var p in participants)
                        if (p.PlayerId == playerId)
                            return p.SpeechDetected;
                }
            }
            catch (Exception)
            {
                // Channel is shutting down.
            }
#endif
            return false;
        }

        static void SetStatus(string status)
        {
            Status = status;
            Changed?.Invoke();
        }
    }
}
