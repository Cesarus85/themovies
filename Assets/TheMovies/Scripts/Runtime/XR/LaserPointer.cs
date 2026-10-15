using System;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.XR
{
    /// <summary>
    /// Ray based pointer for the 3D menu. Driven explicitly by <see cref="XRRig"/> via <see cref="Tick"/>,
    /// so the rig decides which hand is active. The mouse uses the same class without a visible ray.
    /// </summary>
    public sealed class LaserPointer : MonoBehaviour
    {
        public const float MaxDistance = 30f; // far enough for the boxes and the balcony when choosing a seat

        /// <summary>Raised when the trigger is pressed while pointing at nothing.</summary>
        public static event Action PressedOnNothing;

        /// <summary>Raised when the pointer starts hovering a target (used for haptics).</summary>
        public event Action HoverEntered;

        LineRenderer m_Line;
        Transform m_Cursor;
        Material m_LineMaterial;
        IPointerTarget m_Hover;
        IPointerTarget m_Pressed;
        bool m_WasDown;
        bool m_ShowVisual = true;

        public bool IsOverTarget => m_Hover != null;

        public static LaserPointer Create(Transform parent, string name, bool visible)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var pointer = go.AddComponent<LaserPointer>();
            pointer.m_ShowVisual = visible;
            if (visible)
                pointer.BuildVisuals();
            return pointer;
        }

        void BuildVisuals()
        {
            m_LineMaterial = new Material(UIFactory.FindShader("TheMovies/UIPanel")) { renderQueue = 3100 };
            m_LineMaterial.color = new Color(1f, 1f, 1f, 0.55f);

            m_Line = gameObject.AddComponent<LineRenderer>();
            m_Line.sharedMaterial = m_LineMaterial;
            m_Line.useWorldSpace = true;
            m_Line.positionCount = 2;
            m_Line.startWidth = 0.004f;
            m_Line.endWidth = 0.002f;
            m_Line.numCapVertices = 2;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            m_Line.enabled = false;

            var cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursor.name = "Cursor";
            Destroy(cursor.GetComponent<Collider>());
            cursor.transform.SetParent(transform, false);
            cursor.transform.localScale = Vector3.one * 0.012f;
            var mr = cursor.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m_LineMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Cursor = cursor.transform;
            m_Cursor.gameObject.SetActive(false);
        }

        /// <param name="valid">False if there is no tracked ray this frame.</param>
        /// <param name="ray">World space ray.</param>
        /// <param name="down">Trigger / mouse button state.</param>
        /// <param name="active">Inactive pointers hide their ray and release everything.</param>
        /// <param name="showRay">Draw the ray even when nothing is hit (e.g. while the menu is open).</param>
        public void Tick(bool valid, Ray ray, bool down, bool active, bool showRay)
        {
            if (!active || !valid)
            {
                ReleaseAll();
                SetVisual(false, default, default, false);
                m_WasDown = down;
                return;
            }

            IPointerTarget target = null;
            Vector3 end = ray.origin + ray.direction * 2.5f;
            bool hit = Physics.Raycast(ray, out RaycastHit info, MaxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hit)
            {
                target = info.collider.GetComponentInParent<IPointerTarget>();
                end = info.point;
            }

            // Hover
            if (!ReferenceEquals(target, m_Hover))
            {
                if (IsAlive(m_Hover)) m_Hover.OnPointerExit();
                m_Hover = target;
                if (m_Hover != null)
                {
                    m_Hover.OnPointerEnter();
                    HoverEntered?.Invoke();
                }
            }

            // Press / drag / release
            bool pressedNow = down && !m_WasDown;
            bool releasedNow = !down && m_WasDown;

            if (pressedNow)
            {
                if (target != null)
                {
                    m_Pressed = target;
                    target.OnPointerDown(ray, info.point);
                }
                else
                {
                    PressedOnNothing?.Invoke();
                }
            }
            else if (down && IsAlive(m_Pressed))
            {
                m_Pressed.OnPointerDrag(ray);
            }

            if (releasedNow && m_Pressed != null)
            {
                var pressed = m_Pressed;
                m_Pressed = null;
                if (IsAlive(pressed))
                    pressed.OnPointerUp(ReferenceEquals(pressed, target));
            }

            m_WasDown = down;
            SetVisual(showRay || target != null || m_Pressed != null, ray.origin, end, target != null);
        }

        void SetVisual(bool visible, Vector3 start, Vector3 end, bool cursor)
        {
            if (!m_ShowVisual) return;
            m_Line.enabled = visible;
            if (visible)
            {
                m_Line.SetPosition(0, start);
                m_Line.SetPosition(1, end);
            }
            m_Cursor.gameObject.SetActive(visible && cursor);
            if (visible && cursor)
                m_Cursor.position = end;
        }

        void ReleaseAll()
        {
            if (IsAlive(m_Hover)) m_Hover.OnPointerExit();
            m_Hover = null;
            if (IsAlive(m_Pressed)) m_Pressed.OnPointerUp(false);
            m_Pressed = null;
        }

        void OnDisable() => ReleaseAll();

        void OnDestroy()
        {
            if (m_LineMaterial != null) Destroy(m_LineMaterial);
        }

        // Targets are MonoBehaviours that may have been destroyed (e.g. menu page rebuilt).
        static bool IsAlive(IPointerTarget t) => t != null && !(t is UnityEngine.Object o && o == null);
    }
}
