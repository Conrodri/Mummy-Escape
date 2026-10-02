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
        /// <summary>Short loops added on top of braiding: alternative routes the player has to remember.</summary>
        public Ramp ExtraLoops;
        /// <summary>Mandatory obstacles on the route (at least 1: every level asks for a button or a portal).</summary>
        public Ramp Gates = 1;
        /// <summary>Gate kinds, cycled through by level and gate index so consecutive levels alternate.</summary>
        public GateKind[] GateKinds = { GateKind.Door, GateKind.Portal };
        public Ramp DecoyDoors;
        public Ramp SpikeTraps;
        public Ramp DarknessTraps;
        public Ramp DustPatches;
        /// <summary>Optional teleporter pairs on top of the portal gates.</summary>
        public Ramp TeleporterCount;
        /// <summary>Teleporter kinds unlocked in this act, cycled through as the count grows.</summary>
        public TeleporterKind[] TeleporterKinds = { TeleporterKind.Visible };
        public Ramp BreakableFloors;
        public int MinPoiSpacing = 4;
        public int MinHpLeftForPar = 1;
    }

    public static class DifficultyTable
    {
        /// <summary>
        /// Bump this whenever the generator or this table changes the produced levels: it is part of the level seed
        /// and of the leaderboard ids, so scores from different rules never get compared.
        /// </summary>
        public const int GeneratorVersion = 2;

        public static readonly IReadOnlyList<ActDefinition> Acts = new[]
        {
            new ActDefinition
            {
                Name = "L'Antichambre", MinMoves = 15, MaxMoves = new Ramp(24, 30),
                Floors = 1, Cells = new Ramp(6, 7), ExtraLoops = new Ramp(0, 2),
                Gates = new Ramp(1, 2), GateKinds = new[] { GateKind.Door, GateKind.Portal },
                DecoyDoors = new Ramp(0, 1), DarknessTraps = new Ramp(0, 1),
                TeleporterKinds = new[] { TeleporterKind.Visible },
                MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Les Galeries", MinMoves = 22, MaxMoves = new Ramp(32, 38),
                Floors = new Ramp(1, 2), Cells = new Ramp(7, 8), ExtraLoops = new Ramp(1, 3),
                Gates = new Ramp(2, 3), GateKinds = new[] { GateKind.Door, GateKind.Portal, GateKind.Door },
                DecoyDoors = new Ramp(1, 2), SpikeTraps = new Ramp(0, 1), DarknessTraps = new Ramp(1, 2), DustPatches = new Ramp(0, 1),
                TeleporterCount = new Ramp(0, 1), TeleporterKinds = new[] { TeleporterKind.Visible, TeleporterKind.Hidden },
                BreakableFloors = new Ramp(0, 1), MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "La Chambre des Pièges", MinMoves = 28, MaxMoves = new Ramp(44, 50),
                Floors = 2, Cells = new Ramp(8, 9), ExtraLoops = new Ramp(2, 4),
                Gates = 3, GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Door },
                DecoyDoors = new Ramp(1, 2), SpikeTraps = new Ramp(1, 3), DarknessTraps = new Ramp(1, 2), DustPatches = 1,
                TeleporterCount = 1, TeleporterKinds = new[] { TeleporterKind.Hidden, TeleporterKind.Locked, TeleporterKind.Visible },
                BreakableFloors = 1, MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Le Labyrinthe d'Anubis", MinMoves = 34, MaxMoves = new Ramp(52, 60),
                Floors = new Ramp(2, 3), Cells = new Ramp(8, 10), ExtraLoops = new Ramp(3, 5),
                Gates = new Ramp(3, 4), GateKinds = new[] { GateKind.Door, GateKind.Portal, GateKind.Door, GateKind.Portal },
                DecoyDoors = 2, SpikeTraps = new Ramp(2, 3), DarknessTraps = new Ramp(2, 3), DustPatches = new Ramp(1, 2),
                TeleporterCount = 1, TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Locked, TeleporterKind.Hidden },
                BreakableFloors = new Ramp(1, 2), MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Le Sanctuaire d'Osiris", MinMoves = 40, MaxMoves = new Ramp(62, 70),
                Floors = 3, Cells = new Ramp(9, 11), ExtraLoops = new Ramp(4, 6),
                Gates = 4, GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Door },
                DecoyDoors = new Ramp(2, 3), SpikeTraps = new Ramp(3, 4), DarknessTraps = new Ramp(2, 3), DustPatches = 2,
                TeleporterCount = 1, TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Hidden, TeleporterKind.Locked },
                BreakableFloors = 2, MinPoiSpacing = 4,
            },
        };

        public const int ExtraMovesPerFloor = 6;

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
            int floors = act.Floors.At(i, n);
            var spec = new LevelSpec
            {
                Id = id,
                MinMoves = act.MinMoves,
                // Each extra floor adds a climb: give the par window room for it (the minimum stays the act contract).
                MaxMoves = act.MaxMoves.At(i, n) + (floors - 1) * ExtraMovesPerFloor,
                Floors = floors,
                CellsX = cells,
                CellsY = cells,
                ExtraLoops = act.ExtraLoops.At(i, n),
                DecoyDoors = act.DecoyDoors.At(i, n),
                SpikeTraps = act.SpikeTraps.At(i, n),
                DarknessTraps = act.DarknessTraps.At(i, n),
                DustPatches = act.DustPatches.At(i, n),
                BreakableFloors = floors > 1 ? act.BreakableFloors.At(i, n) : 0,
                MinPoiSpacing = act.MinPoiSpacing,
                MinHpLeftForPar = act.MinHpLeftForPar,
                // "Far from the entrance": two thirds of the tomb's side, as the crow flies.
                MinExitDistance = (cells * 2 + 1) * 2 / 3,
                MinMechanics = 1,
            };

            int kinds = act.TeleporterKinds.Length, portal = 0;
            int gates = Math.Max(1, act.Gates.At(i, n));
            for (int k = 0; k < gates; k++)
            {
                var kind = act.GateKinds[(i - 1 + k) % act.GateKinds.Length];
                spec.Gates.Add(kind == GateKind.Door ? Gate.Door : Gate.Teleporter(act.TeleporterKinds[(i + portal++) % kinds]));
            }
            int tp = act.TeleporterCount.At(i, n);
            for (int k = 0; k < tp; k++)
                spec.Teleporters.Add(act.TeleporterKinds[(i + portal + k) % kinds]);
            return spec;
        }

        /// <summary>Base seed of a level; every run mixes in its own variant number to draw a new maze.</summary>
        public static ulong Seed(LevelId id) =>
            Pcg32.Hash(Pcg32.Hash(0x4D554D4D59UL /* "MUMMY" */, (ulong)GeneratorVersion), (ulong)(id.Act * 1000 + id.Index));

        /// <summary>Seed of one maze of a level: (level, variant) always rebuilds the same tomb on every device.</summary>
        public static ulong Seed(LevelId id, int variant) => Pcg32.Hash(Seed(id), (ulong)(uint)variant);
    }
}
