using System;
using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape
{
    /// <summary>
    /// Game localization. The French text is the key (the game is written in French): <c>Loc.T("Jouer")</c> returns
    /// "Play" in English, and the French text itself when French is active or a translation is missing.
    /// Formats use string.Format placeholders: <c>Loc.F("Niveau {0}", id)</c>.
    /// Add a language: a value in <see cref="Lang"/>, its entry in <see cref="Languages"/> and a table (see Loc.En.cs).
    /// The editor command <c>mummy_loc_check</c> lists texts that have no translation.
    /// </summary>
    public static partial class Loc
    {
        public enum Lang { Fr, En }

        public sealed class LangInfo
        {
            public Lang Id;
            public string Code;       // ISO 639-1, stored in the settings
            public string NativeName; // shown in the picker, always in its own language
            public string DecimalSeparator;
            public Dictionary<string, string> Table; // null = source language
        }

        // Built on first use: the tables live in other files of this partial class, whose static
        // initializers have no guaranteed order relative to this one.
        static LangInfo[] _languages;
        public static LangInfo[] Languages => _languages ??= new[]
        {
            new LangInfo { Id = Lang.Fr, Code = "fr", NativeName = "Français", DecimalSeparator = "," },
            new LangInfo { Id = Lang.En, Code = "en", NativeName = "English", DecimalSeparator = ".", Table = En },
        };

        public static Lang Current { get; private set; } = Lang.Fr;
        public static LangInfo Info => Array.Find(Languages, l => l.Id == Current);

        /// <summary>Raised after the language changed (the UI rebuilds itself).</summary>
        public static event Action Changed;

        /// <summary>Applies a stored choice: a language code, or "" to follow the device language.</summary>
        public static void Apply(string code)
        {
            var info = string.IsNullOrEmpty(code) ? FromSystem() : Array.Find(Languages, l => l.Code == code) ?? FromSystem();
            bool changed = info.Id != Current;
            Current = info.Id;
            CoreText.Translate = T;
            CoreText.DecimalSeparator = info.DecimalSeparator;
            if (changed) Changed?.Invoke();
        }

        /// <summary>Device language when supported, English otherwise (the most widely understood fallback).</summary>
        public static LangInfo FromSystem()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.French: return Languages[0];
                default: return Array.Find(Languages, l => l.Id == Lang.En);
            }
        }

        public static string T(string french)
        {
            if (string.IsNullOrEmpty(french)) return french;
            var table = Info.Table;
            return table != null && table.TryGetValue(french, out var s) ? s : french;
        }

        public static string F(string frenchFormat, params object[] args) => string.Format(T(frenchFormat), args);

        /// <summary>Singular or plural form ("1 coup", "3 coups"): pass both French formats.</summary>
        public static string P(int n, string singular, string plural) => F(n > 1 ? plural : singular, n);

        /// <summary>True when the text has a translation in every language (used by the check command).</summary>
        public static IEnumerable<string> MissingIn(Lang lang, IEnumerable<string> keys)
        {
            var table = Array.Find(Languages, l => l.Id == lang).Table;
            if (table == null) yield break;
            foreach (var k in keys) if (!table.ContainsKey(k)) yield return k;
        }
    }
}
