using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Builds the game's two TMP font assets from the static bold instances in Art/Fonts (OFL fonts; instanced from
    /// the variable fonts with fontTools, recipe in docs/STATUS.md): "Display SDF" (Unbounded Bold: numbers, titles,
    /// names) and "Body SDF" (Manrope Bold: small labels and blurbs). Static atlases with exactly the characters the
    /// game prints, so nothing is rasterized at runtime. They live in Resources/Fonts for <c>UiFonts</c>.
    /// </summary>
    public static class FontBuilder
    {
        const string FontDir = "Assets/_Project/Art/Fonts";
        const string OutDir = "Assets/_Project/Resources/Fonts";
        const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "×·•–—’…";   // × · • – — ’ …

        public static string Build()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(OutDir);
            Make("Unbounded-Bold", "Display", log);
            Make("Manrope-Bold", "Body", log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static void Make(string file, string name, StringBuilder log)
        {
            string fontPath = $"{FontDir}/{file}.ttf";
            string assetPath = $"{OutDir}/{name} SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) { log.AppendLine("missing " + fontPath); return; }
            AssetDatabase.DeleteAsset(assetPath);
            // 72 pt samples with 8 px padding: crisp edges at our sizes and room for the outline.
            var asset = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            asset.name = name + " SDF";
            asset.TryAddCharacters(Charset, out string missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, assetPath);
            var atlas = asset.atlasTextures[0];
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            log.AppendLine($"{name}: {asset.characterTable.Count} characters{(string.IsNullOrEmpty(missing) ? "" : ", missing '" + missing + "'")}");
        }
    }
}
