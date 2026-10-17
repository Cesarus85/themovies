using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// The menu font: a TextMeshPro SDF font asset (signed distance field: edges stay sharp at any size and distance,
    /// unlike the old bitmap TextMesh font that blurred and shimmered in the headset). Same settings as in Questwelt:
    /// Liberation Sans (SIL Open Font License, shipped with the TMP Essential Resources), sampled at 90 pt with
    /// 9 px padding into a 2048² atlas, anti-aliased SDF. Dynamic: characters not prepared here (e.g. in movie titles)
    /// are added at runtime from the font file.
    /// </summary>
    public static class FontSetup
    {
        public const string FontAssetPath = "Assets/TheMovies/Resources/TheMovies/MenuFont.asset";
        const string k_SourceFont = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        const string k_EssentialsFolder = "Assets/TextMesh Pro";

        // Prepared in the atlas: ASCII, Latin-1 and the typographic characters the menus use.
        static string PreparedCharacters
        {
            get
            {
                var chars = Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 96)).Select(c => (char)c);
                return new string(chars.ToArray()) + "·…–—„“”‚‘’°→←↑↓×●○−›‹€■";
            }
        }

        [MenuItem("The Movies/Menü-Schrift erzeugen (TextMeshPro)", priority = 70)]
        static void CreateMenu()
        {
            string report = Prepare(true);
            Debug.Log("[TheMovies] " + report);
            EditorUtility.DisplayDialog("The Movies", report, "OK");
        }

        /// <summary>Command line: Unity -batchmode -quit -projectPath . -executeMethod TheMovies.EditorTools.FontSetup.PrepareBatch</summary>
        public static void PrepareBatch()
        {
            string report = Prepare(true);
            Debug.Log("[TheMovies] " + report);
            EditorApplication.Exit(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null ? 0 : 1);
        }

        /// <summary>Imports the TMP Essential Resources if missing, then creates (or with <paramref name="rebuild"/> recreates) the menu font.</summary>
        public static string Prepare(bool rebuild)
        {
            if (!ImportEssentials(out string error)) return error;
            if (!rebuild && AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null) return "Menü-Schrift vorhanden.";

            var font = AssetDatabase.LoadAssetAtPath<Font>(k_SourceFont);
            if (font == null) return "Schriftdatei fehlt: " + k_SourceFont;
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                return "TMP-Shader noch nicht geladen (frisch importiert?) – bitte erneut ausführen.";

            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (asset == null) return "TMP_FontAsset.CreateFontAsset ist fehlgeschlagen.";
            asset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
            asset.TryAddCharacters(PreparedCharacters, out string missing);

            AssetDatabase.DeleteAsset(FontAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(FontAssetPath));
            AssetDatabase.CreateAsset(asset, FontAssetPath);
            // Atlas texture and material live inside the font asset (as TMP's own "Create Font Asset" does it).
            asset.atlasTexture.name = asset.name + " Atlas";
            asset.material.name = asset.name + " Material";
            foreach (var texture in asset.atlasTextures)
                if (texture != null && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            // Keep the prepared glyphs in the build (TMP's "Clear Dynamic Data On Build" stays off).
            var serialized = new SerializedObject(asset);
            var clear = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null && clear.boolValue)
            {
                clear.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);

            string missingText = string.IsNullOrEmpty(missing) ? "keine" : string.Join(" ", missing.Select(c => $"U+{(int)c:X4}"));
            return $"Menü-Schrift erzeugt: {FontAssetPath} ({asset.characterTable.Count} Zeichen vorbereitet, fehlend in Liberation Sans: {missingText}).";
        }

        static bool ImportEssentials(out string error)
        {
            error = null;
            if (AssetDatabase.LoadAssetAtPath<Font>(k_SourceFont) != null && Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
                return true;

            var package = AssetDatabase.FindAssets("\"TMP Essential Resources\"")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".unitypackage"));
            if (package == null)
            {
                // Packages are not always found through the asset database: look in the package cache.
                var cache = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui*").FirstOrDefault();
                string candidate = cache != null ? Path.Combine(cache, "Package Resources", "TMP Essential Resources.unitypackage") : null;
                if (candidate != null && File.Exists(candidate)) package = candidate;
            }
            if (package == null)
            {
                error = "TMP Essential Resources nicht gefunden (Paket com.unity.ugui).";
                return false;
            }

            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<Font>(k_SourceFont) == null)
            {
                // In batch mode ImportPackage only finishes after this method; there Unity's -importPackage is needed.
                error = $"TMP Essential Resources werden importiert, {k_SourceFont} fehlt noch – danach erneut ausführen " +
                        "(Batchmode: Unity -importPackage \"…/TMP Essential Resources.unitypackage\" vorher).";
                return false;
            }
            Debug.Log("[TheMovies] TMP Essential Resources importiert nach " + k_EssentialsFolder + ".");
            return true;
        }
    }
}
