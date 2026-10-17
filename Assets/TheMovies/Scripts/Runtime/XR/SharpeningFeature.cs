#if THEMOVIES_OPENXR
using System;
using System.Runtime.InteropServices;
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
    /// Compositor sharpening of the eye image (XR_FB_composition_layer_settings, "quality sharpening"): the headset
    /// sharpens Unity's projection layer while it maps it onto the displays, which makes menu text crisper. Like in
    /// Questwelt only while reading menus: during the film it could make fine detail shimmer. Hooks xrEndFrame and
    /// chains an XrCompositionLayerSettingsFB to the projection layer for that one call.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "The Movies: Compositor-Schärfen",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "The Movies",
        Desc = "Schärft das Augenbild im Compositor, solange ein Menü offen ist (XR_FB_composition_layer_settings).",
        OpenxrExtensionStrings = Extension,
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public sealed class SharpeningFeature : OpenXRFeature
    {
        public const string FeatureId = "com.themovies.sharpening";
        const string Extension = "XR_FB_composition_layer_settings";

        const int XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35;
        const int XR_TYPE_COMPOSITION_LAYER_SETTINGS_FB = 1000204000;
        const long XR_COMPOSITION_LAYER_SETTINGS_QUALITY_SHARPENING_BIT_FB = 0x8;
        const int MaxLayers = 32;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EndFrameFn(ulong session, IntPtr frameEndInfo);

        static GetInstanceProcAddrFn s_NextGetProcAddr;
        static readonly GetInstanceProcAddrFn s_GetProcAddrHook = GetInstanceProcAddrHook;
        static readonly EndFrameFn s_EndFrameHook = EndFrameHook;
        static EndFrameFn s_NextEndFrame;

        static IntPtr s_Settings;
        static bool s_Supported;
        static volatile bool s_Enabled;

        /// <summary>The runtime offers the extension (only then <see cref="Enabled"/> has an effect).</summary>
        public static bool Supported => s_Supported && s_NextEndFrame != null;

        /// <summary>Sharpen the eye image (e.g. while a menu is open). Can be set from the main thread at any time.</summary>
        public static bool Enabled
        {
            get => s_Enabled;
            set => s_Enabled = value;
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
            s_Supported = OpenXRRuntime.IsExtensionEnabled(Extension);
            if (s_Supported && s_Settings == IntPtr.Zero)
            {
                // XrCompositionLayerSettingsFB: type(0) next(8) layerFlags(16)
                s_Settings = Marshal.AllocHGlobal(24);
                Marshal.WriteInt64(s_Settings, 0, 0);
                Marshal.WriteInt32(s_Settings, 0, XR_TYPE_COMPOSITION_LAYER_SETTINGS_FB);
                Marshal.WriteInt64(s_Settings, 16, XR_COMPOSITION_LAYER_SETTINGS_QUALITY_SHARPENING_BIT_FB);
            }
            Debug.Log("[TheMovies] Compositor sharpening " + (s_Supported ? "available." : "not offered by the runtime."));
            return true; // never block XR start because of sharpening
        }

        /// <summary>Render thread: while enabled, chains the settings to the projection layer for this frame only.</summary>
        [AOT.MonoPInvokeCallback(typeof(EndFrameFn))]
        static int EndFrameHook(ulong session, IntPtr frameEndInfo)
        {
            var next = s_NextEndFrame;
            if (!s_Enabled || !s_Supported || s_Settings == IntPtr.Zero || frameEndInfo == IntPtr.Zero)
                return next(session, frameEndInfo);

            // XrFrameEndInfo: type(0) next(8) displayTime(16) environmentBlendMode(24) layerCount(28) layers(32)
            int count = Marshal.ReadInt32(frameEndInfo, 28);
            IntPtr layers = Marshal.ReadIntPtr(frameEndInfo, 32);
            if (count <= 0 || count > MaxLayers || layers == IntPtr.Zero)
                return next(session, frameEndInfo);

            // Only one settings struct exists, so only the first projection layer gets it (Unity submits one).
            IntPtr changed = IntPtr.Zero, originalNext = IntPtr.Zero;
            for (int i = 0; i < count && changed == IntPtr.Zero; i++)
            {
                IntPtr layer = Marshal.ReadIntPtr(layers, i * IntPtr.Size);
                if (layer == IntPtr.Zero || Marshal.ReadInt32(layer, 0) != XR_TYPE_COMPOSITION_LAYER_PROJECTION) continue;
                // XrCompositionLayerBaseHeader: type(0) next(8) layerFlags(16) space(24)
                IntPtr chain = Marshal.ReadIntPtr(layer, 8);
                if (chain == s_Settings) break; // already chained (must not happen: restored below)
                Marshal.WriteIntPtr(s_Settings, 8, chain);
                Marshal.WriteIntPtr(layer, 8, s_Settings);
                changed = layer;
                originalNext = chain;
            }

            int result = next(session, frameEndInfo);
            // Unity may reuse the layer struct next frame: leave it exactly as it was.
            if (changed != IntPtr.Zero)
                Marshal.WriteIntPtr(changed, 8, originalNext);
            return result;
        }
    }
}
#endif
