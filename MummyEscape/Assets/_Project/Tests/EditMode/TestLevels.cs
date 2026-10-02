using MummyEscape.Core;

namespace MummyEscape.Tests
{
    internal static class TestLevels
    {
        /// <summary>
        /// 1-floor level from ASCII rows (top row first): S start, E exit, # wall, . floor, A-P doors, a-p buttons,
        /// ^ spikes, ~ darkness, , dust, ! wall torch, {>}< currents (up right down left), x fragile slab,
        /// | red barrier and = blue barrier (channel 0), $ switch (channel 0), 0-2 flame jet phase.
        /// </summary>
        public static Level FromAscii(params string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            var level = new Level(w, h, 1) { MaxHp = 2 };
            int traps = 0, channels = 0, crumbling = 0;
            for (int row = 0; row < h; row++)
                for (int x = 0; x < w; x++)
                {
                    var c = new Cell(0, x, h - 1 - row);
                    char ch = rows[row][x];
                    Tile t = Tile.Floor;
                    if (ch == '#') t = Tile.Wall;
                    else if (ch == 'S') level.Start = c;
                    else if (ch == 'E') { t = new Tile { Type = TileType.Exit }; level.Exit = c; }
                    else if (ch >= 'A' && ch <= 'P') t = new Tile { Type = TileType.Door, Channel = (byte)(ch - 'A') };
                    else if (ch >= 'a' && ch <= 'p') { t = new Tile { Type = TileType.Button, Channel = (byte)(ch - 'a') }; channels++; }
                    else if (ch == '^') t = new Tile { Type = TileType.Trap, Trap = TrapKind.Spikes, TrapIndex = (byte)traps++ };
                    else if (ch == '~') t = new Tile { Type = TileType.Trap, Trap = TrapKind.Darkness, TrapIndex = (byte)traps++ };
                    else if (ch == ',') t = new Tile { Type = TileType.Dust };
                    else if (ch == '!') t = new Tile { Type = TileType.WallTorch };
                    else if (Level.CurrentGlyphs.IndexOf(ch) >= 0) t = new Tile { Type = TileType.Current, Param = (byte)Level.CurrentGlyphs.IndexOf(ch) };
                    else if (ch == 'x') t = new Tile { Type = TileType.Crumbling, Param = (byte)crumbling++ };
                    else if (ch == '|') t = new Tile { Type = TileType.Barrier, Param = 0 };
                    else if (ch == '=') t = new Tile { Type = TileType.Barrier, Param = 1 };
                    else if (ch == '$') t = new Tile { Type = TileType.Switch };
                    else if (ch >= '0' && ch <= '2') t = new Tile { Type = TileType.FireJet, Param = (byte)(ch - '0') };
                    level[c] = t;
                }
            level.ChannelCount = channels;
            level.TrapCount = traps;
            return level;
        }
    }
}
