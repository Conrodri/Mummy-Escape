using System.Collections.Generic;

namespace MummyEscape.Core
{
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
        public int Rooms;
        /// <summary>Chance (per mille) to knock out a dead end and create a loop.</summary>
        public int LoopChance = 50;
        /// <summary>Chance (per mille) the maze carver keeps going straight-ish (newest cell) vs branching.</summary>
        public int Windiness = 750;

        // ---- Interactions ----
        /// <summary>Doors that block every route to the exit; their buttons must be pressed.</summary>
        public int RequiredButtons;
        /// <summary>Optional doors on side branches (with their buttons somewhere) to mislead the player.</summary>
        public int DecoyDoors;
        public int SpikeTraps;
        public int DarknessTraps;
        public List<TeleporterKind> Teleporters = new List<TeleporterKind>();
        public int LaddersPerLink = 1;
        public int BreakableFloors;

        // ---- Feel ----
        /// <summary>Minimum Manhattan distance between two points of interest.</summary>
        public int MinPoiSpacing = 4;
        public int MaxHp = 2;
        /// <summary>The optimal solution must leave at least this much HP (avoids "par requires nearly dying").</summary>
        public int MinHpLeftForPar = 1;

        public int Width => CellsX * 2 + 1;
        public int Height => CellsY * 2 + 1;

        public override string ToString() =>
            $"{Id} moves[{MinMoves}-{MaxMoves}] floors:{Floors} cells:{CellsX}x{CellsY} rooms:{Rooms} btn:{RequiredButtons} decoy:{DecoyDoors} " +
            $"spikes:{SpikeTraps} dark:{DarknessTraps} tp:[{string.Join(",", Teleporters)}] breakable:{BreakableFloors}";
    }
}
