using System;

namespace MummyEscape.Core
{
    /// <summary>
    /// Endless mode, solo: one ladder per difficulty mode, opened by finishing that mode's campaign. Every player climbs the
    /// same stored tombs (<see cref="TombArchive"/>) in the same order; only getting out counts (no stars), the time spent
    /// breaks ties. The tombs are drawn once by LevelLab (<c>--endless</c>) and appended to the stored file.
    /// </summary>
    public static class Endless
    {
        /// <summary>Stored tombs of the three ladders, a text asset under Resources (<see cref="TombArchive"/> lines).</summary>
        public const string ResourcePath = "Tombs/endless";

        /// <summary>Tombs over which the campaign level a tomb is drawn from climbs from its act's easiest to its hardest.</summary>
        public const int RampLength = 300;

        /// <summary>Variants of the stored tombs start here, away from the mazes a campaign run draws.</summary>
        public const int VariantBase = 1_000_000;

        public static bool IsUnlocked(Difficulty mode, Func<LevelId, bool> isCompleted) => isCompleted(Progression.Last(mode));

        /// <summary>
        /// The campaign level and variant that draw tomb <paramref name="depth"/> (0-based) of a mode's ladder: any act (its
        /// look and mechanics), and a level of that act a little harder every <see cref="RampLength"/> / 10 tombs, one step
        /// either way at random. Used by the tool only: once stored, a tomb never depends on this again.
        /// </summary>
        public static LevelId Source(Difficulty mode, int depth, out int variant)
        {
            var rng = new Pcg32(Pcg32.Hash(0x454E444C455353UL /* "ENDLESS" */, (ulong)(((int)mode + 1) * 10_000_000L + depth)));
            int act = rng.Range(1, DifficultyTable.ActCount + 1);
            int n = DifficultyTable.LevelsPerAct;
            int rank = Math.Max(0, Math.Min(n - 1, (int)((long)depth * n / RampLength) + rng.Range(-1, 2)));
            variant = VariantBase + depth;
            return new LevelId(mode, act, ByDifficulty()[rank]);
        }

        /// <summary>Level numbers of an act from the easiest to the hardest (the act's sawtooth, sorted).</summary>
        internal static int[] ByDifficulty()
        {
            var saw = DifficultyTable.Sawtooth;
            var order = new int[saw.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i + 1;
            Array.Sort(order, (a, b) => saw[a - 1] != saw[b - 1] ? saw[a - 1].CompareTo(saw[b - 1]) : a.CompareTo(b));
            return order;
        }

        /// <summary>Leaderboard of a ladder; not tied to the generator version since the tombs never change.</summary>
        public static string BoardId(Difficulty mode) => "endless_" + char.ToLowerInvariant(mode.Letter());

        const int MaxReached = 99999;

        /// <summary>Board score, lower is better (ascending boards like the levels'): the furthest tomb reached first, then the time.</summary>
        public static long EncodeScore(int reached, int timeMs) => LevelResult.EncodeScore(MaxReached - Math.Min(Math.Max(reached, 0), MaxReached), timeMs);

        public static (int reached, int timeMs) DecodeScore(long score)
        {
            var (gap, timeMs) = LevelResult.DecodeScore(score);
            return (ReachedOf(gap), timeMs);
        }

        /// <summary>The tomb reached, from the "moves over par" part of a decoded board score (<see cref="LevelResult.DecodeScore"/>).</summary>
        public static int ReachedOf(int overPar) => MaxReached - overPar;
    }

    /// <summary>A player's place on one endless ladder (saved on the device, merged with the cloud copy).</summary>
    [Serializable]
    public sealed class EndlessProgress
    {
        /// <summary>Tombs escaped: the next one to play is tomb <see cref="Cleared"/> + 1.</summary>
        public int Cleared;
        /// <summary>Play time of the escaped tombs, added up (the tie-break of the board).</summary>
        public int TimeMs;
        public int Deaths;

        /// <summary>The tomb the player stands before (1-based): "Tombeau n atteint".</summary>
        public int Reached => Cleared + 1;

        public long Score => Endless.EncodeScore(Reached, TimeMs);

        /// <summary>An escape: on to the next tomb, its time added (capped so the sum never overflows).</summary>
        public void Escaped(int timeMs)
        {
            Cleared++;
            TimeMs = (int)Math.Min(int.MaxValue, (long)TimeMs + Math.Max(0, timeMs));
        }

        /// <summary>The further of two copies (another device): more tombs, or as many in less time.</summary>
        public void MergeWith(EndlessProgress o)
        {
            if (o == null) return;
            if (o.Cleared > Cleared || (o.Cleared == Cleared && o.TimeMs < TimeMs)) { Cleared = o.Cleared; TimeMs = o.TimeMs; }
            Deaths = Math.Max(Deaths, o.Deaths);
        }
    }
}
