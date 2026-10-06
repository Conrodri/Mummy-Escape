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
            if (t.Kind == TitleKind.Developer) return (IsDeveloper(app) ? 1 : 0, 1);
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

        /// <summary>
        /// One of the game's team: by the player id of this session, or the team skin already given to this phone (kept
        /// offline). Others who edit their save only fool themselves: the server strips both from what others see.
        /// </summary>
        public static bool IsDeveloper(GameApp app) =>
            Developers.Is(app.Online?.PlayerId) || app.Save.Data.OwnedSkins.Contains(Developers.SkinId);

        public static Color ColorOf(TitleDef t)
        {
            switch (t.Kind)
            {
                case TitleKind.Developer: return new Color32(255, 96, 220, 255);
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
            if (t.Legendary) return Shimmer("« " + Loc.T(t.Name) + " »", Time.unscaledTime) + (newLine ? "\n" : ""); // noloc
            string hex = ColorUtility.ToHtmlStringRGB(ColorOf(t));
            return $"<color=#{hex}>« {Loc.T(t.Name)} »</color>" + (newLine ? "\n" : ""); // noloc
        }

        /// <summary>
        /// A legendary title at time <paramref name="time"/>: bold, every letter its own colour on a rainbow drifting
        /// along the words, with a white glint sweeping across. <see cref="UI.TitleShimmer"/> redraws it every few frames.
        /// </summary>
        public static string Shimmer(string text, float time)
        {
            var sb = new System.Text.StringBuilder(text.Length * 26 + 7);
            sb.Append(ShimmerOpen);
            float glint = Mathf.Repeat(time * 0.6f, 1.6f) - 0.3f; // sweeps across, then a short rest
            for (int i = 0; i < text.Length; i++)
            {
                float k = text.Length > 1 ? i / (float)(text.Length - 1) : 0f;
                Color c = Color.HSVToRGB(Mathf.Repeat(0.85f + k * 0.45f - time * 0.25f, 1f), 0.55f, 1f);
                float shine = Mathf.Clamp01(1f - Mathf.Abs(k - glint) * 7f);
                c = Color.Lerp(c, Color.white, shine * 0.85f);
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>').Append(text[i]).Append("</color>"); // noloc
            }
            sb.Append(ShimmerClose);
            return sb.ToString();
        }

        /// <summary>Marks a legendary title inside a text, so <see cref="UI.TitleShimmer"/> finds it again.</summary>
        internal const string ShimmerOpen = "<b><color=#00000000></color>", ShimmerClose = "</b>"; // noloc
    }
}
