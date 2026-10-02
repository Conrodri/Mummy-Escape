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

    /// <summary>Identifies a level as "act-index", e.g. 1-1.</summary>
    [Serializable]
    public readonly struct LevelId : IEquatable<LevelId>
    {
        public readonly int Act;
        public readonly int Index;

        public LevelId(int act, int index) { Act = act; Index = index; }

        /// <summary>Stable key used for saves and leaderboard ids ("level_1_1").</summary>
        public string Key => $"level_{Act}_{Index}";

        public bool Equals(LevelId o) => Act == o.Act && Index == o.Index;
        public override bool Equals(object obj) => obj is LevelId l && Equals(l);
        public override int GetHashCode() => Act * 1000 + Index;
        public override string ToString() => $"{Act}-{Index}";
    }
}
