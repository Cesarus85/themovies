using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TheMovies.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Turns a room model from Blender into the prefab the app loads instead of its built-in room
    /// (see CinemaEnvironment.BuildCustom and docs/ART-BRIEF.md):
    /// <list type="bullet">
    /// <item>Assets/TheMovies/Art/Cinema/Cinema.fbx → Resources/TheMovies/CustomCinema.prefab</item>
    /// <item>Assets/TheMovies/Art/Home/Home.fbx → Resources/TheMovies/CustomHome.prefab</item>
    /// <item>Assets/TheMovies/Art/Rooftop/Rooftop.fbx → Resources/TheMovies/CustomRooftop.prefab</item>
    /// </list>
    /// Import settings for the model (keeps the second UV set from Blender) and its textures, one CinemaLit material
    /// per Blender material (texture <c>Textures/&lt;Material&gt;_BaseColor.png</c>, baked light
    /// <c>Textures/&lt;Model&gt;_Light.png</c> on UV2, hints from the material name), then checks markers and budget.
    /// </summary>
    public static class RoomModelSetup
    {
        public const string ArtFolder = "Assets/TheMovies/Art";
        const string k_ResourcesFolder = "Assets/TheMovies/Resources/TheMovies";

        // Quest 3 budget per room (stereo at 72–90 Hz on a mobile GPU).
        const int k_MaxTriangles = 300_000;
        const int k_MaxMaterials = 24;

        struct RoomKind
        {
            public string Folder, Model, Prefab, Title;
        }

        static readonly RoomKind k_Cinema = new RoomKind { Folder = "Cinema", Model = "Cinema", Prefab = "CustomCinema", Title = "Kinosaal" };
        static readonly RoomKind k_Home = new RoomKind { Folder = "Home", Model = "Home", Prefab = "CustomHome", Title = "Heimkino" };
        static readonly RoomKind k_Rooftop = new RoomKind { Folder = "Rooftop", Model = "Rooftop", Prefab = "CustomRooftop", Title = "Dachterrasse" };
        static readonly RoomKind[] k_All = { k_Cinema, k_Home, k_Rooftop };

        [MenuItem("The Movies/Raum-Modell aufbereiten/Kinosaal (Art/Cinema)", priority = 50)]
        static void PrepareCinemaMenu() => Report(Prepare(k_Cinema));

        [MenuItem("The Movies/Raum-Modell aufbereiten/Heimkino (Art/Home)", priority = 51)]
        static void PrepareHomeMenu() => Report(Prepare(k_Home));

        [MenuItem("The Movies/Raum-Modell aufbereiten/Dachterrasse (Art/Rooftop)", priority = 52)]
        static void PrepareRooftopMenu() => Report(Prepare(k_Rooftop));

        [MenuItem("The Movies/Raum-Modell aufbereiten/Eigene Räume entfernen (eingebaute verwenden)", priority = 60)]
        static void RemoveMenu()
        {
            if (!EditorUtility.DisplayDialog("The Movies", "Die aufbereiteten Raum-Prefabs löschen? Die App nutzt dann wieder die eingebauten Räume. " +
                                                           "Die Modelle unter " + ArtFolder + " bleiben.", "Löschen", "Abbrechen"))
                return;
            foreach (var kind in k_All)
                AssetDatabase.DeleteAsset(PrefabPath(kind));
        }

        /// <summary>
        /// Command line: Unity -batchmode -quit -projectPath . -executeMethod TheMovies.EditorTools.RoomModelSetup.PrepareAllBatch
        /// Prepares every room whose model exists; exit code 1 if one of them has errors.
        /// </summary>
        public static void PrepareAllBatch()
        {
            int exitCode = 0;
            foreach (var kind in k_All)
            {
                if (FindModel(kind) == null) continue;
                var result = Prepare(kind);
                Debug.Log("[TheMovies] " + result.Text);
                if (!result.Ok) exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        struct Result
        {
            public bool Ok;
            public string Text;
        }

        static void Report(Result result)
        {
            Debug.Log("[TheMovies] " + result.Text);
            EditorUtility.DisplayDialog("The Movies", result.Text, "OK");
        }

        static string PrefabPath(RoomKind kind) => k_ResourcesFolder + "/" + kind.Prefab + ".prefab";

        static string FindModel(RoomKind kind)
        {
            string folder = ArtFolder + "/" + kind.Folder;
            string preferred = folder + "/" + kind.Model + ".fbx";
            if (File.Exists(preferred)) return preferred;
            if (!Directory.Exists(folder)) return null;
            return Directory.GetFiles(folder, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).FirstOrDefault();
        }

        static Result Prepare(RoomKind kind)
        {
            string modelPath = FindModel(kind);
            if (modelPath == null)
                return new Result { Ok = false, Text = $"{kind.Title}: kein Modell unter {ArtFolder}/{kind.Folder}/{kind.Model}.fbx gefunden." };

            string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            var log = new StringBuilder();
            var problems = new List<string>();
            var warnings = new List<string>();

            ConfigureModel(modelPath);
            var textures = ConfigureTextures(folder + "/Textures", modelName);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
                return new Result { Ok = false, Text = $"{kind.Title}: {modelPath} ließ sich nicht laden." };

            textures.TryGetValue(modelName + "_Light", out var lightTexture);
            if (lightTexture == null)
                warnings.Add($"kein eingebackenes Licht (Textures/{modelName}_Light.png) – der Raum wird nur vom Leinwandschein und einem Grundlicht beleuchtet");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = kind.Prefab;
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);

                var materials = new Dictionary<string, Material>();
                int triangles = 0, withoutUv2 = 0;
                var meshes = new HashSet<Mesh>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    bool baked = lightTexture != null && mesh != null && HasUv2(mesh);
                    if (mesh != null)
                    {
                        for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)mesh.GetIndexCount(s) / 3;
                        if (meshes.Add(mesh) && !HasUv2(mesh)) withoutUv2++;
                    }

                    var source = renderer.sharedMaterials;
                    var converted = new Material[source.Length];
                    for (int i = 0; i < source.Length; i++)
                        converted[i] = Convert(source[i], baked, lightTexture, textures, folder, materials);
                    renderer.sharedMaterials = converted;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }

                // Markers: the app moves the model so that the seats are the standard seats and the screen hangs at the anchor.
                var markers = instance.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t, StringComparer.OrdinalIgnoreCase);
                int seats = 0;
                for (int i = 1; i <= CinemaEnvironment.MaxParticipants; i++)
                    if (markers.ContainsKey("Seat" + i)) seats++;
                if (seats == 0) problems.Add("keine Sitz-Marker (Empties „Seat1“ … „Seat4“ auf dem Boden vor den Sitzen)");
                else if (seats < CinemaEnvironment.MaxParticipants) warnings.Add($"nur {seats} von 4 Sitz-Markern");
                if (!markers.ContainsKey("ScreenAnchor")) problems.Add("kein Marker „ScreenAnchor“ (Unterkante Mitte der Leinwand)");
                if (!markers.ContainsKey("ScreenTop")) warnings.Add("kein Marker „ScreenTop“ – die Leinwand nutzt die Standardhöhe");
                if (markers.TryGetValue("Seat1", out var s1) && markers.TryGetValue("Seat2", out var s2))
                {
                    float spacing = Vector3.Distance(s1.position, s2.position);
                    if (Mathf.Abs(spacing - CinemaEnvironment.SeatSpacing) > 0.1f)
                        warnings.Add($"Sitz-Abstand {spacing:0.00} m statt {CinemaEnvironment.SeatSpacing} m – Avatare der anderen sitzen dann nicht genau");
                }

                if (triangles > k_MaxTriangles) warnings.Add($"{triangles:N0} Dreiecke – mehr als die {k_MaxTriangles:N0} fürs Budget auf der Quest");
                if (materials.Count > k_MaxMaterials) warnings.Add($"{materials.Count} Materialien – mehr als {k_MaxMaterials} (Draw Calls)");
                if (withoutUv2 > 0 && lightTexture != null) warnings.Add($"{withoutUv2} Meshes ohne zweite UV-Ebene – dort kein eingebackenes Licht");

                Directory.CreateDirectory(k_ResourcesFolder);
                if (problems.Count == 0)
                    PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(kind));

                log.Append(kind.Title).Append(": ").Append(problems.Count == 0 ? "fertig → " + PrefabPath(kind) : "NICHT übernommen").Append('\n');
                log.Append($"{triangles:N0} Dreiecke, {meshes.Count} Meshes, {materials.Count} Materialien, {textures.Count} Texturen, " +
                           $"eingebackenes Licht: {(lightTexture != null ? lightTexture.width + "²" : "nein")}, Sitz-Marker: {seats}/4");
                foreach (string p in problems) log.Append("\nFEHLER: ").Append(p);
                foreach (string w in warnings) log.Append("\nHinweis: ").Append(w);
                return new Result { Ok = problems.Count == 0, Text = log.ToString() };
            }
            finally
            {
                Object.DestroyImmediate(instance);
                AssetDatabase.SaveAssets();
            }
        }

        static bool HasUv2(Mesh mesh) => mesh.HasVertexAttribute(VertexAttribute.TexCoord1);

        /// <summary>
        /// CinemaLit material for a Blender material, saved next to the model (Materials/). Texture: the source
        /// material's, else Textures/&lt;Material&gt;_BaseColor. Hints from the name: velvet/samt (sheen), gold/brass/
        /// messing/chrome/metal (highlights), emit/glow/bulb/leucht (glows, dims with the house lights, no baked light),
        /// unlit (glows, does not dim – e.g. a starry sky or an exit sign).
        /// </summary>
        static Material Convert(Material source, bool baked, Texture2D lightTexture, Dictionary<string, Texture2D> textures,
            string folder, Dictionary<string, Material> done)
        {
            string name = source != null ? source.name : "Default";
            string key = name + (baked ? "" : " (ohne Licht)");
            if (done.TryGetValue(key, out var existing)) return existing;

            string materialFolder = folder + "/Materials";
            Directory.CreateDirectory(materialFolder);
            string path = materialFolder + "/" + Sanitize(key) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("TheMovies/CinemaLit");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.name = Sanitize(key);

            Texture texture = null;
            Color color = Color.white;
            if (source != null)
            {
                if (source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
                if (texture == null && source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
                if (source.HasProperty("_Color")) color = source.GetColor("_Color");
                else if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
            }
            if (textures.TryGetValue(name + "_BaseColor", out var named)) texture = named;
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one;
            // A texture carries the color; only a plain material uses the Blender color.
            material.color = texture != null ? Color.white : new Color(color.r, color.g, color.b, 1f);
            material.SetFloat("_VertexColor", 0f);

            string n = name.ToLowerInvariant();
            bool unlit = n.Contains("unlit");
            bool emissive = unlit || n.Contains("emit") || n.Contains("glow") || n.Contains("bulb") || n.Contains("leucht");
            material.SetFloat("_Sheen", n.Contains("velvet") || n.Contains("samt") ? 0.8f : 0f);
            material.SetFloat("_Specular", n.Contains("gold") || n.Contains("brass") || n.Contains("messing") || n.Contains("chrome") || n.Contains("metal") ? 0.5f : 0f);
            material.SetFloat("_Emission", emissive ? 1.2f : 0f);
            material.SetFloat("_RoomLightAffect", emissive && !unlit ? 1f : 0f);
            material.SetFloat("_ScreenGlow", emissive ? 0f : 1f);

            bool useBaked = baked && !emissive;
            material.SetTexture("_LightTex", useBaked ? lightTexture : null);
            material.SetFloat("_BakedDim", 0.04f); // house lights off (0 % / during the movie): nearly dark, the screen lights the room
            if (useBaked) material.EnableKeyword("_TM_BAKED");
            else material.DisableKeyword("_TM_BAKED");
            EditorUtility.SetDirty(material);
            done[key] = material;
            return material;
        }

        static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        static void ConfigureModel(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            Set(importer.globalScale, 1f, v => importer.globalScale = v);
            Set(importer.useFileScale, true, v => importer.useFileScale = v);
            Set(importer.generateSecondaryUV, false, v => importer.generateSecondaryUV = v); // UV2 comes from Blender
            Set(importer.meshCompression, ModelImporterMeshCompression.Off, v => importer.meshCompression = v); // UV2 precision
            Set(importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
            Set(importer.importBlendShapes, false, v => importer.importBlendShapes = v);
            Set(importer.importCameras, false, v => importer.importCameras = v);
            Set(importer.importLights, false, v => importer.importLights = v);
            Set(importer.importAnimation, false, v => importer.importAnimation = v);
            Set(importer.animationType, ModelImporterAnimationType.None, v => importer.animationType = v);
            Set(importer.isReadable, false, v => importer.isReadable = v);
            Set(importer.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription, v => importer.materialImportMode = v);
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>Import settings for the room's textures; returns them by file name (without extension).</summary>
        static Dictionary<string, Texture2D> ConfigureTextures(string folder, string modelName)
        {
            var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folder)) return result;
            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".tga") continue;
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    bool light = name.EndsWith("_Light", StringComparison.OrdinalIgnoreCase);
                    // Surrounding panoramas (sky, far city) cover up to 360°: 2048 would be blurry (~6 px per degree).
                    bool panorama = name.IndexOf("Panorama", StringComparison.OrdinalIgnoreCase) >= 0;
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true; // base color and baked light (saved with Blender's "Standard" view) are sRGB
                    importer.mipmapEnabled = true;
                    importer.anisoLevel = light ? 1 : 4;
                    importer.wrapMode = light ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    importer.maxTextureSize = light || panorama ? 4096 : 2048;
                    importer.anisoLevel = panorama ? 1 : importer.anisoLevel;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    var android = importer.GetPlatformTextureSettings("Android");
                    android.overridden = true;
                    android.maxTextureSize = importer.maxTextureSize;
                    // Light: smooth gradients need the finer blocks; base colors are fine with 6×6.
                    android.format = light ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) result[name] = texture;
            }
            return result;
        }
    }
}
