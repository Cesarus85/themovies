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

            /// <summary>The pose comes from a tracked hand (Hand Interaction Profile), not from a controller.</summary>
            public bool FromHand
            {
                get
                {
                    var control = Position.activeControl;
                    if (control == null && Position.controls.Count > 0) control = Position.controls[0];
                    return control != null && control.device.layout.IndexOf("Hand", System.StringComparison.Ordinal) >= 0;
                }
            }

            public bool TryRead(out Vector3 position, out Quaternion rotation)
            {
                position = default;
                rotation = Quaternion.identity;
                if (Position.controls.Count == 0 || Rotation.controls.Count == 0)
                    return false;
                position = Position.ReadValue<Vector3>();
                rotation = Rotation.ReadValue<Quaternion>();
                return IsValidRotation(rotation);
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
        // Hand tracking comes in two flavours: Meta's aim pose from the XR Hands package ("Meta Hand Tracking Aim",
        // device MetaAimHand: devicePosition/Rotation = aim, pinchStrengthIndex) and the standard OpenXR Hand
        // Interaction Profile (HandInteraction: pointer pose, pinchValue). Both are bound; whichever the runtime offers works.
        const string k_LeftHand = "<HandInteraction>{LeftHand}";
        const string k_RightHand = "<HandInteraction>{RightHand}";
        const string k_LeftMeta = "<MetaAimHand>{LeftHand}";
        const string k_RightMeta = "<MetaAimHand>{RightHand}";
        readonly PoseActions m_LeftAim = new PoseActions("<XRController>{LeftHand}", "pointerPosition", "pointerRotation",
            k_LeftHand + "/pointerPosition|" + k_LeftHand + "/pointerRotation", k_LeftMeta + "/devicePosition|" + k_LeftMeta + "/deviceRotation");
        readonly PoseActions m_RightAim = new PoseActions("<XRController>{RightHand}", "pointerPosition", "pointerRotation",
            k_RightHand + "/pointerPosition|" + k_RightHand + "/pointerRotation", k_RightMeta + "/devicePosition|" + k_RightMeta + "/deviceRotation");
        readonly PoseActions m_LeftGrip = new PoseActions("<XRController>{LeftHand}", "devicePosition", "deviceRotation",
            k_LeftHand + "/devicePosition|" + k_LeftHand + "/deviceRotation", k_LeftMeta + "/devicePosition|" + k_LeftMeta + "/deviceRotation");
        readonly PoseActions m_RightGrip = new PoseActions("<XRController>{RightHand}", "devicePosition", "deviceRotation",
            k_RightHand + "/devicePosition|" + k_RightHand + "/deviceRotation", k_RightMeta + "/devicePosition|" + k_RightMeta + "/deviceRotation");

        // Trigger as an analog value: more reliable than a single "pressed" flag, and a light pull is enough.
        // With hand tracking the pinch (thumb + index finger) is the trigger.
        readonly InputAction m_LeftTrigger = Axis("<XRController>{LeftHand}/trigger", k_LeftHand + "/pinchValue", k_LeftMeta + "/pinchStrengthIndex");
        readonly InputAction m_RightTrigger = Axis("<XRController>{RightHand}/trigger", k_RightHand + "/pinchValue", k_RightMeta + "/pinchStrengthIndex");

        // A fist (grasp) counts as the grip button with hand tracking (push-to-talk).
        readonly InputAction m_LeftGrasp = Axis(k_LeftHand + "/graspValue");
        readonly InputAction m_RightGrasp = Axis(k_RightHand + "/graspValue");

        /// <summary>The hand is tracked as a hand (no controller in it).</summary>
        public bool IsHand(bool left) => (left ? m_LeftAim : m_RightAim).FromHand;
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
            if ((left ? m_LeftAim : m_RightAim).TryRead(out position, out rotation))
                return true;
            var device = Device(left);
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
            if ((left ? m_LeftGrip : m_RightGrip).TryRead(out position, out rotation))
                return true;
            return TryLegacyPose(Device(left), XRCommonUsages.devicePosition, XRCommonUsages.deviceRotation, out position, out rotation);
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
            if (!down && Device(left).TryGetFeatureValue(XRCommonUsages.triggerButton, out bool pressed) && pressed)
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
