using System;

namespace MummyEscape.Core
{
    /// <summary>A handicap the player picks every <see cref="Tower.CurseEvery"/> floors; each can be taken again up to its cap.</summary>
    public enum TowerCurse
    {
        /// <summary>One more trap on every floor (spikes, then a shadow, in turn).</summary>
        Traps,
        /// <summary>Tombs one maze cell wider and taller.</summary>
        Bigger,
        /// <summary>One more storey in every tomb.</summary>
        Storeys,
    }

    /// <summary>
    /// The challenge tower, solo: Facile tombs one after another, the acts in turn, a little harder over the first floors.
    /// Every <see cref="CurseEvery"/> floors escaped, the player must pick a handicap, kept for the rest of the run. The run
    /// ends on a death or an escape under 2 stars; it climbs as far as it can. Each run draws new tombs (its own
    /// <see cref="TowerRun.Seed"/>), so the tower cannot be learnt by heart. Opens with the Facile ladder.
    /// </summary>
    public static class Tower
    {
        /// <summary>Floors over which the source level climbs from the act's easiest to its hardest.</summary>
        public const int RampFloors = 30;

        /// <summary>A handicap to pick after every this many floors escaped.</summary>
        public const int CurseEvery = 3;

        public static readonly TowerCurse[] Curses = { TowerCurse.Traps, TowerCurse.Bigger, TowerCurse.Storeys };

        /// <summary>How many times a handicap can be taken (the tombs stay generable: <c>TowerTests</c>).</summary>
        public static int Cap(TowerCurse curse) => curse == TowerCurse.Traps ? 3 : 2;

        /// <summary>Floors whose escape gives an exclusive skin, and those skins (<c>TowerSkins</c> draws them).</summary>
        public static readonly int[] RewardFloors = { 10, 25, 50 };
        public static readonly string[] RewardSkins = { "tower_torch_beacon", "tower_col_obsidian", "tower_hat_crown" };

        /// <summary>Least stars an escape needs to go on climbing.</summary>
        public const int MinStars = 2;

        public static bool IsUnlocked(Func<LevelId, bool> isCompleted) => Endless.IsUnlocked(Difficulty.Easy, isCompleted);

        /// <summary>
        /// The Facile level floor <paramref name="floor"/> (1-based) is drawn from: the acts in turn, a level a little
        /// harder every few floors (the act's sawtooth, sorted), the hardest from <see cref="RampFloors"/> on.
        /// </summary>
        public static LevelId Source(int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            int act = (floor - 1) % DifficultyTable.ActCount + 1;
            int n = DifficultyTable.LevelsPerAct;
            int rank = Math.Min(n - 1, (floor - 1) * n / RampFloors);
            return new LevelId(Difficulty.Easy, act, Endless.ByDifficulty()[rank]);
        }

        /// <summary>The floor's spec: its Facile level's, with the run's handicaps on top (no difficulty band then).</summary>
        public static LevelSpec Spec(int floor, TowerCurses curses)
        {
            var id = Source(floor);
            var spec = DifficultyTable.Spec(id);
            if (curses.Count == 0) return spec;
            var act = DifficultyTable.GetAct(id.Act);
            spec = spec.WithMoreMoves(0);
            spec.MaxScore = int.MaxValue;
            if (curses.Storeys > 0)
            {
                if (spec.Floors == 1) spec.CellsX = spec.CellsY = act.StackedCells;
                spec.Floors = Math.Min(3, spec.Floors + curses.Storeys);
                spec.MaxMoves += curses.Storeys * DifficultyTable.ExtraMovesPerFloor;
            }
            spec.CellsX += curses.Bigger;
            spec.CellsY += curses.Bigger;
            spec.MaxMoves += curses.Bigger * 6;
            // One trap per handicap, spikes and shadows in turn (more spikes than that leave no tomb in the later acts).
            int spikes = (curses.Traps + 1) / 2;
            spec.SpikeTraps += spikes;
            spec.DarknessTraps += curses.Traps - spikes;
            spec.MaxMoves += spikes;
            spec.MinExitDistance = (spec.CellsX * 2 + 1) * 2 / 3;
            spec.MaxMoves = Math.Min(spec.MaxMoves, DifficultyTable.MaxMovesCap + 12);
            return spec;
        }

        public static ulong Seed(int floor, TowerCurses curses, int runSeed) =>
            Pcg32.Hash(DifficultyTable.Seed(Source(floor)), Pcg32.Hash((ulong)(uint)runSeed, (ulong)(floor * 64 + curses.Traps * 16 + curses.Bigger * 4 + curses.Storeys)));

        public static Level Generate(int floor, TowerCurses curses, int runSeed) =>
            LevelGenerator.Generate(Spec(floor, curses), Seed(floor, curses, runSeed));

        /// <summary>Does the run go on after this tomb: out of it with at least <see cref="MinStars"/> stars.</summary>
        public static bool Continues(LevelResult result) => result.Won && result.Stars >= MinStars;

        /// <summary>Leaderboard of the tower: tied to the generator, which draws its tombs.</summary>
        public static string BoardId => "tower_v" + DifficultyTable.GeneratorVersion;

        const int MaxFloors = 99999;

        /// <summary>The floors escaped, from the "moves over par" part of a decoded board score (<see cref="LevelResult.DecodeScore"/>).</summary>
        public static int FloorsOf(int overPar) => MaxFloors - overPar;

        /// <summary>Board score, lower is better (ascending like the others): the most floors first, then the time.</summary>
        public static long EncodeScore(int floors, int timeMs) => LevelResult.EncodeScore(MaxFloors - Math.Min(Math.Max(floors, 0), MaxFloors), timeMs);

        public static (int floors, int timeMs) DecodeScore(long score)
        {
            var (gap, timeMs) = LevelResult.DecodeScore(score);
            return (FloorsOf(gap), timeMs);
        }
    }

    /// <summary>The handicaps a run has taken so far (each counted).</summary>
    [Serializable]
    public struct TowerCurses
    {
        public int Traps, Bigger, Storeys;

        public int Count => Traps + Bigger + Storeys;

        public int Of(TowerCurse curse) => curse == TowerCurse.Traps ? Traps : curse == TowerCurse.Bigger ? Bigger : Storeys;

        public bool CanTake(TowerCurse curse) => Of(curse) < Tower.Cap(curse);

        public TowerCurses With(TowerCurse curse)
        {
            var c = this;
            if (curse == TowerCurse.Traps) c.Traps++;
            else if (curse == TowerCurse.Bigger) c.Bigger++;
            else c.Storeys++;
            return c;
        }
    }

    /// <summary>A tower run in progress (saved, so quitting the game does not lose it).</summary>
    [Serializable]
    public sealed class TowerRun
    {
        /// <summary>Draws this run's tombs (a new one each run).</summary>
        public int Seed;
        public TowerCurses Curses;
        /// <summary>Floors escaped: the next one to play is <see cref="Cleared"/> + 1.</summary>
        public int Cleared;
        public int TimeMs;

        public int Floor => Cleared + 1;
        public long Score => Tower.EncodeScore(Cleared, TimeMs);

        /// <summary>A handicap must be picked before the next floor: one per <see cref="Tower.CurseEvery"/> floors, while one is left.</summary>
        public bool CurseDue
        {
            get
            {
                if (Curses.Count >= Cleared / Tower.CurseEvery) return false;
                foreach (var c in Tower.Curses) if (Curses.CanTake(c)) return true;
                return false;
            }
        }

        public void Take(TowerCurse curse)
        {
            if (CurseDue && Curses.CanTake(curse)) Curses = Curses.With(curse);
        }

        public void Escaped(int timeMs)
        {
            Cleared++;
            TimeMs = (int)Math.Min(int.MaxValue, (long)TimeMs + Math.Max(0, timeMs));
        }
    }

    /// <summary>The player's best run (floors, then time) and how many runs were started.</summary>
    [Serializable]
    public sealed class TowerRecord
    {
        public int BestFloors, BestTimeMs, Runs;

        public long Score => Tower.EncodeScore(BestFloors, BestTimeMs);

        /// <summary>Keeps <paramref name="run"/> when it beats the record; true when it did.</summary>
        public bool Offer(TowerRun run)
        {
            if (run.Cleared == 0 || run.Cleared < BestFloors || (run.Cleared == BestFloors && run.TimeMs >= BestTimeMs && BestTimeMs > 0)) return false;
            BestFloors = run.Cleared;
            BestTimeMs = run.TimeMs;
            return true;
        }

        public void MergeWith(TowerRecord o)
        {
            if (o == null) return;
            Runs = Math.Max(Runs, o.Runs);
            if (o.BestFloors > BestFloors || (o.BestFloors == BestFloors && o.BestTimeMs > 0 && (BestTimeMs == 0 || o.BestTimeMs < BestTimeMs)))
            {
                BestFloors = o.BestFloors;
                BestTimeMs = o.BestTimeMs;
            }
        }
    }
}
