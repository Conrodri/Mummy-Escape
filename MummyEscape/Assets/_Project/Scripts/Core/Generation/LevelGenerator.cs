using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Procedural tomb generator. Generate-and-test: each attempt builds a candidate tomb with a deterministic RNG,
    /// then the exact <see cref="Solver"/> and <see cref="LevelValidator"/> check it against the spec. The first
    /// attempt that passes is the level, so a (level, variant) pair always yields the same tomb on every device.
    ///
    /// Construction, in short:
    ///  1. a perfect maze per floor (a tree: every tile of the route to the exit is a cut);
    ///  2. start, ladders and a far exit picked by distance along the tree;
    ///  3. gates cut the route in order: a door (its button at the end of a side dead end) or a wall (the only way on
    ///     is a teleporter at the end of a dead end, landing beyond the cut);
    ///  4. decoys, optional portals and breakable floors, never able to skip a gate;
    ///  5. braiding: every dead end that holds nothing gets a wall knocked out (inside its region only, so gates stay
    ///     mandatory). Remaining dead ends always mean something: a button, a portal, a ladder, the exit;
    ///  6. dust + wall torches, then traps, on the route.
    /// </summary>
    public static class LevelGenerator
    {
        public const int MaxAttempts = 3000;

        /// <summary>Generates maze number <paramref name="variant"/> of a campaign level (a new variant per run).</summary>
        public static Level Generate(LevelId id, int variant = 0)
        {
            var level = Generate(DifficultyTable.Spec(id), DifficultyTable.Seed(id, variant));
            level.Variant = variant;
            return level;
        }

        public static Level Generate(LevelSpec spec, ulong seed) => Generate(spec, seed, null);

        /// <summary>Full deterministic search; <paramref name="failures"/> (optional) collects rejection reasons.</summary>
        public static Level Generate(LevelSpec spec, ulong seed, Dictionary<string, int> failures)
        {
            failures = failures ?? new Dictionary<string, int>();
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

        /// <summary>Builds one attempt without solving it (debug tooling: shows what a rejected attempt looked like).</summary>
        public static Level BuildUnchecked(LevelSpec spec, ulong seed, int attempt, out string failure)
        {
            var builder = new Builder(spec, new Pcg32(Pcg32.Hash(seed, (ulong)attempt)));
            builder.Build(out failure);
            return builder.Partial;
        }
        public static Level TryAttempt(LevelSpec spec, ulong seed, int attempt, out string failure)
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
            int _decoyMask;
            int _crumbling;
            readonly List<Cell> _hazards = new List<Cell>();
            /// <summary>Laser gates placed: barrier, switch and channel (blue barriers are added behind them later).</summary>
            readonly List<(Cell barrier, Cell toggle, int channel)> _lasers = new List<(Cell, Cell, int)>();

            // Gate bookkeeping: channel to open each gate (-1 = none) and which gate each portal pad belongs to.
            readonly List<int> _gateChannels = new List<int>();
            readonly Dictionary<Cell, int> _gatePads = new Dictionary<Cell, int>();
            Cell? _lastGate;
            /// <summary>Gates the player must have passed to stand on a tile (int.MaxValue = unreachable).</summary>
            int[] _stage;

            public Builder(LevelSpec spec, Pcg32 rng)
            {
                _spec = spec;
                _rng = rng;
                _level = new Level(spec.Width, spec.Height, spec.Floors) { MaxHp = spec.MaxHp, Spec = spec };
                _reserved = new bool[_level.CellCount];
            }

            int AllOpen => (1 << _channels) - 1;
            public Level Partial => _level;

            public Level Build(out string failure)
            {
                if (_level.CellCount > Solver.MaxCells) { failure = "too-big: tomb exceeds solver packing"; return null; }
                if (_spec.Gates.Count == 0) { failure = "spec: a level needs at least one gate"; return null; }

                for (int f = 0; f < _spec.Floors; f++) CarveMaze(f);
                if (!PlaceStartLaddersExit(out failure)) return null;

                for (int k = 0; k < _spec.Gates.Count; k++)
                    if (!PlaceGate(k, out failure)) return null;

                for (int k = 0; k < _spec.DecoyDoors; k++)
                    if (!PlaceDecoyDoor()) { failure = "decoy: no spot"; return null; }

                ComputeStages();
                foreach (var kind in _spec.Teleporters)
                    if (!PlaceOptionalTeleporter(kind)) { failure = "teleporter: no spot"; return null; }
                for (int k = 0; k < _spec.BreakableFloors; k++)
                    if (!PlaceBreakable()) { failure = "breakable: no spot"; return null; }

                BraidDeadEnds();

                AddLoops(_spec.ExtraLoops);

                var route = ShortestPath(_level.Start, _level.Exit, AllOpen);
                if (route == null) { failure = "route: lost after braiding"; return null; }
                // Cheap early reject: the route with every door open is a lower bound of the par.
                if (route.Count - 1 > _spec.MaxMoves) { failure = $"too-long: open route {route.Count - 1} > {_spec.MaxMoves}"; return null; }

                // Act mechanics, laid on the route once the layout is final.
                if (_spec.BlueBarriers)
                    foreach (var laser in _lasers) PlaceBlueBarrier(route, laser);
                for (int k = 0; k < _spec.Currents; k++)
                    if (!PlaceCurrent(route)) { failure = "current: no spot"; return null; }
                for (int k = 0; k < _spec.CrumblingTiles; k++)
                    if (!PlaceCrumbling(route)) { failure = "crumbling: no spot"; return null; }
                for (int k = 0; k < _spec.FireJets; k++)
                    if (!PlaceFireJet(route)) { failure = "fire: no spot"; return null; }

                for (int k = 0; k < _spec.DustPatches; k++)
                    if (!PlaceDust(route)) { failure = "dust: no spot"; return null; }

                int spikesOnPath = (_spec.SpikeTraps + 1) / 2;
                for (int k = 0; k < _spec.SpikeTraps; k++)
                    if (!PlaceTrap(TrapKind.Spikes, k < spikesOnPath ? route : null)) { failure = "spikes: no spot"; return null; }
                for (int k = 0; k < _spec.DarknessTraps; k++)
                    if (!PlaceTrap(TrapKind.Darkness, k % 2 == 0 ? route : null)) { failure = "darkness: no spot"; return null; }

                if (_channels > Solver.MaxChannels || _traps.Count > Solver.MaxTraps || _crumbling > Solver.MaxCrumbling)
                {
                    failure = "too-many: channels, traps or fragile slabs exceed the solver packing";
                    return null;
                }
                _level.ChannelCount = _channels;
                _level.TrapCount = _traps.Count;
                _level.DecoyChannels = _decoyMask;
                failure = null;
                return _level;
            }

            // ------------------------------------------------------------------ terrain

            void SetFloor(int f, int x, int y) => _level[new Cell(f, x, y)] = Tile.Floor;
            bool IsFloorType(Cell c) => _level.InBounds(c) && _level[c].Type == TileType.Floor;
            bool Walkable(Cell c) => _level.InBounds(c) && !_level[c].IsSolid;
            /// <summary>Maze cells sit on odd coordinates; the tiles between them are passages (or walls).</summary>
            static bool IsCell(Cell c) => (c.X & 1) == 1 && (c.Y & 1) == 1;
            bool IsInterior(Cell c) => c.X > 0 && c.Y > 0 && c.X < _level.Width - 1 && c.Y < _level.Height - 1;

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

            int Degree(Cell c)
            {
                int n = 0;
                foreach (var d in DirExt.All) if (Walkable(c.Step(d))) n++;
                return n;
            }

            /// <summary>A plain dead end: nothing to find there (the start excepted).</summary>
            bool IsPointlessDeadEnd(Cell c) => Degree(c) == 1 && _level[c].Type == TileType.Floor && c != _level.Start && !LevelValidator.IsMeaningfulFloor(_level, c);
            bool IsMeaningfulDeadEnd(Cell c) => Degree(c) == 1 && !IsPointlessDeadEnd(c);

            // ------------------------------------------------------------------ placement helpers

            bool IsFree(Cell c) => IsFloorType(c) && !_reserved[_level.IndexOf(c)] && c != _level.Start;

            bool FarFromPois(Cell c, int spacing)
            {
                foreach (var p in _pois)
                    if (p.Floor == c.Floor && Math.Abs(p.X - c.X) + Math.Abs(p.Y - c.Y) < spacing) return false;
                return true;
            }

            bool FarFromPois(Cell c) => FarFromPois(c, _spec.MinPoiSpacing);

            bool FarFrom(Cell[] others, Cell c)
            {
                if (others != null)
                    foreach (var o in others)
                        if (o.Floor == c.Floor && Math.Abs(o.X - c.X) + Math.Abs(o.Y - c.Y) < _spec.MinPoiSpacing) return false;
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

            List<Cell> CandidatesAllFloors(Func<Cell, bool> filter, bool deadEndsOnly)
            {
                var list = new List<Cell>();
                for (int f = 0; f < _spec.Floors; f++)
                    foreach (var c in Candidates(f, filter))
                        if (!deadEndsOnly || Degree(c) == 1) list.Add(c);
                _rng.Shuffle(list);
                return list;
            }

            // ------------------------------------------------------------------ start, ladders, exit

            /// <summary>Route length budget along the tree (gates add detours on top, braiding trims a little).</summary>
            void RouteWindow(out int lo, out int hi)
            {
                int g = _spec.Gates.Count;
                hi = _spec.MaxMoves - 5 * g;
                lo = Math.Max(_spec.MinMoves - 2 * g, (g + 1) * 4);
                lo = Math.Min(lo, hi);
            }

            bool PlaceStartLaddersExit(out string failure)
            {
                failure = null;
                RouteWindow(out int lo, out int hi);

                var starts = Candidates(0, c => IsCell(c));
                if (starts.Count == 0) { failure = "start: no spot"; return false; }
                _level.Start = starts[0]; // a dead end when possible: the mummy wakes up at the back of a corridor
                Reserve(_level.Start, poi: false);

                var from = _level.Start;
                int used = 0;
                for (int f = 0; f + 1 < _spec.Floors; f++)
                {
                    var dist = Distances(from, 0, out _);
                    int floorsLeft = _spec.Floors - f;
                    int legLo = Math.Max(4, (lo - used) / floorsLeft - 2);
                    int legHi = Math.Max(legLo + 2, (hi - used) / floorsLeft);
                    var list = Candidates(f, c =>
                    {
                        int d = dist[_level.IndexOf(c)];
                        var up = c.WithFloor(f + 1);
                        return IsCell(c) && d >= legLo && d <= legHi && IsFree(up) && FarFromPois(c) && FarFromPois(up);
                    });
                    if (list.Count == 0) { failure = "ladder: no spot"; return false; }
                    var lower = list[0];
                    var upper = lower.WithFloor(f + 1);
                    used += dist[_level.IndexOf(lower)];
                    _level[lower] = new Tile { Type = TileType.LadderUp };
                    _level[upper] = new Tile { Type = TileType.LadderDown };
                    Reserve(lower);
                    Reserve(upper);
                    from = upper;
                }

                var toExit = Distances(from, 0, out _);
                int exitFloor = _spec.Floors - 1;
                int exitLo = Math.Max(4, lo - used), exitHi = Math.Max(exitLo, hi - used);
                var exits = Candidates(exitFloor, c =>
                {
                    int d = toExit[_level.IndexOf(c)];
                    return IsCell(c) && d >= exitLo && d <= exitHi && c.Manhattan(_level.Start) >= _spec.MinExitDistance && FarFromPois(c);
                });
                if (exits.Count == 0) { failure = "exit: no far cell in distance window"; return false; }
                var exit = exits[0];
                _level[exit] = new Tile { Type = TileType.Exit };
                _level.Exit = exit;
                Reserve(exit);
                return true;
            }

            // ------------------------------------------------------------------ gates

            bool PlaceGate(int k, out string failure)
            {
                failure = null;
                var route = ShortestPath(_level.Start, _level.Exit, AllOpen);
                if (route == null) { failure = $"route: lost before gate {k}"; return false; }
                int from = _lastGate.HasValue ? route.IndexOf(_lastGate.Value) : 0;
                if (from < 0) { failure = $"route: previous gate off route at gate {k}"; return false; }

                // Cut tiles of the route, closest to the ideal spot first; the first one that also offers a good
                // button / portal spot wins.
                int remaining = _spec.Gates.Count - k;
                int target = from + (route.Count - from) / (remaining + 1) + _rng.Range(-1, 2);
                int maxDetour = Math.Max(3, (_spec.MaxMoves - route.Count) / (2 * remaining) + 1);
                // Tiles reachable while the previous gate is still shut: no spot for this gate may sit there.
                int[] earlier = null;
                if (k > 0)
                {
                    int prev = k - 1, ch = _gateChannels[prev];
                    earlier = Distances(_level.Start, ch >= 0 ? AllOpen & ~(1 << ch) : AllOpen, out _, null,
                                        pad => !_gatePads.TryGetValue(pad, out int j) || j != prev);
                }
                var tried = new HashSet<int>();
                int cuts = 0;
                for (int off = 0; off < route.Count; off++)
                    foreach (int idx in new[] { target + off, target - off })
                    {
                        if (!tried.Add(idx) || idx < from + 2 || idx >= route.Count - 2) continue;
                        var c = route[idx];
                        // A passage tile of the route (between two maze cells), in a plain corridor.
                        if (IsCell(c) || !IsFree(c) || Degree(c) != 2 || !FarFromPois(c, _spec.MinPoiSpacing - 1)) continue;
                        if (route[idx - 1].Floor != c.Floor || route[idx + 1].Floor != c.Floor) continue;
                        cuts++;
                        if (TryGateAt(k, route, from, idx, maxDetour, earlier)) return true;
                        if (cuts >= 16) break;
                    }
                failure = cuts == 0 ? $"gate: no cut on route k{k}" : $"gate: no button or portal spot k{k} ({_spec.Gates[k]})";
                return false;
            }

            bool TryGateAt(int k, List<Cell> route, int from, int cutIndex, int maxDetour, int[] earlier)
            {
                var gate = _spec.Gates[k];
                var cut = route[cutIndex];
                // Detours are measured from the stretch since the previous gate; spots must lie beyond that gate too.
                var before = route.GetRange(from, cutIndex - from);

                if (gate.Kind == GateKind.Door || gate.Kind == GateKind.Laser)
                {
                    bool laser = gate.Kind == GateKind.Laser;
                    int channel = _channels;
                    // A red barrier behaves like a door: open once its channel is on.
                    _level[cut] = new Tile { Type = laser ? TileType.Barrier : TileType.Door, Channel = (byte)channel };
                    // The new gate is still closed: its channel is not in AllOpen yet.
                    var button = PickSideSpot(before, AllOpen, maxDetour, cut, null, earlier);
                    if (!button.HasValue) { _level[cut] = Tile.Floor; return false; }
                    _channels++;
                    Reserve(cut);
                    _level[button.Value] = new Tile { Type = laser ? TileType.Switch : TileType.Button, Channel = (byte)channel };
                    Reserve(button.Value);
                    _gateChannels.Add(channel);
                    if (laser) _lasers.Add((cut, button.Value, channel));
                    _lastGate = cut;
                    return true;
                }

                // Portal gate: wall the route off; a teleporter at the end of a dead end is the only way on.
                _level[cut] = Tile.Wall;
                var a = PickSideSpot(before, AllOpen, maxDetour, cut, null, earlier);
                if (!a.HasValue) { _level[cut] = Tile.Floor; return false; }

                var reachA = Distances(_level.Start, AllOpen, out _);
                var beyond = route[cutIndex + 1];
                var fromCut = Distances(beyond, AllOpen, out _);
                var toExit = Distances(_level.Exit, AllOpen, out _);
                // The jump must not shrink the tomb: start -> pad + landing -> exit has to stay in the par window
                // (later gates still add a detour each).
                int later = _spec.Gates.Count - 1 - k;
                int lower = _spec.MinMoves + 2 - 3 * later, upper = _spec.MaxMoves - 2 - 3 * later;
                int toPad = reachA[_level.IndexOf(a.Value)];
                int apart = _spec.MinPoiSpacing + 2;
                Cell? b = null;
                foreach (int maxFromCut in new[] { 6, 12, 99 })
                {
                    int mfc = maxFromCut;
                    var list = CandidatesAllFloors(c =>
                    {
                        int i = _level.IndexOf(c);
                        int est = toPad + toExit[i];
                        return IsCell(c) && reachA[i] < 0 && fromCut[i] >= 2 && fromCut[i] <= mfc && toExit[i] >= 0
                               && est >= lower && est <= upper && c.Manhattan(a.Value) >= apart && FarFromPois(c);
                    }, deadEndsOnly: false);
                    if (list.Count > 0) { b = list[0]; break; }
                }
                if (!b.HasValue) { _level[cut] = Tile.Floor; return false; }

                Cell? leverSpot = null;
                if (gate.Portal == TeleporterKind.Locked)
                {
                    leverSpot = PickSideSpot(before, AllOpen, maxDetour, a.Value, new[] { b.Value }, earlier);
                    if (!leverSpot.HasValue) { _level[cut] = Tile.Floor; return false; }
                }

                Reserve(cut, poi: false);
                byte lever = 0;
                if (leverSpot.HasValue)
                {
                    lever = (byte)_channels++;
                    _level[leverSpot.Value] = new Tile { Type = TileType.Button, Channel = lever };
                    Reserve(leverSpot.Value);
                }
                _level[a.Value] = new Tile { Type = TileType.Teleporter, Teleporter = gate.Portal, Channel = lever };
                _level[b.Value] = new Tile { Type = TileType.Teleporter, Teleporter = gate.Portal, Channel = lever };
                _level.LinkTeleporters(a.Value, b.Value);
                Reserve(a.Value);
                Reserve(b.Value);
                _gatePads[a.Value] = k;
                _gatePads[b.Value] = k;
                _gateChannels.Add(leverSpot.HasValue ? lever : -1);
                _lastGate = b.Value;
                return true;
            }

            /// <summary>
            /// A spot for a button or a portal, reachable with <paramref name="pressed"/>, off the route: ideally the end
            /// of a dead end a few steps away from it (a real detour to remember), never right next to the gate.
            /// </summary>
            Cell? PickSideSpot(List<Cell> route, int pressed, int maxDetour, Cell awayFrom, Cell[] avoid, int[] earlier)
            {
                var reach = Distances(_level.Start, pressed, out _);
                var detour = MultiSourceDistances(route, pressed);
                bool Ok(Cell c, int minDetour)
                {
                    int i = _level.IndexOf(c);
                    return IsCell(c) && reach[i] >= 0 && (earlier == null || earlier[i] < 0) && detour[i] >= minDetour && detour[i] <= maxDetour
                           && c.Manhattan(awayFrom) >= _spec.MinPoiSpacing && FarFromPois(c) && FarFrom(avoid, c);
                }
                for (int minDetour = 3; minDetour >= 2; minDetour--)
                {
                    int md = minDetour;
                    var dead = CandidatesAllFloors(c => Ok(c, md), deadEndsOnly: true);
                    if (dead.Count > 0) return dead[0];
                }
                for (int minDetour = 2; minDetour >= 1; minDetour--)
                {
                    int md = minDetour;
                    var any = CandidatesAllFloors(c => Ok(c, md), deadEndsOnly: false);
                    if (any.Count > 0) return any[0];
                }
                return null;
            }

            // ------------------------------------------------------------------ stages

            void ComputeStages()
            {
                int g = _gateChannels.Count;
                _stage = new int[_level.CellCount];
                for (int i = 0; i < _stage.Length; i++) _stage[i] = int.MaxValue;
                int mask = _decoyMask;
                for (int k = 0; k <= g; k++)
                {
                    if (k > 0 && _gateChannels[k - 1] >= 0) mask |= 1 << _gateChannels[k - 1];
                    int passed = k;
                    var dist = Distances(_level.Start, mask, out _, null, pad => !_gatePads.TryGetValue(pad, out int j) || j < passed);
                    for (int i = 0; i < dist.Length; i++)
                        if (dist[i] >= 0 && _stage[i] > k) _stage[i] = k;
                }
            }

            int StageOf(Cell c) => _stage[_level.IndexOf(c)];

            // ------------------------------------------------------------------ optional content

            bool PlaceDecoyDoor()
            {
                var route = ShortestPath(_level.Start, _level.Exit, AllOpen);
                if (route == null) return false;
                var onRoute = new HashSet<Cell>(route);
                var reach = Distances(_level.Start, AllOpen, out _);
                int f = _rng.Range(0, _spec.Floors);
                for (int df = 0; df < _spec.Floors; df++)
                {
                    int floor = (f + df) % _spec.Floors;
                    var doors = Candidates(floor, c => !IsCell(c) && !onRoute.Contains(c) && Degree(c) == 2
                                                      && FarFromPois(c, _spec.MinPoiSpacing - 1) && reach[_level.IndexOf(c)] >= 0, deadEndsFirst: false);
                    foreach (var door in doors)
                    {
                        // The pocket behind the door must be empty of anything the route needs.
                        var cutOff = Distances(_level.Start, AllOpen, out _, door);
                        bool pocketOk = true;
                        int pocket = 0;
                        for (int i = 0; i < reach.Length && pocketOk; i++)
                        {
                            if (reach[i] < 0 || cutOff[i] >= 0 || i == _level.IndexOf(door)) continue;
                            pocket++;
                            var t = _level[_level.CellAt(i)];
                            if (t.Type != TileType.Floor) pocketOk = false;
                        }
                        if (!pocketOk || pocket < 2) continue;

                        int channel = _channels++;
                        _level[door] = new Tile { Type = TileType.Door, Channel = (byte)channel };
                        var reachClosed = Distances(_level.Start, AllOpen & ~(1 << channel), out _);
                        var buttons = CandidatesAllFloors(c => IsCell(c) && !onRoute.Contains(c) && reachClosed[_level.IndexOf(c)] >= 0
                                                               && FarFromPois(c) && c.Manhattan(door) >= _spec.MinPoiSpacing, deadEndsOnly: true);
                        if (buttons.Count == 0)
                        {
                            _level[door] = Tile.Floor;
                            _channels--;
                            continue;
                        }
                        Reserve(door);
                        _level[buttons[0]] = new Tile { Type = TileType.Button, Channel = (byte)channel };
                        Reserve(buttons[0]);
                        _decoyMask |= 1 << channel;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>An extra portal pair between two dead ends of the same stage: a shortcut or a lure, never a skip.</summary>
            bool PlaceOptionalTeleporter(TeleporterKind kind)
            {
                int minApart = Math.Max(6, (_spec.Width + _spec.Height) / 3);
                var aList = CandidatesAllFloors(c => IsCell(c) && StageOf(c) != int.MaxValue && FarFromPois(c), deadEndsOnly: true);
                foreach (var a in aList)
                {
                    int stage = StageOf(a);
                    var bList = CandidatesAllFloors(c => IsCell(c) && StageOf(c) == stage && c.Manhattan(a) >= minApart && FarFromPois(c) && c != a,
                                                    deadEndsOnly: true);
                    if (bList.Count == 0) continue;
                    var b = bList[0];
                    byte channel = 0;
                    if (kind == TeleporterKind.Locked)
                    {
                        var levers = CandidatesAllFloors(c => IsCell(c) && StageOf(c) == stage && FarFromPois(c)
                                                              && c.Manhattan(a) > 2 && c.Manhattan(b) > 2, deadEndsOnly: false);
                        if (levers.Count == 0) continue;
                        channel = (byte)_channels++;
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
                return false;
            }

            /// <summary>A cracked floor: falling through it must never land beyond a gate not yet passed.</summary>
            bool PlaceBreakable()
            {
                int f0 = _rng.Range(1, _spec.Floors);
                for (int df = 0; df < _spec.Floors - 1; df++)
                {
                    int f = 1 + (f0 - 1 + df) % (_spec.Floors - 1);
                    var list = Candidates(f, c =>
                    {
                        var land = c.WithFloor(f - 1);
                        return StageOf(c) != int.MaxValue && IsFree(land) && StageOf(land) <= StageOf(c) && FarFromPois(c);
                    }, deadEndsFirst: false);
                    if (list.Count == 0) continue;
                    var c0 = list[0];
                    _level[c0] = new Tile { Type = TileType.BreakableFloor };
                    Reserve(c0);
                    Reserve(c0.WithFloor(f - 1), poi: false); // landing spot stays plain floor
                    return true;
                }
                return false;
            }

            // ------------------------------------------------------------------ braiding

            /// <summary>Connected areas of one floor with every door closed: knocking walls inside one keeps gates mandatory.</summary>
            int[] FloorComponents()
            {
                var comp = new int[_level.CellCount];
                for (int i = 0; i < comp.Length; i++) comp[i] = -1;
                int next = 0;
                var q = new Queue<Cell>();
                for (int i = 0; i < comp.Length; i++)
                {
                    var c0 = _level.CellAt(i);
                    if (comp[i] >= 0 || !Walkable(c0) || _level[c0].IsGate) continue;
                    comp[i] = next;
                    q.Enqueue(c0);
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        foreach (var d in DirExt.All)
                        {
                            var n = c.Step(d);
                            if (!Walkable(n) || _level[n].IsGate) continue;
                            int ni = _level.IndexOf(n);
                            if (comp[ni] >= 0) continue;
                            comp[ni] = next;
                            q.Enqueue(n);
                        }
                    }
                    next++;
                }
                return comp;
            }

            /// <summary>
            /// No dead end may be pointless. Each one either becomes a loop (a wall knocked out towards a cell of its own
            /// area, picking the cell whose distance from the start is closest, so the par barely moves) or, failing the
            /// <see cref="LevelSpec.BraidChance"/> roll or when wedged between two areas a gate keeps apart, is filled back
            /// with rock up to its junction. New dead ends created by a fill are rolled again on the next pass.
            /// </summary>
            void BraidDeadEnds()
            {
                var comp = FloorComponents();
                var depth = Distances(_level.Start, AllOpen, out _);
                for (int pass = 0; pass < 12; pass++)
                {
                    var dead = new List<Cell>();
                    foreach (var c in _level.AllCells())
                        if (Walkable(c) && IsPointlessDeadEnd(c)) dead.Add(c);
                    if (dead.Count == 0) return;
                    _rng.Shuffle(dead);
                    foreach (var c in dead)
                    {
                        if (!IsPointlessDeadEnd(c)) continue;
                        // Long branches always loop (filling them would erase a whole wing); stubs roll the dice.
                        bool loop = ChainLength(c) > 4 || _rng.Chance(_spec.BraidChance);
                        if (loop) { TrimThenBraid(c, comp, depth); continue; }
                        FillChain(c);
                    }
                }
            }

            bool TryBraid(Cell c, int[] comp, int[] depth)
            {
                int ci = _level.IndexOf(c);
                Cell? best = null;
                int bestScore = int.MaxValue;
                foreach (var d in DirExt.All)
                {
                    var wall = c.Step(d);
                    var other = wall.Step(d);
                    if (!_level.InBounds(other) || !IsInterior(other) || _level[wall].Type != TileType.Wall) continue;
                    if (!Walkable(other) || comp[_level.IndexOf(other)] != comp[ci] || IsMeaningfulDeadEnd(other)) continue;
                    int dc = depth[ci], dn = depth[_level.IndexOf(other)];
                    int score = (dc < 0 || dn < 0 ? 50 : Math.Abs(dc - dn)) * 4 + _rng.Range(0, 4);
                    if (score < bestScore) { bestScore = score; best = wall; }
                }
                if (!best.HasValue) return false;
                _level[best.Value] = Tile.Floor;
                return true;
            }

            /// <summary>
            /// Loops the dead end; when it is wedged against another area, trims it tile by tile until a spot that can loop
            /// (or the junction) is reached, so only the part that has to go is turned to rock.
            /// </summary>
            void TrimThenBraid(Cell c, int[] comp, int[] depth)
            {
                while (Walkable(c) && IsPointlessDeadEnd(c))
                {
                    if (IsCell(c) && TryBraid(c, comp, depth)) return;
                    Cell next = c;
                    foreach (var d in DirExt.All) if (Walkable(c.Step(d))) next = c.Step(d);
                    if (_level[next].IsGate) return;
                    bool junction = Degree(next) >= 3;
                    _level[c] = Tile.Wall;
                    if (junction || _level[next].Type != TileType.Floor) return;
                    c = next;
                }
            }

            /// <summary>Tiles from a dead end back to the junction it hangs from.</summary>
            int ChainLength(Cell c)
            {
                int n = 0;
                var prev = c;
                while (n < 64)
                {
                    n++;
                    Cell next = c;
                    int degree = 0;
                    foreach (var d in DirExt.All)
                    {
                        var s = c.Step(d);
                        if (!Walkable(s)) continue;
                        degree++;
                        if (s != prev) next = s;
                    }
                    if ((degree > 2 && n > 1) || next == c || _level[next].Type != TileType.Floor) return n;
                    prev = c;
                    c = next;
                }
                return n;
            }

            /// <summary>Fills a pointless dead end with rock, back to the junction it hangs from.</summary>
            void FillChain(Cell c)
            {
                while (Walkable(c) && IsPointlessDeadEnd(c))
                {
                    Cell next = c;
                    foreach (var d in DirExt.All) if (Walkable(c.Step(d))) next = c.Step(d);
                    if (_level[next].IsGate) break; // a decoy door keeps its (empty) pocket
                    bool junction = Degree(next) >= 3;
                    _level[c] = Tile.Wall;
                    if (junction) break; // stop at the junction this branch hangs from (no cascade)
                    if (_level[next].Type != TileType.Floor) break;
                    c = next;
                }
            }


            /// <summary>Extra loops between cells at a similar distance from the start: equal-ish routes to tell apart.</summary>
            void AddLoops(int count)
            {
                if (count <= 0) return;
                var comp = FloorComponents();
                var depth = Distances(_level.Start, AllOpen, out _);
                var walls = new List<(Cell wall, Cell a, Cell b)>();
                foreach (var w in _level.AllCells())
                {
                    if (_level[w].Type != TileType.Wall || !IsInterior(w) || IsCell(w) || ((w.X & 1) == 0 && (w.Y & 1) == 0)) continue;
                    var dir = (w.X & 1) == 0 ? Dir.Right : Dir.Up;
                    Cell a = w.Step(dir.Opposite()), b = w.Step(dir);
                    if (!Walkable(a) || !Walkable(b)) continue;
                    int ia = _level.IndexOf(a), ib = _level.IndexOf(b);
                    if (comp[ia] != comp[ib] || depth[ia] < 0 || depth[ib] < 0) continue;
                    int diff = Math.Abs(depth[ia] - depth[ib]);
                    if (diff < 4 || diff > 8 || IsMeaningfulDeadEnd(a) || IsMeaningfulDeadEnd(b)) continue;
                    if (_reserved[_level.IndexOf(w)] && !IsFloorType(w)) continue;
                    walls.Add((w, a, b));
                }
                _rng.Shuffle(walls);
                for (int k = 0; k < walls.Count && count > 0; k++)
                {
                    var (w, a, b) = walls[k];
                    if (IsMeaningfulDeadEnd(a) || IsMeaningfulDeadEnd(b)) continue;
                    _level[w] = Tile.Floor;
                    count--;
                }
            }

            // ------------------------------------------------------------------ act mechanics

            /// <summary>A plain corridor tile of the route, away from other hazards: where act mechanics go.</summary>
            bool IsRouteCorridor(List<Cell> route, int i) =>
                i > 0 && i < route.Count - 1 && IsFree(route[i]) && Degree(route[i]) == 2 && FarFromTraps(route[i])
                && route[i - 1].Floor == route[i].Floor && route[i + 1].Floor == route[i].Floor
                && route[i - 1].Manhattan(route[i]) == 1 && route[i + 1].Manhattan(route[i]) == 1;

            static Dir DirTo(Cell a, Cell b) => b.X > a.X ? Dir.Right : b.X < a.X ? Dir.Left : b.Y > a.Y ? Dir.Up : Dir.Down;

            /// <summary>
            /// Flipping a laser switch raises a blue barrier on the way the player came: the corridor behind closes
            /// until the switch is flipped back. Optional: skipped when the route offers no clean spot.
            /// </summary>
            void PlaceBlueBarrier(List<Cell> route, (Cell barrier, Cell toggle, int channel) laser)
            {
                int cut = route.IndexOf(laser.barrier);
                if (cut < 0) return;
                // Where the switch branch leaves the route: blue goes before that junction, so it seals the way back.
                var fromSwitch = Distances(laser.toggle, AllOpen, out _);
                int junction = -1, best = int.MaxValue;
                for (int i = 0; i < cut; i++)
                {
                    int d = fromSwitch[_level.IndexOf(route[i])];
                    if (d >= 0 && d < best) { best = d; junction = i; }
                }
                var spots = new List<int>();
                for (int i = 2; i < junction - 1; i++)
                    if (IsRouteCorridor(route, i) && !IsCell(route[i]) && FarFromPois(route[i], _spec.MinPoiSpacing - 1)) spots.Add(i);
                if (spots.Count == 0) return;
                // The closest spot to the junction: the player sees it close right behind them.
                var c = route[spots[spots.Count - 1]];
                _level[c] = new Tile { Type = TileType.Barrier, Channel = (byte)laser.channel, Param = 1 };
                Reserve(c);
            }

            /// <summary>A stream of 2-4 current tiles along the route, flowing towards the exit: a one-way passage.</summary>
            bool PlaceCurrent(List<Cell> route)
            {
                var starts = new List<int>();
                for (int i = 2; i < route.Count - 4; i++) starts.Add(i);
                _rng.Shuffle(starts);
                foreach (int i in starts)
                {
                    int len = _rng.Range(2, 5);
                    if (i + len >= route.Count - 1 || _level[route[i - 1]].Type == TileType.Current) continue;
                    bool ok = true;
                    for (int j = i; j < i + len && ok; j++) ok = IsRouteCorridor(route, j);
                    var end = route[i + len];
                    if (!ok || end.Floor != route[i].Floor || !(IsFloorType(end) || _level[end].Type == TileType.Exit)) continue;
                    for (int j = i; j < i + len; j++)
                    {
                        _level[route[j]] = new Tile { Type = TileType.Current, Param = (byte)DirTo(route[j], route[j + 1]) };
                        _reserved[_level.IndexOf(route[j])] = true;
                        _hazards.Add(route[j]);
                    }
                    _reserved[_level.IndexOf(end)] = true; // still ground where the stream lets go
                    return true;
                }
                return false;
            }

            /// <summary>A fragile slab on the route: the optimal route crosses it once, a careless player maybe twice.</summary>
            bool PlaceCrumbling(List<Cell> route)
            {
                var spots = new List<int>();
                for (int i = 2; i < route.Count - 2; i++) if (IsRouteCorridor(route, i)) spots.Add(i);
                if (spots.Count == 0) return false;
                var c = route[_rng.Pick(spots)];
                _level[c] = new Tile { Type = TileType.Crumbling, Param = (byte)_crumbling++ };
                _reserved[_level.IndexOf(c)] = true;
                _hazards.Add(c);
                return true;
            }

            /// <summary>A flame jet on the route with a random beat: the route has to be walked in rhythm.</summary>
            bool PlaceFireJet(List<Cell> route)
            {
                var spots = new List<int>();
                for (int i = 2; i < route.Count - 1; i++) if (IsRouteCorridor(route, i)) spots.Add(i);
                if (spots.Count == 0) return false;
                var c = route[_rng.Pick(spots)];
                _level[c] = new Tile { Type = TileType.FireJet, Param = (byte)_rng.Range(0, Rules.FlameCycle) };
                _reserved[_level.IndexOf(c)] = true;
                _hazards.Add(c);
                return true;
            }

            // ------------------------------------------------------------------ torch

            /// <summary>Dust on the route smothers the torch; a wall torch a few steps further relights it.</summary>
            bool PlaceDust(List<Cell> route)
            {
                var order = new List<int>();
                for (int i = 3; i < route.Count - 6; i++) order.Add(i);
                _rng.Shuffle(order);
                foreach (int i in order)
                {
                    var dust = route[i];
                    if (!IsFree(dust) || Degree(dust) != 2 || Rules.NextToWallTorch(_level, dust) || !FarFromTraps(dust)) continue;
                    for (int j = i + 3; j <= Math.Min(i + 7, route.Count - 2); j++)
                    {
                        // The stretch between the dust and the sconce must be a walk on one floor (no jump).
                        if (route[j].Floor != dust.Floor || route[j].Manhattan(route[j - 1]) != 1) break;
                        foreach (var d in DirExt.All)
                        {
                            var w = route[j].Step(d);
                            if (!_level.InBounds(w) || _level[w].Type != TileType.Wall) continue;
                            bool early = false;
                            foreach (var d2 in DirExt.All)
                            {
                                var n = w.Step(d2);
                                for (int m = i; m < j && !early; m++) if (route[m] == n) early = true;
                                if (_level.Get(n).Type == TileType.Dust) early = true;
                            }
                            if (early) continue;
                            _level[dust] = new Tile { Type = TileType.Dust };
                            _reserved[_level.IndexOf(dust)] = true;
                            _level[w] = new Tile { Type = TileType.WallTorch };
                            return true;
                        }
                    }
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
                foreach (var h in _hazards)
                    if (h.Floor == c.Floor && Math.Abs(h.X - c.X) + Math.Abs(h.Y - c.Y) < 2) return false;
                foreach (var d in DirExt.All)
                    if (_level.Get(c.Step(d)).Type == TileType.Dust) return false;
                return true;
            }

            // ------------------------------------------------------------------ graph queries (rules based)

            /// <summary>
            /// BFS by moves using the real rules with a fixed set of open channels; -1 = unreachable.
            /// <paramref name="portalAllowed"/> (optional) can refuse transport through some pads.
            /// </summary>
            int[] Distances(Cell from, int pressed, out int[] parent, Cell? blocked = null, Func<Cell, bool> portalAllowed = null)
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
                        var pos = r.State.Position;
                        if (portalAllowed != null && r.Has(StepFlags.Teleported) && !portalAllowed(r.SteppedOn)) pos = r.SteppedOn;
                        int ni = _level.IndexOf(pos);
                        if (dist[ni] >= 0) continue;
                        dist[ni] = dist[ci] + 1;
                        parent[ni] = ci;
                        q.Enqueue(pos);
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
                    if (dist[_level.IndexOf(s0)] >= 0) continue;
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
