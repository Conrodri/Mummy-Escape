using System;

namespace MummyEscape.Core
{
    /// <summary>
    /// Translation hook for the few player-facing texts built by Core (scores, share text). Core stays free of
    /// UnityEngine: the game plugs its localization in at startup; without it, texts stay in French (tests rely on it).
    /// </summary>
    public static class CoreText
    {
        /// <summary>French source text (or format string) → current language.</summary>
        public static Func<string, string> Translate = s => s;

        /// <summary>Decimal separator of the current language ("," in French, "." in English).</summary>
        public static string DecimalSeparator = ",";

        public static string T(string french) => Translate(french);
        public static string F(string frenchFormat, params object[] args) => string.Format(Translate(frenchFormat), args);
    }
}
