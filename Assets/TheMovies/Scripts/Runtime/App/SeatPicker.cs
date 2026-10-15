using System;
using System.Collections.Generic;
using TheMovies.Net;
using TheMovies.UI;
using TheMovies.World;
using TheMovies.XR;
using UnityEngine;

namespace TheMovies.App
{
    /// <summary>
    /// Free seat choice: shows a glowing disc above every free seat of the current room (all seats of a room model,
    /// including balcony and boxes); pointing at one and clicking moves the viewer there. The others see the avatar on
    /// that seat if they are in the same room (see NetworkPlayer.SeatChoice), otherwise on its standard seat.
    /// </summary>
    public sealed class SeatPicker : MonoBehaviour
    {
        const float k_MarkerHeight = 0.62f; // above the floor: over the seat cushion
        const float k_Occupied = 0.35f;     // a spot this close to someone else's seat is taken

        CinemaEnvironment m_Cinema;
        XRRig m_Rig;
        Func<int> m_Room;
        Material m_Free, m_Hover;
        readonly List<GameObject> m_Markers = new List<GameObject>();
        Vector3? m_Chosen;
        int m_ChosenRoom = -1;

        /// <summary>The seat discs are shown.</summary>
        public bool Active { get; private set; }

        /// <summary>Raised when the viewer moved to another seat (or back to the standard seat).</summary>
        public event Action SeatChanged;

        public static SeatPicker Create(CinemaEnvironment cinema, XRRig rig, Func<int> room)
        {
            var picker = new GameObject("Seat Picker").AddComponent<SeatPicker>();
            picker.m_Cinema = cinema;
            picker.m_Rig = rig;
            picker.m_Room = room;
            NetworkPlayer.RosterChanged += picker.Publish; // tell a new session (or newcomers) where we sit
            return picker;
        }

        void OnDestroy() => NetworkPlayer.RosterChanged -= Publish;

        /// <summary>True if the viewer sits on a seat of their own choosing in the current room.</summary>
        public bool HasOwnSeat => m_Chosen.HasValue && m_ChosenRoom == m_Room();

        public void Begin()
        {
            if (Active) return;
            Active = true;
            EnsureMaterials();

            var taken = new List<Vector3>();
            int room = m_Room();
            foreach (var p in NetworkPlayer.All)
                if (p != NetworkPlayer.Local && p.SeatChoice.Room == room)
                    taken.Add(p.SeatPosition);
            Vector3 mine = HasOwnSeat ? m_Chosen.Value : StandardSeat().position;

            foreach (var spot in m_Cinema.SeatSpots)
            {
                if (Near(spot.position, mine) || taken.Exists(t => Near(spot.position, t))) continue;
                m_Markers.Add(CreateMarker(spot));
            }
            Debug.Log($"[TheMovies] Seat choice: {m_Markers.Count} free seats shown.");
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            foreach (var marker in m_Markers)
                if (marker != null) Destroy(marker);
            m_Markers.Clear();
        }

        /// <summary>Back to the standard seat of this participant (e.g. after switching rooms or leaving a session).</summary>
        public void ResetToStandardSeat()
        {
            End();
            m_Chosen = null;
            m_ChosenRoom = -1;
            var seat = StandardSeat();
            m_Rig.SetSeat(seat.position, seat.rotation);
            Publish();
            SeatChanged?.Invoke();
        }

        /// <summary>Tells the others the current room and seat (also called whenever the roster changes).</summary>
        public void Publish()
        {
            var local = NetworkPlayer.Local;
            if (local == null) return;
            int room = m_Room();
            local.SetSeatChoice(room, HasOwnSeat ? m_Chosen : null);
            // A session that started after the seat was chosen assigned the standard seat: sit back down on ours.
            if (HasOwnSeat && (m_Rig.Seat.position - m_Chosen.Value).sqrMagnitude > 0.01f)
                m_Rig.SetSeat(m_Chosen.Value, FacingOf(m_Chosen.Value));
        }

        void Choose(Pose spot)
        {
            m_Chosen = spot.position;
            m_ChosenRoom = m_Room();
            m_Rig.SetSeat(spot.position, spot.rotation);
            End();
            Publish();
            SeatChanged?.Invoke();
            Debug.Log($"[TheMovies] Seat chosen at {spot.position}.");
        }

        Pose StandardSeat()
        {
            var local = NetworkPlayer.Local;
            return CinemaEnvironment.SeatPose(local != null && local.Seat >= 0 ? local.Seat : 1);
        }

        Quaternion FacingOf(Vector3 position)
        {
            foreach (var spot in m_Cinema.SeatSpots)
                if (Near(spot.position, position)) return spot.rotation;
            return Quaternion.identity;
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < k_Occupied * k_Occupied;

        void EnsureMaterials()
        {
            if (m_Free != null) return;
            m_Free = Glow(new Color(0.35f, 0.85f, 1f));
            m_Hover = Glow(new Color(1f, 0.82f, 0.35f));
        }

        static Material Glow(Color color)
        {
            var m = new Material(UIFactory.FindShader("TheMovies/Unlit")) { color = color };
            m.SetFloat("_Shading", 0.25f);
            m.SetFloat("_RoomLightAffect", 0f);
            m.SetFloat("_ScreenGlow", 0f);
            return m;
        }

        GameObject CreateMarker(Pose spot)
        {
            var go = new GameObject("Seat Marker");
            go.transform.SetParent(transform, false);
            go.transform.position = m_Cinema.transform.TransformPoint(spot.position + Vector3.up * k_MarkerHeight);
            var disc = Shapes.Create(PrimitiveType.Sphere, go.transform, "Disc", Vector3.zero, new Vector3(0.3f, 0.05f, 0.3f), Color.white, m_Free);
            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.28f;
            var marker = go.AddComponent<SeatMarker>();
            marker.Init(disc, m_Free, m_Hover, () => Choose(spot));
            return go;
        }

        /// <summary>One seat disc: grows and turns gold under the pointer, a click sits down there.</summary>
        sealed class SeatMarker : MonoBehaviour, IPointerTarget
        {
            MeshRenderer m_Disc;
            Material m_Normal, m_Hover;
            Action m_Chosen;

            public void Init(MeshRenderer disc, Material normal, Material hover, Action chosen)
            {
                m_Disc = disc;
                m_Normal = normal;
                m_Hover = hover;
                m_Chosen = chosen;
            }

            public void OnPointerEnter()
            {
                m_Disc.sharedMaterial = m_Hover;
                m_Disc.transform.localScale = new Vector3(0.42f, 0.07f, 0.42f);
            }

            public void OnPointerExit()
            {
                m_Disc.sharedMaterial = m_Normal;
                m_Disc.transform.localScale = new Vector3(0.3f, 0.05f, 0.3f);
            }

            public void OnPointerDown(Ray ray, Vector3 hitPoint)
            {
            }

            public void OnPointerDrag(Ray ray)
            {
            }

            public void OnPointerUp(bool releasedInside)
            {
                if (releasedInside) m_Chosen?.Invoke();
            }
        }
    }
}
