using System;
using System.Collections.Generic;
using System.Text;

namespace MummyEscape.Core
{
    /// <summary>A fully generated tomb: tiles for every floor plus metadata.</summary>
    public sealed class Level
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Floors;
        readonly Tile[] _tiles;
        readonly Dictionary<int, Cell> _teleportTargets = new Dictionary<int, Cell>();

        public LevelId Id { get; internal set; }
        public LevelSpec Spec { get; internal set; }
        public int Seed { get; internal set; }
        /// <summary>Which maze of this level (a new one is drawn for every run).</summary>
        public int Variant { get; internal set; }
        /// <summary>Which generation attempt produced this level (debug only).</summary>
        public int Attempt { get; internal set; }
        public Cell Start { get; internal set; }
        public Cell Exit { get; internal set; }
        public int ChannelCount { get; internal set; }
        public int TrapCount { get; internal set; }
        public int MaxHp { get; internal set; } = 2;
        /// <summary>The optimal (omniscient) solution, i.e. the par of the level.</summary>
        public Solution Solution { get; internal set; }

        public Level(int width, int height, int floors)
        {
            Width = width;
            Height = height;
            Floors = floors;
            _tiles = new Tile[width * height * floors];
        }

        public int CellCount => _tiles.Length;

        public bool InBounds(Cell c) =>
            c.Floor >= 0 && c.Floor < Floors && c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

        public int IndexOf(Cell c) => (c.Floor * Height + c.Y) * Width + c.X;

        public Cell CellAt(int index)
        {
            int x = index % Width;
            int rest = index / Width;
            return new Cell(rest / Height, x, rest % Height);
        }

        public Tile this[Cell c]
        {
            get => _tiles[IndexOf(c)];
            internal set => _tiles[IndexOf(c)] = value;
        }

        public Tile Get(Cell c) => InBounds(c) ? _tiles[IndexOf(c)] : Tile.Wall;

        internal void LinkTeleporters(Cell a, Cell b)
        {
            _teleportTargets[IndexOf(a)] = b;
            _teleportTargets[IndexOf(b)] = a;
        }

        public bool TryGetTeleportTarget(Cell from, out Cell to) => _teleportTargets.TryGetValue(IndexOf(from), out to);

        readonly HashSet<Cell> _decoys = new HashSet<Cell>();

        /// <summary>
        /// Empty alcoves of a decoy corridor (a turning slab at its entry): dead ends drawn on purpose, so the real way on
        /// has to be told from memory once the tomb has turned. The one exception to "every dead end means something".
        /// </summary>
        public bool IsDecoy(Cell c) => _decoys.Contains(c);

        internal void AddDecoy(Cell c) => _decoys.Add(c);

        public bool HasDecoys => _decoys.Count > 0;

        /// <summary>
        /// Which pair this teleporter belongs to (0, 1, 2… in cell order), -1 when it leads nowhere: both ends of a pair share
        /// a colour on screen, so four portals on a floor never leave the player guessing which goes where.
        /// </summary>
        public int TeleporterPair(Cell c)
        {
            if (!_teleportTargets.TryGetValue(IndexOf(c), out var to)) return -1;
            int key = Math.Min(IndexOf(c), IndexOf(to)), rank = 0;
            foreach (var kv in _teleportTargets)
                if (kv.Key < IndexOf(kv.Value) && kv.Key < key) rank++;
            return rank;
        }

        public const int PreviewSecondsPerFloor = 7;

        /// <summary>
        /// How long the map is shown before the fog falls: <see cref="PreviewSecondsPerFloor"/> per floor, pooled. The player
        /// spends them as they like, swiping from floor to floor (3 floors: 21 s to share out).
        /// </summary>
        public int PreviewSeconds => Floors * PreviewSecondsPerFloor;

        /// <summary>
        /// The same tomb trimmed to the bounding box of its ground (every floor alike, so ladders stay aligned), with a
        /// one-tile rock border. The offset is kept even so maze cells stay on odd coordinates.
        /// </summary>
        internal Level CroppedToGround()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            foreach (var c in AllCells())
            {
                if (this[c].Type == TileType.Wall) continue;
                minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
            }
            if (maxX < 0) return this;
            int x0 = (minX - 1) & ~1, y0 = (minY - 1) & ~1;
            int w = maxX + 2 - x0, h = maxY + 2 - y0;
            if (x0 <= 0 && y0 <= 0 && w >= Width && h >= Height) return this;
            Cell Map(Cell c) => new Cell(c.Floor, c.X - x0, c.Y - y0);

            var cropped = new Level(w, h, Floors)
            {
                Id = Id, Spec = Spec, Seed = Seed, Variant = Variant, Attempt = Attempt,
                Start = Map(Start), Exit = Map(Exit), ChannelCount = ChannelCount, TrapCount = TrapCount, MaxHp = MaxHp,
            };
            foreach (var c in cropped.AllCells())
                cropped[c] = Get(new Cell(c.Floor, c.X + x0, c.Y + y0));
            foreach (var kv in _teleportTargets)
                cropped._teleportTargets[cropped.IndexOf(Map(CellAt(kv.Key)))] = Map(kv.Value);
            foreach (var c in _decoys) cropped._decoys.Add(Map(c));
            return cropped;
        }

        /// <summary>
        /// The same tomb with one tile replaced (and its exit moved there when the tile is an exit). Used by the 2v2 relay:
        /// a leg ends on a plate, so the solver and the bots walk to it as to an exit. The copy has no solution.
        /// </summary>
        public Level WithTile(Cell c, Tile tile)
        {
            var copy = new Level(Width, Height, Floors)
            {
                Id = Id, Spec = Spec, Seed = Seed, Variant = Variant, Attempt = Attempt,
                Start = Start, Exit = tile.Type == TileType.Exit ? c : Exit, ChannelCount = ChannelCount, TrapCount = TrapCount, MaxHp = MaxHp,
            };
            Array.Copy(_tiles, copy._tiles, _tiles.Length);
            foreach (var kv in _teleportTargets) copy._teleportTargets[kv.Key] = kv.Value;
            foreach (var d in _decoys) copy._decoys.Add(d);
            copy[c] = tile;
            return copy;
        }

        /// <summary>The same tomb whose way out is <paramref name="goal"/> (see <see cref="WithTile"/>).</summary>
        public Level WithGoal(Cell goal) => WithTile(goal, new Tile { Type = TileType.Exit });

        public IEnumerable<Cell> AllCells()
        {
            for (int i = 0; i < _tiles.Length; i++) yield return CellAt(i);
        }

        /// <summary>Deterministic content hash: two players with the same level must get the same hash.</summary>
        public ulong ComputeHash()
        {
            ulong h = 14695981039346656037UL;
            void Mix(int v) { unchecked { h ^= (uint)v; h *= 1099511628211UL; } }
            Mix(Width); Mix(Height); Mix(Floors);
            for (int i = 0; i < _tiles.Length; i++)
            {
                var t = _tiles[i];
                Mix((int)t.Type | (t.Channel << 8) | ((int)t.Trap << 16) | ((int)t.Teleporter << 24));
            }
            Mix(IndexOf(Start)); Mix(IndexOf(Exit));
            return h;
        }

        /// <summary>ASCII dump, one block per floor (top row first). Handy for debugging and tests.</summary>
        public string ToAscii(ICollection<Cell> highlight = null)
        {
            var sb = new StringBuilder();
            for (int f = Floors - 1; f >= 0; f--)
            {
                sb.Append("Floor ").Append(f).AppendLine();
                for (int y = Height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        var c = new Cell(f, x, y);
                        sb.Append(Glyph(c, highlight));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Current glyphs indexed by <see cref="Dir"/>: up, right, down, left.</summary>
        public const string CurrentGlyphs = "{>}<";

        char Glyph(Cell c, ICollection<Cell> highlight)
        {
            if (c == Start) return 'S';
            var t = this[c];
            switch (t.Type)
            {
                case TileType.Wall: return '#';
                case TileType.Exit: return 'E';
                case TileType.Door: return (char)('A' + t.Channel);
                case TileType.Button: return (char)('a' + t.Channel);
                case TileType.Trap:
                    return t.Trap == TrapKind.Spikes ? '^' : t.Trap == TrapKind.Reverse ? 'X' : t.Trap == TrapKind.Rotate ? (t.Param == 1 ? 'R' : 'W') : '~';
                case TileType.Teleporter:
                    return t.Teleporter == TeleporterKind.Hidden ? '?' : t.Teleporter == TeleporterKind.Cursed ? '%' : t.Teleporter == TeleporterKind.Locked ? '&' : '@';
                case TileType.BreakableFloor: return 'v';
                case TileType.LadderUp: return 'U';
                case TileType.LadderDown: return 'D';
                case TileType.Dust: return ',';
                case TileType.WallTorch: return '!';
                case TileType.Current: return CurrentGlyphs[t.Param & 3];
                case TileType.Crumbling: return 'x';
                case TileType.Barrier: return t.Param == 0 ? '|' : '=';
                case TileType.Switch: return '$';
                case TileType.FireJet: return (char)('0' + t.Param);
            }
            return highlight != null && highlight.Contains(c) ? '*' : '.';
        }
    }

    public sealed class LevelGenerationException : Exception
    {
        public LevelGenerationException(string message) : base(message) { }
    }
}
