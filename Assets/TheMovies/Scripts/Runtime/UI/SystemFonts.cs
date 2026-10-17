using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TheMovies.UI
{
    /// <summary>
    /// Fallbacks for characters the menu font (Liberation Sans: Latin, Greek, Cyrillic) does not have, e.g. Japanese or
    /// Thai movie titles: the headset's own system fonts, as dynamic SDF font assets. Their atlases are only allocated
    /// once a character is actually needed.
    /// </summary>
    public static class SystemFonts
    {
        // Quest system fonts (checked on Horizon OS, /system/fonts); missing files are skipped.
        static readonly string[] k_Fallbacks =
        {
            "/system/fonts/Roboto-Regular.ttf",
            "/system/fonts/NotoSansSymbols-Regular-Subsetted2.ttf",
            "/system/fonts/NotoSansSymbols-Regular-Subsetted.ttf",
            "/system/fonts/NotoSansCJK-Regular.ttc",
            "/system/fonts/NotoSansHebrew-Regular.ttf",
            "/system/fonts/NotoSansThai-Regular.ttf",
        };

        const int k_SamplingSize = 90;
        const int k_Padding = 9;
        const int k_AtlasSize = 1024;

        public static void AddFallbacks(TMP_FontAsset font)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (font.fallbackFontAssetTable.Count > 0) return;
            int added = 0;
            foreach (string path in k_Fallbacks)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var fallback = TMP_FontAsset.CreateFontAsset(path, 0, k_SamplingSize, k_Padding, GlyphRenderMode.SDFAA, k_AtlasSize, k_AtlasSize);
                    if (fallback == null) continue;
                    fallback.name = Path.GetFileNameWithoutExtension(path);
                    font.fallbackFontAssetTable.Add(fallback);
                    added++;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[TheMovies] System font {path}: {e.Message}");
                }
            }
            Debug.Log($"[TheMovies] Menu font: {added} system fonts as fallback.");
#endif
        }

        /// <summary>Puts the glyphs of <paramref name="text"/> into the atlas of the font, or of the fallback that has them.</summary>
        public static void Prepare(TMP_FontAsset font, string text)
        {
            if (font.TryAddCharacters(text, out string missing) || string.IsNullOrEmpty(missing)) return;
            if (font.fallbackFontAssetTable == null) return;
            foreach (var fallback in font.fallbackFontAssetTable)
                if (fallback != null && (fallback.TryAddCharacters(missing, out missing) || string.IsNullOrEmpty(missing))) return;
        }

        /// <summary>
        /// Horizontal advance of <paramref name="c"/> per meter of font size, from the menu font or one of its
        /// fallbacks (the character must have been added already, see <see cref="TMP_FontAsset.TryAddCharacters(string, bool)"/>).
        /// </summary>
        public static bool TryGetAdvance(TMP_FontAsset font, char c, out float advance)
        {
            if (TryGetAdvanceIn(font, c, out advance)) return true;
            if (font.fallbackFontAssetTable != null)
                foreach (var fallback in font.fallbackFontAssetTable)
                    if (fallback != null && TryGetAdvanceIn(fallback, c, out advance)) return true;
            advance = 0f;
            return false;
        }

        static bool TryGetAdvanceIn(TMP_FontAsset font, char c, out float advance)
        {
            advance = 0f;
            if (!font.characterLookupTable.TryGetValue(c, out var character) || character.glyph == null) return false;
            var face = font.faceInfo;
            advance = character.glyph.metrics.horizontalAdvance * character.scale * face.scale / face.pointSize;
            return true;
        }
    }
}
