using MummyEscape.App;
using MummyEscape.Pvp;
using MummyEscape.Visual;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>
    /// The player's side of <see cref="Titles"/>: progress read from the save (solo stars, tombs escaped in under 5 s) and
    /// from the duel profile (wins, best league), the equipped title, and how a title is written under a name.
    /// </summary>
    public static class TitleBook
    {
        public static int StarsInAct(SaveService save, int act)
        {
            int stars = 0;
            foreach (var r in save.Data.Records)
                if (r != null && ActOf(r.Key) == act) stars += r.BestStars;
            return stars;
        }

        /// <summary>Act of a record key ("level_3_7" → 3), 0 when unreadable.</summary>
        static int ActOf(string key)
        {
            var parts = key?.Split('_');
            return parts != null && parts.Length == 3 && int.TryParse(parts[1], out int act) ? act : 0;
        }

        /// <summary>Different tombs escaped in under <see cref="Titles.SpeedLimitMs"/>.</summary>
        public static int FastTombs(SaveService save)
        {
            int n = 0;
            foreach (var r in save.Data.Records)
            {
                if (r == null || r.Completions == 0) continue;
                int best = r.FastestMs > 0 ? r.FastestMs : r.BestTimeMs;
                if (best > 0 && best < Titles.SpeedLimitMs) n++;
            }
            return n;
        }

        public static (int value, int goal) Progress(GameApp app, TitleDef t)
        {
            var d = app.PvpProfile?.Data;
            return Titles.Progress(t, act => StarsInAct(app.Save, act), FastTombs(app.Save), d?.Wins ?? 0, d?.HighestLeague ?? League.Bronze);
        }

        public static bool Earned(GameApp app, TitleDef t)
        {
            var (value, goal) = Progress(app, t);
            return value >= goal;
        }

        /// <summary>
        /// The title the player shows, null for none. Duel titles count as earned until the duel profile says otherwise
        /// (it loads late and a title is never lost: wins and the best league only grow).
        /// </summary>
        public static string Equipped(GameApp app)
        {
            var t = Titles.Get(app.Save.Data.SelectedTitle);
            if (t == null) return null;
            if (t.IsDuel && app.PvpProfile?.Data == null) return t.Id;
            return Earned(app, t) ? t.Id : null;
        }

        public static Color ColorOf(TitleDef t)
        {
            switch (t.Kind)
            {
                case TitleKind.Solo: return Color.Lerp(TombTheme.ForAct(t.Act).Accent, Color.white, 0.2f);
                case TitleKind.Wins: return new Color32(255, 150, 90, 255);
                case TitleKind.League: return PvpSkins.LeagueColor(t.League);
                default: return new Color32(120, 236, 255, 255);
            }
        }

        /// <summary>« Title » in its colour (rich text), "" when there is none; with a line break after it if asked.</summary>
        public static string Line(string id, bool newLine = false)
        {
            var t = Titles.Get(id);
            if (t == null) return "";
            string hex = ColorUtility.ToHtmlStringRGB(ColorOf(t));
            return $"<color=#{hex}>« {Loc.T(t.Name)} »</color>" + (newLine ? "\n" : ""); // noloc
        }
    }
}
