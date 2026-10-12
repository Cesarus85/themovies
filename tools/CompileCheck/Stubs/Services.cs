// API stubs mirroring Unity Gaming Services (Core 1.x, Authentication 3.x, Multiplayer 1.x, Vivox 16.x) as used here.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Unity.Services.Core
{
    public enum ServicesInitializationState { Uninitialized, Initializing, Initialized }
    public class InitializationOptions { }
    public static class UnityServices
    {
        public static ServicesInitializationState State => default;
        public static Task InitializeAsync(InitializationOptions options = null) => Task.CompletedTask;
    }
}

namespace Unity.Services.Authentication
{
    using Unity.Services.Core;
    public static class AuthenticationExtensions
    {
        public static InitializationOptions SetProfile(this InitializationOptions options, string profile) => options;
    }
    public interface IAuthenticationService
    {
        bool IsSignedIn { get; }
        string PlayerId { get; }
        Task SignInAnonymouslyAsync(SignInOptions options = null);
    }
    public class SignInOptions { }
    public static class AuthenticationService { public static IAuthenticationService Instance => null; }
}

namespace Unity.Services.Multiplayer
{
    public class SessionOptions
    {
        public string Name { get; set; }
        public int MaxPlayers { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsLocked { get; set; }
    }
    public static class SessionOptionsExtensions
    {
        public static T WithRelayNetwork<T>(this T options, string region = null) where T : SessionOptions => options;
    }
    public class JoinSessionOptions { }
    public interface IHostSession : ISession { Task DeleteAsync(); }
    public interface ISession
    {
        string Id { get; }
        string Code { get; }
        bool IsHost { get; }
        event Action RemovedFromSession;
        event Action Deleted;
        event Action Changed;
        Task LeaveAsync();
        IHostSession AsHost();
    }
    public interface IMultiplayerService
    {
        Task<ISession> CreateSessionAsync(SessionOptions options);
        Task<ISession> JoinSessionByCodeAsync(string sessionCode, JoinSessionOptions options = default);
    }
    public static class MultiplayerService { public static IMultiplayerService Instance => null; }
}

namespace Unity.Services.Vivox
{
    public enum ChatCapability { TextOnly, AudioOnly, TextAndAudio }
    public class LoginOptions { public string DisplayName { get; set; } }
    public class ChannelOptions { }
    public class VivoxParticipant
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public bool SpeechDetected { get; }
        public bool IsSelf { get; }
    }
    public interface IVivoxService
    {
        bool IsLoggedIn { get; }
        ReadOnlyDictionary<string, ReadOnlyCollection<VivoxParticipant>> ActiveChannels { get; }
        Task InitializeAsync(Unity.Services.Core.InitializationOptions options = null);
        Task LoginAsync(LoginOptions options = null);
        Task LogoutAsync();
        Task JoinGroupChannelAsync(string channelName, ChatCapability chatCapability, ChannelOptions channelOptions = null);
        Task LeaveAllChannelsAsync();
        void MuteInputDevice();
        void UnmuteInputDevice();
    }
    public static class VivoxService { public static IVivoxService Instance => null; }
}
