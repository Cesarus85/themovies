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

        /// <summary>The headset's controller model, ready to show: one sub-mesh and one material per part.</summary>
        public sealed class OfficialModel
        {
            public Mesh Mesh;
            public Material[] Materials;

            /// <summary>Parts shown with their texture (Meta's black and white look); the others are dark grey.</summary>
            public int TexturedParts;

            /// <summary>Why a texture could not be read, null if all were.</summary>
            public string TextureError;
        }

        static Material s_Grey;

        /// <summary>
        /// Loads the headset's model of a controller: the GLB in the background, then its KTX2 textures (Basis UASTC +
        /// Zstandard, decoded by the package KTX for Unity). Calls back on the main thread (null = not available).
        /// </summary>
        public static void LoadOfficial(bool left, MonoBehaviour host, Action<OfficialModel, string> done)
        {
#if THEMOVIES_OPENXR
            if (!RenderModelFeature.Available)
            {
                done(null, RenderModelFeature.Status);
                return;
            }
            // Kept next to the log (Android/data/…/files) to look at the model on a PC.
            string copy = System.IO.Path.Combine(Application.persistentDataPath, left ? "controller-left.glb" : "controller-right.glb");
            var task = Task.Run(() =>
            {
                byte[] glb = RenderModelFeature.LoadController(left, out string error);
                if (glb == null) return ((GltfModel)null, error);
                try
                {
                    System.IO.File.WriteAllBytes(copy, glb);
                }
                catch (Exception)
                {
                    // only a copy for diagnosis
                }
                return (Gltf.ReadGlb(glb), (string)null);
            });
            host.StartCoroutine(Wait(task, done));
#else
            done(null, "OpenXR nicht im Build");
#endif
        }

        static System.Collections.IEnumerator Wait(Task<(GltfModel model, string error)> task, Action<OfficialModel, string> done)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                done(null, task.Exception?.GetBaseException().Message);
                yield break;
            }
            var (gltf, error) = task.Result;
            // The battery indicator is a strip of all charge levels (Meta shifts it by the battery level): left out.
            var parts = gltf?.Meshes.FindAll(p => p.Name.IndexOf("battery", StringComparison.OrdinalIgnoreCase) < 0);
            var mesh = parts != null ? ToMesh(parts) : null;
            if (mesh == null)
            {
                done(null, error ?? "Modell ohne Geometrie");
                yield break;
            }

            var model = new OfficialModel { Mesh = mesh, Materials = new Material[parts.Count] };
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                model.Materials[i] = Grey;
                if (part.Image < 0 || part.Image >= gltf.Images.Count || part.Uvs == null) continue;
                var decode = DecodeTexture(gltf.Images[part.Image]);
                while (!decode.IsCompleted) yield return null;
                var (texture, flipY, textureError) = decode.IsFaulted
                    ? ((Texture2D)null, false, decode.Exception?.GetBaseException().Message)
                    : decode.Result;
                if (texture == null)
                {
                    model.TextureError = textureError;
                    continue;
                }
                texture.name = gltf.Images[part.Image].MimeType + " " + part.Name;
                texture.anisoLevel = 4;
                var material = new Material(Shapes.Lit) { name = "Controller " + part.Name, mainTexture = texture };
                material.EnableKeyword("_TM_TEXTURED");
                material.color = new Color(part.Color[0], part.Color[1], part.Color[2], part.Color[3]);
                if (flipY)
                {
                    // Gltf.ReadGlb flips V for bottom-up textures (PNG/JPEG in Unity); KTX2 rows are stored top-down.
                    material.mainTextureScale = new Vector2(1f, -1f);
                    material.mainTextureOffset = new Vector2(0f, 1f);
                }
                model.Materials[i] = material;
                model.TexturedParts++;
            }
            done(model, null);
        }

        /// <summary>The model's dark grey where no texture could be read.</summary>
        static Material Grey
        {
            get
            {
                if (s_Grey != null) return s_Grey;
                s_Grey = new Material(Shapes.Lit) { name = "Controller (grey)", color = new Color(0.13f, 0.13f, 0.145f) };
                return s_Grey;
            }
        }

        static Task<(Texture2D texture, bool flipY, string error)> DecodeTexture(GltfImage image)
        {
            if (image.Bytes == null || image.Bytes.Length == 0)
                return Task.FromResult(((Texture2D)null, false, "Bild ohne Daten"));
            if (image.MimeType != "image/ktx2")
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (texture.LoadImage(image.Bytes, true))
                    return Task.FromResult((texture, false, (string)null));
                UnityEngine.Object.Destroy(texture);
                return Task.FromResult(((Texture2D)null, false, "Bild nicht lesbar: " + image.MimeType));
            }
#if THEMOVIES_KTX
            return DecodeKtx(image.Bytes);
#else
            return Task.FromResult(((Texture2D)null, false, "KTX2-Texturen brauchen das Paket 'KTX for Unity' (com.unity.cloud.ktx)"));
#endif
        }

#if THEMOVIES_KTX
        static async Task<(Texture2D texture, bool flipY, string error)> DecodeKtx(byte[] bytes)
        {
            using var data = new Unity.Collections.NativeArray<byte>(bytes, Unity.Collections.Allocator.Persistent);
            var result = await new KtxUnity.KtxTexture().LoadFromBytes(data.AsReadOnly(), linear: false);
            if (result.errorCode != KtxUnity.ErrorCode.Success || result.texture == null)
                return (null, false, "KTX2: " + result.errorCode);
            return (result.texture, KtxUnity.TextureOrientationExtension.IsYFlipped(result.orientation), null);
        }
#endif

        /// <summary>The parts of the model as sub-meshes of one mesh (part i = sub-mesh i), with texture coordinates.</summary>
        static Mesh ToMesh(System.Collections.Generic.List<GltfMesh> meshes)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var parts = new System.Collections.Generic.List<int[]>();
            foreach (var part in meshes)
            {
                int start = vertices.Count;
                int count = part.Positions.Length / 3;
                for (int v = 0; v < count; v++)
                {
                    vertices.Add(new Vector3(part.Positions[v * 3], part.Positions[v * 3 + 1], part.Positions[v * 3 + 2]));
                    normals.Add(part.Normals != null && part.Normals.Length >= (v + 1) * 3
                        ? new Vector3(part.Normals[v * 3], part.Normals[v * 3 + 1], part.Normals[v * 3 + 2])
                        : Vector3.up);
                    uvs.Add(part.Uvs != null && part.Uvs.Length >= (v + 1) * 2
                        ? new Vector2(part.Uvs[v * 2], part.Uvs[v * 2 + 1])
                        : Vector2.zero);
                }
                var indices = new int[part.Indices.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = start + part.Indices[i];
                parts.Add(indices);
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = "Controller (headset model)" };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = parts.Count;
            for (int i = 0; i < parts.Count; i++) mesh.SetTriangles(parts[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Shows a loaded headset model under the controller container (hides the stand-in).</summary>
        public static void ShowOfficial(Transform container, OfficialModel model)
        {
            var standIn = container.Find("Stand-in");
            if (standIn != null) standIn.gameObject.SetActive(false);
            var go = new GameObject("Headset Model");
            go.transform.SetParent(container, false);
            // The runtime's model is in OpenXR grip space (right-handed, -Z forward: the head with the buttons lies
            // at -Z). Gltf.ReadGlb converts like any glTF by mirroring X, Unity's OpenXR poses mirror Z – the
            // difference is half a turn about Y. Without it the head points at the little finger (down).
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = model.Materials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
