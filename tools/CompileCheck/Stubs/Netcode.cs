// API stubs mirroring Netcode for GameObjects 2.9 signatures used by this project (compile check only).
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.Netcode
{
    public interface IReaderWriter { bool IsReader { get; } bool IsWriter { get; } }

    public enum NetworkDelivery { Unreliable, UnreliableSequenced, Reliable, ReliableSequenced, ReliableFragmentedSequenced }

    public struct FastBufferWriter : IDisposable
    {
        public struct ForPrimitives { }
        public struct ForEnums { }
        public struct ForStructs { }
        public struct ForNetworkSerializable { }
        public struct ForFixedStrings { }
        public struct ForGeneric { }

        public FastBufferWriter(int size, Allocator allocator, int maxSize = -1) { }
        public int Length => 0;
        public void Dispose() { }
        public void WriteBytesSafe(byte[] value, int size = -1, int offset = 0) { }
        public void WriteValueSafe<T>(in T value, ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { }
    }

    public struct FastBufferReader : IDisposable
    {
        public void Dispose() { }
        public void ReadBytesSafe(ref byte[] value, int size, int offset = 0) { }
        public void ReadValueSafe<T>(out T value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { value = default; }
    }

    public class CustomMessagingManager
    {
        public delegate void HandleNamedMessageDelegate(ulong senderClientId, FastBufferReader messagePayload);
        public void RegisterNamedMessageHandler(string name, HandleNamedMessageDelegate callback) { }
        public void UnregisterNamedMessageHandler(string name) { }
        public void SendNamedMessage(string messageName, ulong clientId, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
    }
    public interface INetworkSerializable { void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter; }
    public interface INetworkSerializeByMemcpy { }


    public ref struct BufferSerializer<TReaderWriter> where TReaderWriter : IReaderWriter
    {
        public bool IsReader => false;
        public bool IsWriter => false;
        public void SerializeValue(ref string s, bool oneByteChars = false) { }
        public void SerializeValue(ref byte value) { }
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { }
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum { }
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy { }
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForNetworkSerializable unused = default) where T : INetworkSerializable, new() { }
        public void SerializeValue(ref Vector2 value) { }
        public void SerializeValue(ref Vector3 value) { }
        public void SerializeValue(ref Vector4 value) { }
        public void SerializeValue(ref Quaternion value) { }
        public void SerializeValue(ref Color value) { }
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForFixedStrings unused = default) where T : unmanaged, INativeList<byte>, IUTF8Bytes { }
    }

    public enum NetworkVariableReadPermission { Everyone, Owner }
    public enum NetworkVariableWritePermission { Server, Owner }

    public abstract class NetworkVariableBase
    {
        protected NetworkVariableBase(NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server) { }
    }

    public class NetworkVariable<T> : NetworkVariableBase
    {
        public delegate void OnValueChangedDelegate(T previousValue, T newValue);
        public OnValueChangedDelegate OnValueChanged;
        public NetworkVariable(T value = default, NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server) : base(readPerm, writePerm) { }
        public virtual T Value { get; set; }
    }

    public enum RpcDelivery { Reliable = 0, Unreliable }
    public enum RpcInvokePermission { Everyone = 0, Server, Owner }
    public enum SendTo { Owner, NotOwner, Server, NotServer, Me, NotMe, Everyone, ClientsAndHost, Authority, NotAuthority, SpecifiedInParams }
    public enum RpcTargetUse { Temp, Persistent }

    [AttributeUsage(AttributeTargets.Method)]
    public class RpcAttribute : Attribute
    {
        public RpcDelivery Delivery = RpcDelivery.Reliable;
        public RpcInvokePermission InvokePermission;
        public bool DeferLocal;
        public bool AllowTargetOverride;
        public RpcAttribute(SendTo target) { }
    }

    public abstract class BaseRpcTarget : IDisposable { public virtual void Dispose() { } }
    public class RpcTarget { public BaseRpcTarget Single(ulong clientId, RpcTargetUse use) => null; }
    public struct RpcSendParams { public BaseRpcTarget Target; }
    public struct RpcReceiveParams { public ulong SenderClientId; }
    public struct RpcParams
    {
        public RpcSendParams Send;
        public RpcReceiveParams Receive;
        public static implicit operator RpcParams(RpcSendParams send) => new RpcParams { Send = send };
        public static implicit operator RpcParams(BaseRpcTarget target) => new RpcParams { Send = new RpcSendParams { Target = target } };
    }

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public NetworkManager NetworkManager => null;
        public NetworkObject NetworkObject => null;
        public RpcTarget RpcTarget { get; private set; }
        public bool IsLocalPlayer { get; private set; }
        public bool IsOwner { get; internal set; }
        public bool IsServer { get; private set; }
        public bool IsClient { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsSpawned { get; internal set; }
        public ulong OwnerClientId { get; internal set; }
        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }
    }

    public sealed class NetworkObject : MonoBehaviour
    {
        public bool IsSpawned { get; internal set; }
        public ulong OwnerClientId { get; internal set; }
        public bool DontDestroyWithOwner;
        public void Spawn(bool destroyWithScene = false) { }
        public void SpawnAsPlayerObject(ulong clientId, bool destroyWithScene = false) { }
        public void Despawn(bool destroy = true) { }
    }

    public class NetworkClient { public ulong ClientId; public NetworkObject PlayerObject; }

    public abstract class NetworkTransport : MonoBehaviour { }

    [Serializable]
    public class NetworkPrefabOverride { }

    [Serializable]
    public class NetworkPrefab
    {
        public NetworkPrefabOverride Override;
        public GameObject Prefab;
    }

    public class NetworkPrefabsList : ScriptableObject
    {
        public void Add(NetworkPrefab prefab) { }
        public bool Contains(GameObject prefab) => false;
        public bool Contains(NetworkPrefab prefab) => false;
    }

    [Serializable]
    public class NetworkPrefabs { public List<NetworkPrefabsList> NetworkPrefabsLists = new List<NetworkPrefabsList>(); }

    [Serializable]
    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport = null;
        public GameObject PlayerPrefab;
        public NetworkPrefabs Prefabs = new NetworkPrefabs();
        public uint TickRate = 30;
        public bool ConnectionApproval = false;
        public byte[] ConnectionData = new byte[0];
    }

    public class NetworkManager : MonoBehaviour
    {
        public const ulong ServerClientId = 0;
        public static NetworkManager Singleton { get; private set; }
        public NetworkConfig NetworkConfig;
        public ulong LocalClientId => 0;
        public IReadOnlyDictionary<ulong, NetworkClient> ConnectedClients => null;
        public IReadOnlyList<ulong> ConnectedClientsIds => null;
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsHost => false;
        public bool IsListening => false;
        public bool IsConnectedClient => false;
        public string DisconnectReason => null;
        public RpcTarget RpcTarget => null;
        public CustomMessagingManager CustomMessagingManager { get; private set; }

        public class ConnectionApprovalResponse
        {
            public bool Approved;
            public bool CreatePlayerObject;
            public uint? PlayerPrefabHash;
            public Vector3? Position;
            public Quaternion? Rotation;
            public bool Pending;
            public string Reason;
        }

        public struct ConnectionApprovalRequest
        {
            public byte[] Payload;
            public ulong ClientNetworkId;
        }

        public Action<ConnectionApprovalRequest, ConnectionApprovalResponse> ConnectionApprovalCallback { get; set; }
        public event Action OnTransportFailure;
        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;
        public event Action OnServerStarted;
        public event Action<bool> OnServerStopped;
        public bool StartHost() => false;
        public bool StartClient() => false;
        public bool StartServer() => false;
        public void Shutdown(bool discardMessageQueue = false) { }
        public void AddNetworkPrefab(GameObject prefab) { }
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : Unity.Netcode.NetworkTransport
    {
        public bool UseWebSockets { get; set; }
        public void SetRelayServerData(string ipv4Address, ushort port, byte[] allocationIdBytes, byte[] keyBytes, byte[] connectionDataBytes, byte[] hostConnectionDataBytes = null, bool isSecure = false) { }
        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress = null) { }
        public void SetConnectionData(bool forceOverrideCommandLineArgs, string ipv4Address, ushort port, string listenAddress = null) { }
    }
}

namespace Unity.Collections
{

    public interface INativeList<T> where T : unmanaged { }
    public interface IUTF8Bytes { }

    public struct FixedString64Bytes : INativeList<byte>, IUTF8Bytes, IEquatable<FixedString64Bytes>
    {
        public const int UTF8MaxLengthInBytes = 61;
        public FixedString64Bytes(string source) { }
        public int Length => 0;
        public bool Equals(FixedString64Bytes other) => true;
        public override string ToString() => string.Empty;
        public static implicit operator FixedString64Bytes(string b) => new FixedString64Bytes(b);
    }

    public struct FixedString128Bytes : INativeList<byte>, IUTF8Bytes, IEquatable<FixedString128Bytes>
    {
        public const int UTF8MaxLengthInBytes = 125;
        public FixedString128Bytes(string source) { }
        public int Length => 0;
        public bool Equals(FixedString128Bytes other) => true;
        public override string ToString() => string.Empty;
        public static implicit operator FixedString128Bytes(string b) => new FixedString128Bytes(b);
    }

    public struct FixedString512Bytes : INativeList<byte>, IUTF8Bytes, IEquatable<FixedString512Bytes>
    {
        public const int UTF8MaxLengthInBytes = 509;
        public FixedString512Bytes(string source) { }
        public int Length => 0;
        public bool Equals(FixedString512Bytes other) => true;
        public override string ToString() => string.Empty;
        public static implicit operator FixedString512Bytes(string b) => new FixedString512Bytes(b);
    }
}
