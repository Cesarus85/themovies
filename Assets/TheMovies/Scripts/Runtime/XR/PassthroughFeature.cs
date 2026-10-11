#if THEMOVIES_OPENXR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace TheMovies.XR
{
    /// <summary>
    /// Own passthrough via the OpenXR extension XR_FB_passthrough, independent of AR Foundation: creates the
    /// passthrough layer itself and, while mixed reality is on, inserts it behind Unity's image in every
    /// xrEndFrame and marks Unity's projection layer as alpha-blended (wherever the camera cleared to alpha 0,
    /// the room shows through). Enable "The Movies: Passthrough" under OpenXR → Android (done automatically).
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "The Movies: Passthrough",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "The Movies",
        Desc = "Passthrough für die Umgebung 'Mixed Reality' (XR_FB_passthrough).",
        OpenxrExtensionStrings = "XR_FB_passthrough",
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public sealed class PassthroughFeature : OpenXRFeature
    {
        public const string FeatureId = "com.themovies.passthrough";

        const int XR_TYPE_PASSTHROUGH_CREATE_INFO_FB = 1000118001;
        const int XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB = 1000118002;
        const int XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB = 1000118003;
        const int XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35;
        const long XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT = 0x2;
        const int MaxLayers = 32;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EndFrameFn(ulong session, IntPtr frameEndInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CreateFn(ulong session, IntPtr createInfo, out ulong handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int HandleFn(ulong handle);

        static GetInstanceProcAddrFn s_NextGetProcAddr;
        static readonly GetInstanceProcAddrFn s_GetProcAddrHook = GetInstanceProcAddrHook;
        static readonly EndFrameFn s_EndFrameHook = EndFrameHook;
        static EndFrameFn s_NextEndFrame;

        static CreateFn s_CreatePassthrough, s_CreateLayer;
        static HandleFn s_Start, s_Pause, s_LayerResume, s_LayerPause, s_DestroyLayer, s_DestroyPassthrough;

        static ulong s_Session, s_Passthrough, s_Layer;
        static volatile bool s_Visible;
        static IntPtr s_LayerStruct, s_LayerArray, s_InfoCopy;
        static int s_Frames;

        /// <summary>Last result of the setup, for the mixed reality status line.</summary>
        public static string Status { get; private set; } = "nicht gestartet";

        /// <summary>The passthrough layer exists and can be shown.</summary>
        public static bool Ready => s_Layer != 0 && s_NextEndFrame != null;

        /// <summary>How many frames had the passthrough layer inserted (status line).</summary>
        public static int FramesWithPassthrough => s_Frames;

        /// <summary>Shows or hides the room. Called from the main thread.</summary>
        public static void SetVisible(bool visible)
        {
            if (!Ready)
            {
                s_Visible = false;
                return;
            }
            try
            {
                if (visible)
                {
                    Check(s_Start(s_Passthrough), "xrPassthroughStartFB");
                    Check(s_LayerResume(s_Layer), "xrPassthroughLayerResumeFB");
                }
                else
                {
                    s_LayerPause(s_Layer);
                    s_Pause(s_Passthrough);
                }
                s_Visible = visible;
            }
            catch (Exception e)
            {
                Status = "Fehler: " + e.Message;
                Debug.LogWarning("[TheMovies] Passthrough: " + e.Message);
            }
        }

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            s_NextGetProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(func);
            return Marshal.GetFunctionPointerForDelegate(s_GetProcAddrHook);
        }

        [AOT.MonoPInvokeCallback(typeof(GetInstanceProcAddrFn))]
        static int GetInstanceProcAddrHook(ulong instance, IntPtr name, IntPtr function)
        {
            int result = s_NextGetProcAddr(instance, name, function);
            if (result >= 0 && function != IntPtr.Zero && Marshal.PtrToStringAnsi(name) == "xrEndFrame")
            {
                IntPtr original = Marshal.ReadIntPtr(function);
                if (original != IntPtr.Zero && original != Marshal.GetFunctionPointerForDelegate(s_EndFrameHook))
                {
                    s_NextEndFrame = Marshal.GetDelegateForFunctionPointer<EndFrameFn>(original);
                    Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_EndFrameHook));
                }
            }
            return result;
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            try
            {
                s_CreatePassthrough = Load<CreateFn>(xrInstance, "xrCreatePassthroughFB");
                s_CreateLayer = Load<CreateFn>(xrInstance, "xrCreatePassthroughLayerFB");
                s_Start = Load<HandleFn>(xrInstance, "xrPassthroughStartFB");
                s_Pause = Load<HandleFn>(xrInstance, "xrPassthroughPauseFB");
                s_LayerResume = Load<HandleFn>(xrInstance, "xrPassthroughLayerResumeFB");
                s_LayerPause = Load<HandleFn>(xrInstance, "xrPassthroughLayerPauseFB");
                s_DestroyLayer = Load<HandleFn>(xrInstance, "xrDestroyPassthroughLayerFB");
                s_DestroyPassthrough = Load<HandleFn>(xrInstance, "xrDestroyPassthroughFB");
                Status = "Erweiterung geladen";
            }
            catch (Exception e)
            {
                Status = "XR_FB_passthrough fehlt: " + e.Message;
                Debug.LogWarning("[TheMovies] Passthrough: " + Status);
            }
            return true; // never block XR start because of passthrough
        }

        protected override void OnSessionCreate(ulong xrSession)
        {
            s_Session = xrSession;
            if (s_CreatePassthrough == null) return;
            try
            {
                if (s_LayerStruct == IntPtr.Zero)
                {
                    s_LayerStruct = Marshal.AllocHGlobal(40);
                    s_LayerArray = Marshal.AllocHGlobal(8 * MaxLayers);
                    s_InfoCopy = Marshal.AllocHGlobal(64);
                }

                IntPtr info = Marshal.AllocHGlobal(48);
                try
                {
                    Zero(info, 48);
                    Marshal.WriteInt32(info, 0, XR_TYPE_PASSTHROUGH_CREATE_INFO_FB);
                    Marshal.WriteInt64(info, 16, 0); // flags: start paused
                    Check(s_CreatePassthrough(xrSession, info, out s_Passthrough), "xrCreatePassthroughFB");

                    Zero(info, 48);
                    Marshal.WriteInt32(info, 0, XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB);
                    Marshal.WriteInt64(info, 16, (long)s_Passthrough);
                    Marshal.WriteInt64(info, 24, 0); // flags: paused
                    Marshal.WriteInt32(info, 32, 0); // purpose: reconstruction (the normal room view)
                    Check(s_CreateLayer(xrSession, info, out s_Layer), "xrCreatePassthroughLayerFB");
                }
                finally
                {
                    Marshal.FreeHGlobal(info);
                }

                // XrCompositionLayerPassthroughFB: type, next, flags, space, layerHandle
                Zero(s_LayerStruct, 40);
                Marshal.WriteInt32(s_LayerStruct, 0, XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB);
                Marshal.WriteInt64(s_LayerStruct, 16, XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT);
                Marshal.WriteInt64(s_LayerStruct, 24, 0);
                Marshal.WriteInt64(s_LayerStruct, 32, (long)s_Layer);
                Status = "bereit";
                Debug.Log("[TheMovies] Passthrough layer created.");
            }
            catch (Exception e)
            {
                Status = "Fehler: " + e.Message;
                Debug.LogWarning("[TheMovies] Passthrough: " + e.Message);
                s_Layer = 0;
            }
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            s_Visible = false;
            try
            {
                if (s_Layer != 0) s_DestroyLayer?.Invoke(s_Layer);
                if (s_Passthrough != 0) s_DestroyPassthrough?.Invoke(s_Passthrough);
            }
            catch (Exception)
            {
                // Session is going away anyway
            }
            s_Layer = 0;
            s_Passthrough = 0;
            s_Session = 0;
            Status = "beendet";
        }

        /// <summary>Render thread: puts the passthrough layer first (behind everything) while visible.</summary>
        [AOT.MonoPInvokeCallback(typeof(EndFrameFn))]
        static int EndFrameHook(ulong session, IntPtr frameEndInfo)
        {
            var next = s_NextEndFrame;
            if (!s_Visible || s_Layer == 0 || frameEndInfo == IntPtr.Zero)
                return next(session, frameEndInfo);

            // XrFrameEndInfo: type(0) next(8) displayTime(16) environmentBlendMode(24) layerCount(28) layers(32)
            int count = Marshal.ReadInt32(frameEndInfo, 28);
            IntPtr layers = Marshal.ReadIntPtr(frameEndInfo, 32);
            if (count < 0 || count >= MaxLayers - 1)
                return next(session, frameEndInfo);

            Marshal.WriteIntPtr(s_LayerArray, 0, s_LayerStruct);
            for (int i = 0; i < count; i++)
            {
                IntPtr layer = Marshal.ReadIntPtr(layers, i * IntPtr.Size);
                if (layer != IntPtr.Zero && Marshal.ReadInt32(layer, 0) == XR_TYPE_COMPOSITION_LAYER_PROJECTION)
                {
                    // XrCompositionLayerBaseHeader: type(0) next(8) layerFlags(16) space(24)
                    long flags = Marshal.ReadInt64(layer, 16);
                    Marshal.WriteInt64(layer, 16, flags | XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT);
                }
                Marshal.WriteIntPtr(s_LayerArray, (i + 1) * IntPtr.Size, layer);
            }

            for (int offset = 0; offset < 40; offset += 8)
                Marshal.WriteInt64(s_InfoCopy, offset, Marshal.ReadInt64(frameEndInfo, offset));
            Marshal.WriteInt32(s_InfoCopy, 28, count + 1);
            Marshal.WriteIntPtr(s_InfoCopy, 32, s_LayerArray);
            s_Frames++;
            return next(session, s_InfoCopy);
        }

        static T Load<T>(ulong instance, string name) where T : Delegate
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            IntPtr result = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(result, IntPtr.Zero);
                int r = s_NextGetProcAddr(instance, namePtr, result);
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
