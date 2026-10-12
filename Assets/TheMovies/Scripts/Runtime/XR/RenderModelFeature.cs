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
    /// The headset's own controller models via the OpenXR extension XR_FB_render_model: the runtime hands out the
    /// model of the connected controllers (Touch Plus on Quest 3) as GLB, with the origin at the grip pose.
    /// Needs the Android manifest entries com.oculus.feature.RENDER_MODEL / com.oculus.permission.RENDER_MODEL
    /// (added by the build step). Enable "The Movies: Controller-Modelle" under OpenXR → Android (done automatically).
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "The Movies: Controller-Modelle",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "The Movies",
        Desc = "Lädt die offiziellen Controller-Modelle der Brille (XR_FB_render_model).",
        OpenxrExtensionStrings = "XR_FB_render_model",
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public sealed class RenderModelFeature : OpenXRFeature
    {
        public const string FeatureId = "com.themovies.rendermodel";

        const int XR_TYPE_RENDER_MODEL_PATH_INFO_FB = 1000119000;
        const int XR_TYPE_RENDER_MODEL_PROPERTIES_FB = 1000119001;
        const int XR_TYPE_RENDER_MODEL_BUFFER_FB = 1000119002;
        const int XR_TYPE_RENDER_MODEL_LOAD_INFO_FB = 1000119003;
        const int XR_TYPE_RENDER_MODEL_CAPABILITIES_REQUEST_FB = 1000119005;
        const int XR_RENDER_MODEL_UNAVAILABLE_FB = 1000119020;
        const long SupportsSubset1 = 1, SupportsSubset2 = 2;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EnumeratePathsFn(ulong session, uint capacity, out uint count, IntPtr paths);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int GetPropertiesFn(ulong session, ulong path, IntPtr properties);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int LoadFn(ulong session, IntPtr info, IntPtr buffer);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StringToPathFn(ulong instance, IntPtr pathString, out ulong path);

        static GetInstanceProcAddrFn s_GetProcAddr;
        static EnumeratePathsFn s_Enumerate;
        static GetPropertiesFn s_GetProperties;
        static LoadFn s_Load;
        static StringToPathFn s_StringToPath;
        static ulong s_Instance, s_Session;
        static bool s_Enumerated;
        static readonly object s_Lock = new object();

        /// <summary>For the log and the status line.</summary>
        public static string Status { get; private set; } = "nicht gestartet";

        public static bool Available => s_Load != null && s_Session != 0;

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            s_GetProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(func);
            return func;
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            s_Instance = xrInstance;
            try
            {
                s_Enumerate = Load<EnumeratePathsFn>("xrEnumerateRenderModelPathsFB");
                s_GetProperties = Load<GetPropertiesFn>("xrGetRenderModelPropertiesFB");
                s_Load = Load<LoadFn>("xrLoadRenderModelFB");
                s_StringToPath = Load<StringToPathFn>("xrStringToPath");
                Status = "Erweiterung geladen";
            }
            catch (Exception e)
            {
                s_Load = null;
                Status = "XR_FB_render_model fehlt: " + e.Message;
                Debug.LogWarning("[TheMovies] Controller models: " + Status);
            }
            return true; // never block XR start because of this
        }

        protected override void OnSessionCreate(ulong xrSession)
        {
            s_Session = xrSession;
            s_Enumerated = false;
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            s_Session = 0;
        }

        /// <summary>
        /// Loads the GLB of the left or right controller. Slow – call from a worker thread. Returns null if the model
        /// is not available (yet), see <paramref name="error"/>.
        /// </summary>
        public static byte[] LoadController(bool left, out string error)
        {
            error = null;
            if (!Available)
            {
                error = Status;
                return null;
            }
            lock (s_Lock)
            {
                try
                {
                    ulong session = s_Session;
                    if (!s_Enumerated)
                    {
                        // The runtime only answers for paths it listed first.
                        Check(s_Enumerate(session, 0, out uint count, IntPtr.Zero), "xrEnumerateRenderModelPathsFB");
                        IntPtr paths = Marshal.AllocHGlobal(24 * (int)Math.Max(1, count));
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                Zero(paths + 24 * i, 24);
                                Marshal.WriteInt32(paths + 24 * i, XR_TYPE_RENDER_MODEL_PATH_INFO_FB);
                            }
                            Check(s_Enumerate(session, count, out count, paths), "xrEnumerateRenderModelPathsFB");
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(paths);
                        }
                        s_Enumerated = true;
                        Debug.Log($"[TheMovies] Controller models: {count} render model paths");
                    }

                    ulong path = ToPath(left ? "/model_fb/controller/left" : "/model_fb/controller/right");
                    // XrRenderModelPropertiesFB (112 bytes) with XrRenderModelCapabilitiesRequestFB (24) in its next chain.
                    IntPtr props = Marshal.AllocHGlobal(112);
                    IntPtr caps = Marshal.AllocHGlobal(24);
                    ulong modelKey;
                    try
                    {
                        Zero(props, 112);
                        Zero(caps, 24);
                        Marshal.WriteInt32(caps, 0, XR_TYPE_RENDER_MODEL_CAPABILITIES_REQUEST_FB);
                        Marshal.WriteInt64(caps, 16, SupportsSubset1 | SupportsSubset2);
                        Marshal.WriteInt32(props, 0, XR_TYPE_RENDER_MODEL_PROPERTIES_FB);
                        Marshal.WriteIntPtr(props, 8, caps);
                        int r = s_GetProperties(session, path, props);
                        Check(r, "xrGetRenderModelPropertiesFB");
                        modelKey = (ulong)Marshal.ReadInt64(props, 88);
                        if (r == XR_RENDER_MODEL_UNAVAILABLE_FB || modelKey == 0)
                        {
                            error = "Modell (noch) nicht verfügbar – Controller verbunden?";
                            return null;
                        }
                        string name = Marshal.PtrToStringAnsi(props + 20);
                        Debug.Log($"[TheMovies] Controller model {(left ? "left" : "right")}: \"{name}\" key {modelKey}");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(props);
                        Marshal.FreeHGlobal(caps);
                    }

                    // Two-call idiom: size first, then the GLB.
                    IntPtr info = Marshal.AllocHGlobal(24);
                    IntPtr buffer = Marshal.AllocHGlobal(32);
                    IntPtr data = IntPtr.Zero;
                    try
                    {
                        Zero(info, 24);
                        Marshal.WriteInt32(info, 0, XR_TYPE_RENDER_MODEL_LOAD_INFO_FB);
                        Marshal.WriteInt64(info, 16, (long)modelKey);
                        Zero(buffer, 32);
                        Marshal.WriteInt32(buffer, 0, XR_TYPE_RENDER_MODEL_BUFFER_FB);
                        int r = s_Load(session, info, buffer);
                        Check(r, "xrLoadRenderModelFB");
                        int size = Marshal.ReadInt32(buffer, 20);
                        if (r == XR_RENDER_MODEL_UNAVAILABLE_FB || size <= 0)
                        {
                            error = "Modell nicht verfügbar";
                            return null;
                        }
                        data = Marshal.AllocHGlobal(size);
                        Marshal.WriteInt32(buffer, 16, size);
                        Marshal.WriteIntPtr(buffer, 24, data);
                        Check(s_Load(session, info, buffer), "xrLoadRenderModelFB");
                        int written = Marshal.ReadInt32(buffer, 20);
                        var glb = new byte[Math.Min(size, written)];
                        Marshal.Copy(data, glb, 0, glb.Length);
                        Status = "Modelle geladen";
                        return glb;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(info);
                        Marshal.FreeHGlobal(buffer);
                        if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    Status = "Fehler: " + e.Message;
                    return null;
                }
            }
        }

        static ulong ToPath(string text)
        {
            IntPtr str = Marshal.StringToHGlobalAnsi(text);
            try
            {
                Check(s_StringToPath(s_Instance, str, out ulong path), "xrStringToPath");
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
                int r = s_GetProcAddr(s_Instance, namePtr, result);
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
