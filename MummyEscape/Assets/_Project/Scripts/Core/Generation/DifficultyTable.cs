using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>Design data for one act: its look, its signature mechanics and what its tombs may hold.</summary>
    public sealed class ActDefinition
    {
        public string Name;
        public int Levels = DifficultyTable.LevelsPerAct;
        /// <summary>Optimal move count window of a level with one gate on one floor; each extra gate and floor adds room.</summary>
        public int MinMoves;
        public int MaxMoves;
        /// <summary>Floors of the act's tombs in Normal and Extrême (Facile keeps one: <see cref="ModeDefinition.MaxFloors"/>).</summary>
        public int Floors = 1;
        /// <summary>Maze cells per side on one floor, and once floors are stacked (smaller, to keep a level short).</summary>
        public int Cells = 6;
        public int StackedCells = 5;
        /// <summary>Kinds of the first gate (the one every tomb has), cycled level after level: the act's signature.</summary>
        public Element[] MainGates = { Element.Door, Element.Portal };
        /// <summary>Interactions the difficulty budget may add (gates, torches), cycled.</summary>
        public Element[] Interactions = { Element.Door, Element.Portal };
        /// <summary>Traps the difficulty budget may add, cycled.</summary>
        public Element[] Traps = { Element.Darkness };
        /// <summary>Signature mechanics of the act's theme, per mode (Facile, Normal, Extrême).</summary>
        public int[] Currents = { 0, 0, 0 };
        public int[] CrumblingTiles = { 0, 0, 0 };
        public int[] FireJets = { 0, 0, 0 };
        /// <summary>
        /// Difficulty thresholds of the act: Facile from [0] to [1], Normal from [1] to [2], Extrême from [2] to [3]. Each
        /// mode's levels climb through its band along <see cref="DifficultyTable.Sawtooth"/>.
        /// </summary>
        public int[] Thresholds;
        public int MinPoiSpacing = 3;
        public int MinHpLeftForPar = 1;
    }

    /// <summary>What a difficulty mode allows, whatever the act.</summary>
    public sealed class ModeDefinition
    {
        public string Name;
        /// <summary>Most floors of a tomb in this mode (below the act's own count).</summary>
        public int MaxFloors = 3;
        /// <summary>
        /// Most obstacles a tomb holds besides its first gate and the act's mechanics: more gates, torches and traps, each
        /// spike counted (<see cref="Composition.Obstacles"/>).
        /// </summary>
        public int MaxObstacles = int.MaxValue;
        /// <summary>Caps on what the budget may add: gates (the chain to the exit), torches, traps besides spikes.</summary>
        public int MaxGates;
        public int MaxTorches;
        public int MaxTraps;
        /// <summary>Spike traps barring the walk (to disarm), and spike shortcuts (a way round exists).</summary>
        public int MinSpikes;
        public int MaxSpikes;
        public int MaxShortcuts;
        /// <summary>Loops knocked into the maze on top of braiding: more ways to tell apart.</summary>
        public int ExtraLoops;
        /// <summary>Floors of a stacked tomb: least ground, and least points of interest besides the ladders.</summary>
        public int MinFloorTiles = 12;
        public int MinFloorInterests = 2;
        /// <summary>Spike patterns the generator may draw (<see cref="SpikePatterns"/>; null = all).</summary>
        public string[] Patterns;
        public bool DecoyCorridor;
        /// <summary>Laser switches also raise a blue barrier behind the player.</summary>
        public bool BlueBarriers;
    }

    public static class DifficultyTable
    {
        /// <summary>
        /// Bump this whenever the generator or this table changes the produced levels: it is part of the level seed
        /// and of the leaderboard ids, so scores from different rules never get compared.
        /// </summary>
        public const int GeneratorVersion = 13;

        /// <summary>Levels of each act, in every mode.</summary>
        public const int LevelsPerAct = 10;

        /// <summary>
        /// Where each level of an act sits in its mode's band, in per mille: a sawtooth that climbs, eases off a little to
        /// breathe, then climbs again, ending on the act's hardest tomb.
        /// </summary>
        public static readonly int[] Sawtooth = { 0, 200, 400, 250, 500, 700, 550, 750, 900, 1000 };

        /// <summary>
        /// One theme and one big novelty per act: the intact antechamber (doors, portals, shadows), the flooded galleries
        /// (currents, and the wall torch), the collapsed ruins (fragile slabs, with mirrors and locked portals), the
        /// high-tech city of Anubis (laser barriers and switches, with turning slabs and cursed portals) and the burning
        /// sanctuary (flame jets on a beat, over crumbling ground).
        ///
        /// Each act is played in three modes (<see cref="Difficulty"/>). What a level holds comes from its difficulty budget:
        /// every element is worth points (<see cref="DifficultyScore"/>), each mode of an act has a band of points
        /// (<see cref="ActDefinition.Thresholds"/>) and its levels go through it along <see cref="Sawtooth"/>. The game plays on
        /// memory and logic, not on getting lost: every element serves the walk and no tomb can wall the player in for good.
        /// </summary>
        public static readonly IReadOnlyList<ActDefinition> Acts = new[]
        {
            new ActDefinition
            {
                Name = "L'Antichambre", MinMoves = 14, MaxMoves = 22, Cells = 6,
                MainGates = new[] { Element.Door, Element.Portal },
                Interactions = new[] { Element.Door, Element.Portal },
                Traps = new[] { Element.Darkness },
                Thresholds = new[] { 3, 10, 17, 25 },
            },
            new ActDefinition
            {
                Name = "Les Galeries inondées", MinMoves = 16, MaxMoves = 24, Cells = 6,
                MainGates = new[] { Element.Door, Element.Portal },
                Interactions = new[] { Element.Torch, Element.Door, Element.Portal },
                Traps = new[] { Element.Darkness },
                Currents = new[] { 1, 1, 2 },
                Thresholds = new[] { 5, 14, 21, 30 },
            },
            new ActDefinition
            {
                Name = "Les Ruines effondrées", MinMoves = 18, MaxMoves = 26, Cells = 6,
                MainGates = new[] { Element.Portal, Element.Door, Element.LockedPortal },
                Interactions = new[] { Element.LockedPortal, Element.Torch, Element.Door, Element.Portal },
                Traps = new[] { Element.Reverse, Element.Darkness },
                CrumblingTiles = new[] { 1, 1, 2 },
                Thresholds = new[] { 5, 16, 25, 36 },
            },
            new ActDefinition
            {
                Name = "La Cité d'Anubis", MinMoves = 20, MaxMoves = 26, Floors = 2, StackedCells = 5,
                // Every tomb has its laser.
                MainGates = new[] { Element.Laser },
                Interactions = new[] { Element.CursedPortal, Element.Laser, Element.Torch, Element.Door },
                Traps = new[] { Element.Rotate, Element.Darkness, Element.Reverse },
                Thresholds = new[] { 4, 16, 28, 42 },
            },
            new ActDefinition
            {
                Name = "Le Sanctuaire embrasé", MinMoves = 22, MaxMoves = 26, Floors = 2, StackedCells = 5,
                MainGates = new[] { Element.Portal, Element.Door },
                Interactions = new[] { Element.Laser, Element.Torch, Element.CursedPortal, Element.Door },
                Traps = new[] { Element.Reverse, Element.Darkness, Element.Rotate },
                FireJets = new[] { 1, 2, 2 }, CrumblingTiles = new[] { 1, 1, 1 },
                Thresholds = new[] { 8, 22, 34, 46 },
            },
        };

        static readonly string[] EasyPatterns = { "pont", "long pont", "pont à torche", "long pont à torche" };
        static readonly string[] NormalPatterns = { "pont", "long pont", "pont profond", "pont à torche", "long pont à torche" };

        /// <summary>
        /// Facile = discovery: one floor, at most 3 obstacles past the first gate (no spike forced on top), finished quickly.
        /// Normal = the same mechanics with more traps (2 spike traps per tomb, a shortcut), a second floor from act 4.
        /// Extrême = the same floors as Normal, more interactions and their variants (blue barriers, decoy corridor, every
        /// pattern, more gates and traps).
        /// </summary>
        public static readonly IReadOnlyList<ModeDefinition> Modes = new[]
        {
            new ModeDefinition
            {
                Name = "Facile", MaxFloors = 1, MaxObstacles = 3, MaxGates = 2, MaxTorches = 1, MaxTraps = 2, MinSpikes = 0, MaxSpikes = 2, MaxShortcuts = 0, ExtraLoops = 2, MinFloorInterests = 1,
                Patterns = EasyPatterns, DecoyCorridor = false, BlueBarriers = false,
            },
            new ModeDefinition
            {
                Name = "Normal", MaxGates = 2, MaxTorches = 1, MaxTraps = 3, MinSpikes = 2, MaxSpikes = 2, MaxShortcuts = 1, ExtraLoops = 3, MinFloorInterests = 1,
                Patterns = NormalPatterns, DecoyCorridor = false, BlueBarriers = false,
            },
            new ModeDefinition
            {
                Name = "Extrême", MaxGates = 3, MaxTorches = 1, MaxTraps = 3, MinSpikes = 2, MaxSpikes = 2, MaxShortcuts = 1, ExtraLoops = 4,
                Patterns = null, DecoyCorridor = true, BlueBarriers = true,
            },
        };

        public const int ExtraMovesPerFloor = 4;
        public const int ExtraMovesPerGate = 9;
        public const int LockedPortalExtraMoves = 3;
        /// <summary>Longest ideal walk the dust's room may stretch a tomb to (still about 2 minutes of play).</summary>
        public const int MaxMovesCap = 56;

        public static int ActCount => Acts.Count;

        public static ActDefinition GetAct(int act)
        {
            if (act < 1 || act > Acts.Count) throw new ArgumentOutOfRangeException(nameof(act));
            return Acts[act - 1];
        }

        public static ModeDefinition GetMode(Difficulty mode) => Modes[(int)mode];

        /// <summary>Every level of one mode, act by act.</summary>
        public static IEnumerable<LevelId> Levels(Difficulty mode)
        {
            for (int a = 1; a <= Acts.Count; a++)
                for (int i = 1; i <= Acts[a - 1].Levels; i++)
                    yield return new LevelId(mode, a, i);
        }

        /// <summary>Every level of every mode.</summary>
        public static IEnumerable<LevelId> AllLevels()
        {
            foreach (var mode in DifficultyExt.All)
                foreach (var id in Levels(mode))
                    yield return id;
        }

        /// <summary>Band of points of an act in a mode.</summary>
        public static void Band(Difficulty mode, int act, out int min, out int max)
        {
            var t = GetAct(act).Thresholds;
            min = t[(int)mode];
            max = t[(int)mode + 1];
        }

        /// <summary>Points a level aims at: its place on the act's <see cref="Sawtooth"/>, inside its mode's band.</summary>
        public static int Target(LevelId id)
        {
            Band(id.Mode, id.Act, out int min, out int max);
            return min + ((max - min) * Sawtooth[id.Index - 1] + 500) / 1000;
        }

        /// <summary>Floors of a level: the act's, within what its mode allows.</summary>
        public static int Floors(Difficulty mode, int act) => Math.Min(GetAct(act).Floors, GetMode(mode).MaxFloors);

        public static LevelSpec Spec(LevelId id)
        {
            var act = GetAct(id.Act);
            var mode = GetMode(id.Mode);
            int m = (int)id.Mode, i = id.Index;
            if (i < 1 || i > act.Levels) throw new ArgumentOutOfRangeException(nameof(id));

            int floors = Floors(id.Mode, id.Act);
            int cells = floors > 1 ? act.StackedCells : act.Cells;
            Band(id.Mode, id.Act, out int min, out int max);
            var spec = new LevelSpec
            {
                Id = id,
                Floors = floors,
                CellsX = cells,
                CellsY = cells,
                ExtraLoops = mode.ExtraLoops,
                Currents = act.Currents[m],
                CrumblingTiles = act.CrumblingTiles[m],
                FireJets = act.FireJets[m],
                BlueBarriers = mode.BlueBarriers,
                SpikePatternNames = mode.Patterns,
                DecoyCorridor = mode.DecoyCorridor,
                MinFloorTiles = mode.MinFloorTiles,
                MinFloorInterests = mode.MinFloorInterests,
                MinPoiSpacing = act.MinPoiSpacing,
                MinHpLeftForPar = act.MinHpLeftForPar,
                // "Far from the entrance": two thirds of the tomb's side, as the crow flies.
                MinExitDistance = (cells * 2 + 1) * 2 / 3,
                MinMechanics = 1,
                MinScore = min,
                MaxScore = max,
            };
            AddElement(spec, act.MainGates[(i - 1) % act.MainGates.Length]);
            FillBudget(spec, act, mode, Target(id), min, max, i);
            // The decoy corridor is drawn when a straight stretch allows it (seldom): only where its points stay in the band.
            spec.DecoyCorridor = mode.DecoyCorridor && spec.RotateTraps > 0
                                 && DifficultyScore.Of(spec).Score + DifficultyScore.Weight(Element.DecoyCorridor) <= max;
            SetMoves(spec, act);
            return spec;
        }

        /// <summary>
        /// Picks what the level holds on top of its first gate and its act mechanics: how many more gates, torches, traps and
        /// spikes, each kind taken in turn from the act's pools (from an offset that moves level after level, so neighbours
        /// differ). Every mix within the mode's caps (obstacles included) is tried; the one closest to the target wins (below it on a tie), then
        /// the one that balances interactions and traps best, then the one with fewer spikes.
        /// </summary>
        static void FillBudget(LevelSpec spec, ActDefinition act, ModeDefinition mode, int target, int min, int max, int index)
        {
            var gates = new List<Element>();
            var traps = new List<Element>();
            bool torches = false;
            foreach (var e in act.Interactions) { if (e == Element.Torch) torches = true; else gates.Add(e); }
            foreach (var e in act.Traps) if (e != Element.Spikes) traps.Add(e);
            int maxGates = gates.Count > 0 ? mode.MaxGates - spec.Gates.Count : 0;
            int maxTorches = torches ? mode.MaxTorches : 0;
            int maxTraps = traps.Count > 0 ? mode.MaxTraps : 0;
            // Spike shortcuts need room for their way round: single-floor tombs only (on stacked floors they rarely fit).
            int maxShortcuts = spec.Floors == 1 ? mode.MaxShortcuts : 0;

            (int g, int t, int k, int s, int sc)? best = null;
            (int, int, int, int, int) bestKey = default;
            for (int g = 0; g <= maxGates; g++)
                for (int t = 0; t <= maxTorches; t++)
                    for (int k = 0; k <= maxTraps; k++)
                        for (int s = mode.MinSpikes; s <= mode.MaxSpikes; s++)
                            for (int sc = 0; sc <= maxShortcuts; sc++)
                            {
                                // A torch already brings its toll spikes: with a shortcut on top, the tomb rarely fits.
                                if (t > 0 && sc > 0) continue;
                                var trial = spec.WithMoreMoves(0);
                                Lay(trial, gates, traps, index, g, t, k, s, sc);
                                // Nor beside more than two mechanics, a locked portal counting two (thousands of attempts, at times none).
                                if (sc > 0 && trial.RequiredButtons + trial.RequiredPortals > 2) continue;
                                var composition = DifficultyScore.Of(trial);
                                int score = composition.Score;
                                if (score < min || score > max || composition.Obstacles > mode.MaxObstacles) continue;
                                var key = (Math.Abs(score - target) * 2 + (score > target ? 1 : 0), Math.Abs(g + t - k), s + sc, sc, g + t + k);
                                if (best.HasValue && key.CompareTo(bestKey) >= 0) continue;
                                best = (g, t, k, s, sc);
                                bestKey = key;
                            }
            if (!best.HasValue) throw new InvalidOperationException($"{spec.Id}: no mix of elements lands in [{min}, {max}]");
            var (bg, bt, bk, bs, bsc) = best.Value;
            Lay(spec, gates, traps, index, bg, bt, bk, bs, bsc);
        }

        static void Lay(LevelSpec spec, List<Element> gates, List<Element> traps, int index, int g, int t, int k, int s, int sc)
        {
            for (int j = 0; j < g; j++) AddElement(spec, gates[(index + j) % gates.Count]);
            spec.DustPatches += t;
            for (int j = 0; j < k; j++) AddElement(spec, traps[(index - 1 + j) % traps.Count]);
            // The spikes past the dust bar the walk: they are spike traps too.
            spec.SpikeTraps = Math.Max(s, LevelValidator.TollSpikes * spec.DustPatches);
            spec.SpikeShortcuts = sc;
        }

        static void AddElement(LevelSpec spec, Element e)
        {
            switch (e)
            {
                case Element.Spikes: spec.SpikeTraps++; break;
                case Element.Darkness: spec.DarknessTraps++; break;
                case Element.Reverse: spec.ReverseTraps++; break;
                case Element.Rotate: spec.RotateTraps++; break;
                case Element.Torch: spec.DustPatches++; break;
                case Element.Door: spec.Gates.Add(Gate.Door); break;
                case Element.Laser: spec.Gates.Add(Gate.Laser); break;
                case Element.Portal: spec.Gates.Add(Gate.Teleporter(TeleporterKind.Visible)); break;
                case Element.LockedPortal: spec.Gates.Add(Gate.Teleporter(TeleporterKind.Locked)); break;
                case Element.CursedPortal: spec.Gates.Add(Gate.Teleporter(TeleporterKind.Cursed)); break;
                default: throw new ArgumentException($"{e} is not laid by the budget");
            }
        }

        /// <summary>The par window: each gate is a detour to walk back from, each floor a climb, each torch its detour.</summary>
        static void SetMoves(LevelSpec spec, ActDefinition act)
        {
            int gates = spec.Gates.Count;
            spec.MinMoves = act.MinMoves + 2 * (gates - 1);
            spec.MaxMoves = act.MaxMoves + (gates - 1) * ExtraMovesPerGate + (spec.Floors - 1) * ExtraMovesPerFloor;
            foreach (var g in spec.Gates)
                // A locked portal is two mechanics (its lever, then the portal): room for the lever's detour.
                if (g.Kind == GateKind.Portal && g.Portal == TeleporterKind.Locked) spec.MaxMoves += LockedPortalExtraMoves;
            // Relighting is part of the ideal walk (the spikes past the dust are disarmed by torchlight): room for it, within
            // about 2 minutes of play.
            int dustRoom = spec.DustPatches * (LevelValidator.MaxTorchDetour + LevelValidator.TollSpikes);
            // Each spike trap barring the walk is one disarm (the dust's are in its room).
            spec.MaxMoves += Math.Max(0, spec.SpikeTraps - LevelValidator.TollSpikes * spec.DustPatches);
            spec.MaxMoves = Math.Max(spec.MaxMoves, Math.Min(spec.MaxMoves + dustRoom, MaxMovesCap));
        }

        /// <summary>Base seed of a level; every run mixes in its own variant number to draw a new maze.</summary>
        public static ulong Seed(LevelId id) =>
            Pcg32.Hash(Pcg32.Hash(0x4D554D4D59UL /* "MUMMY" */, (ulong)GeneratorVersion), (ulong)(((int)id.Mode + 1) * 100000 + id.Act * 1000 + id.Index));

        /// <summary>Seed of one maze of a level: (level, variant) always rebuilds the same tomb on every device.</summary>
        public static ulong Seed(LevelId id, int variant) => Pcg32.Hash(Seed(id), (ulong)(uint)variant);
    }
}
