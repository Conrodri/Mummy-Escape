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
        public Ramp SpikeTraps;
        public Ramp DarknessTraps;
        public Ramp DustPatches;
        /// <summary>Kinds of the portal gates, cycled through level after level (never hidden: the preview shows every pad).</summary>
        public TeleporterKind[] TeleporterKinds = { TeleporterKind.Visible };
        /// <summary>Signature mechanics of the act's theme.</summary>
        public Ramp Currents;
        public Ramp CrumblingTiles;
        public Ramp FireJets;
        public bool BlueBarriers;
        public int MinPoiSpacing = 4;
        public int MinHpLeftForPar = 1;
    }

    public static class DifficultyTable
    {
        /// <summary>
        /// Bump this whenever the generator or this table changes the produced levels: it is part of the level seed
        /// and of the leaderboard ids, so scores from different rules never get compared.
        /// </summary>
        public const int GeneratorVersion = 6;

        /// <summary>
        /// One theme and one signature mechanic per act: the intact antechamber (doors, portals), the flooded galleries
        /// (currents), the collapsed ruins (fragile slabs), the high-tech city of Anubis (laser barriers and switches)
        /// and the burning sanctuary (flame jets on a beat, over crumbling ground).
        ///
        /// The game plays on memory and logic, not on getting lost: a human makes ~2.5× the ideal moves (tools/LevelLab
        /// --human), so the ideal route stays short (about 30 moves at most, a level lasts ~2 minutes) and acts get
        /// harder through the mechanics to chain, never through length. Every element serves the route, and no tomb can
        /// wall the player in for good. Never more than 2 floors: the whole tomb is memorised during the preview.
        /// </summary>
        public static readonly IReadOnlyList<ActDefinition> Acts = new[]
        {
            new ActDefinition
            {
                Name = "L'Antichambre", MinMoves = 16, MaxMoves = new Ramp(22, 28),
                Floors = 1, Cells = new Ramp(5, 6), ExtraLoops = new Ramp(2, 3),
                Gates = new Ramp(1, 2), GateKinds = new[] { GateKind.Door, GateKind.Portal },
                DarknessTraps = new Ramp(0, 1),
                TeleporterKinds = new[] { TeleporterKind.Visible },
                MinPoiSpacing = 4,
            },
            new ActDefinition
            {
                Name = "Les Galeries inondées", MinMoves = 20, MaxMoves = new Ramp(28, 32),
                Floors = 1, Cells = 6, ExtraLoops = new Ramp(3, 4),
                Gates = 2, GateKinds = new[] { GateKind.Door, GateKind.Portal },
                Currents = new Ramp(1, 2),
                SpikeTraps = new Ramp(0, 1), DarknessTraps = new Ramp(0, 1), DustPatches = new Ramp(0, 1),
                TeleporterKinds = new[] { TeleporterKind.Visible },
                MinPoiSpacing = 3,
            },
            new ActDefinition
            {
                Name = "Les Ruines effondrées", MinMoves = 22, MaxMoves = new Ramp(30, 34),
                Floors = 1, Cells = 6, ExtraLoops = new Ramp(3, 4),
                Gates = 2, GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Door },
                CrumblingTiles = new Ramp(1, 2),
                SpikeTraps = 1, DarknessTraps = 1, DustPatches = 1,
                TeleporterKinds = new[] { TeleporterKind.Locked, TeleporterKind.Visible },
                MinPoiSpacing = 3,
            },
            new ActDefinition
            {
                Name = "La Cité d'Anubis", MinMoves = 23, MaxMoves = new Ramp(30, 31),
                Floors = new Ramp(1, 2), Cells = new Ramp(6, 4), ExtraLoops = new Ramp(3, 4),
                Gates = 2, GateKinds = new[] { GateKind.Laser, GateKind.Portal, GateKind.Laser, GateKind.Door },
                BlueBarriers = true,
                SpikeTraps = 1, DarknessTraps = 1, DustPatches = 1,
                TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Locked, TeleporterKind.Visible },
                MinPoiSpacing = 3,
            },
            new ActDefinition
            {
                Name = "Le Sanctuaire embrasé", MinMoves = 24, MaxMoves = new Ramp(28, 30),
                Floors = 2, Cells = 4, ExtraLoops = new Ramp(3, 4),
                Gates = 2, GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Laser, GateKind.Door },
                FireJets = 2, CrumblingTiles = 1,
                DarknessTraps = new Ramp(0, 1), DustPatches = 1,
                TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Visible },
                MinPoiSpacing = 3,
            },
        };

        public const int ExtraMovesPerFloor = 4;
        public const int LockedPortalExtraMoves = 3;

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
                SpikeTraps = act.SpikeTraps.At(i, n),
                DarknessTraps = act.DarknessTraps.At(i, n),
                DustPatches = act.DustPatches.At(i, n),
                Currents = act.Currents.At(i, n),
                CrumblingTiles = act.CrumblingTiles.At(i, n),
                FireJets = act.FireJets.At(i, n),
                BlueBarriers = act.BlueBarriers,
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
                spec.Gates.Add(kind == GateKind.Door ? Gate.Door : kind == GateKind.Laser ? Gate.Laser
                               : Gate.Teleporter(act.TeleporterKinds[(i + portal++) % kinds]));
                // A locked portal is two mechanics (its lever, then the portal): room for the lever's detour.
                if (spec.Gates[k].Kind == GateKind.Portal && spec.Gates[k].Portal == TeleporterKind.Locked) spec.MaxMoves += LockedPortalExtraMoves;
            }
            return spec;
        }

        /// <summary>Base seed of a level; every run mixes in its own variant number to draw a new maze.</summary>
        public static ulong Seed(LevelId id) =>
            Pcg32.Hash(Pcg32.Hash(0x4D554D4D59UL /* "MUMMY" */, (ulong)GeneratorVersion), (ulong)(id.Act * 1000 + id.Index));

        /// <summary>Seed of one maze of a level: (level, variant) always rebuilds the same tomb on every device.</summary>
        public static ulong Seed(LevelId id, int variant) => Pcg32.Hash(Seed(id), (ulong)(uint)variant);
    }
}
