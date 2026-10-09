using System;

namespace MummyEscape.Core
{
    /// <summary>The four swipe directions. Y grows upward (matches Unity world space).</summary>
    public enum Dir : byte { Up, Right, Down, Left }

    public static class DirExt
    {
        public static readonly Dir[] All = { Dir.Up, Dir.Right, Dir.Down, Dir.Left };

        public static int Dx(this Dir d) => d == Dir.Right ? 1 : d == Dir.Left ? -1 : 0;
        public static int Dy(this Dir d) => d == Dir.Up ? 1 : d == Dir.Down ? -1 : 0;
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
        /// <summary>Turned clockwise by this many quarter turns (negative = anticlockwise).</summary>
        public static Dir Turn(this Dir d, int quarters) => (Dir)(((int)d + quarters) & 3);
    }

    /// <summary>A tile coordinate in the tomb: floor (storey) + grid position.</summary>
    [Serializable]
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int Floor;
        public readonly int X;
        public readonly int Y;

        public Cell(int floor, int x, int y) { Floor = floor; X = x; Y = y; }

        public Cell Step(Dir d) => new Cell(Floor, X + d.Dx(), Y + d.Dy());
        public Cell WithFloor(int floor) => new Cell(floor, X, Y);

        public int Manhattan(Cell o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y) + Math.Abs(Floor - o.Floor) * 4;

        public bool Equals(Cell o) => Floor == o.Floor && X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => (Floor * 73856093) ^ (X * 19349663) ^ (Y * 83492791);
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"(f{Floor}:{X},{Y})";
    }

    /// <summary>Solo difficulty modes: each holds the whole campaign (every act), the next one opens once it is finished.</summary>
    public enum Difficulty : byte { Easy, Normal, Extreme }

    public static class DifficultyExt
    {
        public static readonly Difficulty[] All = { Difficulty.Easy, Difficulty.Normal, Difficulty.Extreme };

        /// <summary>One letter for ids and keys: F(acile), N(ormal), X (extrême).</summary>
        public static char Letter(this Difficulty d) => d == Difficulty.Easy ? 'F' : d == Difficulty.Normal ? 'N' : 'X';

        public static bool TryParse(char c, out Difficulty d)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'F': case 'E': d = Difficulty.Easy; return true;
                case 'N': d = Difficulty.Normal; return true;
                case 'X': d = Difficulty.Extreme; return true;
                default: d = Difficulty.Easy; return false;
            }
        }
    }

    /// <summary>Identifies a level as "mode act-index", e.g. F1-1 (Facile, act 1, level 1).</summary>
    [Serializable]
    public readonly struct LevelId : IEquatable<LevelId>
    {
        public readonly Difficulty Mode;
        public readonly int Act;
        public readonly int Index;

        public LevelId(Difficulty mode, int act, int index) { Mode = mode; Act = act; Index = index; }

        /// <summary>Stable key used for saves and leaderboard ids ("level_f1_1").</summary>
        public string Key => $"level_{char.ToLowerInvariant(Mode.Letter())}{Act}_{Index}";

        /// <summary>Act and level inside the mode, as shown to the player ("1-3").</summary>
        public string Short => $"{Act}-{Index}";

        public LevelId WithMode(Difficulty mode) => new LevelId(mode, Act, Index);

        /// <summary>"F2-3", "x5-1" or "2-3" (Facile when the letter is left out).</summary>
        public static bool TryParse(string text, out LevelId id)
        {
            id = default;
            if (string.IsNullOrEmpty(text)) return false;
            var mode = Difficulty.Easy;
            if (char.IsLetter(text[0]))
            {
                if (!DifficultyExt.TryParse(text[0], out mode)) return false;
                text = text.Substring(1);
            }
            var parts = text.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int act) || !int.TryParse(parts[1], out int index)) return false;
            id = new LevelId(mode, act, index);
            return true;
        }

        /// <summary>Reads a <see cref="Key"/> back ("level_n2_4"); false for keys of the old campaign ("level_2_4").</summary>
        public static bool TryParseKey(string key, out LevelId id)
        {
            id = default;
            if (key == null || !key.StartsWith("level_") || key.Length < 9) return false;
            return TryParse(key.Substring(6).Replace('_', '-'), out id) && char.IsLetter(key[6]);
        }

        public bool Equals(LevelId o) => Mode == o.Mode && Act == o.Act && Index == o.Index;
        public override bool Equals(object obj) => obj is LevelId l && Equals(l);
        public override int GetHashCode() => (int)Mode * 100000 + Act * 1000 + Index;
        public override string ToString() => $"{Mode.Letter()}{Act}-{Index}";
    }
}
