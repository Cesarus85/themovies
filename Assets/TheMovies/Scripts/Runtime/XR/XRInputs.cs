using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace TheMovies.XR
{
    /// <summary>
    /// Reads head and controller poses and buttons. Uses Input System bindings (OpenXR layouts)
    /// first and falls back to the XR InputDevices API, so it works with OpenXR on Quest and with
    /// Quest Link on PC without any extra configuration.
    /// Hand tracking: with the OpenXR "Hand Interaction Profile" the hands report an aim pose and a pinch value
    /// like a controller's pointer and trigger, so pinching clicks and the ray comes out of the hand.
    /// </summary>
    public sealed class XRInputs
    {
        sealed class PoseActions
        {
            public InputAction Position;
            public InputAction Rotation;
            bool m_ControllersOnly;

            /// <summary>
            /// Pose of a real controller: &lt;XRController&gt; also matches the hands of the Hand Interaction Profile, so
            /// bound controls on hand devices are skipped.
            /// </summary>
            public static PoseActions Controller(string device, string position, string rotation) =>
                new PoseActions(device, position, rotation) { m_ControllersOnly = true };

            /// <param name="extra">More "position binding|rotation binding" pairs (hand tracking devices).</param>
            public PoseActions(string device, string position, string rotation, params string[] extra)
            {
                Position = new InputAction(type: InputActionType.Value, binding: $"{device}/{position}", expectedControlType: "Vector3");
                Rotation = new InputAction(type: InputActionType.Value, binding: $"{device}/{rotation}", expectedControlType: "Quaternion");
                foreach (string pair in extra)
                {
                    int bar = pair.IndexOf('|');
                    Position.AddBinding(pair.Substring(0, bar));
                    Rotation.AddBinding(pair.Substring(bar + 1));
                }
                Position.Enable();
                Rotation.Enable();
            }

            public bool TryRead(out Vector3 position, out Quaternion rotation)
            {
                position = default;
                rotation = Quaternion.identity;
                if (Position.controls.Count == 0 || Rotation.controls.Count == 0)
                    return false;
                if (m_ControllersOnly)
                    return TryReadController(out position, out rotation);
                position = Position.ReadValue<Vector3>();
                rotation = Rotation.ReadValue<Quaternion>();
                return IsValidRotation(rotation);
            }

            bool TryReadController(out Vector3 position, out Quaternion rotation)
            {
                position = default;
                rotation = Quaternion.identity;
                foreach (var p in Position.controls)
                {
                    if (IsHandDevice(p.device) || !(p is InputControl<Vector3> pos)) continue;
                    foreach (var r in Rotation.controls)
                    {
                        if (r.device != p.device || !(r is InputControl<Quaternion> rot)) continue;
                        position = pos.ReadValue();
                        rotation = rot.ReadValue();
                        if (IsValidRotation(rotation)) return true;
                    }
                }
                return false;
            }

            public void Dispose()
            {
                Position.Dispose();
                Rotation.Dispose();
            }
        }

        static readonly InputFeatureUsage<Vector3> k_PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        static readonly InputFeatureUsage<Quaternion> k_PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");

        readonly PoseActions m_Head = new PoseActions("<XRHMD>", "centerEyePosition", "centerEyeRotation");
        // Controllers
        readonly PoseActions m_LeftAim = PoseActions.Controller("<XRController>{LeftHand}", "pointerPosition", "pointerRotation");
        readonly PoseActions m_RightAim = PoseActions.Controller("<XRController>{RightHand}", "pointerPosition", "pointerRotation");
        readonly PoseActions m_LeftGrip = PoseActions.Controller("<XRController>{LeftHand}", "devicePosition", "deviceRotation");
        readonly PoseActions m_RightGrip = PoseActions.Controller("<XRController>{RightHand}", "devicePosition", "deviceRotation");

        // Trigger as an analog value: more reliable than a single "pressed" flag, and a light pull is enough.
        readonly InputAction m_LeftTrigger = Axis("<XRController>{LeftHand}/trigger");
        readonly InputAction m_RightTrigger = Axis("<XRController>{RightHand}/trigger");
        readonly InputAction m_LeftControllerTracked = Axis("<XRController>{LeftHand}/isTracked");
        readonly InputAction m_RightControllerTracked = Axis("<XRController>{RightHand}/isTracked");

        // Hands, three ways (whichever the runtime offers): the joints of the XR Hands package (HandJointSource),
        // Meta's aim pose (XR Hands feature "Meta Hand Tracking Aim", device MetaAimHand) and the OpenXR Hand
        // Interaction Profile (device HandInteraction).
        const string k_LeftHand = "<HandInteraction>{LeftHand}";
        const string k_RightHand = "<HandInteraction>{RightHand}";
        const string k_LeftMeta = "<MetaAimHand>{LeftHand}";
        const string k_RightMeta = "<MetaAimHand>{RightHand}";
        readonly PoseActions m_LeftHandAim = new PoseActions(k_LeftMeta, "devicePosition", "deviceRotation",
            k_LeftHand + "/pointerPosition|" + k_LeftHand + "/pointerRotation");
        readonly PoseActions m_RightHandAim = new PoseActions(k_RightMeta, "devicePosition", "deviceRotation",
            k_RightHand + "/pointerPosition|" + k_RightHand + "/pointerRotation");
        readonly InputAction m_LeftPinch = Axis(k_LeftMeta + "/pinchStrengthIndex", k_LeftHand + "/pinchValue");
        readonly InputAction m_RightPinch = Axis(k_RightMeta + "/pinchStrengthIndex", k_RightHand + "/pinchValue");
        readonly InputAction m_LeftGrasp = Axis(k_LeftHand + "/graspValue");
        readonly InputAction m_RightGrasp = Axis(k_RightHand + "/graspValue");
        readonly IHandJointSource m_Joints = HandJointSources.Create();

        public enum Source { None, Controller, HandJoints, HandAim }

        readonly Source[] m_Sources = new Source[2];
        readonly Pose[] m_HandAim = new Pose[2];
        readonly Pose[] m_HandPalm = new Pose[2];
        readonly float[] m_HandPinch = new float[2];

        /// <summary>The hand is tracked as a hand (no controller in it).</summary>
        public bool IsHand(bool left) => m_Sources[left ? 0 : 1] == Source.HandJoints || m_Sources[left ? 0 : 1] == Source.HandAim;

        public Source SourceOf(bool left) => m_Sources[left ? 0 : 1];

        /// <summary>Joint poses of a tracked hand (tracking space, indexed like XRHandJointID) for drawing it.</summary>
        public bool TryGetHandJoints(bool left, Pose[] joints) =>
            m_Sources[left ? 0 : 1] == Source.HandJoints && m_Joints != null && m_Joints.TryGetJoints(left, joints);

        /// <summary>The runtime's mesh of a hand for drawing it (see IHandJointSource.TryCreateMesh); null if not available.</summary>
        public Mesh TryCreateHandMesh(bool left, out string status)
        {
            if (m_Joints == null)
            {
                status = "XR Hands nicht im Build";
                return null;
            }
            return m_Joints.TryCreateMesh(left, out status);
        }

        /// <summary>
        /// Decides per hand where input comes from this frame: a tracked controller first, then hand joints, then the
        /// hand aim devices. Call once per frame before reading poses; <paramref name="head"/> in tracking space.
        /// The hand rays are smoothed a little (tracked fingers tremble).
        /// </summary>
        public void UpdateSources(Pose head)
        {
            float smoothing = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                if (ControllerTracked(left))
                {
                    m_Sources[i] = Source.Controller;
                    continue;
                }
                if (m_Joints != null && m_Joints.TryGetHand(left, head, out var handAim, out m_HandPalm[i], out m_HandPinch[i]))
                {
                    bool wasHand = m_Sources[i] == Source.HandJoints;
                    m_HandAim[i] = !wasHand ? handAim : new Pose(Vector3.Lerp(m_HandAim[i].position, handAim.position, smoothing),
                        Quaternion.Slerp(m_HandAim[i].rotation, handAim.rotation, smoothing));
                    m_Sources[i] = Source.HandJoints;
                    continue;
                }
                var aim = left ? m_LeftHandAim : m_RightHandAim;
                if (aim.TryRead(out var p, out var r) && p.sqrMagnitude > 1e-6f)
                {
                    m_HandAim[i] = m_HandPalm[i] = new Pose(p, r);
                    var pinch = left ? m_LeftPinch : m_RightPinch;
                    m_HandPinch[i] = pinch.controls.Count > 0 ? pinch.ReadValue<float>() : 0f;
                    m_Sources[i] = Source.HandAim;
                    continue;
                }
                m_Sources[i] = (left ? m_LeftAim : m_RightAim).TryRead(out _, out _) || ControllerDevice(left).isValid ? Source.Controller : Source.None;
            }
        }

        bool ControllerTracked(bool left)
        {
            var action = left ? m_LeftControllerTracked : m_RightControllerTracked;
            bool sawController = false;
            foreach (var control in action.controls)
            {
                if (IsHandDevice(control.device)) continue;
                sawController = true;
                if (control is InputControl<float> value && value.ReadValue() > 0.5f) return true;
            }
            if (sawController) return false;
            var device = ControllerDevice(left);
            return device.isValid && device.TryGetFeatureValue(XRCommonUsages.isTracked, out bool tracked) && tracked;
        }

        /// <summary>
        /// Hands as input devices: the Hand Interaction Profile's hands (which Unity derives from XRController, so
        /// &lt;XRController&gt; bindings match them too), MetaAimHand and XRHandDevice. Their layouts all contain "Hand".
        /// </summary>
        static bool IsHandDevice(UnityEngine.InputSystem.InputDevice device) =>
            device != null && device.layout.IndexOf("Hand", System.StringComparison.Ordinal) >= 0;

        /// <summary>The XR input API's device at the hand node if it is a controller (not a tracked hand).</summary>
        static XRInputDevice ControllerDevice(bool left)
        {
            var device = Device(left);
            if (!device.isValid || (device.characteristics & InputDeviceCharacteristics.HandTracking) != 0 ||
                device.name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return default;
            return device;
        }

        /// <summary>
        /// For the settings page: whether hand tracking runs (XR Hands subsystem) and which OpenXR hand extensions are on.
        /// <paramref name="ok"/> is false if something hand tracking needs is missing.
        /// </summary>
        public string HandTrackingStatus(out bool ok)
        {
            ok = m_Joints != null && m_Joints.Running;
            string text = "Handtracking: " + (m_Joints != null ? m_Joints.Status : "XR Hands nicht im Build");
#if THEMOVIES_OPENXR
            try
            {
                bool tracking = Ext("XR_EXT_hand_tracking"), aim = Ext("XR_FB_hand_tracking_aim"),
                    interaction = Ext("XR_EXT_hand_interaction"), mesh = Ext("XR_FB_hand_tracking_mesh");
                ok &= tracking;
                text += " · Erweiterungen: " + Flag("Gelenke", tracking) + ", " + Flag("Aim", aim) + ", " +
                        Flag("Interaction", interaction) + ", " + Flag("Mesh", mesh);
            }
            catch (System.Exception)
            {
                // OpenXR not running (PC without headset)
            }
#endif
            return text;
        }

        static string Flag(string label, bool on) => label + (on ? " an" : " aus");

#if THEMOVIES_OPENXR
        static bool Ext(string name) => UnityEngine.XR.OpenXR.OpenXRRuntime.IsExtensionEnabled(name);
#endif

        const float k_TriggerPress = 0.35f;
        const float k_TriggerRelease = 0.2f;

        static InputAction Axis(string binding, params string[] more)
        {
            var action = new InputAction(type: InputActionType.Value, binding: binding, expectedControlType: "Axis");
            foreach (string b in more) action.AddBinding(b);
            action.Enable();
            return action;
        }

        public XRInputs()
        {
            // For the log (Einstellungen > Protokoll): which input devices the headset reports, e.g. whether hands appear.
            foreach (var device in InputSystem.devices)
                Debug.Log($"[TheMovies] Input device: {device.layout} \"{device.name}\" {string.Join(",", device.usages)}");
            InputSystem.onDeviceChange += (device, change) =>
            {
                if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed)
                    Debug.Log($"[TheMovies] Input device {change}: {device.layout} \"{device.name}\" {string.Join(",", device.usages)}");
            };
        }

        readonly Dictionary<string, bool> m_PreviousButtons = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> m_CurrentButtons = new Dictionary<string, bool>();

        public void Dispose()
        {
            m_Head.Dispose();
            m_LeftAim.Dispose();
            m_RightAim.Dispose();
            m_LeftGrip.Dispose();
            m_RightGrip.Dispose();
            m_LeftTrigger.Dispose();
            m_RightTrigger.Dispose();
            m_LeftControllerTracked.Dispose();
            m_RightControllerTracked.Dispose();
            m_LeftHandAim.Dispose();
            m_RightHandAim.Dispose();
            m_LeftPinch.Dispose();
            m_RightPinch.Dispose();
            m_LeftGrasp.Dispose();
            m_RightGrasp.Dispose();
        }

        public bool TryGetHead(out Vector3 position, out Quaternion rotation)
        {
            if (m_Head.TryRead(out position, out rotation))
                return true;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (TryLegacyPose(device, XRCommonUsages.centerEyePosition, XRCommonUsages.centerEyeRotation, out position, out rotation))
                return true;
            device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return TryLegacyPose(device, XRCommonUsages.devicePosition, XRCommonUsages.deviceRotation, out position, out rotation);
        }

        /// <summary>The pose the laser should come out of (OpenXR "aim" pose).</summary>
        public bool TryGetAim(bool left, out Vector3 position, out Quaternion rotation)
        {
            if (IsHand(left))
            {
                var aim = m_HandAim[left ? 0 : 1];
                position = aim.position;
                rotation = aim.rotation;
                return true;
            }
            if ((left ? m_LeftAim : m_RightAim).TryRead(out position, out rotation))
                return true;
            var device = ControllerDevice(left);
            if (TryLegacyPose(device, k_PointerPosition, k_PointerRotation, out position, out rotation))
                return true;
            if (TryGetGrip(left, out position, out rotation))
            {
                // Grip pose points along the handle; tilt it so the ray points where the controller "looks".
                rotation *= Quaternion.Euler(45f, 0f, 0f);
                return true;
            }
            return false;
        }

        /// <summary>The pose of the controller body (OpenXR "grip" pose), used for visuals and avatars.</summary>
        public bool TryGetGrip(bool left, out Vector3 position, out Quaternion rotation)
        {
            if (IsHand(left))
            {
                var palm = m_HandPalm[left ? 0 : 1];
                position = palm.position;
                rotation = palm.rotation;
                return true;
            }
            if ((left ? m_LeftGrip : m_RightGrip).TryRead(out position, out rotation))
                return true;
            return TryLegacyPose(ControllerDevice(left), XRCommonUsages.devicePosition, XRCommonUsages.deviceRotation, out position, out rotation);
        }

        /// <summary>Call once per frame before querying button edges.</summary>
        public void UpdateButtons()
        {
            m_PreviousButtons.Clear();
            foreach (var kv in m_CurrentButtons)
                m_PreviousButtons[kv.Key] = kv.Value;
            m_CurrentButtons.Clear();

            SampleTrigger("LTrigger", true, m_LeftTrigger);
            SampleTrigger("RTrigger", false, m_RightTrigger);
            Sample("LPrimary", true, XRCommonUsages.primaryButton);
            Sample("RPrimary", false, XRCommonUsages.primaryButton);
            Sample("LSecondary", true, XRCommonUsages.secondaryButton);
            Sample("RSecondary", false, XRCommonUsages.secondaryButton);
            Sample("LMenu", true, XRCommonUsages.menuButton);
            Sample("RMenu", false, XRCommonUsages.menuButton);
            Sample("LGrip", true, XRCommonUsages.gripButton);
            Sample("RGrip", false, XRCommonUsages.gripButton);
            if (m_LeftGrasp.controls.Count > 0 && m_LeftGrasp.ReadValue<float>() > 0.85f) m_CurrentButtons["LGrip"] = true;
            if (m_RightGrasp.controls.Count > 0 && m_RightGrasp.ReadValue<float>() > 0.85f) m_CurrentButtons["RGrip"] = true;
        }

        public bool IsPressed(string button) => m_CurrentButtons.TryGetValue(button, out bool v) && v;

        public bool WasPressed(string button) =>
            IsPressed(button) && !(m_PreviousButtons.TryGetValue(button, out bool before) && before);

        public Vector2 Thumbstick(bool left)
        {
            return Device(left).TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 v) ? v : Vector2.zero;
        }

        public void Haptic(bool left, float amplitude, float duration)
        {
            var device = Device(left);
            if (device.isValid)
                device.SendHapticImpulse(0u, amplitude, duration);
        }

        /// <summary>Trigger value of a hand (0..1), from the Input System or the XR input API, whichever reports more.</summary>
        public float TriggerValue(bool left)
        {
            if (IsHand(left)) return m_HandPinch[left ? 0 : 1];
            float value = 0f;
            var action = left ? m_LeftTrigger : m_RightTrigger;
            if (action.controls.Count > 0)
                value = action.ReadValue<float>();
            if (Device(left).TryGetFeatureValue(XRCommonUsages.trigger, out float legacy))
                value = Mathf.Max(value, legacy);
            return value;
        }

        void SampleTrigger(string key, bool left, InputAction action)
        {
            float value = TriggerValue(left);
            bool wasDown = m_PreviousButtons.TryGetValue(key, out bool before) && before;
            // A pinch counts only when the fingers really touch (a hand half-closes when pointing).
            bool hand = IsHand(left);
            bool down = wasDown ? value > (hand ? 0.5f : k_TriggerRelease) : value > (hand ? 0.85f : k_TriggerPress);
            // The XR input API's own "pressed" flag counts as well (some runtimes only report that).
            if (!down && !hand && Device(left).TryGetFeatureValue(XRCommonUsages.triggerButton, out bool pressed) && pressed)
                down = true;
            m_CurrentButtons[key] = down;
        }

        void Sample(string key, bool left, InputFeatureUsage<bool> usage)
        {
            m_CurrentButtons[key] = Device(left).TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        static XRInputDevice Device(bool left) => InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);

        static bool TryLegacyPose(XRInputDevice device, InputFeatureUsage<Vector3> posUsage, InputFeatureUsage<Quaternion> rotUsage,
            out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!device.isValid)
                return false;
            if (!device.TryGetFeatureValue(posUsage, out position) || !device.TryGetFeatureValue(rotUsage, out rotation))
                return false;
            return IsValidRotation(rotation);
        }

        static bool IsValidRotation(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f;
    }
}
