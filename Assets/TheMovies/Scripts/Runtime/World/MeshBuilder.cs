using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheMovies.World
{
    /// <summary>
    /// Collects geometry for one material into a single mesh (few draw calls on the Quest).
    /// Colors go into vertex colors; UVs are in meters so tiling textures keep their real-world size.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector3> m_Normals = new List<Vector3>();
        readonly List<Vector2> m_Uvs = new List<Vector2>();
        readonly List<Color32> m_Colors = new List<Color32>();
        readonly List<int> m_Triangles = new List<int>();

        public int VertexCount => m_Vertices.Count;

        /// <summary>Applied to everything added afterwards (position/rotation of a part).</summary>
        public Matrix4x4 Transform = Matrix4x4.identity;

        public void Add(Vector3 position, Vector3 normal, Vector2 uv, Color color)
        {
            m_Vertices.Add(Transform.MultiplyPoint3x4(position));
            m_Normals.Add(Transform.MultiplyVector(normal).normalized);
            m_Uvs.Add(uv);
            m_Colors.Add(color);
        }

        public void Triangle(int a, int b, int c)
        {
            m_Triangles.Add(a);
            m_Triangles.Add(b);
            m_Triangles.Add(c);
        }

        /// <summary>A flat rectangle facing <paramref name="normal"/>; <paramref name="up"/> is its vertical direction.</summary>
        public void AddQuad(Vector3 center, Vector3 normal, Vector3 up, float width, float height, Color color, float uvScale = 1f)
        {
            normal.Normalize();
            up = Vector3.ProjectOnPlane(up, normal).normalized;
            Vector3 right = Vector3.Cross(normal, up);
            Vector3 hw = right * (width * 0.5f), hh = up * (height * 0.5f);
            int i = VertexCount;
            Add(center - hw - hh, normal, new Vector2(0, 0), color);
            Add(center + hw - hh, normal, new Vector2(width * uvScale, 0), color);
            Add(center + hw + hh, normal, new Vector2(width * uvScale, height * uvScale), color);
            Add(center - hw + hh, normal, new Vector2(0, height * uvScale), color);
            Triangle(i, i + 3, i + 2);
            Triangle(i, i + 2, i + 1);
        }

        public void AddBox(Vector3 center, Vector3 size, Color color, float uvScale = 1f)
        {
            Vector3 h = size * 0.5f;
            AddQuad(center + new Vector3(h.x, 0, 0), Vector3.right, Vector3.up, size.z, size.y, color, uvScale);
            AddQuad(center - new Vector3(h.x, 0, 0), Vector3.left, Vector3.up, size.z, size.y, color, uvScale);
            AddQuad(center + new Vector3(0, 0, h.z), Vector3.forward, Vector3.up, size.x, size.y, color, uvScale);
            AddQuad(center - new Vector3(0, 0, h.z), Vector3.back, Vector3.up, size.x, size.y, color, uvScale);
            AddQuad(center + new Vector3(0, h.y, 0), Vector3.up, Vector3.forward, size.x, size.z, color, uvScale);
            AddQuad(center - new Vector3(0, h.y, 0), Vector3.down, Vector3.back, size.x, size.z, color, uvScale);
        }

        /// <summary>Box with rounded edges (subdivided cube pushed onto a rounded shape).</summary>
        public void AddRoundedBox(Vector3 center, Vector3 size, float radius, int segments, Color color, float uvScale = 1f)
        {
            Vector3 half = size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            Vector3 inner = half - Vector3.one * radius;

            AddRoundedFace(center, half, inner, radius, segments, Vector3.right, Vector3.up, color, uvScale);
            AddRoundedFace(center, half, inner, radius, segments, Vector3.left, Vector3.up, color, uvScale);
            AddRoundedFace(center, half, inner, radius, segments, Vector3.forward, Vector3.up, color, uvScale);
            AddRoundedFace(center, half, inner, radius, segments, Vector3.back, Vector3.up, color, uvScale);
            AddRoundedFace(center, half, inner, radius, segments, Vector3.up, Vector3.forward, color, uvScale);
            AddRoundedFace(center, half, inner, radius, segments, Vector3.down, Vector3.back, color, uvScale);
        }

        void AddRoundedFace(Vector3 center, Vector3 half, Vector3 inner, float radius, int segments, Vector3 normal, Vector3 up,
            Color color, float uvScale)
        {
            Vector3 right = Vector3.Cross(normal, up);
            float sizeU = Mathf.Abs(Vector3.Dot(right, half)) * 2f;
            float sizeV = Mathf.Abs(Vector3.Dot(up, half)) * 2f;
            int start = VertexCount;
            for (int j = 0; j <= segments; j++)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float s = (float)i / segments * 2f - 1f;
                    float t = (float)j / segments * 2f - 1f;
                    Vector3 p = Vector3.Scale(normal + right * s + up * t, half);
                    Vector3 q = new Vector3(
                        Mathf.Clamp(p.x, -inner.x, inner.x),
                        Mathf.Clamp(p.y, -inner.y, inner.y),
                        Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 dir = p - q;
                    Vector3 n = dir.sqrMagnitude > 1e-8f ? dir.normalized : normal;
                    Add(center + q + n * radius, n, new Vector2((s + 1f) * 0.5f * sizeU * uvScale, (t + 1f) * 0.5f * sizeV * uvScale), color);
                }
            }
            int row = segments + 1;
            for (int j = 0; j < segments; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = start + j * row + i;
                    Triangle(a, a + row, a + row + 1);
                    Triangle(a, a + row + 1, a + 1);
                }
            }
        }

        /// <summary>Vertical cylinder standing on <paramref name="baseCenter"/>.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int sides, Color color, bool caps = true, float uvScale = 1f)
        {
            int start = VertexCount;
            float circumference = 2f * Mathf.PI * radius;
            for (int i = 0; i <= sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float u = (float)i / sides * circumference * uvScale;
                Add(baseCenter + n * radius, n, new Vector2(u, 0), color);
                Add(baseCenter + n * radius + Vector3.up * height, n, new Vector2(u, height * uvScale), color);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2;
                Triangle(a, a + 1, a + 3);
                Triangle(a, a + 3, a + 2);
            }
            if (!caps) return;
            foreach (float y in new[] { 0f, height })
            {
                var n = y > 0 ? Vector3.up : Vector3.down;
                int c = VertexCount;
                Add(baseCenter + Vector3.up * y, n, Vector2.zero, color);
                for (int i = 0; i <= sides; i++)
                {
                    float a = (float)i / sides * Mathf.PI * 2f;
                    Add(baseCenter + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius), n,
                        new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * uvScale, color);
                }
                for (int i = 0; i < sides; i++)
                    Triangle(c, c + 1 + i, c + 2 + i);
            }
        }

        /// <summary>
        /// A hanging curtain with soft pleats. Spans along +X from <paramref name="bottomLeft"/>,
        /// the pleats bulge along ±Z. Shading uses the analytic normal of the wave.
        /// </summary>
        public void AddCurtain(Vector3 bottomLeft, float width, float height, float pleatWidth, float depth, Color color, float uvScale = 1f)
        {
            int columns = Mathf.Max(4, Mathf.CeilToInt(width / pleatWidth * 8f));
            int start = VertexCount;
            for (int i = 0; i <= columns; i++)
            {
                float x = width * i / columns;
                float phase = x / pleatWidth * Mathf.PI * 2f;
                float z = Mathf.Sin(phase) * depth;
                float dz = Mathf.Cos(phase) * depth * Mathf.PI * 2f / pleatWidth;
                var n = new Vector3(dz, 0, -1f).normalized;
                // Slightly darker in the folds
                float shade = 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(phase));
                var c = color * shade;
                c.a = 1f;
                Add(bottomLeft + new Vector3(x, 0, z), n, new Vector2(x * uvScale, 0), c);
                Add(bottomLeft + new Vector3(x, height, z), n, new Vector2(x * uvScale, height * uvScale), c);
            }
            for (int i = 0; i < columns; i++)
            {
                int a = start + i * 2;
                Triangle(a, a + 1, a + 3);
                Triangle(a, a + 3, a + 2);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (m_Vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(m_Vertices);
            mesh.SetNormals(m_Normals);
            mesh.SetUVs(0, m_Uvs);
            mesh.SetColors(m_Colors);
            mesh.SetTriangles(m_Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Creates a GameObject rendering the collected geometry with <paramref name="material"/>.</summary>
        public MeshRenderer Build(Transform parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }
    }
}
