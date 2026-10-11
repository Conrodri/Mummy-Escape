using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MummyEscape.Core
{
    /// <summary>
    /// Whole tombs stored as text, one per line, for tombs that must never change when the generator does (endless mode,
    /// event acts). The file is append-only: a line once written is never edited, so a player's position in a sequence
    /// always points at the same tomb. Reading never involves the generator nor <see cref="DifficultyTable.GeneratorVersion"/>.
    ///
    /// Line, format 1 (fields separated by one space):
    /// <c>1 N3-7 g13 v1000042 W H F hp start exit tiles links decoys</c>
    /// <list type="bullet">
    /// <item>1: format of the line (a later format adds its own reader; lines already written keep this one);</item>
    /// <item>N3-7: the campaign level whose spec drew the tomb (mode, and act for the look and the music);</item>
    /// <item>g13 / v1000042: generator version and variant that drew it (for the record only, never read back);</item>
    /// <item>W H F: width, height, floors; hp: the mummy's hit points; start, exit: cell indices
    /// (<see cref="Level.IndexOf"/>);</item>
    /// <item>tiles: base64 of every tile in index order, one byte <see cref="TileType"/>, with bit 7 set when five more bytes
    /// follow (channel, trap kind, trap index, teleporter kind, param);</item>
    /// <item>links: teleporter pairs "a-b,c-d" (cell indices), decoys: "i,j" (cell indices), "-" for none.</item>
    /// </list>
    /// Enum values are stored as numbers: <see cref="TileType"/>, <see cref="TrapKind"/> and <see cref="TeleporterKind"/>
    /// may only grow at their end.
    /// </summary>
    public static class TombArchive
    {
        public const int Format = 1;
        const int Extra = 0x80;

        /// <summary>One line describing <paramref name="level"/> (no line break).</summary>
        public static string Write(Level level)
        {
            var bytes = new List<byte>(level.CellCount * 2);
            var links = new List<string>();
            var decoys = new List<string>();
            for (int i = 0; i < level.CellCount; i++)
            {
                var c = level.CellAt(i);
                var t = level[c];
                bool extra = t.Channel != 0 || t.Trap != 0 || t.TrapIndex != 0 || t.Teleporter != 0 || t.Param != 0;
                bytes.Add((byte)((int)t.Type | (extra ? Extra : 0)));
                if (extra)
                {
                    bytes.Add(t.Channel);
                    bytes.Add((byte)t.Trap);
                    bytes.Add(t.TrapIndex);
                    bytes.Add((byte)t.Teleporter);
                    bytes.Add(t.Param);
                }
                if (level.TryGetTeleportTarget(c, out var to) && i < level.IndexOf(to)) links.Add(i + "-" + level.IndexOf(to));
                if (level.IsDecoy(c)) decoys.Add(i.ToString(CultureInfo.InvariantCulture));
            }
            var sb = new StringBuilder();
            sb.Append(Format).Append(' ').Append(level.Id).Append(" g").Append(DifficultyTable.GeneratorVersion).Append(" v").Append(level.Variant)
              .Append(' ').Append(level.Width).Append(' ').Append(level.Height).Append(' ').Append(level.Floors).Append(' ').Append(level.MaxHp)
              .Append(' ').Append(level.IndexOf(level.Start)).Append(' ').Append(level.IndexOf(level.Exit))
              .Append(' ').Append(Convert.ToBase64String(bytes.ToArray()))
              .Append(' ').Append(links.Count > 0 ? string.Join(",", links) : "-")
              .Append(' ').Append(decoys.Count > 0 ? string.Join(",", decoys) : "-");
            return sb.ToString();
        }

        /// <summary>The tomb of one line, without its solution (see <see cref="Load"/>). Throws <see cref="FormatException"/>.</summary>
        public static Level Read(string line)
        {
            var f = (line ?? "").Trim().Split(' ');
            if (f.Length != 13 || f[0] != "1") throw new FormatException("Unknown tomb line: " + Head(line));
            if (!LevelId.TryParse(f[1], out var id)) throw new FormatException("Bad level id: " + f[1]);
            int w = Int(f[4]), h = Int(f[5]), floors = Int(f[6]);
            if (w <= 0 || h <= 0 || floors <= 0 || w * h * floors > 1 << 16) throw new FormatException("Bad size: " + Head(line));
            var level = new Level(w, h, floors) { Id = id, Variant = Int(f[3].Substring(1)), MaxHp = Int(f[7]) };
            level.Start = Cell(level, f[8]);
            level.Exit = Cell(level, f[9]);

            var bytes = Convert.FromBase64String(f[10]);
            int at = 0;
            for (int i = 0; i < level.CellCount; i++)
            {
                if (at >= bytes.Length) throw new FormatException("Truncated tiles: " + Head(line));
                int b = bytes[at++];
                var t = new Tile { Type = (TileType)(b & ~Extra) };
                if ((b & Extra) != 0)
                {
                    if (at + 5 > bytes.Length) throw new FormatException("Truncated tiles: " + Head(line));
                    t.Channel = bytes[at++];
                    t.Trap = (TrapKind)bytes[at++];
                    t.TrapIndex = bytes[at++];
                    t.Teleporter = (TeleporterKind)bytes[at++];
                    t.Param = bytes[at++];
                }
                level[level.CellAt(i)] = t;
            }
            if (at != bytes.Length) throw new FormatException("Extra tile bytes: " + Head(line));

            if (f[11] != "-")
                foreach (var pair in f[11].Split(','))
                {
                    var ab = pair.Split('-');
                    if (ab.Length != 2) throw new FormatException("Bad link: " + pair);
                    level.LinkTeleporters(Cell(level, ab[0]), Cell(level, ab[1]));
                }
            if (f[12] != "-")
                foreach (var d in f[12].Split(',')) level.AddDecoy(Cell(level, d));
            return level;
        }

        /// <summary>The tomb of one line with its optimal solution (the par) under today's rules; throws when it has none.</summary>
        public static Level Load(string line)
        {
            var level = Read(line);
            level.Solution = Solver.Solve(level) ?? throw new FormatException("No way out under the current rules: " + Head(line));
            return level;
        }

        /// <summary>The lines of a stored file drawn from <paramref name="mode"/>'s specs, in file order: that mode's sequence.</summary>
        public static List<string> Lines(string text, Difficulty mode)
        {
            var list = new List<string>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(' ');
                if (f.Length > 1 && LevelId.TryParse(f[1], out var id) && id.Mode == mode) list.Add(line);
            }
            return list;
        }

        static int Int(string s)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) throw new FormatException("Bad number: " + s);
            return v;
        }

        static Cell Cell(Level level, string s)
        {
            int i = Int(s);
            if (i < 0 || i >= level.CellCount) throw new FormatException("Cell out of the tomb: " + s);
            return level.CellAt(i);
        }

        static string Head(string line) => line == null ? "null" : line.Length > 40 ? line.Substring(0, 40) + "…" : line;
    }
}
