using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Procedural tomb generator. Generate-and-test: each attempt builds a candidate tomb with a deterministic RNG,
    /// then the exact <see cref="Solver"/> and <see cref="LevelValidator"/> check it against the spec. The first
    /// attempt that passes is the level, so the same LevelId always yields the same tomb for every player.
    /// </summary>
    public static class LevelGenerator
    {
        public const int MaxAttempts = 4000;

        /// <summary>
        /// Generates a campaign level. Uses the baked attempt index when available (one attempt instead of up to
        /// thousands on a phone); the result is identical to a full search as long as the table is current, which
        /// the test suite verifies.
        /// </summary>
        public static Level Generate(LevelId id)
        {
            var spec = DifficultyTable.Spec(id);
            ulong seed = DifficultyTable.Seed(id);
            if (LevelAttemptTable.TryGet(id, out int baked))
            {
                var level = TryAttempt(spec, seed, baked, out _);
                if (level != null) return level;
            }
            return Generate(spec, seed);
        }

        /// <summary>Full deterministic search from attempt 0 (ignores the baked table).</summary>
        public static Level GenerateFromScratch(LevelId id) => Generate(DifficultyTable.Spec(id), DifficultyTable.Seed(id));

        public static Level Generate(LevelSpec spec, ulong seed)
        {
            var failures = new Dictionary<string, int>();
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var level = TryAttempt(spec, seed, attempt, out string failure);
                if (level != null) return level;
                string bucket = failure.Split(':')[0];
                failures[bucket] = failures.TryGetValue(bucket, out int n) ? n + 1 : 1;
            }

            var summary = new List<string>();
            foreach (var kv in failures) summary.Add($"{kv.Key} x{kv.Value}");
            throw new LevelGenerationException($"Level {spec.Id}: no valid tomb after {MaxAttempts} attempts ({string.Join(", ", summary)}). Spec: {spec}");
        }

        static Level TryAttempt(LevelSpec spec, ulong seed, int attempt, out string failure)
        {
            var rng = new Pcg32(Pcg32.Hash(seed, (ulong)attempt));
            var level = new Builder(spec, rng).Build(out failure);
            if (level == null) return null;
            level.Id = spec.Id;
            level.Seed = (int)(seed & 0x7FFFFFFF);
            level.Attempt = attempt;
            var solution = Solver.Solve(level);
            failure = LevelValidator.Validate(level, spec, solution);
            if (failure != null) return null;
            level.Solution = solution;
            return level;
        }
        sealed class Builder
        {
            readonly LevelSpec _spec;
            readonly Pcg32 _rng;
            readonly Level _level;
            readonly bool[] _reserved;
            readonly List<Cell> _pois = new List<Cell>();
            readonly List<Cell> _traps = new List<Cell>();
            int _channels;

            public Builder(LevelSpec spec, Pcg32 rng)
            {
                _spec = spec;
                _rng = rng;
                _level = new Level(spec.Width, spec.Height, spec.Floors) { MaxHp = spec.MaxHp, Spec = spec };
                _reserved = new bool[_level.CellCount];
            }

            public Level Build(out string failure)
            {
                if (_level.CellCount >= 1 << 16) { failure = "too-big: tomb exceeds solver packing"; return null; }

                for (int f = 0; f < _spec.Floors; f++)
                {
                    CarveMaze(f);
                    CarveRooms(f);
                    Braid(f);
                }

                for (int f = 0; f + 1 < _spec.Floors; f++)
                    for (int k = 0; k < Math.Max(1, _spec.LaddersPerLink); k++)
                        if (!PlaceLadder(f)) { failure = "ladder: no spot"; return null; }

                if (!PlaceStart()) { failure = "start: no spot"; return null; }
                if (!PlaceExit(out var path)) { failure = "exit: no cell in distance window"; return null; }

                if (!PlaceRequiredDoors(path, out failure)) return null;
                for (int k = 0; k < _spec.DecoyDoors; k++)
                    if (!PlaceDecoyDoor(path)) { failure = "decoy: no spot"; return null; }

                // Optional shortcuts come after the mandatory chain so they never steal its space.
                for (int k = 0; k < _spec.BreakableFloors; k++)
                    if (!PlaceBreakable()) { failure = "breakable: no spot"; return null; }

                // Refresh the path: traps should sit on the real route, which now includes button detours.
                var route = ShortestPath(_level.Start, _level.Exit, (1 << _channels) - 1);
                if (route == null) { failure = "route: lost after doors"; return null; }

                int spikesOnPath = (_spec.SpikeTraps + 1) / 2;
                for (int k = 0; k < _spec.SpikeTraps; k++)
                    if (!PlaceTrap(TrapKind.Spikes, k < spikesOnPath ? route : null)) { failure = "spikes: no spot"; return null; }
                for (int k = 0; k < _spec.DarknessTraps; k++)
                    if (!PlaceTrap(TrapKind.Darkness, k % 2 == 0 ? route : null)) { failure = "darkness: no spot"; return null; }

                foreach (var kind in _spec.Teleporters)
                    if (!PlaceTeleporterPair(kind)) { failure = "teleporter: no spot"; return null; }

                if (_channels > 16 || _traps.Count > 16) { failure = "too-many: channels or traps exceed 16"; return null; }
                _level.ChannelCount = _channels;
                _level.TrapCount = _traps.Count;
                failure = null;
                return _level;
            }

            // ------------------------------------------------------------------ terrain

            void SetFloor(int f, int x, int y) => _level[new Cell(f, x, y)] = Tile.Floor;
            bool IsFloorType(Cell c) => _level.InBounds(c) && _level[c].Type == TileType.Floor;

            void CarveMaze(int f)
            {
                int cw = _spec.CellsX, ch = _spec.CellsY;
                var visited = new bool[cw * ch];
                var active = new List<int>();
                int first = _rng.Range(0, cw * ch);
                visited[first] = true;
                active.Add(first);
                SetFloor(f, first % cw * 2 + 1, first / cw * 2 + 1);
                var options = new List<Dir>(4);

                while (active.Count > 0)
                {
                    int ai = _rng.Chance(_spec.Windiness) ? active.Count - 1 : _rng.Range(0, active.Count);
                    int cur = active[ai];
                    int cx = cur % cw, cy = cur / cw;
                    options.Clear();
                    foreach (var d in DirExt.All)
                    {
                        int nx = cx + d.Dx(), ny = cy + d.Dy();
                        if (nx >= 0 && ny >= 0 && nx < cw && ny < ch && !visited[ny * cw + nx]) options.Add(d);
                    }
                    if (options.Count == 0) { active.RemoveAt(ai); continue; }

                    var dir = _rng.Pick(options);
                    int ncx = cx + dir.Dx(), ncy = cy + dir.Dy();
                    visited[ncy * cw + ncx] = true;
                    active.Add(ncy * cw + ncx);
                    SetFloor(f, cx * 2 + 1 + dir.Dx(), cy * 2 + 1 + dir.Dy());
                    SetFloor(f, ncx * 2 + 1, ncy * 2 + 1);
                }
            }

            void CarveRooms(int f)
            {
                for (int r = 0; r < _spec.Rooms; r++)
                {
                    int w = _rng.Range(1, 3), h = _rng.Range(1, 3);
                    if (w == 1 && h == 1) w = 2;
                    int cx = _rng.Range(0, _spec.CellsX - w + 1), cy = _rng.Range(0, _spec.CellsY - h + 1);
                    for (int x = cx * 2 + 1; x <= (cx + w - 1) * 2 + 1; x++)
                        for (int y = cy * 2 + 1; y <= (cy + h - 1) * 2 + 1; y++)
                            SetFloor(f, x, y);
                }
            }

            void Braid(int f)
            {
                for (int y = 1; y < _level.Height; y += 2)
                    for (int x = 1; x < _level.Width; x += 2)
                    {
                        var c = new Cell(f, x, y);
                        if (Degree(c) != 1 || !_rng.Chance(_spec.LoopChance)) continue;
                        var dirs = new List<Dir>(DirExt.All);
                        _rng.Shuffle(dirs);
                        foreach (var d in dirs)
                        {
                            var wall = c.Step(d);
                            var beyond = wall.Step(d);
                            if (!_level.InBounds(beyond) || beyond.X == 0 || beyond.Y == 0) continue;
                            if (_level[wall].Type == TileType.Wall && IsFloorType(beyond))
                            {
                                _level[wall] = Tile.Floor;
                                break;
                            }
                        }
                    }
            }

            int Degree(Cell c)
            {
                int n = 0;
                foreach (var d in DirExt.All) if (_level.Get(c.Step(d)).Type != TileType.Wall) n++;
                return n;
            }

            // ------------------------------------------------------------------ placement helpers

            bool IsFree(Cell c) => IsFloorType(c) && !_reserved[_level.IndexOf(c)] && c != _level.Start;

            bool FarFromPois(Cell c, int spacing)
            {
                foreach (var p in _pois)
                    if (p.Floor == c.Floor && Math.Abs(p.X - c.X) + Math.Abs(p.Y - c.Y) < spacing) return false;
                return true;
            }

            void Reserve(Cell c, bool poi = true)
            {
                _reserved[_level.IndexOf(c)] = true;
                // Keep the 4 neighbours plain so mechanics never stack on top of each other.
                foreach (var d in DirExt.All)
                    if (_level.InBounds(c.Step(d))) _reserved[_level.IndexOf(c.Step(d))] = true;
                if (poi) _pois.Add(c);
            }

            /// <summary>Candidate tiles on a floor, dead ends first (shuffled within each group).</summary>
            List<Cell> Candidates(int floor, Func<Cell, bool> filter, bool deadEndsFirst = true)
            {
                var dead = new List<Cell>();
                var other = new List<Cell>();
                for (int y = 1; y < _level.Height - 1; y++)
                    for (int x = 1; x < _level.Width - 1; x++)
                    {
                        var c = new Cell(floor, x, y);
                        if (!IsFree(c) || !filter(c)) continue;
                        if (deadEndsFirst && Degree(c) == 1) dead.Add(c); else other.Add(c);
                    }
                _rng.Shuffle(dead);
                _rng.Shuffle(other);
                dead.AddRange(other);
                return dead;
            }

            // ------------------------------------------------------------------ floors

            /// <summary>Max moves spent crossing one floor, so stacking floors cannot blow the par window.</summary>
            int LegBudget => Math.Max(5, (_spec.MaxMoves - 4 * _spec.RequiredButtons) / _spec.Floors);

            /// <summary>Where the player arrives on floor f (the LadderDown placed by the previous link).</summary>
            Cell? _arrival;

            bool PlaceLadder(int f)
            {
                int[] fromArrival = _arrival.HasValue ? Distances(_arrival.Value, 0, out _) : null;
                var list = Candidates(f, c =>
                {
                    if (!IsFree(c.WithFloor(f + 1)) || !FarFromPois(c, _spec.MinPoiSpacing) || !FarFromPois(c.WithFloor(f + 1), _spec.MinPoiSpacing))
                        return false;
                    if (fromArrival == null) return true;
                    int d = fromArrival[_level.IndexOf(c)];
                    return d >= 3 && d <= LegBudget;
                });
                if (list.Count == 0) return false;
                var lo = list[0];
                var hi = lo.WithFloor(f + 1);
                _level[lo] = new Tile { Type = TileType.LadderUp };
                _level[hi] = new Tile { Type = TileType.LadderDown };
                Reserve(lo);
                Reserve(hi);
                _arrival = hi;
                return true;
            }

            bool PlaceBreakable()
            {
                int f = _rng.Range(1, _spec.Floors);
                var list = Candidates(f, c => IsFree(c.WithFloor(f - 1)) && FarFromPois(c, _spec.MinPoiSpacing));
                if (list.Count == 0) return false;
                var c0 = list[0];
                _level[c0] = new Tile { Type = TileType.BreakableFloor };
                Reserve(c0);
                Reserve(c0.WithFloor(f - 1), poi: false); // landing spot stays plain floor
                return true;
            }

            // ------------------------------------------------------------------ start / exit

            bool PlaceStart()
            {
                // With several floors, start within one leg of the first ladder so the climb fits the par window.
                Cell? firstLadder = null;
                if (_spec.Floors > 1)
                    foreach (var c in _level.AllCells())
                        if (c.Floor == 0 && _level[c].Type == TileType.LadderUp) { firstLadder = c; break; }
                int[] fromLadder = firstLadder.HasValue ? Distances(firstLadder.Value, 0, out _) : null;

                var list = Candidates(0, c =>
                {
                    if (!FarFromPois(c, _spec.MinPoiSpacing)) return false;
                    if (fromLadder == null) return true;
                    int d = fromLadder[_level.IndexOf(c)];
                    return d >= 3 && d <= LegBudget;
                });
                if (list.Count == 0) return false;
                _level.Start = list[0];
                Reserve(list[0], poi: false);
                return true;
            }

            bool PlaceExit(out List<Cell> path)
            {
                path = null;
                int r = _spec.RequiredButtons;
                // Each mandatory button costs a detour (there and back): keep ~4 moves of budget per button.
                int hi = _spec.MaxMoves - 4 * r;
                // The route must be long enough to host every mandatory door with the required spacing.
                int lo = Math.Max(Math.Max(4, _spec.MinMoves - 4 * r), (r + 1) * (_spec.MinPoiSpacing - 1));
                lo = Math.Min(lo, hi);
                var dist = Distances(_level.Start, 0, out var parent);
                int exitFloor = _spec.Floors - 1;

                var list = Candidates(exitFloor, c =>
                {
                    int d = dist[_level.IndexOf(c)];
                    return d >= lo && d <= hi && FarFromPois(c, _spec.MinPoiSpacing);
                });
                if (list.Count == 0) return false;

                var exit = list[0];
                _level[exit] = new Tile { Type = TileType.Exit };
                _level.Exit = exit;
                Reserve(exit);
                path = BuildPath(parent, exit);
                return true;
            }

            // ------------------------------------------------------------------ doors & buttons

            bool PlaceRequiredDoors(List<Cell> path, out string failure)
            {
                failure = null;
                int r = _spec.RequiredButtons;
                for (int k = 0; k < r; k++)
                {
                    int openMask = (1 << _channels) - 1;
                    int target = path.Count * (k + 1) / (r + 1);
                    Cell door = default;
                    bool found = false;

                    // Search outward from the ideal position along the path for a corridor tile that cuts the tomb.
                    int nFree = 0, nDeg = 0, nFar = 0, nCut = 0;
                    for (int off = 0; off < path.Count && !found; off++)
                    {
                        foreach (int idx in new[] { target + off, target - off })
                        {
                            if (idx < 2 || idx >= path.Count - 1) continue;
                            var c = path[idx];
                            if (!IsFree(c)) continue; nFree++;
                            if (Degree(c) != 2) continue; nDeg++;
                            if (!FarFromPois(c, _spec.MinPoiSpacing - 1)) continue; nFar++;
                            if (Reachable(_level.Start, _level.Exit, openMask, c)) continue; nCut++;
                            door = c;
                            found = true;
                            break;
                        }
                    }
                    if (!found) { failure = $"door: no cut on path k{k} len{path.Count} free{nFree} deg{nDeg} far{nFar}"; return false; }

                    int channel = _channels++;
                    _level[door] = new Tile { Type = TileType.Door, Channel = (byte)channel };
                    Reserve(door);

                    // Button: reachable with the doors placed so far. Prefer a short side detour (a real choice for
                    // a blind player); fall back to a tile on the main route itself, which still costs an interaction.
                    var pathSet = new HashSet<Cell>(path);
                    var region = Distances(_level.Start, openMask, out _);
                    var detour = MultiSourceDistances(path, openMask);
                    int budget = _spec.MaxMoves - path.Count;
                    int maxDetour = Math.Max(2, budget / (2 * (r - k)));
                    bool Fits(Cell c, int minDetour)
                    {
                        int i = _level.IndexOf(c);
                        return region[i] >= 0 && detour[i] >= minDetour && detour[i] <= maxDetour && FarFromPois(c, _spec.MinPoiSpacing);
                    }
                    var list = new List<Cell>();
                    for (int minDetour = 1; minDetour >= 0 && list.Count == 0; minDetour--)
                        for (int df = 0; df < _spec.Floors && list.Count == 0; df++)
                        {
                            int f = (door.Floor + df) % _spec.Floors; // the door's floor first
                            int md = minDetour;
                            list = Candidates(f, c => Fits(c, md) && (md > 0 || Degree(c) == 2));
                        }
                    if (list.Count == 0) { failure = "button: no reachable spot"; return false; }
                    _level[list[0]] = new Tile { Type = TileType.Button, Channel = (byte)channel };
                    Reserve(list[0]);

                    // Path now must go through the door: recompute with that door open.
                    var newPath = ShortestPath(_level.Start, _level.Exit, (1 << _channels) - 1);
                    if (newPath == null) { failure = "door: path lost"; return false; }
                    path = newPath;
                }
                return true;
            }

            bool PlaceDecoyDoor(List<Cell> path)
            {
                var onPath = new HashSet<Cell>(path);
                int f = _rng.Range(0, _spec.Floors);
                int all = (1 << _channels) - 1;
                var reachAll = Distances(_level.Start, all, out _);
                var doors = Candidates(f, c => !onPath.Contains(c) && Degree(c) == 2 && FarFromPois(c, _spec.MinPoiSpacing - 1)
                                               && reachAll[_level.IndexOf(c)] >= 0, deadEndsFirst: false);
                if (doors.Count == 0) return false;
                var door = doors[0];
                int channel = _channels++;
                _level[door] = new Tile { Type = TileType.Door, Channel = (byte)channel };
                Reserve(door);

                int open = ((1 << _channels) - 1) & ~(1 << channel);
                var reachOpen = Distances(_level.Start, open, out _);
                for (int bf = 0; bf < _spec.Floors; bf++)
                {
                    var buttons = Candidates((f + bf) % _spec.Floors, c => !onPath.Contains(c) && FarFromPois(c, _spec.MinPoiSpacing)
                                                                           && reachOpen[_level.IndexOf(c)] >= 0);
                    if (buttons.Count == 0) continue;
                    _level[buttons[0]] = new Tile { Type = TileType.Button, Channel = (byte)channel };
                    Reserve(buttons[0]);
                    return true;
                }
                return false;
            }

            // ------------------------------------------------------------------ traps

            bool PlaceTrap(TrapKind kind, List<Cell> onRoute)
            {
                IList<Cell> pool;
                if (onRoute != null)
                {
                    var l = new List<Cell>();
                    for (int i = 2; i < onRoute.Count - 1; i++)
                    {
                        var c = onRoute[i];
                        if (IsFree(c) && Degree(c) == 2 && FarFromTraps(c)) l.Add(c);
                    }
                    _rng.Shuffle(l);
                    pool = l;
                }
                else
                {
                    pool = Candidates(_rng.Range(0, _spec.Floors), c => Degree(c) == 2 && FarFromTraps(c), deadEndsFirst: false);
                }
                if (pool.Count == 0) return false;

                var t = pool[0];
                _level[t] = new Tile { Type = TileType.Trap, Trap = kind, TrapIndex = (byte)_traps.Count };
                _traps.Add(t);
                _reserved[_level.IndexOf(t)] = true;
                return true;
            }

            bool FarFromTraps(Cell c)
            {
                foreach (var t in _traps)
                    if (t.Floor == c.Floor && Math.Abs(t.X - c.X) + Math.Abs(t.Y - c.Y) < 3) return false;
                return true;
            }

            // ------------------------------------------------------------------ teleporters

            bool PlaceTeleporterPair(TeleporterKind kind)
            {
                int all = (1 << _channels) - 1;
                var reach = Distances(_level.Start, all, out _);
                int fa = _rng.Range(0, _spec.Floors);
                var aList = Candidates(fa, c => FarFromPois(c, _spec.MinPoiSpacing) && reach[_level.IndexOf(c)] >= 0);
                if (aList.Count == 0) return false;
                var a = aList[0];
                int minApart = Math.Max(6, (_spec.Width + _spec.Height) / 3);
                int fb = _rng.Range(0, _spec.Floors);
                var bList = Candidates(fb, c => c.Manhattan(a) >= minApart && FarFromPois(c, _spec.MinPoiSpacing));
                if (bList.Count == 0) return false;
                var b = bList[0];

                byte channel = 0;
                if (kind == TeleporterKind.Locked)
                {
                    channel = (byte)_channels++;
                    var levers = Candidates(_rng.Range(0, _spec.Floors), c => FarFromPois(c, _spec.MinPoiSpacing)
                                                                              && c.Manhattan(a) > 2 && c.Manhattan(b) > 2
                                                                              && reach[_level.IndexOf(c)] >= 0);
                    if (levers.Count == 0) return false;
                    _level[levers[0]] = new Tile { Type = TileType.Button, Channel = channel };
                    Reserve(levers[0]);
                }

                _level[a] = new Tile { Type = TileType.Teleporter, Teleporter = kind, Channel = channel };
                _level[b] = new Tile { Type = TileType.Teleporter, Teleporter = kind, Channel = channel };
                _level.LinkTeleporters(a, b);
                Reserve(a);
                Reserve(b);
                return true;
            }

            // ------------------------------------------------------------------ graph queries (rules based)

            /// <summary>BFS by moves using the real rules with a fixed set of open channels; -1 = unreachable.</summary>
            int[] Distances(Cell from, int pressed, out int[] parent, Cell? blocked = null)
            {
                var dist = new int[_level.CellCount];
                parent = new int[_level.CellCount];
                for (int i = 0; i < dist.Length; i++) { dist[i] = -1; parent[i] = -1; }
                var q = new Queue<Cell>();
                dist[_level.IndexOf(from)] = 0;
                q.Enqueue(from);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    int ci = _level.IndexOf(c);
                    if (_level[c].Type == TileType.Exit && c != from) continue;
                    var s = new RuleState { Position = c, Pressed = pressed, Disarmed = -1, Hp = 99 };
                    foreach (var d in DirExt.All)
                    {
                        if (blocked.HasValue && c.Step(d) == blocked.Value) continue;
                        var r = Rules.Step(_level, s, PlayerAction.Move(d));
                        if (r.Has(StepFlags.Blocked)) continue;
                        int ni = _level.IndexOf(r.State.Position);
                        if (dist[ni] >= 0) continue;
                        dist[ni] = dist[ci] + 1;
                        parent[ni] = ci;
                        q.Enqueue(r.State.Position);
                    }
                }
                return dist;
            }

            int[] MultiSourceDistances(List<Cell> sources, int pressed)
            {
                var dist = new int[_level.CellCount];
                for (int i = 0; i < dist.Length; i++) dist[i] = -1;
                var q = new Queue<Cell>();
                foreach (var s0 in sources)
                {
                    dist[_level.IndexOf(s0)] = 0;
                    q.Enqueue(s0);
                }
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    var s = new RuleState { Position = c, Pressed = pressed, Disarmed = -1, Hp = 99 };
                    foreach (var d in DirExt.All)
                    {
                        var r = Rules.Step(_level, s, PlayerAction.Move(d));
                        if (r.Has(StepFlags.Blocked)) continue;
                        int ni = _level.IndexOf(r.State.Position);
                        if (dist[ni] >= 0) continue;
                        dist[ni] = dist[_level.IndexOf(c)] + 1;
                        q.Enqueue(r.State.Position);
                    }
                }
                return dist;
            }

            bool Reachable(Cell from, Cell to, int pressed, Cell? blocked)
            {
                var dist = Distances(from, pressed, out _, blocked);
                return dist[_level.IndexOf(to)] >= 0;
            }

            List<Cell> ShortestPath(Cell from, Cell to, int pressed)
            {
                var dist = Distances(from, pressed, out var parent);
                return dist[_level.IndexOf(to)] < 0 ? null : BuildPath(parent, to);
            }

            List<Cell> BuildPath(int[] parent, Cell to)
            {
                var path = new List<Cell>();
                for (int i = _level.IndexOf(to); i >= 0; i = parent[i]) path.Add(_level.CellAt(i));
                path.Reverse();
                return path;
            }
        }
    }
}
