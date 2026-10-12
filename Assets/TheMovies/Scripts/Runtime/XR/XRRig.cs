using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;

namespace TheMovies.XR
{
    /// <summary>
    /// Camera + controllers. Builds itself at runtime, follows the headset when XR is running and
    /// falls back to mouse/keyboard controls on a desktop (useful for testing in the editor).
    /// </summary>
    public sealed class XRRig : MonoBehaviour
    {
        /// <summary>Eye height used when there is no headset or no floor tracking.</summary>
        public const float SeatedEyeHeight = 1.2f;

        /// <summary>Extra eye height chosen by the user (menu "Sitzhöhe"), in meters.</summary>
        public float HeightOffset { get; private set; }

        public static XRRig Instance { get; private set; }

        /// <summary>Toggle the menu (menu button, B/Y, or M on the keyboard).</summary>
        public event Action MenuToggleRequested;

        /// <summary>Push-to-talk button held: a grip button on Quest, V on the PC.</summary>
        public bool TalkHeld { get; private set; }

        /// <summary>Play/pause shortcut (A/X, or Space).</summary>
        public event Action PlayPauseRequested;

        /// <summary>Seek shortcut in seconds (thumbstick left/right, or arrow keys).</summary>
        public event Action<double> SeekStepRequested;

        public Camera Camera { get; private set; }
        public Transform Head { get; private set; }
        public Transform LeftGrip { get; private set; }
        public Transform RightGrip { get; private set; }
        public bool XRActive { get; private set; }

        /// <summary>Floor position and facing of the current seat.</summary>
        public Pose Seat => m_Seat;
        public bool LeftTracked { get; private set; }
        public bool RightTracked { get; private set; }

        /// <summary>True once the head pose is known (immediately on a desktop).</summary>
        public bool HeadReady => !XRActive || m_HeadTrackedSince >= 0;

        /// <summary>
        /// True once tracking has settled and the user was placed on the seat. The very first head poses after
        /// start are placeholders; anything positioned relative to the head before this ends up in the wrong place.
        /// </summary>
        public bool Ready => !XRActive || (m_HasAligned && Time.unscaledTime - m_HeadTrackedSince > 1.0f);

        /// <summary>Raised after the user was (re)placed on the seat, e.g. after a recenter.</summary>
        public event Action Aligned;

        bool m_HasAligned;
        bool m_SecondAlignDone;

        /// <summary>Vertical thumbstick input of either controller (-1..1).</summary>
        public float StickY { get; private set; }

        /// <summary>While true, the thumbstick does not seek (e.g. while it moves the menu).</summary>
        public bool SuppressStickSeek { get; set; }

        /// <summary>Show the laser rays even when they point at nothing (true while the menu is open).</summary>
        public bool ShowRays { get; set; } = true;

        Transform m_Offset;
        Transform m_LeftAim;
        HandView m_LeftHandView, m_RightHandView;
        Material m_MaskMaterial;
        bool m_Masking;

        /// <summary>Show the real hands and controllers through passthrough instead of models (when passthrough is available).</summary>
        public bool RealHands { get; set; } = true;

        /// <summary>The real hands/controllers are shown right now (passthrough holes).</summary>
        public bool ShowingRealHands => m_Masking;
        readonly Pose[] m_Joints = new Pose[HandView.JointCount];
        Transform m_RightAim;
        LaserPointer m_LeftPointer;
        LaserPointer m_RightPointer;
        LaserPointer m_MousePointer;
        XRInputs m_Inputs;
        bool m_RightIsActive = true;

        Pose m_Seat = new Pose(Vector3.zero, Quaternion.identity);
        bool m_AlignPending = true;
        float m_HeadTrackedSince = -1;
        bool m_FloorOrigin;
        readonly List<XRInputSubsystem> m_Subsystems = new List<XRInputSubsystem>();

        float m_DesktopYaw;
        float m_DesktopPitch;
        int m_StickDirection;

        public static XRRig Create()
        {
            var go = new GameObject("XR Rig");
            return go.AddComponent<XRRig>();
        }

        void Awake()
        {
            Instance = this;
            m_Inputs = new XRInputs();

            m_Offset = new GameObject("Camera Offset").transform;
            m_Offset.SetParent(transform, false);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(m_Offset, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.nearClipPlane = 0.03f;
            Camera.farClipPlane = 250f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            // No HDR: cheaper on the Quest, and an HDR buffer has no alpha channel, which passthrough needs.
            Camera.allowHDR = false;
            Camera.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            Head = camGo.transform;

            LeftGrip = CreateHand("Left Hand", true, out m_LeftAim);
            RightGrip = CreateHand("Right Hand", false, out m_RightAim);
            m_LeftHandView = HandView.Create(m_Offset, "Left Tracked Hand");
            m_RightHandView = HandView.Create(m_Offset, "Right Tracked Hand");
            m_LeftPointer = LaserPointer.Create(m_LeftAim, "Left Pointer", true);
            m_RightPointer = LaserPointer.Create(m_RightAim, "Right Pointer", true);
            m_MousePointer = LaserPointer.Create(transform, "Mouse Pointer", false);
            m_LeftPointer.HoverEntered += () => m_Inputs.Haptic(true, 0.2f, 0.015f);
            m_RightPointer.HoverEntered += () => m_Inputs.Haptic(false, 0.2f, 0.015f);

            Application.onBeforeRender += OnBeforeRender;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;
        }

        Transform CreateHand(string name, bool left, out Transform aim)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(m_Offset, false);
            var controller = new GameObject("Controller Model").transform;
            controller.SetParent(hand, false);
            var standIn = new GameObject("Stand-in").transform;
            standIn.SetParent(controller, false);
            ControllerVisual.Build(standIn, left);
            var handModel = new GameObject("Hand Model").transform;
            handModel.SetParent(hand, false);
            ControllerVisual.BuildHand(handModel);
            handModel.gameObject.SetActive(false);
            aim = new GameObject(name + " Aim").transform;
            aim.SetParent(m_Offset, false);
            return hand;
        }

        void Start()
        {
            ConfigureTracking();
            UpdateMode(force: true);
        }

        void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            foreach (var s in m_Subsystems)
                s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            m_Inputs?.Dispose();
            if (Instance == this) Instance = null;
        }

        void ConfigureTracking()
        {
            SubsystemManager.GetSubsystems(m_Subsystems);
            m_FloorOrigin = false;
            foreach (var s in m_Subsystems)
            {
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                    m_FloorOrigin |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                s.trackingOriginUpdated -= OnTrackingOriginUpdated;
                s.trackingOriginUpdated += OnTrackingOriginUpdated;
            }
        }

        // The user long-pressed the Meta button to recenter: line up with the seat again.
        void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => m_AlignPending = true;

        /// <summary>Places the user on a seat (floor position, facing the screen).</summary>
        public void SetSeat(Vector3 floorPosition, Quaternion facing)
        {
            m_Seat = new Pose(floorPosition, facing);
            m_AlignPending = true;
            if (!XRActive)
                Align();
        }

        /// <summary>Re-centers the user on their seat, looking at the screen.</summary>
        public void RequestAlign() => m_AlignPending = true;

        void Align()
        {
            m_AlignPending = false;
            if (!XRActive)
            {
                transform.SetPositionAndRotation(m_Seat.position, m_Seat.rotation);
                return;
            }

            // Rotate so the current head yaw faces the screen, then shift so the head is above the seat.
            Vector3 headLocal = Head.localPosition;
            float headYaw = Head.localEulerAngles.y;
            Quaternion rigRotation = m_Seat.rotation * Quaternion.Euler(0f, -headYaw, 0f);
            Vector3 flatHead = rigRotation * new Vector3(headLocal.x, 0f, headLocal.z);
            transform.SetPositionAndRotation(m_Seat.position - flatHead, rigRotation);
            ApplyHeight();
            m_HasAligned = true;
            Aligned?.Invoke();
        }

        /// <summary>
        /// Puts the eyes at a cinema seat's eye height regardless of how the user sits or stands at home
        /// (and regardless of how well the Quest's floor level is calibrated).
        /// </summary>
        void ApplyHeight()
        {
            if (!XRActive) return;
            float headAboveRig = m_Offset.localPosition.y + Head.localPosition.y;
            Vector3 p = transform.position;
            p.y = m_Seat.position.y + SeatedEyeHeight + HeightOffset - headAboveRig;
            transform.position = p;
        }

        public void SetHeightOffset(float offset)
        {
            HeightOffset = Mathf.Clamp(offset, -0.5f, 0.8f);
            if (XRActive) ApplyHeight();
            else Head.localPosition = new Vector3(0, SeatedEyeHeight + HeightOffset, 0);
        }

        void UpdateMode(bool force = false)
        {
            bool xr = XRSettings.isDeviceActive;
            if (xr == XRActive && !force) return;
            XRActive = xr;
            if (xr)
            {
                ConfigureTracking();
                m_Offset.localPosition = new Vector3(0, m_FloorOrigin ? 0f : SeatedEyeHeight, 0);
                m_HeadTrackedSince = -1;
                m_HasAligned = false;
                m_SecondAlignDone = false;
            }
            else
            {
                m_Offset.localPosition = Vector3.zero;
                Head.localPosition = new Vector3(0, SeatedEyeHeight + HeightOffset, 0);
                Head.localRotation = Quaternion.identity;
                m_DesktopYaw = 0;
                m_DesktopPitch = 0;
            }
            LeftGrip.gameObject.SetActive(false);
            RightGrip.gameObject.SetActive(false);
            m_AlignPending = true;
        }

        void Update()
        {
            UpdateMode();
            if (XRActive)
                UpdateXR();
            else
                UpdateDesktop();
        }

        // Poses are read again right before rendering for minimal latency: once when the Input System
        // has done its before-render update (OpenXR devices) and once in onBeforeRender (fallback API).
        void OnAfterInputUpdate()
        {
            if (XRActive && InputState.currentUpdateType == InputUpdateType.BeforeRender)
                ReadPoses();
        }

        void OnBeforeRender()
        {
            if (XRActive)
                ReadPoses();
        }

        // The headset's own controller models (XR_FB_render_model); retried until the controllers are connected.
        readonly bool[] m_OfficialModel = new bool[2];
        readonly bool[] m_OfficialLoading = new bool[2];
        readonly float[] m_NextModelTry = new float[2];
        readonly int[] m_ModelTries = new int[2];

        /// <summary>For the status line: whether the official controller models are shown.</summary>
        public string ControllerModelStatus { get; private set; } = "Ersatzmodell";

        void TryLoadControllerModels()
        {
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                if (m_OfficialModel[i] || m_OfficialLoading[i] || m_ModelTries[i] >= 20 || Time.unscaledTime < m_NextModelTry[i]) continue;
                if (!(left ? LeftTracked : RightTracked) || m_Inputs.IsHand(left)) continue; // only with the controller in hand
                m_OfficialLoading[i] = true;
                m_ModelTries[i]++;
                int index = i;
                ControllerVisual.LoadOfficial(left, this, (mesh, error) =>
                {
                    m_OfficialLoading[index] = false;
                    if (mesh == null)
                    {
                        m_NextModelTry[index] = Time.unscaledTime + 3f;
                        ControllerModelStatus = "Ersatzmodell (" + error + ")";
                        if (m_ModelTries[index] == 1) Debug.Log("[TheMovies] Controller model not loaded yet: " + error);
                        return;
                    }
                    m_OfficialModel[index] = true;
                    ControllerModelStatus = "Meta-Modell";
                    var container = (index == 0 ? LeftGrip : RightGrip).Find("Controller Model");
                    if (container != null)
                    {
                        ControllerVisual.ShowOfficial(container, mesh);
                        if (m_Masking)
                            foreach (var r in container.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m_MaskMaterial;
                    }
                    Debug.Log("[TheMovies] Showing the headset's controller model (" + (index == 0 ? "left" : "right") + ").");
                });
            }
        }

        /// <summary>Hands and controllers as holes into the picture with passthrough behind them – you see your real ones.</summary>
        void UpdateRealHands()
        {
            bool available = false;
#if THEMOVIES_OPENXR
            available = PassthroughFeature.Ready;
#endif
            bool masking = XRActive && RealHands && available;
            if (masking != m_Masking)
            {
                m_Masking = masking;
                if (m_MaskMaterial == null) m_MaskMaterial = new Material(TheMovies.UI.UIFactory.FindShader("TheMovies/PassthroughMask"));
                m_LeftHandView.SetMask(masking, m_MaskMaterial);
                m_RightHandView.SetMask(masking, m_MaskMaterial);
                foreach (var grip in new[] { LeftGrip, RightGrip })
                {
                    foreach (var r in grip.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterial = masking ? m_MaskMaterial : TheMovies.World.Shapes.Lit;
                    var container = grip.Find("Controller Model");
                    // A little larger than the controller, so its edges are not cut off.
                    if (container != null) container.localScale = Vector3.one * (masking ? 1.12f : 1f);
                }
            }
#if THEMOVIES_OPENXR
            PassthroughFeature.SetForHands(masking && (LeftTracked || RightTracked));
#endif
        }

        void ReadPoses()
        {
            if (m_Inputs.TryGetHead(out var hp, out var hr))
            {
                Head.localPosition = hp;
                Head.localRotation = hr;
                if (m_HeadTrackedSince < 0) m_HeadTrackedSince = Time.unscaledTime;
            }

            m_Inputs.UpdateSources(new Pose(Head.localPosition, Head.localRotation));
            LeftTracked = ReadHand(true, LeftGrip, m_LeftAim);
            RightTracked = ReadHand(false, RightGrip, m_RightAim);
            if (XRActive) TryLoadControllerModels();
            UpdateRealHands();
        }

        /// <summary>The hand is tracked as a hand (hand tracking) rather than holding a controller.</summary>
        public bool IsHandTracked(bool left) => XRActive && m_Inputs.IsHand(left);

        /// <summary>What the hands are (controller / hand) and which hand tracking runs, for the settings page.</summary>
        public string InputStatus => XRActive ? m_Inputs.SourceStatus + " · Controller: " + ControllerModelStatus : "Ohne Brille (Maus/Tastatur)";

        bool ReadHand(bool left, Transform grip, Transform aim)
        {
            bool tracked = m_Inputs.TryGetGrip(left, out var gp, out var gr);
            bool isHand = m_Inputs.IsHand(left);
            // Hand tracking with joints: draw the real hand shape instead of the simple stand-in.
            var view = left ? m_LeftHandView : m_RightHandView;
            bool jointsShown = isHand && m_Inputs.TryGetHandJoints(left, m_Joints);
            if (jointsShown) view.Apply(m_Joints);
            if (view.gameObject.activeSelf != jointsShown) view.gameObject.SetActive(jointsShown);
            var controllerModel = grip.Find("Controller Model");
            var handModel = grip.Find("Hand Model");
            if (controllerModel != null && controllerModel.gameObject.activeSelf == isHand) controllerModel.gameObject.SetActive(!isHand);
            bool standIn = isHand && !jointsShown;
            if (handModel != null && handModel.gameObject.activeSelf != standIn) handModel.gameObject.SetActive(standIn);
            if (tracked)
            {
                grip.localPosition = gp;
                grip.localRotation = gr;
            }
            if (grip.gameObject.activeSelf != tracked)
                grip.gameObject.SetActive(tracked);

            if (m_Inputs.TryGetAim(left, out var ap, out var ar))
            {
                aim.localPosition = ap;
                aim.localRotation = ar;
                return true;
            }
            return false;
        }

        void UpdateXR()
        {
            ReadPoses();
            m_Inputs.UpdateButtons();

            // Head tracking needs a moment after startup before alignment makes sense.
            // Align once tracking runs, and once more a bit later when the first poses have settled.
            if (!m_SecondAlignDone && m_HasAligned && Time.unscaledTime - m_HeadTrackedSince > 1.5f)
            {
                m_SecondAlignDone = true;
                m_AlignPending = true;
            }
            if (m_AlignPending && m_HeadTrackedSince >= 0 && Time.unscaledTime - m_HeadTrackedSince > 0.3f)
                Align();

            bool leftDown = m_Inputs.IsPressed("LTrigger");
            bool rightDown = m_Inputs.IsPressed("RTrigger");
            if (m_Inputs.WasPressed("LTrigger")) m_RightIsActive = false;
            if (m_Inputs.WasPressed("RTrigger")) m_RightIsActive = true;
            if (m_RightIsActive && !RightTracked && LeftTracked) m_RightIsActive = false;
            if (!m_RightIsActive && !LeftTracked && RightTracked) m_RightIsActive = true;

            // Both hands can always click; the ray of the hand used last is drawn permanently,
            // the other one only while it points at something.
            m_LeftPointer.Tick(LeftTracked, new Ray(m_LeftAim.position, m_LeftAim.forward), leftDown, true, ShowRays && !m_RightIsActive);
            m_RightPointer.Tick(RightTracked, new Ray(m_RightAim.position, m_RightAim.forward), rightDown, true, ShowRays && m_RightIsActive);
            m_MousePointer.Tick(false, default, false, false, false);

            TalkHeld = m_Inputs.IsPressed("LGrip") || m_Inputs.IsPressed("RGrip");
            if (m_Inputs.WasPressed("LMenu") || m_Inputs.WasPressed("RMenu") ||
                m_Inputs.WasPressed("LSecondary") || m_Inputs.WasPressed("RSecondary"))
                MenuToggleRequested?.Invoke();
            if (m_Inputs.WasPressed("RPrimary") || m_Inputs.WasPressed("LPrimary"))
                PlayPauseRequested?.Invoke();

            Vector2 rightStick = m_Inputs.Thumbstick(false), leftStick = m_Inputs.Thumbstick(true);
            StickY = Mathf.Abs(rightStick.y) >= Mathf.Abs(leftStick.y) ? rightStick.y : leftStick.y;

            // Thumbstick flick left/right = seek 10 s
            float x = rightStick.x;
            if (Mathf.Abs(x) < 0.1f) x = leftStick.x;
            if (SuppressStickSeek) x = 0f;
            int dir = x > 0.75f ? 1 : x < -0.75f ? -1 : 0;
            if (dir != 0 && m_StickDirection == 0)
                SeekStepRequested?.Invoke(dir * 10.0);
            if (Mathf.Abs(x) < 0.3f) m_StickDirection = 0;
            else if (dir != 0) m_StickDirection = dir;
        }

        void UpdateDesktop()
        {
            if (m_AlignPending)
                Align();

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            TalkHeld = keyboard != null && keyboard.vKey.isPressed;

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                m_DesktopYaw += delta.x * 0.15f;
                m_DesktopPitch = Mathf.Clamp(m_DesktopPitch - delta.y * 0.15f, -80f, 80f);
                Head.localRotation = Quaternion.Euler(m_DesktopPitch, m_DesktopYaw, 0f);
            }

            m_LeftPointer.Tick(false, default, false, false, false);
            m_RightPointer.Tick(false, default, false, false, false);
            if (TryGetScreenPointer(out Vector2 screenPos, out bool pressed))
                m_MousePointer.Tick(true, Camera.ScreenPointToRay(screenPos), pressed, true, false);
            else
                m_MousePointer.Tick(false, default, false, false, false);

            if (keyboard != null)
            {
                if (keyboard.mKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame)
                    MenuToggleRequested?.Invoke();
                if (keyboard.spaceKey.wasPressedThisFrame)
                    PlayPauseRequested?.Invoke();
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                    SeekStepRequested?.Invoke(-10);
                if (keyboard.rightArrowKey.wasPressedThisFrame)
                    SeekStepRequested?.Invoke(10);
            }
        }

        /// <summary>
        /// Mouse on a PC; touch or the system laser when the app runs as a flat 2D window on the Quest
        /// (which happens when XR is not enabled in the build).
        /// </summary>
        static bool TryGetScreenPointer(out Vector2 position, out bool pressed)
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                position = touch.primaryTouch.position.ReadValue();
                pressed = true;
                return true;
            }

            var pointer = Pointer.current;
            if (pointer != null)
            {
                position = pointer.position.ReadValue();
                pressed = pointer.press.isPressed;
                return true;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            position = Input.mousePosition;
            pressed = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (Input.touchCount > 0)
                position = Input.GetTouch(0).position;
            return true;
#else
            position = default;
            pressed = false;
            return false;
#endif
        }

        /// <summary>World space poses for the avatar network sync.</summary>
        public void GetTrackedPoses(out Pose head, out Pose left, out Pose right)
        {
            head = new Pose(Head.position, Head.rotation);
            left = new Pose(LeftGrip.position, LeftGrip.rotation);
            right = new Pose(RightGrip.position, RightGrip.rotation);
        }
    }
}
