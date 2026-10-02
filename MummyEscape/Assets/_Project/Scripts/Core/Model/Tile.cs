namespace MummyEscape.Core
{
    public enum TileType : byte
    {
        Wall,
        Floor,
        Exit,
        /// <summary>Blocks the way until the button with the same channel is pressed.</summary>
        Door,
        /// <summary>Pressure plate / lever: stepping on it activates its channel (an interaction).</summary>
        Button,
        Trap,
        Teleporter,
        /// <summary>Cracked floor: stepping on it drops the player one floor down (same x,y).</summary>
        BreakableFloor,
        /// <summary>Climbs to floor+1 (same x,y), landing on the matching LadderDown.</summary>
        LadderUp,
        /// <summary>Climbs to floor-1 (same x,y), landing on the matching LadderUp.</summary>
        LadderDown,
        /// <summary>Thick dust: walking onto it smothers the mummy's torch (vision shrinks to its own tile).</summary>
        Dust,
        /// <summary>A wall with a burning sconce: passing next to it relights the mummy's torch. Solid like a wall.</summary>
        WallTorch,
    }

    public enum TrapKind : byte
    {
        None,
        /// <summary>Spikes: -1 HP each time the player walks onto it while armed.</summary>
        Spikes,
        /// <summary>Cursed sand: player is blinded (sees only their own tile) for a few moves.</summary>
        Darkness,
    }

    public enum TeleporterKind : byte
    {
        None,
        /// <summary>A visible glyph portal, free to use.</summary>
        Visible,
        /// <summary>Looks like plain floor until the player steps on it.</summary>
        Hidden,
        /// <summary>Visible but sealed until its channel (lever) is activated.</summary>
        Locked,
        /// <summary>Works, but the curse wipes the player's map memory (everything goes black again).</summary>
        Cursed,
    }

    public struct Tile
    {
        public TileType Type;
        /// <summary>Door / Button / Locked teleporter channel (bit index in the pressed mask).</summary>
        public byte Channel;
        public TrapKind Trap;
        /// <summary>Index of this trap in the disarmed mask.</summary>
        public byte TrapIndex;
        public TeleporterKind Teleporter;

        public bool IsWalkableTerrain => !IsSolid;
        /// <summary>Blocks movement (plain wall or wall torch).</summary>
        public bool IsSolid => Type == TileType.Wall || Type == TileType.WallTorch;

        /// <summary>True for every tile the generator treats as a point of interest.</summary>
        public bool IsPointOfInterest =>
            Type == TileType.Exit || Type == TileType.Door || Type == TileType.Button ||
            Type == TileType.Teleporter || Type == TileType.BreakableFloor ||
            Type == TileType.LadderUp || Type == TileType.LadderDown;

        public static Tile Wall => new Tile { Type = TileType.Wall };
        public static Tile Floor => new Tile { Type = TileType.Floor };
    }
}
