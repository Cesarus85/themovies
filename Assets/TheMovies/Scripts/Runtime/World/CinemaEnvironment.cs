using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// Builds the cinema hall: stadium seating with velvet seats, patterned carpet, fabric walls with wood
    /// pilasters and sconces, a starry ceiling, curtains, a stage and a slightly curved screen.
    /// Everything is generated at runtime and merged into a handful of meshes (one per material).
    /// Each room can be replaced by a model: a prefab at Resources/TheMovies/CustomCinema (or CustomHome for the living
    /// room), made with the editor menu "The Movies > Raum-Modell aufbereiten". It needs the children "Seat1".."Seat4"
    /// (floor position, facing the screen) and "ScreenAnchor" (bottom center of the screen); "ScreenTop" (optional) marks
    /// how high the screen may get. The model is moved so that its seats are the standard seats: the avatars of the
    /// others sit correctly whichever room each participant has chosen.
    /// </summary>
    public sealed partial class CinemaEnvironment : MonoBehaviour
    {
        public const int MaxParticipants = 4;
        public const float SeatSpacing = 0.75f;
        public const float RowPitch = 1.65f;
        public const float StepHeight = 0.45f;
        public const string CustomPrefabPath = "TheMovies/CustomCinema";
        public const string CustomHomePrefabPath = "TheMovies/CustomHome";

        public static readonly Color[] ParticipantColors =
        {
            new Color(0.98f, 0.60f, 0.20f), // orange
            new Color(0.25f, 0.80f, 0.95f), // cyan
            new Color(0.90f, 0.35f, 0.75f), // magenta
            new Color(0.55f, 0.90f, 0.35f), // green
        };

        // Room dimensions (our row's floor is y = 0, the screen is at +Z)
        const float k_HalfWidth = 11f;
        const float k_BackZ = -7.2f;
        const float k_FrontZ = 13.6f;
        const float k_LowFloor = -0.9f;
        const float k_Ceiling = 10.5f;
        const float k_ScreenZ = 11.9f;
        const float k_ScreenBottom = 0.4f;
        const float k_StageTop = -0.35f;

        static readonly Color k_SeatRed = new Color(0.55f, 0.06f, 0.09f);
        static readonly Color k_WallColor = new Color(0.32f, 0.16f, 0.17f);
        static readonly Color k_CurtainRed = new Color(0.50f, 0.04f, 0.07f);
        static readonly Color k_Dark = new Color(0.07f, 0.065f, 0.07f);

        static Pose[] s_Seats = DefaultSeats();

        public ScreenController Screen { get; private set; }

        bool m_HallWanted = true;

        /// <summary>Hides the hall (for mixed reality) but keeps the screen. It also stays hidden during 180°/360° videos.</summary>
        public void SetHallVisible(bool visible)
        {
            m_HallWanted = visible;
            ApplyHall();
        }

        void ApplyHall()
        {
            bool visible = m_HallWanted && (Screen == null || !Screen.IsImmersive);
            foreach (Transform child in transform)
            {
                if (Screen != null && child == Screen.transform) continue;
                bool isRoom = IsRoomRoot(child);
                bool roomActive = !isRoom || child == RootOf(m_Room);
                child.gameObject.SetActive(visible && roomActive);
            }
        }

        /// <summary>Floor position and facing of a participant seat.</summary>
        public static Pose SeatPose(int index) => s_Seats[Mathf.Clamp(index, 0, MaxParticipants - 1)];

        readonly System.Collections.Generic.List<Pose> m_HallSpots = new System.Collections.Generic.List<Pose>();
        readonly System.Collections.Generic.List<Pose> m_HomeSpots = new System.Collections.Generic.List<Pose>();

        /// <summary>
        /// Every seat one can move to in the current room: floor position below the seated head, facing the screen
        /// (free seat choice). Includes the four standard seats.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Pose> SeatSpots =>
            m_Room == Room.Home ? m_HomeSpots : m_Room == Room.Cinema ? m_HallSpots
            : m_ModelRooms.TryGetValue(m_Room, out var model) ? model.Spots : (System.Collections.Generic.IReadOnlyList<Pose>)m_HallSpots;

        /// <summary>Adds a seat spot facing the screen (skipped if another one is within 0.3 m).</summary>
        static void AddSpot(System.Collections.Generic.List<Pose> spots, Vector3 floor, Vector3 screen)
        {
            foreach (var s in spots)
                if ((s.position - floor).sqrMagnitude < 0.09f) return;
            var towards = new Vector3(screen.x - floor.x, 0f, screen.z - floor.z);
            spots.Add(new Pose(floor, towards.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(towards.normalized, Vector3.up) : Quaternion.identity));
        }

        static Pose[] DefaultSeats()
        {
            var seats = new Pose[MaxParticipants];
            for (int i = 0; i < MaxParticipants; i++)
                seats[i] = new Pose(new Vector3((i - (MaxParticipants - 1) * 0.5f) * SeatSpacing, 0f, 0f), Quaternion.identity);
            return seats;
        }

        public static CinemaEnvironment Build()
        {
            var go = new GameObject("Cinema");
            var env = go.AddComponent<CinemaEnvironment>();
            s_Seats = DefaultSeats();
            env.m_RoomsAvailable = true;

            env.m_HallRoot = new GameObject("Hall").transform;
            env.m_HallRoot.SetParent(go.transform, false);
            var custom = Resources.Load<GameObject>(CustomPrefabPath);
            if (custom == null || !env.BuildCustom(custom, env.m_HallRoot, k_HallPlacement, out env.m_HallPlacement, env.m_HallSpots))
            {
                env.m_HallPlacement = k_HallPlacement;
                env.BuildHall();
                // Seats of the built-in hall: rows of a center block and two side blocks (see BuildSeating).
                var screen = k_HallPlacement.Position;
                foreach (var seat in s_Seats) AddSpot(env.m_HallSpots, seat.position, screen);
                foreach (int row in k_Rows)
                {
                    var origin = new Vector3(0, RowY(row), RowZ(row));
                    for (int i = 0; i < 14; i++) AddSpot(env.m_HallSpots, origin + new Vector3((i - 6.5f) * SeatSpacing, 0, 0), screen);
                    for (int j = 0; j < 4; j++)
                    {
                        float x = 6.65f + j * SeatSpacing;
                        AddSpot(env.m_HallSpots, origin + new Vector3(x, 0, 0), screen);
                        AddSpot(env.m_HallSpots, origin + new Vector3(-x, 0, 0), screen);
                    }
                }
            }
            else
            {
                env.m_HallLightPos = new Vector4[0];
                env.m_HallLightColor = new Vector4[0];
                SetLights(env.m_HallLightPos, env.m_HallLightColor);
            }
            var hall = env.m_HallPlacement;
            env.Screen = ScreenController.Create(go.transform, hall.Position, hall.Rotation, maxHeight: hall.MaxHeight);
            env.Screen.ImmersiveChanged += _ => env.ApplyHall();
            return env;
        }

        // ---------------- Custom models ----------------

        /// <summary>Where the screen hangs in a room: bottom center, facing, size step scale and tallest height.</summary>
        struct ScreenPlacement
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float SizeScale;
            public float MaxHeight;

            public ScreenPlacement(Vector3 position, float sizeScale, float maxHeight)
            {
                Position = position;
                Rotation = Quaternion.identity;
                SizeScale = sizeScale;
                MaxHeight = maxHeight;
            }
        }

        static readonly ScreenPlacement k_HallPlacement = new ScreenPlacement(new Vector3(0, k_ScreenBottom, k_ScreenZ), 1f, 8.2f);
        ScreenPlacement m_HallPlacement;

        /// <summary>
        /// Puts a room model under <paramref name="root"/>, moved and turned so that the center of its seats is the
        /// center of the standard seats and they face +Z. False (and nothing added) without seats or screen anchor.
        /// </summary>
        bool BuildCustom(GameObject prefab, Transform root, ScreenPlacement defaults, out ScreenPlacement placement,
            System.Collections.Generic.List<Pose> spots)
        {
            placement = defaults;
            var instance = Instantiate(prefab, root);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var seats = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < MaxParticipants; i++)
            {
                var t = FindDeep(instance.transform, "Seat" + (i + 1));
                if (t != null) seats.Add(t);
            }
            var anchor = FindDeep(instance.transform, "ScreenAnchor");
            if (seats.Count == 0 || anchor == null)
            {
                Debug.LogWarning($"[TheMovies] Room model '{prefab.name}' needs children 'Seat1'..'Seat4' and 'ScreenAnchor'. Using the built-in room.");
                DestroyImmediate(instance);
                return false;
            }

            // Seats' center → standard seats' center (origin); the direction towards the screen anchor → +Z. (Not the
            // markers' own orientation: axis conversions on export from Blender etc. easily turn empties.)
            Vector3 center = Vector3.zero;
            foreach (var seat in seats) center += root.InverseTransformPoint(seat.position);
            center /= seats.Count;
            var forward = root.InverseTransformPoint(anchor.position) - center;
            forward.y = 0f;
            var turn = forward.sqrMagnitude > 1e-4f ? Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up)) : Quaternion.identity;
            instance.transform.localRotation = turn;
            instance.transform.localPosition = -(turn * center);

            for (int i = 0; i < MaxParticipants; i++)
            {
                var t = FindDeep(instance.transform, "Seat" + (i + 1));
                if (t == null) continue;
                float off = Vector3.Distance(root.InverseTransformPoint(t.position), s_Seats[i].position);
                if (off > 0.15f)
                    Debug.LogWarning($"[TheMovies] Room model '{prefab.name}': Seat{i + 1} is {off:0.00} m away from the standard seat " +
                                     $"(seats {SeatSpacing} m apart in a row) – the avatars of others won't sit exactly on it.");
            }

            placement.Position = root.InverseTransformPoint(anchor.position);
            var towardsScreen = new Vector3(placement.Position.x, 0f, placement.Position.z);
            placement.Rotation = towardsScreen.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(towardsScreen.normalized, Vector3.up) : Quaternion.identity;
            var top = FindDeep(instance.transform, "ScreenTop");
            if (top != null)
                placement.MaxHeight = Mathf.Max(0.5f, root.InverseTransformPoint(top.position).y - placement.Position.y);
            // Free seat choice: the standard seats first, then every "Spot_###" marker of the model.
            foreach (var seat in s_Seats) AddSpot(spots, seat.position, placement.Position);
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Spot_", System.StringComparison.OrdinalIgnoreCase))
                    AddSpot(spots, root.InverseTransformPoint(t.position), placement.Position);
            Debug.Log($"[TheMovies] Room model '{prefab.name}': {seats.Count} seats, {spots.Count} places to sit, screen at {placement.Position}, " +
                      $"up to {placement.MaxHeight:0.0} m high.");
            return true;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        // ---------------- Built-in hall ----------------

        void BuildHall()
        {
            var carpet = Lit("Carpet", ProceduralTextures.Carpet(), uvTiling: 1f / 1.4f);
            var velvet = Lit("Velvet", ProceduralTextures.Velvet(), uvTiling: 1f / 0.6f, sheen: 0.9f);
            var fabric = Lit("Wall Fabric", ProceduralTextures.Fabric(), uvTiling: 1f / 1.5f);
            var wood = Lit("Wood", ProceduralTextures.Wood(), uvTiling: 1f / 1.2f, specular: 0.25f);
            var dark = Lit("Dark", Texture2D.whiteTexture, specular: 0.15f);
            var ceiling = Lit("Ceiling", ProceduralTextures.StarryCeiling(), uvTiling: 1f / 14f, emission: 1f);
            var lamps = Lit("Lamps", Texture2D.whiteTexture, emission: 2.2f, roomLightAffect: 1f);
            var glowing = Lit("Step Lights", Texture2D.whiteTexture, emission: 2.5f);
            glowing.SetFloat("_ScreenGlow", 0f);
            ceiling.SetFloat("_ScreenGlow", 0.4f);

            var carpetMesh = new MeshBuilder();
            var velvetMesh = new MeshBuilder();
            var fabricMesh = new MeshBuilder();
            var woodMesh = new MeshBuilder();
            var darkMesh = new MeshBuilder();
            var ceilingMesh = new MeshBuilder();
            var lampMesh = new MeshBuilder();
            var glowMesh = new MeshBuilder();

            BuildFloors(carpetMesh, woodMesh, glowMesh);
            BuildSeating(velvetMesh, woodMesh, darkMesh, glowMesh);
            BuildWalls(fabricMesh, woodMesh, darkMesh, lampMesh, glowMesh);
            BuildFront(velvetMesh, woodMesh, darkMesh, glowMesh);
            ceilingMesh.AddQuad(new Vector3(0, k_Ceiling, (k_FrontZ + k_BackZ) * 0.5f), Vector3.down, Vector3.forward,
                k_HalfWidth * 2, k_FrontZ - k_BackZ, Color.white);

            var root = m_HallRoot;
            carpetMesh.Build(root, "Floor", carpet);
            velvetMesh.Build(root, "Velvet", velvet);
            fabricMesh.Build(root, "Walls", fabric);
            woodMesh.Build(root, "Wood", wood);
            darkMesh.Build(root, "Dark", dark);
            ceilingMesh.Build(root, "Ceiling", ceiling);
            lampMesh.Build(root, "Lamps", lamps);
            glowMesh.Build(root, "Step Lights", glowing);

            BuildExitSign(new Vector3(-k_HalfWidth + 0.06f, k_LowFloor + 2.6f, 8.2f), Vector3.left);
            BuildExitSign(new Vector3(k_HalfWidth - 0.06f, k_LowFloor + 2.6f, 8.2f), Vector3.right);
        }

        static Material Lit(string name, Texture texture, float uvTiling = 1f, float sheen = 0f, float emission = 0f,
            float roomLightAffect = 0f, float specular = 0f)
        {
            var m = new Material(UIFactory.FindShader("TheMovies/CinemaLit")) { name = name, mainTexture = texture };
            m.mainTextureScale = new Vector2(uvTiling, uvTiling);
            m.SetFloat("_Sheen", sheen);
            m.SetFloat("_Emission", emission);
            m.SetFloat("_RoomLightAffect", roomLightAffect);
            m.SetFloat("_Specular", specular);
            return m;
        }

        static float RowY(int row) => row * StepHeight;   // row 0 = participants, negative = closer to the screen
        static float RowZ(int row) => -row * RowPitch;

        static readonly int[] k_Rows = { -2, -1, 0, 1, 2, 3 };

        void BuildFloors(MeshBuilder carpet, MeshBuilder wood, MeshBuilder glow)
        {
            float width = k_HalfWidth * 2f;
            // Low floor across the whole hall; the stepped rows stand on it
            carpet.AddBox(new Vector3(0, k_LowFloor - 0.05f, (k_BackZ + k_FrontZ) * 0.5f), new Vector3(width, 0.1f, k_FrontZ - k_BackZ), Color.white);

            // One step per row; the last one reaches the back wall
            foreach (int row in k_Rows)
            {
                float top = RowY(row);
                if (top <= k_LowFloor + 0.01f) continue;
                float zFront = RowZ(row) + RowPitch * 0.5f;
                float zBack = row == k_Rows[k_Rows.Length - 1] ? k_BackZ : RowZ(row) - RowPitch * 0.5f;
                float height = top - k_LowFloor;
                carpet.AddBox(new Vector3(0, k_LowFloor + height * 0.5f, (zFront + zBack) * 0.5f), new Vector3(width, height, zFront - zBack), Color.white);
                // Wooden nosing and aisle step lights at each step edge
                wood.AddBox(new Vector3(0, top - 0.02f, zFront - 0.02f), new Vector3(width, 0.05f, 0.06f), Color.white);
                foreach (float x in new[] { -5.75f, 5.75f })
                    glow.AddBox(new Vector3(x, top - 0.09f, zFront + 0.005f), new Vector3(0.35f, 0.03f, 0.02f), new Color(0.55f, 0.65f, 0.9f));
            }
        }

        void BuildSeating(MeshBuilder velvet, MeshBuilder wood, MeshBuilder dark, MeshBuilder glow)
        {
            foreach (int row in k_Rows)
            {
                var origin = new Vector3(0, RowY(row), RowZ(row));
                // Center block of 14 seats, side blocks of 4 seats behind the aisles
                for (int i = 0; i < 14; i++)
                {
                    float x = (i - 6.5f) * SeatSpacing;
                    bool participant = row == 0 && i >= 5 && i <= 8;
                    Color? accent = participant ? ParticipantColors[i - 5] : (Color?)null;
                    BuildSeat(velvet, wood, dark, glow, origin + new Vector3(x, 0, 0), accent);
                }
                for (int j = 0; j < 4; j++)
                {
                    float x = 6.65f + j * SeatSpacing;
                    BuildSeat(velvet, wood, dark, glow, origin + new Vector3(x, 0, 0), null);
                    BuildSeat(velvet, wood, dark, glow, origin + new Vector3(-x, 0, 0), null);
                }
            }
        }

        static void BuildSeat(MeshBuilder velvet, MeshBuilder wood, MeshBuilder dark, MeshBuilder glow, Vector3 floor, Color? accent)
        {
            var seatRot = Matrix4x4.TRS(floor, Quaternion.identity, Vector3.one);
            velvet.Transform = seatRot;
            velvet.AddRoundedBox(new Vector3(0, 0.45f, 0.04f), new Vector3(0.54f, 0.15f, 0.52f), 0.06f, 3, k_SeatRed);
            // Backrest, tilted back a little
            velvet.Transform = seatRot * Matrix4x4.TRS(new Vector3(0, 0.5f, -0.24f), Quaternion.Euler(-12f, 0, 0), Vector3.one);
            velvet.AddRoundedBox(new Vector3(0, 0.38f, 0), new Vector3(0.56f, 0.72f, 0.13f), 0.06f, 3, k_SeatRed * 0.92f);
            velvet.Transform = Matrix4x4.identity;

            dark.Transform = seatRot;
            dark.AddBox(new Vector3(0, 0.2f, -0.02f), new Vector3(0.44f, 0.36f, 0.34f), k_Dark);
            dark.AddBox(new Vector3(-0.32f, 0.3f, -0.02f), new Vector3(0.05f, 0.6f, 0.42f), k_Dark);
            dark.AddBox(new Vector3(0.32f, 0.3f, -0.02f), new Vector3(0.05f, 0.6f, 0.42f), k_Dark);
            dark.Transform = Matrix4x4.identity;
            if (accent.HasValue)
            {
                // Glowing piping on the backrest marks the seats of the participants
                glow.Transform = seatRot * Matrix4x4.TRS(new Vector3(0, 0.5f, -0.24f), Quaternion.Euler(-12f, 0, 0), Vector3.one);
                glow.AddBox(new Vector3(0, 0.75f, 0), new Vector3(0.5f, 0.02f, 0.14f), accent.Value * 0.6f);
                glow.Transform = Matrix4x4.identity;
            }

            wood.Transform = seatRot;
            wood.AddRoundedBox(new Vector3(-0.32f, 0.63f, 0.02f), new Vector3(0.08f, 0.05f, 0.5f), 0.02f, 2, Color.white);
            wood.AddRoundedBox(new Vector3(0.32f, 0.63f, 0.02f), new Vector3(0.08f, 0.05f, 0.5f), 0.02f, 2, Color.white);
            wood.Transform = Matrix4x4.identity;
        }

        void BuildWalls(MeshBuilder fabric, MeshBuilder wood, MeshBuilder dark, MeshBuilder lamps, MeshBuilder glow)
        {
            float depth = k_FrontZ - k_BackZ, midZ = (k_FrontZ + k_BackZ) * 0.5f;
            float wallHeight = k_Ceiling - k_LowFloor, midY = (k_Ceiling + k_LowFloor) * 0.5f;

            // Fabric wall surfaces
            fabric.AddQuad(new Vector3(-k_HalfWidth, midY, midZ), Vector3.right, Vector3.up, depth, wallHeight, k_WallColor);
            fabric.AddQuad(new Vector3(k_HalfWidth, midY, midZ), Vector3.left, Vector3.up, depth, wallHeight, k_WallColor);
            fabric.AddQuad(new Vector3(0, midY, k_BackZ), Vector3.forward, Vector3.up, k_HalfWidth * 2, wallHeight, k_WallColor);
            fabric.AddQuad(new Vector3(0, midY, k_FrontZ), Vector3.back, Vector3.up, k_HalfWidth * 2, wallHeight, k_WallColor * 0.4f);

            var lightPos = new Vector4[8];
            var lightCol = new Vector4[8];
            int light = 0;
            var warm = new Color(1f, 0.70f, 0.42f) * 1.5f;

            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * (k_HalfWidth - 0.04f);
                var inward = new Vector3(-side, 0, 0);

                // Wainscot and crown moulding
                wood.AddBox(new Vector3(x, k_LowFloor + 0.6f, midZ), new Vector3(0.08f, 1.2f, depth), Color.white * 0.75f);
                wood.AddBox(new Vector3(x, k_Ceiling - 0.25f, midZ), new Vector3(0.14f, 0.3f, depth), Color.white);
                wood.AddBox(new Vector3(x, 6.4f, midZ), new Vector3(0.1f, 0.12f, depth), Color.white * 0.8f);

                for (int i = 0; i < 7; i++)
                {
                    float z = k_BackZ + 1.2f + i * 3.15f;
                    // Pilasters
                    wood.AddBox(new Vector3(x + inward.x * 0.06f, midY, z), new Vector3(0.12f, wallHeight, 0.32f), Color.white * 0.9f);

                    // Sconces on four pilasters per side
                    if (i >= 1 && i <= 4 && light < 8)
                    {
                        var center = new Vector3(x + inward.x * 0.2f, 3.4f, z);
                        wood.AddBox(center - inward * 0.1f, new Vector3(0.06f, 0.6f, 0.26f), Color.white);
                        lamps.AddRoundedBox(center + inward * 0.04f, new Vector3(0.22f, 0.42f, 0.3f), 0.08f, 3, new Color(1f, 0.82f, 0.6f));
                        var pos = center + inward * 0.45f;
                        lightPos[light] = new Vector4(pos.x, pos.y, pos.z, 6.5f);
                        lightCol[light] = new Vector4(warm.r, warm.g, warm.b, 1f);
                        light++;
                    }
                }

                // Doors near the front with a dark frame
                var door = new Vector3(x + inward.x * 0.02f, k_LowFloor + 1.1f, 8.2f);
                dark.AddBox(door, new Vector3(0.06f, 2.2f, 1.3f), k_Dark * 1.6f);
                wood.AddBox(door + Vector3.up * 1.15f, new Vector3(0.09f, 0.1f, 1.45f), Color.white);
            }

            // Back wall: projection booth window and a wood band
            wood.AddBox(new Vector3(0, 6.4f, k_BackZ + 0.05f), new Vector3(k_HalfWidth * 2, 0.12f, 0.1f), Color.white * 0.8f);
            dark.AddBox(new Vector3(0, 7.6f, k_BackZ + 0.04f), new Vector3(1.6f, 0.9f, 0.06f), k_Dark);
            glow.AddQuad(new Vector3(0, 7.6f, k_BackZ + 0.08f), Vector3.forward, Vector3.up, 1.3f, 0.65f, new Color(0.08f, 0.1f, 0.16f));

            m_HallLightPos = lightPos;
            m_HallLightColor = lightCol;
            SetLights(lightPos, lightCol);
        }

        void BuildFront(MeshBuilder velvet, MeshBuilder wood, MeshBuilder dark, MeshBuilder glow)
        {
            // Stage
            float stageFront = 10.4f;
            float stageDepth = k_FrontZ - stageFront;
            float stageHeight = k_StageTop - k_LowFloor;
            dark.AddBox(new Vector3(0, k_LowFloor + stageHeight * 0.5f, stageFront + stageDepth * 0.5f),
                new Vector3(k_HalfWidth * 2, stageHeight, stageDepth), k_Dark * 1.3f);
            wood.AddBox(new Vector3(0, k_StageTop - 0.03f, stageFront), new Vector3(k_HalfWidth * 2, 0.06f, 0.08f), Color.white);
            for (int i = -10; i <= 10; i++)
                glow.AddBox(new Vector3(i * 1.0f, k_LowFloor + 0.12f, stageFront - 0.01f), new Vector3(0.18f, 0.03f, 0.02f), new Color(0.9f, 0.7f, 0.45f) * 0.8f);

            // Black masking behind the screen
            dark.AddQuad(new Vector3(0, (k_StageTop + k_Ceiling) * 0.5f, k_FrontZ - 0.6f), Vector3.back, Vector3.up,
                k_HalfWidth * 2, k_Ceiling - k_StageTop, k_Dark * 0.4f);

            // Side curtains and a valance across the top
            float curtainZ = k_FrontZ - 0.9f;
            float curtainHeight = k_Ceiling - k_StageTop - 0.2f;
            velvet.AddCurtain(new Vector3(-k_HalfWidth, k_StageTop, curtainZ), 1.7f, curtainHeight, 0.35f, 0.12f, k_CurtainRed);
            velvet.AddCurtain(new Vector3(k_HalfWidth - 1.7f, k_StageTop, curtainZ), 1.7f, curtainHeight, 0.35f, 0.12f, k_CurtainRed);
            velvet.AddCurtain(new Vector3(-k_HalfWidth, k_Ceiling - 1.5f, curtainZ - 0.3f), k_HalfWidth * 2, 1.5f, 0.5f, 0.1f, k_CurtainRed * 0.9f);
            wood.AddBox(new Vector3(0, k_Ceiling - 1.55f, curtainZ - 0.35f), new Vector3(k_HalfWidth * 2, 0.08f, 0.06f), new Color(1f, 0.85f, 0.55f));
        }

        void BuildExitSign(Vector3 position, Vector3 wallDirection)
        {
            var sign = new GameObject("Exit Sign").transform;
            sign.SetParent(m_HallRoot, false);
            sign.position = position;
            // TextMesh is readable when looking along its +Z, so +Z points into the wall.
            sign.rotation = Quaternion.LookRotation(wallDirection, Vector3.up);
            var mat = Lit("Exit", Texture2D.whiteTexture, emission: 1.8f);
            mat.SetFloat("_ScreenGlow", 0f);
            var box = new MeshBuilder();
            box.AddBox(Vector3.zero, new Vector3(0.75f, 0.24f, 0.04f), new Color(0.05f, 0.55f, 0.2f));
            box.Build(sign, "Panel", mat);
            UIFactory.CreateText(sign, "NOTAUSGANG", new Vector3(0, 0, -0.025f), 0.13f, TextAnchor.MiddleCenter, Color.white);
        }

        static void SetLights(Vector4[] positions, Vector4[] colors)
        {
            var pos = new Vector4[8];
            var col = new Vector4[8];
            int count = Mathf.Min(8, Mathf.Min(positions.Length, colors.Length));
            for (int i = 0; i < count; i++)
            {
                pos[i] = positions[i];
                col[i] = colors[i];
            }
            Shader.SetGlobalVectorArray("_TM_LightPos", pos);
            Shader.SetGlobalVectorArray("_TM_LightColor", col);
            Shader.SetGlobalFloat("_TM_LightCount", count);
        }
    }
}
