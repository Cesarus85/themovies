using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// Builds the cinema hall procedurally: room, stadium seating, curtains and the screen.
    /// The screen is at +Z, the four participant seats are in the middle row at z = 0.
    /// </summary>
    public sealed class CinemaEnvironment : MonoBehaviour
    {
        public const int MaxParticipants = 4;
        public const float SeatSpacing = 0.95f;
        public const float ScreenZ = 11f;

        public static readonly Color[] ParticipantColors =
        {
            new Color(0.98f, 0.60f, 0.20f), // orange
            new Color(0.25f, 0.80f, 0.95f), // cyan
            new Color(0.90f, 0.35f, 0.75f), // magenta
            new Color(0.55f, 0.90f, 0.35f), // green
        };

        static readonly Color k_SeatColor = new Color(0.32f, 0.05f, 0.07f);
        static readonly Color k_WallColor = new Color(0.10f, 0.08f, 0.10f);
        static readonly Color k_FloorColor = new Color(0.07f, 0.05f, 0.06f);
        static readonly Color k_CurtainColor = new Color(0.38f, 0.04f, 0.06f);

        public ScreenController Screen { get; private set; }

        /// <summary>Floor position and facing of a participant seat.</summary>
        public static Pose SeatPose(int index)
        {
            index = Mathf.Clamp(index, 0, MaxParticipants - 1);
            float x = (index - (MaxParticipants - 1) * 0.5f) * SeatSpacing;
            return new Pose(new Vector3(x, 0f, 0f), Quaternion.identity);
        }

        public static CinemaEnvironment Build()
        {
            var go = new GameObject("Cinema");
            var env = go.AddComponent<CinemaEnvironment>();
            env.BuildRoom();
            env.BuildSeating();
            env.Screen = ScreenController.Create(go.transform, ScreenZ);
            return env;
        }

        void BuildRoom()
        {
            var room = new GameObject("Room").transform;
            room.SetParent(transform, false);
            var mat = Shapes.Room;

            const float halfWidth = 13f, back = -10f, front = 12.6f, height = 15f;
            float depth = front - back, midZ = (front + back) * 0.5f;

            Shapes.Create(PrimitiveType.Cube, room, "Floor", new Vector3(0, -0.05f, midZ), new Vector3(halfWidth * 2, 0.1f, depth), k_FloorColor, mat);
            Shapes.Create(PrimitiveType.Cube, room, "Ceiling", new Vector3(0, height, midZ), new Vector3(halfWidth * 2, 0.1f, depth), k_WallColor * 0.7f, mat);
            Shapes.Create(PrimitiveType.Cube, room, "Wall Left", new Vector3(-halfWidth, height * 0.5f, midZ), new Vector3(0.1f, height, depth), k_WallColor, mat);
            Shapes.Create(PrimitiveType.Cube, room, "Wall Right", new Vector3(halfWidth, height * 0.5f, midZ), new Vector3(0.1f, height, depth), k_WallColor, mat);
            Shapes.Create(PrimitiveType.Cube, room, "Wall Back", new Vector3(0, height * 0.5f, back), new Vector3(halfWidth * 2, height, 0.1f), k_WallColor, mat);
            Shapes.Create(PrimitiveType.Cube, room, "Wall Front", new Vector3(0, height * 0.5f, front), new Vector3(halfWidth * 2, height, 0.1f), k_WallColor * 0.5f, mat);

            // Acoustic panels on the side walls
            for (int i = 0; i < 7; i++)
            {
                float z = back + 2.5f + i * 3f;
                foreach (float side in new[] { -1f, 1f })
                {
                    Shapes.Create(PrimitiveType.Cube, room, "Panel", new Vector3(side * (halfWidth - 0.08f), 4.2f, z),
                        new Vector3(0.06f, 5.5f, 2.2f), k_WallColor * 1.6f, mat);
                    Shapes.Create(PrimitiveType.Cube, room, "Lamp", new Vector3(side * (halfWidth - 0.12f), 7.6f, z),
                        new Vector3(0.08f, 0.25f, 0.5f), new Color(1f, 0.78f, 0.45f), Shapes.Lamp);
                }
            }

            // Curtains left and right of the screen and a stage in front of it
            for (int i = 0; i < 6; i++)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * (halfWidth - 0.6f - i * 0.45f);
                    Shapes.Create(PrimitiveType.Cylinder, room, "Curtain", new Vector3(x, height * 0.5f, front - 0.4f),
                        new Vector3(0.55f, height * 0.5f, 0.3f), k_CurtainColor * (i % 2 == 0 ? 1f : 0.8f), mat);
                }
            }
            Shapes.Create(PrimitiveType.Cube, room, "Stage", new Vector3(0, 0.2f, front - 1.2f), new Vector3(halfWidth * 2 - 0.2f, 0.4f, 2.2f), k_WallColor * 0.9f, mat);
        }

        void BuildSeating()
        {
            var seating = new GameObject("Seating").transform;
            seating.SetParent(transform, false);

            // Stadium rows: two in front (lower), ours, three behind (higher).
            float[] rowZ = { 3.2f, 1.6f, 0f, -1.7f, -3.4f, -5.1f };
            float[] rowY = { -0.0f, 0.0f, 0f, 0.4f, 0.8f, 1.2f };
            for (int r = 0; r < rowZ.Length; r++)
            {
                var row = new GameObject("Row " + r).transform;
                row.SetParent(seating, false);
                row.localPosition = new Vector3(0, rowY[r], rowZ[r]);
                if (rowY[r] > 0)
                    Shapes.Create(PrimitiveType.Cube, row, "Step", new Vector3(0, -rowY[r] * 0.5f, -0.2f),
                        new Vector3(22f, rowY[r], 1.7f), k_FloorColor * 1.2f, Shapes.Room);

                for (int s = -6; s <= 6; s++)
                {
                    float x = (s + 0.5f) * SeatSpacing;
                    bool participantSeat = r == 2 && Mathf.Abs(x) < MaxParticipants * 0.5f * SeatSpacing;
                    if (participantSeat)
                        continue;
                    if (Mathf.Abs(x) > 11.5f) continue;
                    BuildSeat(row, new Vector3(x, 0, 0), null);
                }
            }

            for (int i = 0; i < MaxParticipants; i++)
            {
                var pose = SeatPose(i);
                BuildSeat(seating, pose.position, ParticipantColors[i]);
            }
        }

        static void BuildSeat(Transform parent, Vector3 position, Color? accent)
        {
            var seat = new GameObject("Seat").transform;
            seat.SetParent(parent, false);
            seat.localPosition = position;
            Shapes.Create(PrimitiveType.Cube, seat, "Cushion", new Vector3(0, 0.42f, 0.02f), new Vector3(0.56f, 0.12f, 0.5f), k_SeatColor);
            Shapes.Create(PrimitiveType.Cube, seat, "Back", new Vector3(0, 0.78f, -0.26f), new Vector3(0.56f, 0.7f, 0.1f), k_SeatColor * 0.9f);
            Shapes.Create(PrimitiveType.Cube, seat, "Base", new Vector3(0, 0.18f, 0f), new Vector3(0.5f, 0.36f, 0.4f), k_SeatColor * 0.5f);
            Shapes.Create(PrimitiveType.Cube, seat, "Arm L", new Vector3(-0.31f, 0.6f, 0f), new Vector3(0.06f, 0.06f, 0.5f), new Color(0.08f, 0.08f, 0.09f));
            Shapes.Create(PrimitiveType.Cube, seat, "Arm R", new Vector3(0.31f, 0.6f, 0f), new Vector3(0.06f, 0.06f, 0.5f), new Color(0.08f, 0.08f, 0.09f));
            if (accent.HasValue)
                Shapes.Create(PrimitiveType.Cube, seat, "Accent", new Vector3(0, 1.14f, -0.26f), new Vector3(0.56f, 0.03f, 0.11f), accent.Value, Shapes.Flat);
        }
    }
}
