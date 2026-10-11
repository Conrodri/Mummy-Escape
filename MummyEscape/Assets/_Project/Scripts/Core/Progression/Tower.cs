using System;

namespace MummyEscape.Core
{
    /// <summary>Handicaps a player may take for a whole tower run, each worth more points per floor.</summary>
    [Flags]
    public enum TowerCurse
    {
        None = 0,
        /// <summary>One more spike trap and one more shadow on every floor.</summary>
        Traps = 1,
        /// <summary>Tombs one maze cell wider and taller.</summary>
        Bigger = 2,
        /// <summary>One more storey in every tomb (three at most).</summary>
        Storeys = 4,
    }

    /// <summary>
    /// The challenge tower, solo: tombs one after another, each a little harder (Facile, then Normal, then Extrême, the
    /// acts in turn). The run ends on a death or an escape under 2 stars; it climbs as far as it can. Each run draws new
    /// tombs (its own <see cref="TowerRun.Seed"/>), so the tower cannot be learnt by heart. Opens with the Facile ladder.
    /// </summary>
    public static class Tower
    {
        public const int EasyFloors = 10, NormalFloors = 30;

        /// <summary>Floors whose escape gives an exclusive skin, and those skins (<c>TowerSkins</c> draws them).</summary>
        public static readonly int[] RewardFloors = { 10, 25, 50 };
        public static readonly string[] RewardSkins = { "tower_torch_beacon", "tower_col_obsidian", "tower_hat_crown" };

        /// <summary>Least stars an escape needs to go on climbing.</summary>
        public const int MinStars = 2;

        public static bool IsUnlocked(Func<LevelId, bool> isCompleted) => Endless.IsUnlocked(Difficulty.Easy, isCompleted);

        /// <summary>
        /// The campaign level floor <paramref name="floor"/> (1-based) is drawn from: its mode by height, the acts in turn,
        /// and within a mode a level a little harder every few floors (the act's sawtooth, sorted).
        /// </summary>
        public static LevelId Source(int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            var mode = floor <= EasyFloors ? Difficulty.Easy : floor <= NormalFloors ? Difficulty.Normal : Difficulty.Extreme;
            int start = mode == Difficulty.Easy ? 1 : mode == Difficulty.Normal ? EasyFloors + 1 : NormalFloors + 1;
            int span = mode == Difficulty.Easy ? EasyFloors : mode == Difficulty.Normal ? NormalFloors - EasyFloors : 40;
            int act = (floor - 1) % DifficultyTable.ActCount + 1;
            int n = DifficultyTable.LevelsPerAct;
            int rank = Math.Min(n - 1, (floor - start) * n / span);
            return new LevelId(mode, act, Endless.ByDifficulty()[rank]);
        }

        /// <summary>The floor's spec: its campaign level's, with the run's handicaps on top (no difficulty band then).</summary>
        public static LevelSpec Spec(int floor, TowerCurse curses)
        {
            var id = Source(floor);
            var spec = DifficultyTable.Spec(id);
            if (curses == TowerCurse.None) return spec;
            var act = DifficultyTable.GetAct(id.Act);
            spec = spec.WithMoreMoves(0);
            spec.MaxScore = int.MaxValue;
            if ((curses & TowerCurse.Storeys) != 0 && spec.Floors < 3)
            {
                if (spec.Floors == 1) spec.CellsX = spec.CellsY = act.StackedCells;
                spec.Floors++;
                spec.MaxMoves += DifficultyTable.ExtraMovesPerFloor;
            }
            if ((curses & TowerCurse.Bigger) != 0)
            {
                spec.CellsX++;
                spec.CellsY++;
                spec.MaxMoves += 6;
            }
            if ((curses & TowerCurse.Traps) != 0)
            {
                spec.SpikeTraps++;
                spec.DarknessTraps++;
                spec.MaxMoves += 1;
            }
            spec.MinExitDistance = (spec.CellsX * 2 + 1) * 2 / 3;
            spec.MaxMoves = Math.Min(spec.MaxMoves, DifficultyTable.MaxMovesCap + 8);
            return spec;
        }

        public static ulong Seed(int floor, TowerCurse curses, int runSeed) =>
            Pcg32.Hash(DifficultyTable.Seed(Source(floor)), Pcg32.Hash((ulong)(uint)runSeed, (ulong)(floor * 8 + (int)curses)));

        public static Level Generate(int floor, TowerCurse curses, int runSeed) =>
            LevelGenerator.Generate(Spec(floor, curses), Seed(floor, curses, runSeed));

        /// <summary>Does the run go on after this tomb: out of it with at least <see cref="MinStars"/> stars.</summary>
        public static bool Continues(LevelResult result) => result.Won && result.Stars >= MinStars;

        public static int CurseCount(TowerCurse curses)
        {
            int n = 0;
            for (int c = (int)curses; c != 0; c &= c - 1) n++;
            return n;
        }

        /// <summary>Points of a floor escaped: 2, and one more per handicap taken.</summary>
        public static int PointsPerFloor(TowerCurse curses) => 2 + CurseCount(curses);

        /// <summary>Leaderboard of the tower: tied to the generator, which draws its tombs.</summary>
        public static string BoardId => "tower_v" + DifficultyTable.GeneratorVersion;

        const int MaxPoints = 99999;

        /// <summary>The points, from the "moves over par" part of a decoded board score (<see cref="LevelResult.DecodeScore"/>).</summary>
        public static int PointsOf(int overPar) => MaxPoints - overPar;

        /// <summary>Board score, lower is better (ascending like the others): the most points first, then the time.</summary>
        public static long EncodeScore(int points, int timeMs) => LevelResult.EncodeScore(MaxPoints - Math.Min(Math.Max(points, 0), MaxPoints), timeMs);

        public static (int points, int timeMs) DecodeScore(long score)
        {
            var (gap, timeMs) = LevelResult.DecodeScore(score);
            return (PointsOf(gap), timeMs);
        }
    }

    /// <summary>A tower run in progress (saved, so quitting the game does not lose it).</summary>
    [Serializable]
    public sealed class TowerRun
    {
        /// <summary>Draws this run's tombs (a new one each run).</summary>
        public int Seed;
        public TowerCurse Curses;
        /// <summary>Floors escaped: the next one to play is <see cref="Cleared"/> + 1.</summary>
        public int Cleared;
        public int TimeMs;

        public int Floor => Cleared + 1;
        public int Points => Cleared * Tower.PointsPerFloor(Curses);
        public long Score => Tower.EncodeScore(Points, TimeMs);

        public void Escaped(int timeMs)
        {
            Cleared++;
            TimeMs = (int)Math.Min(int.MaxValue, (long)TimeMs + Math.Max(0, timeMs));
        }
    }

    /// <summary>The player's best run (points, then time) and the highest floor ever escaped.</summary>
    [Serializable]
    public sealed class TowerRecord
    {
        public int BestPoints, BestTimeMs, BestFloor, HighestCleared, Runs;

        public long Score => Tower.EncodeScore(BestPoints, BestTimeMs);

        /// <summary>Keeps <paramref name="run"/> when it beats the record; true when it did.</summary>
        public bool Offer(TowerRun run)
        {
            HighestCleared = Math.Max(HighestCleared, run.Cleared);
            if (run.Cleared == 0 || run.Points < BestPoints || (run.Points == BestPoints && run.TimeMs >= BestTimeMs && BestTimeMs > 0)) return false;
            BestPoints = run.Points;
            BestTimeMs = run.TimeMs;
            BestFloor = run.Cleared;
            return true;
        }

        public void MergeWith(TowerRecord o)
        {
            if (o == null) return;
            HighestCleared = Math.Max(HighestCleared, o.HighestCleared);
            Runs = Math.Max(Runs, o.Runs);
            if (o.BestPoints > BestPoints || (o.BestPoints == BestPoints && o.BestTimeMs > 0 && (BestTimeMs == 0 || o.BestTimeMs < BestTimeMs)))
            {
                BestPoints = o.BestPoints;
                BestTimeMs = o.BestTimeMs;
                BestFloor = o.BestFloor;
            }
        }
    }
}
