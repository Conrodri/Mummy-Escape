using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    /// <summary>The game's four ladders, all written and served by the server only.</summary>
    public enum Ladder { Duel = 0, Duo = 1, Tower = 2, Endless = 3 }

    /// <summary>Who a ladder page shows: everyone, the players of the viewer's country, or the viewer's friends.</summary>
    public enum LadderScope { World = 0, Country = 1, Friends = 2 }

    /// <summary>One row of a ladder.</summary>
    [Serializable]
    public sealed class LadderRow
    {
        /// <summary>1-based rank in the page's scope; 0: not ranked in it.</summary>
        public int Rank;
        public string PlayerId;
        public string PlayerName;
        /// <summary>2v2: the other member of the duo (null otherwise).</summary>
        public string MateId, MateName;
        /// <summary>ISO 3166 alpha-2 code ("FR") the player chose, "" when unknown.</summary>
        public string Country = "";
        /// <summary>2v2: the mate's country (a duo shows on the country page of either member).</summary>
        public string MateCountry = "";
        /// <summary>Duel and 2v2: Elo. Tower: floors climbed. Endless: weighted points (<see cref="Ladders.EndlessPoints"/>).</summary>
        public int Value;
        /// <summary>Tower and endless: the time that breaks ties (lower ranks higher).</summary>
        public int TimeMs;
        /// <summary>Duel (this page's month) and 2v2: the record.</summary>
        public int Wins, Losses, Draws;
        public bool IsMe;

        public LadderRow Copy() => (LadderRow)MemberwiseClone();
    }

    /// <summary>A ladder as the server serves it: the top rows of the scope and the viewer's own row.</summary>
    [Serializable]
    public sealed class LadderPage
    {
        public Ladder Ladder;
        public LadderScope Scope;
        /// <summary>The country of a Country page (the viewer's), "" otherwise.</summary>
        public string Country = "";
        /// <summary>Duel: the page's month ("2026-11").</summary>
        public string Season = "";
        public List<LadderRow> Rows = new List<LadderRow>();
        /// <summary>The viewer's row (Rank 0 when outside the scope's ranks), null when the viewer has no score on this ladder.</summary>
        public LadderRow Me;
        public long BuiltAtUnixMs;
        /// <summary>"NO_COUNTRY" (Country scope, no country chosen), "UNKNOWN_LADDER".</summary>
        public string Error;
    }

    public static class Ladders
    {
        /// <summary>Rows a page shows at most.</summary>
        public const int Size = 100;

        /// <summary>The UGS leaderboards behind the ladders (to create in the dashboard, see DEPLOY.md); the 2v2 is the duo index.</summary>
        public const string DuelBoard = "pvp_elo", TowerBoard = "ladder_tower", EndlessBoard = "ladder_endless";

        /// <summary>Top entries of a board read at once and kept a few minutes for every page: the country pages filter them.</summary>
        public const int PoolSize = 1000;
        public const int CacheMinutes = 5;

        /// <summary>Endless: what one tomb escaped is worth on each ladder (<see cref="Core.Difficulty"/> order: Easy, Normal, Extreme).</summary>
        public static readonly int[] EndlessWeights = { 1, 2, 3 };

        /// <summary>Endless points: the tombs escaped on each ladder (by <see cref="Core.Difficulty"/>) times its weight.</summary>
        public static int EndlessPoints(IReadOnlyList<int> tombsByMode)
        {
            int points = 0;
            for (int m = 0; m < EndlessWeights.Length && tombsByMode != null && m < tombsByMode.Count; m++)
                points += Math.Max(0, tombsByMode[m]) * EndlessWeights[m];
            return points;
        }

        const int MaxPoints = 99999;

        /// <summary>Endless board score, lower is better (ascending like the tower's): the most points first, then the least time.</summary>
        public static long EncodeEndless(int points, int timeMs) => Core.LevelResult.EncodeScore(MaxPoints - Math.Min(Math.Max(points, 0), MaxPoints), timeMs);

        public static (int points, int timeMs) DecodeEndless(long score)
        {
            var (gap, timeMs) = Core.LevelResult.DecodeScore(score);
            return (MaxPoints - gap, timeMs);
        }

        /// <summary>A country code the ladders accept: two capital letters A-Z (empty: none).</summary>
        public static bool IsCountryCode(string code) =>
            code != null && code.Length == 2 && code[0] >= 'A' && code[0] <= 'Z' && code[1] >= 'A' && code[1] <= 'Z';
    }
}
