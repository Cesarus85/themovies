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
    /// </summary>
    public sealed class XRInputs
    {
        sealed class PoseActions
        {
            public InputAction Position;
            public InputAction Rotation;

            public PoseActions(string device, string position, string rotation)
            {
                Position = new InputAction(type: InputActionType.Value, binding: $"{device}/{position}", expectedControlType: "Vector3");
                Rotation = new InputAction(type: InputActionType.Value, binding: $"{device}/{rotation}", expectedControlType: "Quaternion");
                Position.Enable();
                Rotation.Enable();
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
        readonly PoseActions m_LeftAim = new PoseActions("<XRController>{LeftHand}", "pointerPosition", "pointerRotation");
        readonly PoseActions m_RightAim = new PoseActions("<XRController>{RightHand}", "pointerPosition", "pointerRotation");
        readonly PoseActions m_LeftGrip = new PoseActions("<XRController>{LeftHand}", "devicePosition", "deviceRotation");
        readonly PoseActions m_RightGrip = new PoseActions("<XRController>{RightHand}", "devicePosition", "deviceRotation");

        readonly Dictionary<string, bool> m_PreviousButtons = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> m_CurrentButtons = new Dictionary<string, bool>();

        public void Dispose()
        {
            m_Head.Dispose();
            m_LeftAim.Dispose();
            m_RightAim.Dispose();
            m_LeftGrip.Dispose();
            m_RightGrip.Dispose();
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

            Sample("LTrigger", true, XRCommonUsages.triggerButton);
            Sample("RTrigger", false, XRCommonUsages.triggerButton);
            Sample("LPrimary", true, XRCommonUsages.primaryButton);
            Sample("RPrimary", false, XRCommonUsages.primaryButton);
            Sample("LSecondary", true, XRCommonUsages.secondaryButton);
            Sample("RSecondary", false, XRCommonUsages.secondaryButton);
            Sample("LMenu", true, XRCommonUsages.menuButton);
            Sample("RMenu", false, XRCommonUsages.menuButton);
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
