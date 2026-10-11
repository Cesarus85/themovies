using System;
using System.Collections.Generic;
using TheMovies.World;
using TheMovies.XR;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>
    /// One participant in the cinema. The owner writes its name, poses and playback status;
    /// the host assigns the seat. Remote participants are shown as simple avatars.
    /// </summary>
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        const float k_PoseInterval = 1f / 20f;
        const float k_StatusInterval = 0.5f;

        // Seats are handed out from the middle outwards.
        static readonly int[] k_SeatOrder = { 1, 2, 0, 3 };

        static readonly List<NetworkPlayer> s_All = new List<NetworkPlayer>();

        public static IReadOnlyList<NetworkPlayer> All => s_All;
        public static NetworkPlayer Local { get; private set; }

        /// <summary>Raised whenever someone joins, leaves, or changes name or seat.</summary>
        public static event Action RosterChanged;

        /// <summary>Set by the app: name the local player announces.</summary>
        public static Func<string> LocalNameProvider;

        /// <summary>Set by the app: voice chat id of the local player (to match speaking indicators).</summary>
        public static Func<string> LocalVoiceIdProvider;

        /// <summary>Set by the app: called on the owner to fill in its playback status.</summary>
        public static Func<ParticipantStatus> LocalStatusProvider;

        /// <summary>Set by the app: whether a voice id is currently speaking.</summary>
        public static Func<string, bool> IsSpeaking;

        readonly NetworkVariable<FixedString64Bytes> m_Name = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<FixedString64Bytes> m_VoiceId = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<int> m_Seat = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<AvatarPose> m_Pose = new NetworkVariable<AvatarPose>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<ParticipantStatus> m_Status = new NetworkVariable<ParticipantStatus>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        AvatarView m_Avatar;
        float m_NextPoseAt;
        float m_NextStatusAt;

        public string DisplayName => m_Name.Value.Length > 0 ? m_Name.Value.ToString() : "Gast";
        public int Seat => m_Seat.Value;
        public ParticipantStatus Status => m_Status.Value;
        public bool IsHostPlayer => OwnerClientId == NetworkManager.ServerClientId;
        public Color Color => m_Seat.Value >= 0 ? CinemaEnvironment.ParticipantColors[m_Seat.Value % CinemaEnvironment.ParticipantColors.Length] : Color.gray;

        public override void OnNetworkSpawn()
        {
            s_All.Add(this);
            m_Name.OnValueChanged += OnNameChanged;
            m_Seat.OnValueChanged += OnSeatChanged;

            if (IsServer && m_Seat.Value < 0)
                m_Seat.Value = FreeSeat();

            if (IsOwner)
            {
                Local = this;
                SetName(LocalNameProvider?.Invoke() ?? "Gast");
                m_VoiceId.Value = new FixedString64Bytes(Playback.TruncateUtf8(LocalVoiceIdProvider?.Invoke() ?? string.Empty, 61));
                ApplyLocalSeat();
            }
            else
            {
                m_Avatar = AvatarView.Create(transform);
                m_Avatar.SetIdentity(DisplayName, Color);
            }

            RosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            s_All.Remove(this);
            m_Name.OnValueChanged -= OnNameChanged;
            m_Seat.OnValueChanged -= OnSeatChanged;
            if (Local == this) Local = null;
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
            RosterChanged?.Invoke();
        }

        public void SetName(string name)
        {
            if (!IsOwner) return;
            name = string.IsNullOrWhiteSpace(name) ? "Gast" : name.Trim();
            m_Name.Value = new FixedString64Bytes(Playback.TruncateUtf8(name, 61));
        }

        int FreeSeat()
        {
            foreach (int seat in k_SeatOrder)
            {
                bool taken = false;
                foreach (var p in s_All)
                    if (p != this && p.m_Seat.Value == seat)
                        taken = true;
                if (!taken) return seat;
            }
            return 0;
        }

        void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            if (m_Avatar != null) m_Avatar.SetIdentity(DisplayName, Color);
            RosterChanged?.Invoke();
        }

        void OnSeatChanged(int previous, int current)
        {
            if (IsOwner) ApplyLocalSeat();
            if (m_Avatar != null) m_Avatar.SetIdentity(DisplayName, Color);
            RosterChanged?.Invoke();
        }

        void ApplyLocalSeat()
        {
            if (m_Seat.Value < 0 || XRRig.Instance == null) return;
            var pose = CinemaEnvironment.SeatPose(m_Seat.Value);
            XRRig.Instance.SetSeat(pose.position, pose.rotation);
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                SendPose();
                SendStatus();
            }
            else if (m_Avatar != null)
            {
                var status = m_Status.Value;
                bool speaking = IsSpeaking != null && m_VoiceId.Value.Length > 0 && IsSpeaking(m_VoiceId.Value.ToString());
                m_Avatar.Apply(m_Pose.Value, speaking, status.Muted);
            }
        }

        void SendPose()
        {
            var rig = XRRig.Instance;
            if (rig == null || Time.unscaledTime < m_NextPoseAt) return;
            m_NextPoseAt = Time.unscaledTime + k_PoseInterval;

            rig.GetTrackedPoses(out var head, out var left, out var right);
            var pose = new AvatarPose
            {
                HeadPosition = head.position,
                HeadRotation = head.rotation,
                LeftPosition = left.position,
                LeftRotation = left.rotation,
                RightPosition = right.position,
                RightRotation = right.rotation,
                Flags = (byte)((rig.XRActive && rig.LeftTracked ? AvatarPose.LeftTracked : 0) |
                               (rig.XRActive && rig.RightTracked ? AvatarPose.RightTracked : 0)),
            };
            if (pose.DiffersFrom(m_Pose.Value))
                m_Pose.Value = pose;
        }

        void SendStatus()
        {
            if (LocalStatusProvider == null || Time.unscaledTime < m_NextStatusAt) return;
            m_NextStatusAt = Time.unscaledTime + k_StatusInterval;
            var status = LocalStatusProvider();
            if (!status.Equals(m_Status.Value))
                m_Status.Value = status;
        }
    }
}
