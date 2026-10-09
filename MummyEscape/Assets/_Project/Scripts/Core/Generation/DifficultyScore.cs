using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>What a tomb is made of, as the difficulty score counts it.</summary>
    public enum Element : byte
    {
        /// <summary>Each floor above the first.</summary>
        Floor,
        // ---- Traps ----
        /// <summary>Spikes barring the walk: disarmed by torchlight, a hit in the dark.</summary>
        Spikes,
        /// <summary>Spikes on a shortcut: a spike-free way round costs a few more moves.</summary>
        SpikeShortcut,
        /// <summary>A cloud of darkness: blind for a few steps.</summary>
        Darkness,
        /// <summary>A mirror of Seth: controls reversed for ten steps.</summary>
        Reverse,
        /// <summary>A turning slab: the tomb turns a quarter turn.</summary>
        Rotate,
        // ---- Interactions ----
        /// <summary>A door and its button.</summary>
        Door,
        /// <summary>A laser barrier and its switch.</summary>
        Laser,
        /// <summary>A blue barrier the switch raises behind the player.</summary>
        BlueBarrier,
        /// <summary>A pair of teleporters (visible).</summary>
        Portal,
        /// <summary>A pair of teleporters that needs its lever first.</summary>
        LockedPortal,
        /// <summary>A pair of cursed teleporters: the curse wipes the map the player remembers.</summary>
        CursedPortal,
        /// <summary>Dust that smothers the torch, its wall torch and the spikes past it that need the light.</summary>
        Torch,
        // ---- Act mechanics ----
        Current,
        Crumbling,
        FireJet,
        // ---- Drawn patterns ----
        /// <summary>The corridor of decoy alcoves after a turning slab.</summary>
        DecoyCorridor,
    }

    /// <summary>
    /// Difficulty points: every element of a tomb adds its weight. A level's score must stay inside the band of its act and
    /// mode (<see cref="DifficultyTable"/>): Facile below a threshold, Normal from there to the next, Extrême beyond.
    /// Tune the game by changing these weights and the thresholds of the acts.
    /// </summary>
    public static class DifficultyScore
    {
        public static int Weight(Element e)
        {
            switch (e)
            {
                case Element.Floor: return 8;
                case Element.Spikes: return 2;
                case Element.SpikeShortcut: return 3;
                case Element.Darkness: return 3;
                case Element.Reverse: return 5;
                case Element.Rotate: return 6;
                case Element.Door: return 3;
                case Element.Laser: return 4;
                case Element.BlueBarrier: return 2;
                case Element.Portal: return 3;
                case Element.LockedPortal: return 5;
                case Element.CursedPortal: return 5;
                case Element.Torch: return 5;
                case Element.Current: return 2;
                case Element.Crumbling: return 2;
                case Element.FireJet: return 3;
                case Element.DecoyCorridor: return 3;
                default: return 0;
            }
        }

        public static bool IsTrap(Element e) => e >= Element.Spikes && e <= Element.Rotate;
        public static bool IsInteraction(Element e) => e >= Element.Door && e <= Element.Torch;
        public static bool IsActMechanic(Element e) => e >= Element.Current && e <= Element.FireJet;

        public static Element ElementOf(Gate g) =>
            g.Kind == GateKind.Door ? Element.Door
            : g.Kind == GateKind.Laser ? Element.Laser
            : g.Portal == TeleporterKind.Locked ? Element.LockedPortal
            : g.Portal == TeleporterKind.Cursed ? Element.CursedPortal
            : Element.Portal;

        /// <summary>What the spec asks for (the score shown before playing: the same for every maze of the level).</summary>
        public static Composition Of(LevelSpec spec)
        {
            var c = new Composition();
            c.Add(Element.Floor, spec.Floors - 1);
            c.Add(Element.Spikes, spec.SpikeTraps);
            c.Add(Element.SpikeShortcut, spec.SpikeShortcuts);
            c.Add(Element.Darkness, spec.DarknessTraps);
            c.Add(Element.Reverse, spec.ReverseTraps);
            c.Add(Element.Rotate, spec.RotateTraps);
            foreach (var g in spec.Gates)
            {
                c.Add(ElementOf(g), 1);
                if (g.Kind == GateKind.Laser && spec.BlueBarriers) c.Add(Element.BlueBarrier, 1);
            }
            c.Add(Element.Torch, spec.DustPatches);
            c.Add(Element.Current, spec.Currents);
            c.Add(Element.Crumbling, spec.CrumblingTiles);
            c.Add(Element.FireJet, spec.FireJets);
            return c;
        }

        /// <summary>
        /// What the tomb really holds, read from its tiles: spikes with no way round are spike traps (those past the dust
        /// included), the others shortcuts.
        /// </summary>
        public static Composition Of(Level level)
        {
            var c = new Composition();
            c.Add(Element.Floor, level.Floors - 1);
            int portals = 0, locked = 0, cursed = 0;
            foreach (var cell in level.AllCells())
            {
                var t = level[cell];
                switch (t.Type)
                {
                    case TileType.Trap:
                        switch (t.Trap)
                        {
                            case TrapKind.Spikes: c.Add(LevelValidator.SpikeDetour(level, cell) < 0 ? Element.Spikes : Element.SpikeShortcut, 1); break;
                            case TrapKind.Darkness: c.Add(Element.Darkness, 1); break;
                            case TrapKind.Reverse: c.Add(Element.Reverse, 1); break;
                            case TrapKind.Rotate: c.Add(Element.Rotate, 1); break;
                        }
                        break;
                    case TileType.Door: c.Add(Element.Door, 1); break;
                    case TileType.Barrier: c.Add(t.Param == 1 ? Element.BlueBarrier : Element.Laser, 1); break;
                    case TileType.Teleporter:
                        if (t.Teleporter == TeleporterKind.Locked) locked++;
                        else if (t.Teleporter == TeleporterKind.Cursed) cursed++;
                        else portals++;
                        break;
                    case TileType.Dust: c.Add(Element.Torch, 1); break;
                    case TileType.Current:
                        // One per stream: the tile no other current flows into.
                        if (!FedByCurrent(level, cell)) c.Add(Element.Current, 1);
                        break;
                    case TileType.Crumbling: c.Add(Element.Crumbling, 1); break;
                    case TileType.FireJet: c.Add(Element.FireJet, 1); break;
                }
            }
            c.Add(Element.Portal, portals / 2);
            c.Add(Element.LockedPortal, locked / 2);
            c.Add(Element.CursedPortal, cursed / 2);
            if (level.HasDecoys) c.Add(Element.DecoyCorridor, 1);
            return c;
        }

        static bool FedByCurrent(Level level, Cell c)
        {
            foreach (var d in DirExt.All)
            {
                var from = c.Step(d.Opposite());
                if (level.InBounds(from) && level[from].Type == TileType.Current && level[from].Param == (byte)d) return true;
            }
            return false;
        }
    }

    /// <summary>How many of each element, and the points they add up to.</summary>
    public sealed class Composition
    {
        readonly int[] _counts = new int[(int)Element.DecoyCorridor + 1];

        public int this[Element e] => _counts[(int)e];

        public void Add(Element e, int n)
        {
            if (n > 0) _counts[(int)e] += n;
        }

        public int Score
        {
            get
            {
                int s = 0;
                for (int i = 0; i < _counts.Length; i++) s += _counts[i] * DifficultyScore.Weight((Element)i);
                return s;
            }
        }

        public int Floors => 1 + this[Element.Floor];

        public int Traps
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _counts.Length; i++) if (DifficultyScore.IsTrap((Element)i)) n += _counts[i];
                return n;
            }
        }

        /// <summary>Mechanisms to use or relight (blue barriers come with their laser and are not counted apart).</summary>
        public int Interactions
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _counts.Length; i++)
                    if (DifficultyScore.IsInteraction((Element)i) && (Element)i != Element.BlueBarrier) n += _counts[i];
                return n;
            }
        }

        public int ActMechanics
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _counts.Length; i++) if (DifficultyScore.IsActMechanic((Element)i)) n += _counts[i];
                return n;
            }
        }

        public IEnumerable<(Element element, int count)> Items
        {
            get
            {
                for (int i = 0; i < _counts.Length; i++) if (_counts[i] > 0) yield return ((Element)i, _counts[i]);
            }
        }

        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var (e, n) in Items) parts.Add($"{e}x{n}");
            return $"{Score} pts [{string.Join(" ", parts)}]";
        }
    }
}
