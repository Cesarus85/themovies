// API stubs mirroring the Input System package as used here.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }

    public static class InputSystem
    {
        public static event Action onAfterUpdate;
    }

    public class InputControl { }

    public struct ReadOnlyArray<T> { public int Count => 0; public T this[int index] => default; }

    public sealed class InputAction : IDisposable
    {
        public InputAction(string name = null, InputActionType type = default, string binding = null,
            string interactions = null, string processors = null, string expectedControlType = null) { }
        public ReadOnlyArray<InputControl> controls => default;
        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }
        public TValue ReadValue<TValue>() where TValue : struct => default;
        public bool IsPressed() => false;
        public bool WasPressedThisFrame() => false;
    }

    namespace Controls
    {
        public class ButtonControl : InputControl
        {
            public bool isPressed => false;
            public bool wasPressedThisFrame => false;
            public bool wasReleasedThisFrame => false;
        }
        public class KeyControl : ButtonControl { }
        public class Vector2Control : InputControl { public Vector2 ReadValue() => default; }
    }

    public class Pointer
    {
        public static Pointer current => null;
        public Controls.Vector2Control position => null;
        public Controls.ButtonControl press => null;
    }

    namespace Controls
    {
        public class TouchControl : InputControl
        {
            public ButtonControl press => null;
            public Vector2Control position => null;
        }
    }

    public class Touchscreen : Pointer
    {
        public static new Touchscreen current => null;
        public Controls.TouchControl primaryTouch => null;
    }

    public class Mouse
    {
        public static Mouse current => null;
        public Controls.ButtonControl leftButton => null;
        public Controls.ButtonControl rightButton => null;
        public Controls.Vector2Control position => null;
        public Controls.Vector2Control delta => null;
    }

    public class Keyboard
    {
        public static Keyboard current => null;
        public Controls.KeyControl spaceKey => null;
        public Controls.KeyControl mKey => null;
        public Controls.KeyControl tabKey => null;
        public Controls.KeyControl leftArrowKey => null;
        public Controls.KeyControl rightArrowKey => null;
        public Controls.KeyControl escapeKey => null;
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    [Flags]
    public enum InputUpdateType { None = 0, Dynamic = 1, Fixed = 2, BeforeRender = 4, Editor = 8, Manual = 16 }

    public static class InputState
    {
        public static InputUpdateType currentUpdateType => default;
    }
}
