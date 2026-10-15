using System.Collections.Generic;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// The second room: a cozy living room ("Heimkino") with a four-seat sofa, rug, coffee table, fireplace,
    /// bookshelf, floor lamps and a window to the night sky; the screen hangs on the front wall like a big
    /// projection screen. The sofa seats are exactly where the cinema seats are, so the avatars of the others sit
    /// correctly whichever room each participant has chosen (the room is a personal setting).
    /// </summary>
    public sealed partial class CinemaEnvironment
    {
        public enum Room
        {
            Cinema = 0,
            Home = 1,
            Rooftop = 2,
        }

        public static readonly string[] RoomNames = { "Kino", "Heimkino", "Dachterrasse" };

        /// <summary>
        /// Rooms that exist only as a model (no built-in version): prefab under Resources and where the screen hangs if the
        /// model has no ScreenTop marker. A room is offered once its prefab is in the build.
        /// </summary>
        static readonly Dictionary<Room, (string prefab, ScreenPlacement defaults)> k_ModelRooms =
            new Dictionary<Room, (string, ScreenPlacement)>
            {
                // Open air: screen on a wall 7–10 m away, picture 3.5–9.5 m wide.
                { Room.Rooftop, ("TheMovies/CustomRooftop", new ScreenPlacement(new Vector3(0, 1f, 9f), 0.5f, 4f)) },
            };

        sealed class ModelRoom
        {
            public Transform Root;
            public ScreenPlacement Placement;
            public readonly List<Pose> Spots = new List<Pose>();
        }

        readonly Dictionary<Room, ModelRoom> m_ModelRooms = new Dictionary<Room, ModelRoom>();
        static readonly Dictionary<Room, bool> s_Available = new Dictionary<Room, bool>();

        /// <summary>Whether a room can be chosen: the cinema and the living room always, the others if their model is in the build.</summary>
        public static bool IsAvailable(Room room)
        {
            if (room == Room.Cinema || room == Room.Home) return true;
            if (!k_ModelRooms.TryGetValue(room, out var definition)) return false;
            if (!s_Available.TryGetValue(room, out bool available))
                s_Available[room] = available = Resources.Load<GameObject>(definition.prefab) != null;
            return available;
        }

        /// <summary>Builds a model-only room on first use; null if its model is missing or unusable.</summary>
        ModelRoom GetModelRoom(Room room)
        {
            if (m_ModelRooms.TryGetValue(room, out var built)) return built;
            if (!k_ModelRooms.TryGetValue(room, out var definition)) return null;
            var prefab = Resources.Load<GameObject>(definition.prefab);
            if (prefab == null) return null;
            var model = new ModelRoom { Root = new GameObject(RoomNames[(int)room]).transform };
            model.Root.SetParent(transform, false);
            if (!BuildCustom(prefab, model.Root, definition.defaults, out model.Placement, model.Spots))
            {
                Destroy(model.Root.gameObject);
                s_Available[room] = false;
                return null;
            }
            m_ModelRooms[room] = model;
            return model;
        }

        Transform RootOf(Room room) =>
            room == Room.Cinema ? m_HallRoot : room == Room.Home ? m_HomeRoot : m_ModelRooms.TryGetValue(room, out var m) ? m.Root : null;

        bool IsRoomRoot(Transform t)
        {
            if (t == m_HallRoot || t == m_HomeRoot) return true;
            foreach (var m in m_ModelRooms.Values)
                if (t == m.Root) return true;
            return false;
        }

        // Living room (floor y = 0 at the sofa, the sofa faces +Z like the cinema seats)
        const float k_HomeHalfWidth = 4.2f;
        const float k_HomeBackZ = -2.6f;
        const float k_HomeFrontZ = 5.2f;
        const float k_HomeCeiling = 2.75f;

        Transform m_HallRoot;
        Transform m_HomeRoot;
        bool m_RoomsAvailable;
        Room m_Room = Room.Cinema;
        Vector4[] m_HallLightPos = new Vector4[0], m_HallLightColor = new Vector4[0];
        Vector4[] m_HomeLightPos, m_HomeLightColor;
        int m_FireLight = -1;
        Color m_FireColor;

        /// <summary>The room shown.</summary>
        public Room CurrentRoom => m_Room;

        public bool RoomsAvailable => m_RoomsAvailable;

        // Projection screen on the living room's front wall: smaller than in the cinema, bottom at sideboard height.
        static readonly ScreenPlacement k_HomePlacement = new ScreenPlacement(new Vector3(0, 0.78f, k_HomeFrontZ - 0.08f), 0.32f, 1.85f);
        ScreenPlacement m_HomePlacement;

        public void SetRoom(Room room)
        {
            if (!m_RoomsAvailable || !IsAvailable(room)) return;
            ScreenPlacement placement;
            if (room == Room.Home)
            {
                if (m_HomeRoot == null) CreateHome();
                SetLights(m_HomeLightPos, m_HomeLightColor);
                placement = m_HomePlacement;
            }
            else if (room == Room.Cinema)
            {
                SetLights(m_HallLightPos, m_HallLightColor);
                placement = m_HallPlacement;
            }
            else
            {
                var model = GetModelRoom(room);
                if (model == null) return;
                SetLights(new Vector4[0], new Vector4[0]); // the light is baked into the model
                placement = model.Placement;
            }
            m_Room = room;
            Screen.SetRoomPlacement(placement.Position, placement.Rotation, placement.SizeScale, placement.MaxHeight);
            ApplyHall();
        }

        /// <summary>The living room: the model from Resources/TheMovies/CustomHome if there is one, else the built-in one.</summary>
        void CreateHome()
        {
            m_HomeRoot = new GameObject("Living Room").transform;
            m_HomeRoot.SetParent(transform, false);
            var custom = Resources.Load<GameObject>(CustomHomePrefabPath);
            if (custom != null && BuildCustom(custom, m_HomeRoot, k_HomePlacement, out m_HomePlacement, m_HomeSpots))
            {
                m_HomeLightPos = new Vector4[0];
                m_HomeLightColor = new Vector4[0];
                m_FireLight = -1;
                return;
            }
            m_HomePlacement = k_HomePlacement;
            BuildHome();
            foreach (var seat in s_Seats) AddSpot(m_HomeSpots, seat.position, k_HomePlacement.Position); // the sofa
        }

        void Update()
        {
            // Flickering fireplace light
            if (m_Room != Room.Home || m_FireLight < 0 || m_HomeRoot == null || !m_HomeRoot.gameObject.activeInHierarchy) return;
            float t = Time.time;
            float flicker = 0.75f + 0.15f * Mathf.PerlinNoise(t * 3.1f, 0.3f) + 0.1f * Mathf.PerlinNoise(t * 9.7f, 1.7f);
            var c = m_FireColor * flicker;
            m_HomeLightColor[m_FireLight] = new Vector4(c.r, c.g, c.b, 1f);
            SetLights(m_HomeLightPos, m_HomeLightColor);
        }

        void BuildHome()
        {

            var floorMat = Lit("Parquet", ProceduralTextures.Wood(), uvTiling: 1f / 0.9f, specular: 0.3f);
            var rugMat = Lit("Rug", ProceduralTextures.Carpet(), uvTiling: 1f / 1.1f);
            var plasterMat = Lit("Plaster", ProceduralTextures.Fabric(), uvTiling: 1f / 2.5f);
            var sofaMat = Lit("Sofa", ProceduralTextures.Velvet(), uvTiling: 1f / 0.5f, sheen: 0.6f);
            var woodMat = Lit("Furniture", ProceduralTextures.Wood(), uvTiling: 1f / 0.8f, specular: 0.35f);
            var plainMat = Lit("Plain", Texture2D.whiteTexture, specular: 0.1f);
            var lampMat = Lit("Lamp Shades", Texture2D.whiteTexture, emission: 1.6f, roomLightAffect: 1f);
            var fireMat = Lit("Fire", Texture2D.whiteTexture, emission: 2.8f);
            fireMat.SetFloat("_ScreenGlow", 0f);
            var skyMat = Lit("Night Sky", ProceduralTextures.StarryCeiling(), uvTiling: 1f / 3f, emission: 1f);
            skyMat.SetFloat("_ScreenGlow", 0f);

            var floor = new MeshBuilder();
            var rug = new MeshBuilder();
            var plaster = new MeshBuilder();
            var sofa = new MeshBuilder();
            var wood = new MeshBuilder();
            var plain = new MeshBuilder();
            var lamps = new MeshBuilder();
            var fire = new MeshBuilder();
            var sky = new MeshBuilder();

            float width = k_HomeHalfWidth * 2f, depth = k_HomeFrontZ - k_HomeBackZ, midZ = (k_HomeFrontZ + k_HomeBackZ) * 0.5f;
            var wall = new Color(0.86f, 0.80f, 0.72f);
            var frontWall = new Color(0.20f, 0.22f, 0.27f);

            // Room shell
            floor.AddBox(new Vector3(0, -0.05f, midZ), new Vector3(width, 0.1f, depth), new Color(0.9f, 0.78f, 0.62f));
            plaster.AddQuad(new Vector3(-k_HomeHalfWidth, k_HomeCeiling * 0.5f, midZ), Vector3.right, Vector3.up, depth, k_HomeCeiling, wall);
            plaster.AddQuad(new Vector3(k_HomeHalfWidth, k_HomeCeiling * 0.5f, midZ), Vector3.left, Vector3.up, depth, k_HomeCeiling, wall);
            plaster.AddQuad(new Vector3(0, k_HomeCeiling * 0.5f, k_HomeBackZ), Vector3.forward, Vector3.up, width, k_HomeCeiling, wall);
            plaster.AddQuad(new Vector3(0, k_HomeCeiling * 0.5f, k_HomeFrontZ), Vector3.back, Vector3.up, width, k_HomeCeiling, frontWall);
            plain.AddQuad(new Vector3(0, k_HomeCeiling, midZ), Vector3.down, Vector3.forward, width, depth, new Color(0.92f, 0.90f, 0.86f));
            // Baseboards
            foreach (float side in new[] { -1f, 1f })
                wood.AddBox(new Vector3(side * (k_HomeHalfWidth - 0.01f), 0.05f, midZ), new Vector3(0.02f, 0.1f, depth), Color.white * 0.9f);
            wood.AddBox(new Vector3(0, 0.05f, k_HomeBackZ + 0.01f), new Vector3(width, 0.1f, 0.02f), Color.white * 0.9f);

            // Rug under sofa and table
            rug.AddBox(new Vector3(0, 0.006f, 0.95f), new Vector3(4.4f, 0.012f, 3.2f), new Color(0.75f, 0.55f, 0.45f));

            BuildSofa(sofa, plain);
            BuildTableAndSideboard(wood, plain);

            // Floor lamps beside the sofa
            var lightPos = new Vector4[8];
            var lightColor = new Vector4[8];
            int light = 0;
            var warm = new Color(1f, 0.72f, 0.45f) * 1.2f;
            foreach (float side in new[] { -1f, 1f })
            {
                var basePos = new Vector3(side * 2.35f, 0, -0.35f);
                plain.AddCylinder(basePos, 0.16f, 0.03f, 16, new Color(0.08f, 0.08f, 0.09f));
                plain.AddCylinder(basePos, 0.015f, 1.45f, 8, new Color(0.08f, 0.08f, 0.09f));
                lamps.AddCylinder(basePos + Vector3.up * 1.42f, 0.2f, 0.28f, 20, new Color(1f, 0.86f, 0.66f));
                var p = basePos + Vector3.up * 1.5f;
                lightPos[light] = new Vector4(p.x, p.y, p.z, 4.2f);
                lightColor[light] = new Vector4(warm.r, warm.g, warm.b, 1f);
                light++;
            }

            // Fireplace on the left wall
            {
                float x = -k_HomeHalfWidth + 0.2f, z = 1.9f;
                plain.AddBox(new Vector3(x, 0.6f, z), new Vector3(0.4f, 1.2f, 1.6f), new Color(0.42f, 0.40f, 0.38f));
                plain.AddBox(new Vector3(x + 0.21f, 0.42f, z), new Vector3(0.02f, 0.62f, 0.95f), new Color(0.03f, 0.025f, 0.02f));
                wood.AddBox(new Vector3(x + 0.05f, 1.24f, z), new Vector3(0.5f, 0.08f, 1.8f), Color.white);
                // Glowing embers and flames
                fire.AddBox(new Vector3(x + 0.24f, 0.18f, z), new Vector3(0.05f, 0.08f, 0.7f), new Color(1f, 0.35f, 0.08f));
                fire.AddRoundedBox(new Vector3(x + 0.25f, 0.33f, z - 0.15f), new Vector3(0.04f, 0.26f, 0.16f), 0.03f, 2, new Color(1f, 0.55f, 0.15f));
                fire.AddRoundedBox(new Vector3(x + 0.25f, 0.37f, z + 0.12f), new Vector3(0.04f, 0.32f, 0.2f), 0.03f, 2, new Color(1f, 0.62f, 0.2f));
                var firePos = new Vector3(x + 0.6f, 0.45f, z);
                m_FireColor = new Color(1f, 0.48f, 0.18f) * 1.6f;
                m_FireLight = light;
                lightPos[light] = new Vector4(firePos.x, firePos.y, firePos.z, 4.5f);
                lightColor[light] = new Vector4(m_FireColor.r, m_FireColor.g, m_FireColor.b, 1f);
                light++;
            }

            // Bookshelf on the right wall
            {
                float x = k_HomeHalfWidth - 0.2f, z = 1.6f;
                wood.AddBox(new Vector3(x, 1.0f, z), new Vector3(0.36f, 2.0f, 2.0f), new Color(0.55f, 0.42f, 0.32f));
                var rng = new System.Random(7);
                for (int shelf = 0; shelf < 5; shelf++)
                {
                    float y = 0.12f + shelf * 0.38f;
                    float zz = z - 0.9f;
                    while (zz < z + 0.88f)
                    {
                        float thick = 0.03f + (float)rng.NextDouble() * 0.04f;
                        float height = 0.22f + (float)rng.NextDouble() * 0.1f;
                        var color = Color.HSVToRGB((float)rng.NextDouble(), 0.45f, 0.25f + (float)rng.NextDouble() * 0.35f);
                        plain.AddBox(new Vector3(x - 0.19f, y + height * 0.5f, zz + thick * 0.5f), new Vector3(0.02f, height, thick), color);
                        zz += thick + 0.005f;
                        if (rng.NextDouble() < 0.08) zz += 0.12f;
                    }
                }
            }

            // Window to the night sky on the back wall
            {
                var center = new Vector3(-1.2f, 1.55f, k_HomeBackZ + 0.02f);
                sky.AddQuad(center, Vector3.forward, Vector3.up, 2.0f, 1.3f, new Color(0.55f, 0.6f, 0.85f));
                wood.AddBox(center + new Vector3(0, 0.68f, 0.03f), new Vector3(2.14f, 0.07f, 0.06f), Color.white);
                wood.AddBox(center + new Vector3(0, -0.68f, 0.05f), new Vector3(2.2f, 0.07f, 0.12f), Color.white);
                wood.AddBox(center + new Vector3(-1.04f, 0, 0.03f), new Vector3(0.07f, 1.3f, 0.06f), Color.white);
                wood.AddBox(center + new Vector3(1.04f, 0, 0.03f), new Vector3(0.07f, 1.3f, 0.06f), Color.white);
                wood.AddBox(center + new Vector3(0, 0, 0.03f), new Vector3(0.04f, 1.3f, 0.04f), Color.white);
            }

            // Plant in the corner
            {
                var pot = new Vector3(3.55f, 0, -2.1f);
                plain.AddCylinder(pot, 0.22f, 0.45f, 16, new Color(0.62f, 0.36f, 0.24f));
                var leaf = new Color(0.18f, 0.42f, 0.2f);
                plain.AddRoundedBox(pot + new Vector3(0, 0.85f, 0), new Vector3(0.6f, 0.7f, 0.6f), 0.28f, 3, leaf);
                plain.AddRoundedBox(pot + new Vector3(0.12f, 1.3f, -0.05f), new Vector3(0.45f, 0.5f, 0.45f), 0.2f, 3, leaf * 1.15f);
            }

            // Recessed ceiling lights
            foreach (float z in new[] { -0.8f, 2.4f })
            {
                lamps.AddCylinder(new Vector3(0, k_HomeCeiling - 0.02f, z), 0.09f, 0.02f, 16, new Color(1f, 0.92f, 0.8f));
                lightPos[light] = new Vector4(0, k_HomeCeiling - 0.3f, z, 3.8f);
                var c = new Color(1f, 0.85f, 0.7f) * 0.7f;
                lightColor[light] = new Vector4(c.r, c.g, c.b, 1f);
                light++;
            }

            m_HomeLightPos = new Vector4[light];
            m_HomeLightColor = new Vector4[light];
            System.Array.Copy(lightPos, m_HomeLightPos, light);
            System.Array.Copy(lightColor, m_HomeLightColor, light);

            floor.Build(m_HomeRoot, "Floor", floorMat);
            rug.Build(m_HomeRoot, "Rug", rugMat);
            plaster.Build(m_HomeRoot, "Walls", plasterMat);
            sofa.Build(m_HomeRoot, "Sofa", sofaMat);
            wood.Build(m_HomeRoot, "Wood", woodMat);
            plain.Build(m_HomeRoot, "Things", plainMat);
            lamps.Build(m_HomeRoot, "Lamps", lampMat);
            fire.Build(m_HomeRoot, "Fire", fireMat);
            sky.Build(m_HomeRoot, "Window", skyMat);
        }

        /// <summary>Sofa with one cushion per seat; the seat cushions are centered on the participant seats.</summary>
        static void BuildSofa(MeshBuilder sofa, MeshBuilder plain)
        {
            var color = new Color(0.30f, 0.34f, 0.42f);
            float halfLength = (MaxParticipants - 1) * 0.5f * SeatSpacing + 0.42f;
            // Base and back frame
            sofa.AddRoundedBox(new Vector3(0, 0.22f, -0.02f), new Vector3(halfLength * 2f, 0.3f, 0.92f), 0.06f, 3, color * 0.85f);
            sofa.AddRoundedBox(new Vector3(0, 0.62f, -0.43f), new Vector3(halfLength * 2f, 0.6f, 0.2f), 0.08f, 3, color * 0.85f);
            // Armrests
            foreach (float side in new[] { -1f, 1f })
                sofa.AddRoundedBox(new Vector3(side * (halfLength + 0.1f), 0.42f, -0.02f), new Vector3(0.22f, 0.5f, 0.92f), 0.08f, 3, color * 0.8f);
            // Seat and back cushions per seat
            for (int i = 0; i < MaxParticipants; i++)
            {
                var seat = SeatPose(i).position;
                sofa.AddRoundedBox(new Vector3(seat.x, 0.45f, 0.04f), new Vector3(SeatSpacing - 0.04f, 0.16f, 0.72f), 0.06f, 3, color);
                sofa.Transform = Matrix4x4.TRS(new Vector3(seat.x, 0.62f, -0.28f), Quaternion.Euler(-14f, 0, 0), Vector3.one);
                sofa.AddRoundedBox(Vector3.zero, new Vector3(SeatSpacing - 0.06f, 0.5f, 0.18f), 0.08f, 3, color * 1.05f);
                sofa.Transform = Matrix4x4.identity;
                // A cushion in the participant's color at each end and in the middle
                if (i == 0 || i == MaxParticipants - 1)
                {
                    sofa.Transform = Matrix4x4.TRS(new Vector3(seat.x + (i == 0 ? -0.22f : 0.22f), 0.62f, -0.12f), Quaternion.Euler(-20f, i == 0 ? 20f : -20f, 0), Vector3.one);
                    sofa.AddRoundedBox(Vector3.zero, new Vector3(0.36f, 0.34f, 0.12f), 0.06f, 3, ParticipantColors[i] * 0.7f);
                    sofa.Transform = Matrix4x4.identity;
                }
            }
            // Feet
            foreach (float x in new[] { -halfLength, halfLength })
            foreach (float z in new[] { -0.4f, 0.36f })
                plain.AddBox(new Vector3(x, 0.035f, z), new Vector3(0.06f, 0.07f, 0.06f), new Color(0.12f, 0.1f, 0.08f));
        }

        static void BuildTableAndSideboard(MeshBuilder wood, MeshBuilder plain)
        {
            // Coffee table with a bowl of popcorn
            wood.AddRoundedBox(new Vector3(0, 0.4f, 1.3f), new Vector3(1.3f, 0.05f, 0.65f), 0.02f, 2, Color.white);
            foreach (float x in new[] { -0.58f, 0.58f })
            foreach (float z in new[] { 1.03f, 1.57f })
                wood.AddBox(new Vector3(x, 0.19f, z), new Vector3(0.05f, 0.38f, 0.05f), Color.white * 0.8f);
            plain.AddCylinder(new Vector3(0.3f, 0.425f, 1.25f), 0.13f, 0.08f, 16, new Color(0.75f, 0.12f, 0.1f));
            plain.AddRoundedBox(new Vector3(0.3f, 0.51f, 1.25f), new Vector3(0.22f, 0.07f, 0.22f), 0.035f, 2, new Color(1f, 0.92f, 0.65f));

            // Low sideboard under the screen
            wood.AddBox(new Vector3(0, 0.27f, k_HomeFrontZ - 0.3f), new Vector3(3.4f, 0.5f, 0.45f), new Color(0.45f, 0.33f, 0.25f));
            plain.AddBox(new Vector3(0, 0.27f, k_HomeFrontZ - 0.525f), new Vector3(3.3f, 0.02f, 0.01f), new Color(0.2f, 0.16f, 0.12f));
        }
    }
}
