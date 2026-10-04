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

    /// <summary>Traps laid on the walk; a level gets one per <see cref="DifficultyTable.Tier"/>, cycled through the act's list.</summary>
    public enum Peril : byte
    {
        /// <summary>Spikes: cost a life unless disarmed.</summary>
        Spikes,
        /// <summary>A cloud of darkness: blinds the mummy for a few steps.</summary>
        Darkness,
        /// <summary>Dust that smothers the torch, with a wall torch further on to light it again.</summary>
        Dust,
    }

    /// <summary>Design data for one act. Add an entry to <see cref="DifficultyTable.Acts"/> to add an act.</summary>
    public sealed class ActDefinition
    {
        public string Name;
        public int Levels = 10;
        /// <summary>Optimal move count window of a level with one gate on one floor; each extra gate and floor adds room.</summary>
        public int MinMoves;
        public int MaxMoves;
        public int Floors = 1;
        /// <summary>From this level of the act on, one more floor (0 = never).</summary>
        public int MoreFloorsFrom;
        /// <summary>Maze cells per side on one floor, and once floors are stacked (smaller, to keep a level short).</summary>
        public int Cells;
        public int StackedCells;
        /// <summary>Loops added on top of braiding: two ways to the same spot (a short one, a long one) to tell apart.</summary>
        public Ramp ExtraLoops;
        /// <summary>Gate kinds, cycled through by level and gate index so consecutive levels alternate.</summary>
        public GateKind[] GateKinds = { GateKind.Door, GateKind.Portal };
        /// <summary>Traps of the act, handed out in turn (one per tier).</summary>
        public Peril[] Perils = { Peril.Darkness };
        /// <summary>Kinds of the portal gates, cycled through level after level (never hidden: the preview shows every pad).</summary>
        public TeleporterKind[] TeleporterKinds = { TeleporterKind.Visible };
        /// <summary>Signature mechanics of the act's theme.</summary>
        public Ramp Currents;
        public Ramp CrumblingTiles;
        public Ramp FireJets;
        public bool BlueBarriers;
        public int MinPoiSpacing = 3;
        public int MinHpLeftForPar = 1;
    }

    public static class DifficultyTable
    {
        /// <summary>
        /// Bump this whenever the generator or this table changes the produced levels: it is part of the level seed
        /// and of the leaderboard ids, so scores from different rules never get compared.
        /// </summary>
        public const int GeneratorVersion = 8;

        /// <summary>
        /// One theme and one signature mechanic per act: the intact antechamber (doors, portals), the flooded galleries
        /// (currents), the collapsed ruins (fragile slabs), the high-tech city of Anubis (laser barriers and switches)
        /// and the burning sanctuary (flame jets on a beat, over crumbling ground).
        ///
        /// Every act climbs the same three steps (<see cref="Tier"/>): levels 1-3 ask for one mechanism and hold one
        /// trap, levels 4-7 two of each, levels 8-10 three, the mechanisms chained so the player walks back and forth.
        /// Floors stack up: a second one from level 3-5, three in the whole of act 5. The game plays on memory and
        /// logic, not on getting lost: every element serves the walk and no tomb can wall the player in for good.
        /// </summary>
        public static readonly IReadOnlyList<ActDefinition> Acts = new[]
        {
            new ActDefinition
            {
                Name = "L'Antichambre", MinMoves = 14, MaxMoves = 22,
                Cells = 6, ExtraLoops = new Ramp(2, 4),
                GateKinds = new[] { GateKind.Door, GateKind.Portal },
                Perils = new[] { Peril.Darkness, Peril.Spikes },
            },
            new ActDefinition
            {
                Name = "Les Galeries inondées", MinMoves = 16, MaxMoves = 24,
                Cells = 6, ExtraLoops = new Ramp(3, 4),
                GateKinds = new[] { GateKind.Door, GateKind.Portal },
                Currents = new Ramp(1, 2),
                Perils = new[] { Peril.Spikes, Peril.Dust, Peril.Darkness },
            },
            new ActDefinition
            {
                Name = "Les Ruines effondrées", MinMoves = 18, MaxMoves = 26,
                MoreFloorsFrom = 5, Cells = 6, StackedCells = 5, ExtraLoops = new Ramp(3, 4),
                GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Door },
                CrumblingTiles = new Ramp(1, 2),
                Perils = new[] { Peril.Dust, Peril.Spikes, Peril.Darkness },
                TeleporterKinds = new[] { TeleporterKind.Locked, TeleporterKind.Visible },
            },
            new ActDefinition
            {
                Name = "La Cité d'Anubis", MinMoves = 20, MaxMoves = 26,
                Floors = 2, StackedCells = 5, ExtraLoops = new Ramp(3, 4),
                // Cycled so every level, even with a single gate, has its laser.
                GateKinds = new[] { GateKind.Laser, GateKind.Laser, GateKind.Laser, GateKind.Portal, GateKind.Laser, GateKind.Door },
                BlueBarriers = true,
                Perils = new[] { Peril.Darkness, Peril.Spikes, Peril.Dust },
                TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Locked, TeleporterKind.Visible },
            },
            new ActDefinition
            {
                Name = "Le Sanctuaire embrasé", MinMoves = 22, MaxMoves = 26,
                Floors = 3, StackedCells = 4, ExtraLoops = new Ramp(3, 4),
                GateKinds = new[] { GateKind.Portal, GateKind.Door, GateKind.Laser, GateKind.Door },
                FireJets = 2, CrumblingTiles = 1,
                Perils = new[] { Peril.Dust, Peril.Darkness, Peril.Spikes },
                TeleporterKinds = new[] { TeleporterKind.Cursed, TeleporterKind.Visible },
            },
        };

        public const int ExtraMovesPerFloor = 4;
        public const int ExtraMovesPerGate = 6;
        public const int LockedPortalExtraMoves = 3;

        /// <summary>Step of a level inside its act: 1 for levels 1-3, 2 for 4-7, 3 for 8-10 (mechanisms and traps).</summary>
        public static int Tier(int index) => index <= 3 ? 1 : index <= 7 ? 2 : 3;

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

            int tier = Tier(i);
            int floors = act.Floors + (act.MoreFloorsFrom > 0 && i >= act.MoreFloorsFrom ? 1 : 0);
            int cells = floors > 1 ? act.StackedCells : act.Cells;
            var spec = new LevelSpec
            {
                Id = id,
                // Each extra gate is a detour to walk back from, each extra floor a climb: room for both.
                MinMoves = act.MinMoves + 2 * (tier - 1),
                MaxMoves = act.MaxMoves + (tier - 1) * ExtraMovesPerGate + (floors - 1) * ExtraMovesPerFloor,
                Floors = floors,
                CellsX = cells,
                CellsY = cells,
                ExtraLoops = act.ExtraLoops.At(i, n),
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

            // One trap per tier, the act's kinds in turn (shifted level after level so neighbours differ).
            for (int k = 0; k < tier; k++)
            {
                switch (act.Perils[(i - 1 + k) % act.Perils.Length])
                {
                    case Peril.Spikes: spec.SpikeTraps++; break;
                    case Peril.Darkness: spec.DarknessTraps++; break;
                    default: spec.DustPatches++; break;
                }
            }

            int kinds = act.TeleporterKinds.Length, portal = 0;
            for (int k = 0; k < tier; k++)
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
