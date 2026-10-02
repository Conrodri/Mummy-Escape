using System.Collections.Generic;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>One piece of music: a file of <c>Resources/Music</c>, or a theme composed by the game.</summary>
    public sealed class MusicTrack
    {
        /// <summary>0 = menus, 1..5 = acts.</summary>
        public int Theme;
        /// <summary>Floor (1-based) the track is reserved for; 0 = every floor of the act.</summary>
        public int Floor;
        /// <summary>Resource name under <c>Resources/Music</c>; null for the theme composed by <see cref="MusicComposer"/>.</summary>
        public string Resource;
        public string Id => Resource ?? $"composed{Theme}";
        public bool Composed => Resource == null;
    }

    /// <summary>
    /// Every track the game can play, read from the file names in <c>Resources/Music</c>:
    /// <c>menu</c>, <c>act{n}</c> (whole act), <c>act{n}_f{floor}</c> (one floor), each optionally followed by a
    /// variant suffix (<c>act2_b</c>, <c>act2_f2_calme</c>…). Several tracks for the same slot are variants: one is
    /// drawn at random for each run. An act without any file falls back to its composed theme.
    /// </summary>
    public static class MusicCatalog
    {
        static List<MusicTrack> _tracks;

        public static IReadOnlyList<MusicTrack> All
        {
            get
            {
                if (_tracks == null) Load();
                return _tracks;
            }
        }

        static void Load()
        {
            _tracks = new List<MusicTrack>();
            foreach (var clip in Resources.LoadAll<AudioClip>("Music"))
            {
                var t = Parse(clip.name);
                if (t != null) _tracks.Add(t);
                Resources.UnloadAsset(clip); // only the names are kept; a track loads when it plays
            }
            // The composed theme of each act (and the built-in menu loop) stays available when no file covers the whole act.
            for (int act = 0; act <= Core.DifficultyTable.ActCount; act++)
                if (!_tracks.Exists(t => t.Theme == act && t.Floor == 0))
                    _tracks.Add(new MusicTrack { Theme = act });
            _tracks.Sort((a, b) => a.Theme != b.Theme ? a.Theme.CompareTo(b.Theme)
                                 : a.Floor != b.Floor ? a.Floor.CompareTo(b.Floor)
                                 : string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>"menu", "act3", "act3_f2", "act3_b", "act3_f2_b" → track; anything else is ignored.</summary>
        public static MusicTrack Parse(string name)
        {
            var parts = name.ToLowerInvariant().Split('_');
            int theme;
            if (parts[0] == "menu") theme = 0;
            else if (parts[0].StartsWith("act") && int.TryParse(parts[0].Substring(3), out theme) && theme >= 1) { }
            else return null;
            int floor = 0;
            if (theme > 0 && parts.Length > 1 && parts[1].StartsWith("f") && int.TryParse(parts[1].Substring(1), out int f) && f >= 1)
                floor = f;
            return new MusicTrack { Theme = theme, Floor = floor, Resource = name };
        }

        /// <summary>
        /// Tracks that may play on a floor (1-based) of a theme: those reserved for that floor if there are any,
        /// otherwise those of the whole act.
        /// </summary>
        public static List<MusicTrack> For(int theme, int floor)
        {
            if (_tracks == null) Load();
            var own = _tracks.FindAll(t => t.Theme == theme && t.Floor == floor && floor > 0);
            return own.Count > 0 ? own : _tracks.FindAll(t => t.Theme == theme && t.Floor == 0);
        }
    }
}
