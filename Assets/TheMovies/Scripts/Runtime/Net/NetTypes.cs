using System;
using TheMovies.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>The movie everyone is watching. Participants look for their own copy by <see cref="Key"/>.</summary>
    public struct MediaDescriptor : INetworkSerializable, IEquatable<MediaDescriptor>
    {
        /// <summary>Incremented whenever another movie is selected; 0 = nothing selected.</summary>
        public int Version;

        /// <summary>File name (for files) or full URL (for streams).</summary>
        public FixedString512Bytes Key;

        public FixedString128Bytes Title;
        public bool IsUrl;

        /// <summary>File size in bytes, helps to find renamed copies. 0 for streams.</summary>
        public long Size;

        /// <summary>Length reported by the participant who selected the movie, 0 until known.</summary>
        public double Duration;

        public byte Format;
        public bool SwapEyes;

        /// <summary>The host streams its file to participants who don't have a copy.</summary>
        public bool HostStream;

        public bool IsEmpty => Version == 0;
        public StereoFormat StereoFormat => (StereoFormat)Format;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref Key);
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref IsUrl);
            serializer.SerializeValue(ref Size);
            serializer.SerializeValue(ref Duration);
            serializer.SerializeValue(ref Format);
            serializer.SerializeValue(ref SwapEyes);
            serializer.SerializeValue(ref HostStream);
        }

        public bool Equals(MediaDescriptor other) =>
            Version == other.Version && Key.Equals(other.Key) && Title.Equals(other.Title) && IsUrl == other.IsUrl &&
            Size == other.Size && Duration.Equals(other.Duration) && Format == other.Format && SwapEyes == other.SwapEyes &&
            HostStream == other.HostStream;

        public override bool Equals(object obj) => obj is MediaDescriptor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Version, Key, Size, Duration, Format, SwapEyes);
    }

    /// <summary>Network form of <see cref="PlaybackTimeline"/>.</summary>
    public struct NetTimeline : INetworkSerializable, IEquatable<NetTimeline>
    {
        public bool Playing;
        public double AnchorMediaTime;
        public double AnchorHostTime;
        public int Revision;

        public static NetTimeline From(PlaybackTimeline t) => new NetTimeline
        {
            Playing = t.Playing,
            AnchorMediaTime = t.AnchorMediaTime,
            AnchorHostTime = t.AnchorHostTime,
            Revision = t.Revision,
        };

        public PlaybackTimeline ToTimeline() => new PlaybackTimeline
        {
            Playing = Playing,
            AnchorMediaTime = AnchorMediaTime,
            AnchorHostTime = AnchorHostTime,
            Revision = Revision,
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Playing);
            serializer.SerializeValue(ref AnchorMediaTime);
            serializer.SerializeValue(ref AnchorHostTime);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(NetTimeline other) =>
            Playing == other.Playing && AnchorMediaTime.Equals(other.AnchorMediaTime) &&
            AnchorHostTime.Equals(other.AnchorHostTime) && Revision == other.Revision;

        public override bool Equals(object obj) => obj is NetTimeline other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Playing, AnchorMediaTime, AnchorHostTime, Revision);
    }

    /// <summary>
    /// Where a participant sits: the room they see (0 = cinema, 1 = living room, 2 = mixed reality – each person picks
    /// their own) and, if they chose a seat themselves, its floor position. The others show the avatar there when they
    /// are in the same room, otherwise on the participant's standard seat.
    /// </summary>
    public struct SeatChoice : INetworkSerializable, IEquatable<SeatChoice>
    {
        public byte Room;
        public bool Chosen;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Room);
            serializer.SerializeValue(ref Chosen);
            serializer.SerializeValue(ref Position);
        }

        public bool Equals(SeatChoice other) => Room == other.Room && Chosen == other.Chosen && Position == other.Position;

        public override bool Equals(object obj) => obj is SeatChoice other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Room, Chosen, Position);
    }

    /// <summary>Head and controller poses in world space.</summary>
    public struct AvatarPose : INetworkSerializable, IEquatable<AvatarPose>
    {
        public const byte LeftTracked = 1;
        public const byte RightTracked = 2;

        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        public Vector3 LeftPosition;
        public Quaternion LeftRotation;
        public Vector3 RightPosition;
        public Quaternion RightRotation;
        public byte Flags;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref HeadPosition);
            serializer.SerializeValue(ref HeadRotation);
            serializer.SerializeValue(ref LeftPosition);
            serializer.SerializeValue(ref LeftRotation);
            serializer.SerializeValue(ref RightPosition);
            serializer.SerializeValue(ref RightRotation);
            serializer.SerializeValue(ref Flags);
        }

        /// <summary>True if the pose moved enough to be worth sending.</summary>
        public bool DiffersFrom(in AvatarPose other, float meters = 0.004f, float degrees = 0.8f)
        {
            return Flags != other.Flags ||
                   (HeadPosition - other.HeadPosition).sqrMagnitude > meters * meters ||
                   (LeftPosition - other.LeftPosition).sqrMagnitude > meters * meters ||
                   (RightPosition - other.RightPosition).sqrMagnitude > meters * meters ||
                   Quaternion.Angle(HeadRotation, other.HeadRotation) > degrees ||
                   Quaternion.Angle(LeftRotation, other.LeftRotation) > degrees ||
                   Quaternion.Angle(RightRotation, other.RightRotation) > degrees;
        }

        public bool Equals(AvatarPose other) =>
            HeadPosition == other.HeadPosition && HeadRotation == other.HeadRotation &&
            LeftPosition == other.LeftPosition && LeftRotation == other.LeftRotation &&
            RightPosition == other.RightPosition && RightRotation == other.RightRotation && Flags == other.Flags;

        public override bool Equals(object obj) => obj is AvatarPose other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(HeadPosition, HeadRotation, LeftPosition, RightPosition, Flags);

        /// <summary>The same pose moved by <paramref name="offset"/> (avatar of someone in another room on their standard seat).</summary>
        public AvatarPose Moved(Vector3 offset)
        {
            var moved = this;
            moved.HeadPosition += offset;
            moved.LeftPosition += offset;
            moved.RightPosition += offset;
            return moved;
        }
    }

    public enum ParticipantMediaState : byte
    {
        NoMovie = 0,
        Missing = 1,
        Loading = 2,
        Ready = 3,
        Buffering = 4,
        Error = 5,
    }

    /// <summary>What each participant reports about their own playback, shown in everyone's menu.</summary>
    public struct ParticipantStatus : INetworkSerializable, IEquatable<ParticipantStatus>
    {
        public ParticipantMediaState Media;
        public int MediaVersion;
        public short DriftMs;
        public short RttMs;
        public bool Muted;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Media);
            serializer.SerializeValue(ref MediaVersion);
            serializer.SerializeValue(ref DriftMs);
            serializer.SerializeValue(ref RttMs);
            serializer.SerializeValue(ref Muted);
        }

        public bool Equals(ParticipantStatus other) =>
            Media == other.Media && MediaVersion == other.MediaVersion && DriftMs == other.DriftMs &&
            RttMs == other.RttMs && Muted == other.Muted;

        public override bool Equals(object obj) => obj is ParticipantStatus other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Media, MediaVersion, DriftMs, RttMs, Muted);
    }
}
