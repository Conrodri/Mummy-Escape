using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>What blocks the route to the exit until the player uses a mechanic.</summary>
    public enum GateKind : byte
    {
        /// <summary>A door across the route; its button waits at the end of a dead end, away from the route.</summary>
        Door,
        /// <summary>The route is cut by a wall; the only way on is a teleporter hidden at the end of a dead end.</summary>
        Portal,
        /// <summary>A red laser barrier across the route; its toggle switch waits at the end of a dead end. Flipping it
        /// also raises a blue barrier behind the player when the spec asks for one.</summary>
        Laser,
    }

    public struct Gate
    {
        public GateKind Kind;
        /// <summary>Teleporter kind for portal gates (a Locked portal also needs its lever).</summary>
        public TeleporterKind Portal;

        public static Gate Door => new Gate { Kind = GateKind.Door };
        public static Gate Laser => new Gate { Kind = GateKind.Laser };
        public static Gate Teleporter(TeleporterKind kind) => new Gate { Kind = GateKind.Portal, Portal = kind };
        public override string ToString() => Kind == GateKind.Portal ? "Portal:" + Portal : Kind.ToString();
    }

    /// <summary>
    /// The strict "cahier des charges" of one level. The generator retries until the produced tomb satisfies every
    /// constraint, and the solver proves it. Everything is integer based so generation is identical on all devices.
    /// </summary>
    public sealed class LevelSpec
    {
        public LevelId Id;

        // ---- Fairness: optimal (par) number of moves must land in this window ----
        public int MinMoves;
        public int MaxMoves;

        // ---- Size ----
        public int Floors = 1;
        /// <summary>Maze cells per side; the tile grid is (2 * cells + 1) wide.</summary>
        public int CellsX = 5;
        public int CellsY = 5;
        /// <summary>Chance (per mille) the maze carver keeps going straight-ish (newest cell) vs branching.</summary>
        public int Windiness = 600;
        /// <summary>Extra short loops knocked into the maze (alternative routes to remember), on top of braiding.</summary>
        public int ExtraLoops;
        /// <summary>
        /// Chance (per mille) that a pointless dead end becomes a loop; otherwise it is filled back with rock. Either way no
        /// dead end is left without a purpose. More loops = more routes to tell apart, more rock = narrower galleries.
        /// </summary>
        public int BraidChance = 750;

        // ---- Route structure ----
        /// <summary>Mandatory obstacles along the route to the exit, in order (at least one per level).</summary>
        public List<Gate> Gates = new List<Gate>();
        /// <summary>The exit must be at least this far from the start (Manhattan, a floor counts as 4).</summary>
        public int MinExitDistance = 8;
        /// <summary>Mechanics (buttons pressed + portals taken) the optimal route must use.</summary>
        public int MinMechanics = 1;

        // ---- Hazards, all on the route (nothing is placed just to mislead) ----
        public int SpikeTraps;
        public int DarknessTraps;
        /// <summary>Dust patches on the route (torch goes out), each followed by a wall torch further on.</summary>
        public int DustPatches;

        // ---- Act mechanics ----
        /// <summary>Streams of current (2-4 tiles) on the route, flowing towards the exit: one-way shortcuts, with a way
        /// back around so they never wall the player in.</summary>
        public int Currents;
        /// <summary>Fragile slabs on the route: a one-time crossing, with a longer way around once they are rubble.</summary>
        public int CrumblingTiles;
        /// <summary>Flame jets on the route, each with its own beat.</summary>
        public int FireJets;
        /// <summary>Laser gates also raise a blue barrier on the way back when their switch is flipped.</summary>
        public bool BlueBarriers;

        // ---- Feel ----
        /// <summary>Minimum Manhattan distance between two points of interest.</summary>
        public int MinPoiSpacing = 4;
        public int MaxHp = 2;
        /// <summary>The optimal solution must leave at least this much HP (avoids "par requires nearly dying").</summary>
        public int MinHpLeftForPar = 1;

        public int Width => CellsX * 2 + 1;
        public int Height => CellsY * 2 + 1;

        /// <summary>The same spec with a wider par window (generator safety net, see <see cref="LevelGenerator"/>).</summary>
        public LevelSpec WithMoreMoves(int extra)
        {
            var copy = (LevelSpec)MemberwiseClone();
            copy.Gates = new List<Gate>(Gates);
            copy.MaxMoves += extra;
            return copy;
        }

        /// <summary>Buttons the optimal route must press: one per door gate and per locked portal gate.</summary>
        public int RequiredButtons
        {
            get
            {
                int n = 0;
                foreach (var g in Gates) if (g.Kind != GateKind.Portal || g.Portal == TeleporterKind.Locked) n++;
                return n;
            }
        }

        public int RequiredPortals
        {
            get
            {
                int n = 0;
                foreach (var g in Gates) if (g.Kind == GateKind.Portal) n++;
                return n;
            }
        }

        public override string ToString() =>
            $"{Id} moves[{MinMoves}-{MaxMoves}] floors:{Floors} cells:{CellsX}x{CellsY} gates:[{string.Join(",", Gates)}] " +
            $"spikes:{SpikeTraps} dark:{DarknessTraps} dust:{DustPatches} loops:{ExtraLoops}" +
            $" currents:{Currents} crumbling:{CrumblingTiles} fire:{FireJets}{(BlueBarriers ? " blue" : "")}";
    }
}
