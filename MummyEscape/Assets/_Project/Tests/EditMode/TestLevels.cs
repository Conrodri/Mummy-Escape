using MummyEscape.Core;

namespace MummyEscape.Tests
{
    internal static class TestLevels
    {
        /// <summary>
        /// 1-floor level from ASCII rows (top row first): S start, E exit, # wall, . floor, A-P doors, a-p buttons,
        /// ^ spikes, ~ darkness.
        /// </summary>
        public static Level FromAscii(params string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            var level = new Level(w, h, 1) { MaxHp = 2 };
            int traps = 0, channels = 0;
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
                    level[c] = t;
                }
            level.ChannelCount = channels;
            level.TrapCount = traps;
            return level;
        }
    }
}
