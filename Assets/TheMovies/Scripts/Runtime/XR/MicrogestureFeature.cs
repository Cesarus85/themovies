#if THEMOVIES_OPENXR
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace TheMovies.XR
{
    /// <summary>
    /// Meta's thumb microgestures (XR_META_hand_tracking_microgestures): with a loose fist, swiping the thumb left or
    /// right along the index finger. The extension adds these as inputs of the hand interaction profile
    /// (/interaction_profiles/ext/hand_interaction_ext). Unity's input system doesn't know them, so this feature has its
    /// own small action set: its bindings are appended to Unity's suggestion for that profile (one call per profile is
    /// allowed – a second one would replace Unity's), the set is attached with Unity's and synced with every
    /// xrSyncActions. Enable "The Movies: Mikrogesten" under OpenXR → Android (done automatically).
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "The Movies: Mikrogesten",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "The Movies",
        Desc = "Daumen-Wischgesten (Faust, Daumen nach links/rechts) zum Drehen (XR_META_hand_tracking_microgestures).",
        OpenxrExtensionStrings = Extension,
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public sealed class MicrogestureFeature : OpenXRFeature
    {
        public const string FeatureId = "com.themovies.microgestures";
        const string Extension = "XR_META_hand_tracking_microgestures";
        const string Profile = "/interaction_profiles/ext/hand_interaction_ext";

        const int XR_TYPE_ACTION_STATE_BOOLEAN = 23;
        const int XR_TYPE_ACTION_SET_CREATE_INFO = 28;
        const int XR_TYPE_ACTION_CREATE_INFO = 29;
        const int XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING = 51;
        const int XR_TYPE_ACTION_STATE_GET_INFO = 58;
        const int XR_ACTION_TYPE_BOOLEAN_INPUT = 1;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int SuggestFn(ulong instance, IntPtr suggestedBindings);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int SessionInfoFn(ulong session, IntPtr info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StringToPathFn(ulong instance, IntPtr pathString, out ulong path);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CreateActionSetFn(ulong instance, IntPtr createInfo, out ulong actionSet);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CreateActionFn(ulong actionSet, IntPtr createInfo, out ulong action);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetActionStateBooleanFn(ulong session, IntPtr getInfo, IntPtr state);

        static GetInstanceProcAddrFn s_NextGetProcAddr;
        static readonly GetInstanceProcAddrFn s_GetProcAddrHook = GetInstanceProcAddrHook;
        static readonly SuggestFn s_SuggestHook = SuggestHook;
        static readonly SessionInfoFn s_AttachHook = AttachHook;
        static readonly SessionInfoFn s_SyncHook = SyncHook;
        static SuggestFn s_NextSuggest;
        static SessionInfoFn s_NextAttach, s_NextSync;
        static GetActionStateBooleanFn s_GetState;

        static ulong s_Instance, s_ProfilePath, s_ActionSet;
        static readonly ulong[] s_HandPaths = new ulong[2]; // left, right
        static ulong s_SwipeLeft, s_SwipeRight;
        static readonly ulong[] s_Bindings = new ulong[4]; // left hand: swipe left, swipe right; right hand: same
        static volatile bool s_Suggested, s_Attached;
        static IntPtr s_GetInfo, s_State;

        static int s_Turns; // net steps: +1 per swipe right, −1 per swipe left (written on the render/input thread)
        static int s_Detected;

        /// <summary>For the status line.</summary>
        public static string Status { get; private set; } = "nicht gestartet";

        public static bool Active => s_Attached;

        /// <summary>Thumb swipes since the last call: positive = to the right, negative = to the left.</summary>
        public static int TakeTurnSteps() => Interlocked.Exchange(ref s_Turns, 0);

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            s_NextGetProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(func);
            return Marshal.GetFunctionPointerForDelegate(s_GetProcAddrHook);
        }

        [AOT.MonoPInvokeCallback(typeof(GetInstanceProcAddrFn))]
        static int GetInstanceProcAddrHook(ulong instance, IntPtr name, IntPtr function)
        {
            int result = s_NextGetProcAddr(instance, name, function);
            if (result < 0 || function == IntPtr.Zero) return result;
            IntPtr original = Marshal.ReadIntPtr(function);
            if (original == IntPtr.Zero) return result;
            switch (Marshal.PtrToStringAnsi(name))
            {
                case "xrSuggestInteractionProfileBindings":
                    if (original != Marshal.GetFunctionPointerForDelegate(s_SuggestHook))
                    {
                        s_NextSuggest = Marshal.GetDelegateForFunctionPointer<SuggestFn>(original);
                        Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_SuggestHook));
                    }
                    break;
                case "xrAttachSessionActionSets":
                    if (original != Marshal.GetFunctionPointerForDelegate(s_AttachHook))
                    {
                        s_NextAttach = Marshal.GetDelegateForFunctionPointer<SessionInfoFn>(original);
                        Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_AttachHook));
                    }
                    break;
                case "xrSyncActions":
                    if (original != Marshal.GetFunctionPointerForDelegate(s_SyncHook))
                    {
                        s_NextSync = Marshal.GetDelegateForFunctionPointer<SessionInfoFn>(original);
                        Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_SyncHook));
                    }
                    break;
            }
            return result;
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            s_Instance = xrInstance;
            s_ActionSet = 0;
            s_Suggested = s_Attached = false;
            if (!OpenXRRuntime.IsExtensionEnabled(Extension))
            {
                Status = "von der Brille nicht angeboten";
                return true;
            }
            try
            {
                var toPath = Load<StringToPathFn>("xrStringToPath");
                var createSet = Load<CreateActionSetFn>("xrCreateActionSet");
                var createAction = Load<CreateActionFn>("xrCreateAction");
                s_GetState = Load<GetActionStateBooleanFn>("xrGetActionStateBoolean");

                s_ProfilePath = Path(toPath, Profile);
                s_HandPaths[0] = Path(toPath, "/user/hand/left");
                s_HandPaths[1] = Path(toPath, "/user/hand/right");

                // XrActionSetCreateInfo: type, next, actionSetName[64] @16, localizedActionSetName[128] @80, priority @208
                IntPtr info = Marshal.AllocHGlobal(224);
                try
                {
                    Zero(info, 224);
                    Marshal.WriteInt32(info, 0, XR_TYPE_ACTION_SET_CREATE_INFO);
                    WriteString(info, 16, "themovies_microgestures", 64);
                    WriteString(info, 80, "The Movies Mikrogesten", 128);
                    Check(createSet(xrInstance, info, out s_ActionSet), "xrCreateActionSet");
                    s_SwipeLeft = CreateAction(createAction, info, "swipe_left", "Daumen nach links");
                    s_SwipeRight = CreateAction(createAction, info, "swipe_right", "Daumen nach rechts");
                }
                finally
                {
                    Marshal.FreeHGlobal(info);
                }

                s_Bindings[0] = Path(toPath, "/user/hand/left/input/swipe_left_meta/click");
                s_Bindings[1] = Path(toPath, "/user/hand/left/input/swipe_right_meta/click");
                s_Bindings[2] = Path(toPath, "/user/hand/right/input/swipe_left_meta/click");
                s_Bindings[3] = Path(toPath, "/user/hand/right/input/swipe_right_meta/click");

                if (s_GetInfo == IntPtr.Zero)
                {
                    s_GetInfo = Marshal.AllocHGlobal(32);
                    s_State = Marshal.AllocHGlobal(40);
                }
                Status = "Erweiterung geladen";
            }
            catch (Exception e)
            {
                s_ActionSet = 0;
                Status = "Fehler: " + e.Message;
                Debug.LogWarning("[TheMovies] Microgestures: " + e.Message);
            }
            return true; // never block XR start because of this
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            s_Attached = false;
        }

        /// <summary>Unity suggests all bindings of a profile in one call: ours are added to the hand interaction profile's.</summary>
        [AOT.MonoPInvokeCallback(typeof(SuggestFn))]
        static int SuggestHook(ulong instance, IntPtr suggested)
        {
            // XrInteractionProfileSuggestedBinding: type, next, interactionProfile @16, count @24, bindings @32
            if (s_ActionSet == 0 || suggested == IntPtr.Zero || (ulong)Marshal.ReadInt64(suggested, 16) != s_ProfilePath)
                return s_NextSuggest(instance, suggested);

            int count = Marshal.ReadInt32(suggested, 24);
            IntPtr existing = Marshal.ReadIntPtr(suggested, 32);
            IntPtr bindings = Marshal.AllocHGlobal(16 * (count + 4));
            IntPtr copy = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < count * 2; i++)
                    Marshal.WriteInt64(bindings, i * 8, Marshal.ReadInt64(existing, i * 8));
                WriteOwnBindings(bindings, count);
                for (int offset = 0; offset < 40; offset += 8)
                    Marshal.WriteInt64(copy, offset, Marshal.ReadInt64(suggested, offset));
                Marshal.WriteInt32(copy, 24, count + 4);
                Marshal.WriteIntPtr(copy, 32, bindings);
                int result = s_NextSuggest(instance, copy);
                if (result >= 0)
                {
                    s_Suggested = true;
                    return result;
                }
                // The runtime rejected the combination: keep Unity's bindings working without ours.
                Status = "Bindung abgelehnt (" + result + ")";
                return s_NextSuggest(instance, suggested);
            }
            finally
            {
                Marshal.FreeHGlobal(bindings);
                Marshal.FreeHGlobal(copy);
            }
        }

        static void WriteOwnBindings(IntPtr bindings, int start)
        {
            // XrActionSuggestedBinding: action, binding path
            ulong[] actions = { s_SwipeLeft, s_SwipeRight, s_SwipeLeft, s_SwipeRight };
            for (int i = 0; i < 4; i++)
            {
                Marshal.WriteInt64(bindings, (start + i) * 16, (long)actions[i]);
                Marshal.WriteInt64(bindings, (start + i) * 16 + 8, (long)s_Bindings[i]);
            }
        }

        /// <summary>Attaches our action set together with Unity's.</summary>
        [AOT.MonoPInvokeCallback(typeof(SessionInfoFn))]
        static int AttachHook(ulong session, IntPtr info)
        {
            if (s_ActionSet == 0 || info == IntPtr.Zero)
                return s_NextAttach(session, info);

            if (!s_Suggested)
            {
                // Unity didn't suggest anything for the hand interaction profile (feature off): suggest ours alone.
                IntPtr own = Marshal.AllocHGlobal(16 * 4);
                IntPtr suggest = Marshal.AllocHGlobal(40);
                try
                {
                    WriteOwnBindings(own, 0);
                    Zero(suggest, 40);
                    Marshal.WriteInt32(suggest, 0, XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING);
                    Marshal.WriteInt64(suggest, 16, (long)s_ProfilePath);
                    Marshal.WriteInt32(suggest, 24, 4);
                    Marshal.WriteIntPtr(suggest, 32, own);
                    s_Suggested = s_NextSuggest != null && s_NextSuggest(s_Instance, suggest) >= 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(own);
                    Marshal.FreeHGlobal(suggest);
                }
                if (!s_Suggested) return s_NextAttach(session, info);
            }

            // XrSessionActionSetsAttachInfo: type, next, count @16, actionSets @24
            int count = Marshal.ReadInt32(info, 16);
            IntPtr sets = Marshal.ReadIntPtr(info, 24);
            IntPtr all = Marshal.AllocHGlobal(8 * (count + 1));
            IntPtr copy = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < count; i++) Marshal.WriteInt64(all, i * 8, Marshal.ReadInt64(sets, i * 8));
                Marshal.WriteInt64(all, count * 8, (long)s_ActionSet);
                for (int offset = 0; offset < 32; offset += 8) Marshal.WriteInt64(copy, offset, Marshal.ReadInt64(info, offset));
                Marshal.WriteInt32(copy, 16, count + 1);
                Marshal.WriteIntPtr(copy, 24, all);
                int result = s_NextAttach(session, copy);
                if (result >= 0)
                {
                    s_Attached = true;
                    Status = "aktiv";
                    return result;
                }
                Status = "Anhängen abgelehnt (" + result + ")";
                return s_NextAttach(session, info);
            }
            finally
            {
                Marshal.FreeHGlobal(all);
                Marshal.FreeHGlobal(copy);
            }
        }

        /// <summary>Syncs our action set with Unity's and counts new swipes.</summary>
        [AOT.MonoPInvokeCallback(typeof(SessionInfoFn))]
        static int SyncHook(ulong session, IntPtr info)
        {
            if (!s_Attached || info == IntPtr.Zero)
                return s_NextSync(session, info);

            // XrActionsSyncInfo: type, next, count @16, activeActionSets @24; XrActiveActionSet: set, subactionPath
            int count = Marshal.ReadInt32(info, 16);
            IntPtr sets = Marshal.ReadIntPtr(info, 24);
            IntPtr all = Marshal.AllocHGlobal(16 * (count + 1));
            IntPtr copy = Marshal.AllocHGlobal(32);
            int result;
            try
            {
                for (int i = 0; i < count * 2; i++) Marshal.WriteInt64(all, i * 8, Marshal.ReadInt64(sets, i * 8));
                Marshal.WriteInt64(all, count * 16, (long)s_ActionSet);
                Marshal.WriteInt64(all, count * 16 + 8, 0);
                for (int offset = 0; offset < 32; offset += 8) Marshal.WriteInt64(copy, offset, Marshal.ReadInt64(info, offset));
                Marshal.WriteInt32(copy, 16, count + 1);
                Marshal.WriteIntPtr(copy, 24, all);
                result = s_NextSync(session, copy);
            }
            finally
            {
                Marshal.FreeHGlobal(all);
                Marshal.FreeHGlobal(copy);
            }
            if (result < 0) return result;

            for (int hand = 0; hand < 2; hand++)
            {
                if (Pressed(session, s_SwipeLeft, s_HandPaths[hand])) Interlocked.Decrement(ref s_Turns);
                if (Pressed(session, s_SwipeRight, s_HandPaths[hand])) Interlocked.Increment(ref s_Turns);
            }
            return result;
        }

        /// <summary>True on the sync where the gesture became true.</summary>
        static bool Pressed(ulong session, ulong action, ulong hand)
        {
            // XrActionStateGetInfo: type, next, action @16, subactionPath @24
            Zero(s_GetInfo, 32);
            Marshal.WriteInt32(s_GetInfo, 0, XR_TYPE_ACTION_STATE_GET_INFO);
            Marshal.WriteInt64(s_GetInfo, 16, (long)action);
            Marshal.WriteInt64(s_GetInfo, 24, (long)hand);
            // XrActionStateBoolean: type, next, currentState @16, changedSinceLastSync @20, lastChangeTime @24, isActive @32
            Zero(s_State, 40);
            Marshal.WriteInt32(s_State, 0, XR_TYPE_ACTION_STATE_BOOLEAN);
            if (s_GetState(session, s_GetInfo, s_State) < 0) return false;
            bool pressed = Marshal.ReadInt32(s_State, 16) != 0 && Marshal.ReadInt32(s_State, 20) != 0;
            if (pressed && Interlocked.Exchange(ref s_Detected, 1) == 0)
                Status = "aktiv, erkannt";
            return pressed;
        }

        static ulong CreateAction(CreateActionFn create, IntPtr info, string name, string localized)
        {
            // XrActionCreateInfo: type, next, actionName[64] @16, actionType @80, countSubactionPaths @84,
            // subactionPaths @88, localizedActionName[128] @96
            IntPtr paths = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt64(paths, 0, (long)s_HandPaths[0]);
                Marshal.WriteInt64(paths, 8, (long)s_HandPaths[1]);
                Zero(info, 224);
                Marshal.WriteInt32(info, 0, XR_TYPE_ACTION_CREATE_INFO);
                WriteString(info, 16, name, 64);
                Marshal.WriteInt32(info, 80, XR_ACTION_TYPE_BOOLEAN_INPUT);
                Marshal.WriteInt32(info, 84, 2);
                Marshal.WriteIntPtr(info, 88, paths);
                WriteString(info, 96, localized, 128);
                Check(create(s_ActionSet, info, out ulong action), "xrCreateAction " + name);
                return action;
            }
            finally
            {
                Marshal.FreeHGlobal(paths);
            }
        }

        static ulong Path(StringToPathFn toPath, string text)
        {
            IntPtr str = Marshal.StringToHGlobalAnsi(text);
            try
            {
                Check(toPath(s_Instance, str, out ulong path), "xrStringToPath " + text);
                return path;
            }
            finally
            {
                Marshal.FreeHGlobal(str);
            }
        }

        static T Load<T>(string name) where T : Delegate
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            IntPtr result = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(result, IntPtr.Zero);
                int r = s_NextGetProcAddr(s_Instance, namePtr, result);
                IntPtr fn = Marshal.ReadIntPtr(result);
                if (r < 0 || fn == IntPtr.Zero) throw new MissingMethodException(name + " (" + r + ")");
                return Marshal.GetDelegateForFunctionPointer<T>(fn);
            }
            finally
            {
                Marshal.FreeHGlobal(namePtr);
                Marshal.FreeHGlobal(result);
            }
        }

        static void WriteString(IntPtr p, int offset, string text, int capacity)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            int n = Math.Min(bytes.Length, capacity - 1);
            Marshal.Copy(bytes, 0, p + offset, n);
            Marshal.WriteByte(p, offset + n, 0);
        }

        static void Check(int result, string call)
        {
            if (result < 0) throw new InvalidOperationException(call + " → XrResult " + result);
        }

        static void Zero(IntPtr p, int bytes)
        {
            for (int i = 0; i < bytes; i += 8) Marshal.WriteInt64(p, i, 0);
        }
    }
}
#endif
