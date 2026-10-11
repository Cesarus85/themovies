using System;
using TheMovies.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>
    /// Shared playback state of a cinema session, spawned by the host. Everyone may send play/pause/seek
    /// requests; the host applies them to the timeline, which is replicated to all participants.
    /// Clients estimate the host clock with periodic pings, so each of them can compute the exact
    /// position the movie should be at.
    /// </summary>
    public sealed class CinemaSync : NetworkBehaviour, IPlaybackAuthority
    {
        public static CinemaSync Instance { get; private set; }

        readonly NetworkVariable<MediaDescriptor> m_Media = new NetworkVariable<MediaDescriptor>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<NetTimeline> m_Timeline = new NetworkVariable<NetTimeline>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<FixedString64Bytes> m_VoiceChannel = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<bool> m_WaitingForOthers = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly ClockSync m_Clock = new ClockSync();
        double m_WaitingSince = -1;
        double m_ReadySince = -1;
        float m_NextPing;
        int m_PingCount;

        static double LocalNow => Time.realtimeSinceStartupAsDouble;

        public bool IsNetworked => true;
        public MediaDescriptor Media => m_Media.Value;
        public PlaybackTimeline Timeline => m_Timeline.Value.ToTimeline();
        public double HostNow => IsServer ? LocalNow : m_Clock.ToHost(LocalNow);
        public bool ClockReady => IsServer || m_Clock.HasEstimate;
        public double RoundTrip => IsServer ? 0 : m_Clock.LastRtt;

        /// <summary>True while the host paused automatically because someone is still loading or buffering.</summary>
        public bool WaitingForOthers => m_WaitingForOthers.Value;

        /// <summary>Voice chat channel of this cinema, chosen by the host.</summary>
        public string VoiceChannel => m_VoiceChannel.Value.ToString();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            m_Clock.Reset();
            m_PingCount = 0;
            m_NextPing = 0;

            if (IsServer)
            {
                m_VoiceChannel.Value = new FixedString64Bytes("TheMovies_" + Guid.NewGuid().ToString("N").Substring(0, 16));

                // The host continues whatever it was watching alone.
                LocalAuthority.Instance.Export(out var media, out var timeline);
                if (!media.IsEmpty)
                {
                    m_Media.Value = media;
                    var now = LocalNow;
                    double position = timeline.MediaTimeAt(LocalAuthority.Instance.HostNow);
                    m_Timeline.Value = NetTimeline.From(new PlaybackTimeline
                    {
                        AnchorMediaTime = position,
                        AnchorHostTime = now,
                        Revision = 1,
                    });
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!IsSpawned)
                return;

            if (IsServer)
            {
                StopAtEnd();
                WaitForEveryone();
                return;
            }

            // Ping quickly after joining for a good first estimate, then every few seconds.
            if (Time.unscaledTime >= m_NextPing)
            {
                m_PingCount++;
                m_NextPing = Time.unscaledTime + (m_PingCount < 12 ? 0.2f : 2f);
                PingRpc(LocalNow);
            }
        }

        void StopAtEnd()
        {
            var media = m_Media.Value;
            var t = Timeline;
            if (t.Playing && media.Duration > 0 && t.MediaTimeAt(LocalNow) > media.Duration + 0.5)
                SetTimeline(t.WithSeek(LocalNow, media.Duration, 0).WithPause(LocalNow));
        }

        /// <summary>
        /// Pauses when a participant is still loading or buffering (e.g. a stream that can't keep up)
        /// and continues once everybody is ready again. Participants without the file are not waited for.
        /// </summary>
        void WaitForEveryone()
        {
            var media = m_Media.Value;
            if (media.IsEmpty) return;
            double now = LocalNow;
            var t = Timeline;

            bool someoneNotReady = false;
            foreach (var player in NetworkPlayer.All)
            {
                var status = player.Status;
                if (status.MediaVersion == media.Version &&
                    (status.Media == ParticipantMediaState.Loading || status.Media == ParticipantMediaState.Buffering))
                    someoneNotReady = true;
            }

            if (t.Playing && !t.IsPreRoll(now))
            {
                m_ReadySince = -1;
                if (!someoneNotReady)
                {
                    m_WaitingSince = -1;
                }
                else if (m_WaitingSince < 0)
                {
                    m_WaitingSince = now;
                }
                else if (now - m_WaitingSince > 1.5)
                {
                    SetTimeline(t.WithPause(now));
                    m_WaitingForOthers.Value = true;
                    m_WaitingSince = -1;
                }
            }
            else if (m_WaitingForOthers.Value && !t.Playing)
            {
                m_WaitingSince = -1;
                if (someoneNotReady)
                {
                    m_ReadySince = -1;
                }
                else if (m_ReadySince < 0)
                {
                    m_ReadySince = now;
                }
                else if (now - m_ReadySince > 1.0)
                {
                    SetTimeline(t.WithPlay(now, Playback.StartDelay));
                    m_WaitingForOthers.Value = false;
                    m_ReadySince = -1;
                }
            }
        }

        // ---------- Clock sync ----------

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void PingRpc(double clientTime, RpcParams rpcParams = default)
        {
            PongRpc(clientTime, LocalNow, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        void PongRpc(double clientTime, double hostTime, RpcParams rpcParams)
        {
            m_Clock.AddSample(clientTime, hostTime, LocalNow);
        }

        // ---------- Requests (any participant) ----------

        public void RequestPlay() => SetPlayingRpc(true);

        public void RequestPause() => SetPlayingRpc(false);

        public void RequestSeek(double mediaTime) => SeekRpc(mediaTime, false);

        public void RequestSeekRelative(double delta) => SeekRpc(delta, true);

        public void RequestSelectMedia(MediaEntry entry, StereoFormat format)
        {
            var d = Playback.Describe(entry, format, 0);
            SelectMediaRpc(d.Key, d.Title, d.IsUrl, d.Size, d.Format);
        }

        public void RequestFormat(StereoFormat format, bool swapEyes) => SetFormatRpc((byte)format, swapEyes);

        public void ReportDuration(int mediaVersion, double duration) => ReportDurationRpc(mediaVersion, duration);

        // ---------- Host side ----------

        [Rpc(SendTo.Server)]
        void SetPlayingRpc(bool play)
        {
            var t = Timeline;
            if (m_Media.Value.IsEmpty || t.Playing == play)
                return;
            m_WaitingForOthers.Value = false;
            SetTimeline(play ? t.WithPlay(LocalNow, Playback.StartDelay) : t.WithPause(LocalNow));
        }

        [Rpc(SendTo.Server)]
        void SeekRpc(double value, bool relative)
        {
            if (m_Media.Value.IsEmpty)
                return;
            var t = Timeline;
            double target = relative ? t.MediaTimeAt(LocalNow) + value : value;
            target = Math.Max(0, target);
            double duration = m_Media.Value.Duration;
            if (duration > 0)
                target = Math.Min(target, duration);
            SetTimeline(t.WithSeek(LocalNow, target, Playback.StartDelay));
        }

        [Rpc(SendTo.Server)]
        void SelectMediaRpc(FixedString512Bytes key, FixedString128Bytes title, bool isUrl, long size, byte format,
            RpcParams rpcParams = default)
        {
            m_WaitingForOthers.Value = false;
            var media = new MediaDescriptor
            {
                Version = m_Media.Value.Version + 1,
                Key = key,
                Title = title,
                IsUrl = isUrl,
                Size = size,
                Format = format,
                // Only the host can serve its file to the others.
                HostStream = !isUrl && size > 0 && rpcParams.Receive.SenderClientId == NetworkManager.ServerClientId,
            };
            m_Media.Value = media;
            SetTimeline(new PlaybackTimeline
            {
                AnchorHostTime = LocalNow,
                Revision = Timeline.Revision + 1,
            });
        }

        [Rpc(SendTo.Server)]
        void SetFormatRpc(byte format, bool swapEyes)
        {
            var media = m_Media.Value;
            if (media.IsEmpty) return;
            media.Format = format;
            media.SwapEyes = swapEyes;
            m_Media.Value = media;
        }

        [Rpc(SendTo.Server)]
        void ReportDurationRpc(int mediaVersion, double duration)
        {
            var media = m_Media.Value;
            if (media.Version != mediaVersion || duration <= 0 || media.Duration > 0)
                return;
            media.Duration = duration;
            m_Media.Value = media;
        }

        void SetTimeline(PlaybackTimeline t) => m_Timeline.Value = NetTimeline.From(t);
    }
}
