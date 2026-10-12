using System;
using System.Threading.Tasks;
using TheMovies.Core;
using TheMovies.World;
using UnityEngine;

namespace TheMovies.XR
{
    /// <summary>
    /// Controller models. The headset's own model (Touch Plus on Quest 3) is loaded through XR_FB_render_model when
    /// available; until then – and on other runtimes – a modelled stand-in in the same shape and colors is shown.
    /// Both sit at the grip pose.
    /// </summary>
    public static class ControllerVisual
    {
        static readonly Color k_Body = new Color(0.10f, 0.10f, 0.11f);
        static readonly Color k_Face = new Color(0.16f, 0.16f, 0.18f);
        static readonly Color k_Button = new Color(0.30f, 0.30f, 0.33f);

        /// <summary>Stand-in shaped like a Touch Plus controller (no tracking ring): handle, head with thumbstick and two buttons, trigger.</summary>
        public static void Build(Transform grip, bool left)
        {
            float side = left ? -1f : 1f;
            var body = new MeshBuilder();
            var details = new MeshBuilder();

            // Handle, slightly tilted forward
            body.Transform = Matrix4x4.TRS(new Vector3(0, -0.012f, -0.012f), Quaternion.Euler(14f, 0, 0), Vector3.one);
            body.AddRoundedBox(Vector3.zero, new Vector3(0.034f, 0.042f, 0.105f), 0.016f, 4, k_Body);
            // Head: the upper part with the face plate
            body.Transform = Matrix4x4.TRS(new Vector3(-side * 0.004f, 0.016f, 0.045f), Quaternion.Euler(-8f, 0, 0), Vector3.one);
            body.AddRoundedBox(Vector3.zero, new Vector3(0.05f, 0.03f, 0.062f), 0.014f, 4, k_Body);
            body.Transform = Matrix4x4.identity;

            // Face plate, thumbstick, buttons and trigger
            details.Transform = Matrix4x4.TRS(new Vector3(-side * 0.004f, 0.016f, 0.045f), Quaternion.Euler(-8f, 0, 0), Vector3.one);
            details.AddRoundedBox(new Vector3(0, 0.0146f, 0), new Vector3(0.044f, 0.002f, 0.054f), 0.001f, 1, k_Face);
            details.AddCylinder(new Vector3(-side * 0.01f, 0.015f, -0.006f), 0.0045f, 0.007f, 12, k_Button);
            details.AddCylinder(new Vector3(-side * 0.01f, 0.022f, -0.006f), 0.0095f, 0.004f, 18, k_Button);
            details.AddCylinder(new Vector3(side * 0.011f, 0.015f, 0.006f), 0.0052f, 0.003f, 14, k_Button);
            details.AddCylinder(new Vector3(side * 0.003f, 0.015f, 0.019f), 0.0052f, 0.003f, 14, k_Button);
            details.Transform = Matrix4x4.TRS(new Vector3(0, -0.006f, 0.062f), Quaternion.Euler(35f, 0, 0), Vector3.one);
            details.AddRoundedBox(Vector3.zero, new Vector3(0.016f, 0.012f, 0.026f), 0.005f, 2, k_Button * 0.8f);
            details.Transform = Matrix4x4.identity;

            var b = body.Build(grip, "Body", Shapes.Lit);
            Shapes.SetColor(b, k_Body);
            var d = details.Build(grip, "Details", Shapes.Lit);
            Shapes.SetColor(d, k_Button);
        }

        /// <summary>Simple hand for runtimes without hand joints: palm and a dot where thumb and index finger meet.</summary>
        public static void BuildHand(Transform grip)
        {
            var skin = new Color(0.78f, 0.74f, 0.70f);
            Shapes.Create(PrimitiveType.Sphere, grip, "Palm", new Vector3(0, 0, 0.0f), new Vector3(0.075f, 0.025f, 0.085f), skin);
            Shapes.Create(PrimitiveType.Sphere, grip, "Pinch", new Vector3(0, 0.01f, 0.075f), Vector3.one * 0.016f,
                new Color(0.95f, 0.62f, 0.18f));
        }

        // ---------------- Official models ----------------

        /// <summary>Loads the headset's model of a controller in the background; calls back on the main thread (null = not available).</summary>
        public static void LoadOfficial(bool left, MonoBehaviour host, Action<Mesh, string> done)
        {
#if THEMOVIES_OPENXR
            if (!RenderModelFeature.Available)
            {
                done(null, RenderModelFeature.Status);
                return;
            }
            var task = Task.Run(() =>
            {
                byte[] glb = RenderModelFeature.LoadController(left, out string error);
                if (glb == null) return ((GltfModel)null, error);
                return (Gltf.ReadGlb(glb), (string)null);
            });
            host.StartCoroutine(Wait(task, done));
#else
            done(null, "OpenXR nicht im Build");
#endif
        }

        static System.Collections.IEnumerator Wait(Task<(GltfModel model, string error)> task, Action<Mesh, string> done)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                done(null, task.Exception?.GetBaseException().Message);
                yield break;
            }
            var (model, error) = task.Result;
            done(model != null ? ToMesh(model) : null, error);
        }

        /// <summary>All parts of the model in one mesh.</summary>
        static Mesh ToMesh(GltfModel model)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (var part in model.Meshes)
            {
                int start = vertices.Count;
                int count = part.Positions.Length / 3;
                for (int v = 0; v < count; v++)
                {
                    vertices.Add(new Vector3(part.Positions[v * 3], part.Positions[v * 3 + 1], part.Positions[v * 3 + 2]));
                    normals.Add(part.Normals != null && part.Normals.Length >= (v + 1) * 3
                        ? new Vector3(part.Normals[v * 3], part.Normals[v * 3 + 1], part.Normals[v * 3 + 2])
                        : Vector3.up);
                }
                foreach (int i in part.Indices) triangles.Add(start + i);
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = "Controller (headset model)" };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Shows a loaded headset model under the controller container (hides the stand-in).</summary>
        public static void ShowOfficial(Transform container, Mesh mesh)
        {
            var standIn = container.Find("Stand-in");
            if (standIn != null) standIn.gameObject.SetActive(false);
            var go = new GameObject("Headset Model");
            go.transform.SetParent(container, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.Lit;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // The model's textures are Basis-compressed (not readable here): Meta's dark grey instead.
            Shapes.SetColor(mr, new Color(0.13f, 0.13f, 0.145f));
        }
    }
}
