using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>Integer value that ramps from First (level 1 of the act) to Last (last level of the act).</summary>
    public readonly struct Ramp
    {
        public readonly int First;
        public readonly int Last;
        public Ramp(int first, int last) { First = first; Last = last; }
        public static implicit operator Ramp(int constant) => new Ramp(constant, constant);

        /// <summary>Rounded linear interpolation, levelIndex in [1, levelCount].</summary>
        public int At(int levelIndex, int levelCount)
        {
            if (levelCount <= 1) return Last;
            int num = (Last - First) * (levelIndex - 1);
            int den = levelCount - 1;
            int rounded = (num * 2 + Math.Sign(num) * den) / (den * 2);
            return First + rounded;
        }
    }

    /// <summary>Design data for one act. Add an entry to <see cref="DifficultyTable.Acts"/> to add an act.</summary>
    public sealed class ActDefinition
    {
        public string Name;
        public int Levels = 10;
        /// <summary>Hard floor of the optimal move count for every level of the act.</summary>
        public int MinMoves;
        /// <summary>Upper bound of the optimal move count (grows slightly as interactions get added).</summary>
        public Ramp MaxMoves;
        public Ramp Floors = 1;
        public Ramp Cells;
        public Ramp Rooms;
        public Ramp LoopChance = 50;
        public Ramp RequiredButtons;
        public Ramp DecoyDoors;
        public Ramp SpikeTraps;
        public Ramp DarknessTraps;
        public Ramp TeleporterCount;
        /// <summary>Teleporter kinds unlocked in this act, cycled through as the count grows.</summary>
        public TeleporterKind[] TeleporterKinds = Array.Empty<TeleporterKind>();
        public Ramp BreakableFloors;
        public int MinPoiSpacing = 4;
        public int MinHpLeftForPar = 1;
    }

    public static class DifficultyTable
    {
        /// <summary>
        /// Bump this whenever the generator or this table changes the produced levels: it is part of the level seed
        /// and of the leaderboard ids, so scores from different layouts never get compared.
        /// </summary>
        public const int GeneratorVersion = 1;

        public static readonly IReadOnlyList<ActDefinition> Acts = new[]
        {
            new ActDefinition
            {
                Name = "L'Antichambre", MinMoves = 10, MaxMoves = new Ramp(14, 18),
                Floors = 1, Cells = new Ramp(5, 7), Rooms = new Ramp(0, 1), LoopChance = new Ramp(30, 80),
                RequiredButtons = new Ramp(0, 1), DecoyDoors = new Ramp(0, 1),
                SpikeTraps = 0, DarknessTraps = new Ramp(0, 1),
                TeleporterCount = new Ramp(0, 1), TeleporterKinds = new[] { TeleporterKind.Visible },
                MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Les Galeries", MinMoves = 15, MaxMoves = new Ramp(21, 26),
                Floors = new Ramp(1, 2), Cells = new Ramp(6, 8), Rooms = new Ramp(1, 2), LoopChance = new Ramp(60, 120),
                RequiredButtons = new Ramp(1, 2), DecoyDoors = new Ramp(1, 2),
                SpikeTraps = new Ramp(0, 1), DarknessTraps = new Ramp(1, 2),
                TeleporterCount = 1, TeleporterKinds = new[] { TeleporterKind.Visible, TeleporterKind.Hidden },
                BreakableFloors = new Ramp(0, 1), MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "La Chambre des Pièges", MinMoves = 20, MaxMoves = new Ramp(28, 33),
                Floors = 2, Cells = new Ramp(7, 9), Rooms = new Ramp(2, 3), LoopChance = new Ramp(80, 140),
                RequiredButtons = new Ramp(2, 3), DecoyDoors = new Ramp(1, 3),
                SpikeTraps = new Ramp(1, 3), DarknessTraps = new Ramp(1, 2),
                TeleporterCount = new Ramp(1, 2), TeleporterKinds = new[] { TeleporterKind.Hidden, TeleporterKind.Locked, TeleporterKind.Visible },
                BreakableFloors = 1, MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Le Labyrinthe d'Anubis", MinMoves = 25, MaxMoves = new Ramp(35, 41),
                Floors = new Ramp(2, 3), Cells = new Ramp(8, 10), Rooms = new Ramp(2, 4), LoopChance = new Ramp(100, 160),
                RequiredButtons = new Ramp(2, 3), DecoyDoors = new Ramp(2, 3),
                SpikeTraps = new Ramp(2, 3), DarknessTraps = new Ramp(2, 3),
                TeleporterCount = 2, TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Locked, TeleporterKind.Hidden },
                BreakableFloors = new Ramp(1, 2), MinPoiSpacing = 5,
            },
            new ActDefinition
            {
                Name = "Le Sanctuaire d'Osiris", MinMoves = 30, MaxMoves = new Ramp(44, 52),
                Floors = 3, Cells = new Ramp(9, 11), Rooms = new Ramp(2, 3), LoopChance = new Ramp(100, 140),
                RequiredButtons = new Ramp(3, 4), DecoyDoors = 3,
                SpikeTraps = new Ramp(3, 4), DarknessTraps = new Ramp(2, 3),
                TeleporterCount = new Ramp(2, 3), TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Hidden, TeleporterKind.Locked },
                BreakableFloors = 2, MinPoiSpacing = 5, MinHpLeftForPar = 1,
            },
        };

        public const int ExtraMovesPerFloor = 5;

        public static int ActCount => Acts.Count;

        public static ActDefinition GetAct(int act)
        {
            if (act < 1 || act > Acts.Count) throw new ArgumentOutOfRangeException(nameof(act));
            return Acts[act - 1];
        }

        public static IEnumerable<LevelId> AllLevels()
        {
            for (int a = 1; a <= Acts.Count; a++)
                for (int i = 1; i <= Acts[a - 1].Levels; i++)
                    yield return new LevelId(a, i);
        }

        public static LevelSpec Spec(LevelId id)
        {
            var act = GetAct(id.Act);
            int n = act.Levels, i = id.Index;
            if (i < 1 || i > n) throw new ArgumentOutOfRangeException(nameof(id));

            int cells = act.Cells.At(i, n);
            var spec = new LevelSpec
            {
                Id = id,
                MinMoves = act.MinMoves,
                // Each extra floor adds a climb: give the par window room for it (the minimum stays the act contract).
                MaxMoves = act.MaxMoves.At(i, n) + (act.Floors.At(i, n) - 1) * ExtraMovesPerFloor,
                Floors = act.Floors.At(i, n),
                CellsX = cells,
                CellsY = cells,
                Rooms = act.Rooms.At(i, n),
                LoopChance = act.LoopChance.At(i, n),
                RequiredButtons = act.RequiredButtons.At(i, n),
                DecoyDoors = act.DecoyDoors.At(i, n),
                SpikeTraps = act.SpikeTraps.At(i, n),
                DarknessTraps = act.DarknessTraps.At(i, n),
                BreakableFloors = act.Floors.At(i, n) > 1 ? act.BreakableFloors.At(i, n) : 0,
                MinPoiSpacing = act.MinPoiSpacing,
                MinHpLeftForPar = act.MinHpLeftForPar,
            };
            int tp = act.TeleporterCount.At(i, n);
            for (int k = 0; k < tp && act.TeleporterKinds.Length > 0; k++)
                spec.Teleporters.Add(act.TeleporterKinds[(i + k) % act.TeleporterKinds.Length]);
            return spec;
        }

        /// <summary>Seed shared by every player for this level (fairness for the leaderboard).</summary>
        public static ulong Seed(LevelId id) =>
            Pcg32.Hash(Pcg32.Hash(0x4D554D4D59UL /* "MUMMY" */, (ulong)GeneratorVersion), (ulong)(id.Act * 1000 + id.Index));
    }
}
