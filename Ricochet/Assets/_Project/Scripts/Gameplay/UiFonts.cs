using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The game's two faces (built by <c>FontBuilder</c> into Resources/Fonts): Display (Unbounded Bold) for numbers,
    /// titles and names, Body (Manrope Bold) for small labels and blurbs. Both are real bolds, so TMP's faux bold is
    /// dropped. Missing assets leave TMP's default font in place.
    /// </summary>
    public static class UiFonts
    {
        static TMP_FontAsset s_display, s_body;
        static bool s_loaded;

        public static TMP_FontAsset Display { get { Load(); return s_display; } }
        public static TMP_FontAsset Body { get { Load(); return s_body; } }

        static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            s_display = Resources.Load<TMP_FontAsset>("Fonts/Display SDF");
            s_body = Resources.Load<TMP_FontAsset>("Fonts/Body SDF");
        }

        /// <summary>Gives a text its face. Call before setting the outline (it instances the font's material).</summary>
        public static void Use(TMP_Text text, bool display)
        {
            var font = display ? Display : Body;
            if (font == null) return;
            text.font = font;
            text.fontStyle &= ~FontStyles.Bold;
        }
    }
}
