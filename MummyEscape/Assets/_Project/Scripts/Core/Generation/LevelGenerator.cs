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
    ///  3. gates form a chain built back from the exit: the last one cuts the way out, the one before locks the way
    ///     to its button or portal, and so on, with each button placed where reaching it means backtracking;
    ///  4. nothing optional: no decoy door, no lure portal: every element serves the walk;
    ///  5. braiding: every dead end that holds nothing gets a wall knocked out (inside its region only, so gates stay
    ///     mandatory). Remaining dead ends always mean something: a button, a portal, a ladder, the exit;
    ///     Wings the walk never comes near go back to rock, short loops beside it stay (short and long ways);
    ///  6. dust + wall torches, then traps, on the walk.
    /// </summary>
    public static class LevelGenerator
    {
        public const int MaxAttempts = 3000;
        /// <summary>Fallback searches with a wider par window when the spec itself finds no tomb.</summary>
        public const int MaxRelaxSteps = 2, RelaxMoves = 3;

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
            // Safety net for the rare unlucky seed: the same search again with a little more room for the par
            // (still deterministic, so every device lands on the same tomb). Short routes keep 2 minutes per level.
            for (int relax = 0; relax <= MaxRelaxSteps; relax++)
            {
                var s = relax == 0 ? spec : spec.WithMoreMoves(relax * RelaxMoves);
                int attempts = relax == 0 ? MaxAttempts : MaxAttempts / 2;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    var level = TryAttempt(s, seed, attempt, out string failure);
                    if (level != null) return level;
                    string bucket = failure.Split(':')[0];
                    failures[bucket] = failures.TryGetValue(bucket, out int n) ? n + 1 : 1;
                }
            }

            var summary = new List<string>();
            foreach (var kv in failures) summary.Add($"{kv.Key} x{kv.Value}");
            throw new LevelGenerationException($"Level {spec.Id}: no valid tomb after {MaxAttempts + MaxRelaxSteps * (MaxAttempts / 2)} attempts ({string.Join(", ", summary)}). Spec: {spec}");
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
            int _crumbling;
            readonly List<Cell> _hazards = new List<Cell>();
            /// <summary>Laser gates placed: barrier, switch and channel (blue barriers are added behind them later).</summary>
            readonly List<(Cell barrier, Cell toggle, int channel)> _lasers = new List<(Cell, Cell, int)>();

            // Which gate each portal pad belongs to.
            readonly Dictionary<Cell, int> _gatePads = new Dictionary<Cell, int>();

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

                if (!PlaceGates(out failure)) return null;

                BraidDeadEnds();
                var walk = PlannedWalk();
                if (walk == null) { failure = "route: chain lost after braiding"; return null; }
                PruneWings(walk);

                AddLoops(_spec.ExtraLoops);
                AddBypasses(_spec.ExtraLoops + 3);

                // Mechanics and traps go on the walk the chain asks for (backtracking included).
                var route = PlannedWalk();
                if (route == null) { failure = "route: lost after braiding"; return null; }
                // New loops can shift the walk: drop any pocket it no longer needs.
                PruneWings(route);
                // Cheap early reject: the planned walk is close to the par (each portal pad jump counts as no move).
                int planned = route.Count - 1 - _spec.RequiredPortals;
                if (planned > _spec.MaxMoves) { failure = $"too-long: planned walk {planned} > {_spec.MaxMoves}"; return null; }

                // Act mechanics, laid on the route once the layout is final. Each one is kept only where no sequence of
                // moves can wall the player in (see LevelValidator.CheckNoDeadLock).
                if (_spec.BlueBarriers)
                    foreach (var laser in _lasers) PlaceBlueBarrier(route, laser);
                for (int k = 0; k < _spec.Currents; k++)
                    if (!PlaceCurrent(route)) { failure = "current: no spot that cannot wall the player in"; return null; }
                for (int k = 0; k < _spec.CrumblingTiles; k++)
                    if (!PlaceCrumbling(route)) { failure = "crumbling: no spot that cannot wall the player in"; return null; }
                for (int k = 0; k < _spec.FireJets; k++)
                    if (!PlaceFireJet(route)) { failure = "fire: no spot"; return null; }

                for (int k = 0; k < _spec.DustPatches; k++)
                    if (!PlaceDust(route)) { failure = "dust: no spot"; return null; }

                // Traps sit on the route only: something to remember and get past, never a pointless side hazard.
                for (int k = 0; k < _spec.SpikeTraps; k++)
                    if (!PlaceTrap(TrapKind.Spikes, route)) { failure = "spikes: no spot"; return null; }
                for (int k = 0; k < _spec.DarknessTraps; k++)
                    if (!PlaceTrap(TrapKind.Darkness, route)) { failure = "darkness: no spot"; return null; }

                if (_channels > Solver.MaxChannels || _traps.Count > Solver.MaxTraps || _crumbling > Solver.MaxCrumbling)
                {
                    failure = "too-many: channels, traps or fragile slabs exceed the solver packing";
                    return null;
                }
                _level.ChannelCount = _channels;
                _level.TrapCount = _traps.Count;
                failure = null;
                // Pruned wings leave rock around the tomb: frame only the ground.
                return _level.CroppedToGround();
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
                // The way out is only part of the walk: the gate chain adds a backtracking detour per gate.
                hi = _spec.MaxMoves - 7 * g;
                lo = Math.Max(_spec.MinMoves - 10 * g, (g + 1) * 3);
                // Keep the window at least 3 wide: maze cells sit 2 tiles apart, a single odd distance has no cell.
                lo = Math.Min(lo, hi - 3);
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

            /// <summary>Least moves each gate still to place keeps for its own detour.</summary>
            const int MinGateDetour = 4;

            // The chain in play order: what the player triggers for each gate (button, switch or lever) and its portal pads.
            Cell?[] _mech, _padA, _padB;
            int[] _mechChannel;

            bool NoPlacedPads(Cell pad) => !_gatePads.ContainsKey(pad);

            /// <summary>
            /// The gates form a chain built backwards from the exit. The last gate cuts the way to the exit; its button
            /// (or portal) goes somewhere else in the tomb, ideally where reaching it means walking back the other way.
            /// The gate before it then locks the way to that button, and so on: go left for the button that opens the
            /// door on the right, behind which waits the button for the door on the left, behind which a portal leads
            /// to the exit. Every gate shares out the moves left in the par window, so the backtracking stays short.
            /// </summary>
            bool PlaceGates(out string failure)
            {
                int n = _spec.Gates.Count;
                _mech = new Cell?[n]; _padA = new Cell?[n]; _padB = new Cell?[n]; _mechChannel = new int[n];
                Cell target = _level.Exit;
                int tail = 0;
                for (int i = n - 1; i >= 0; i--)
                    if (!PlaceGate(i, ref target, ref tail, out failure)) return false;
                failure = null;
                return true;
            }

            bool PlaceGate(int i, ref Cell target, ref int tail, out string failure)
            {
                var dist = Distances(_level.Start, 0, out var parent, null, NoPlacedPads);
                if (dist[_level.IndexOf(target)] < 0) { failure = $"chain: target of gate {i} unreachable"; return false; }
                var path = BuildPath(parent, target);

                // Cut tiles: in the second half of the way first (the door closes a wing, not the hub), then the rest.
                var late = new List<int>();
                var early = new List<int>();
                for (int idx = 2; idx < path.Count - 2; idx++) (idx >= path.Count / 2 ? late : early).Add(idx);
                _rng.Shuffle(late);
                _rng.Shuffle(early);
                late.AddRange(early);
                int cuts = 0;
                foreach (int idx in late)
                {
                    var c = path[idx];
                    if (IsCell(c) || !IsFree(c) || Degree(c) != 2 || !FarFromPois(c, _spec.MinPoiSpacing - 1)) continue;
                    if (path[idx - 1].Floor != c.Floor || path[idx + 1].Floor != c.Floor) continue;
                    if (++cuts > 12) break;
                    if (TryGate(i, c, path.Count - 1 + tail, ref target, ref tail)) { failure = null; return true; }
                }
                failure = cuts == 0 ? $"gate: no cut on the way k{i}" : $"gate: no spot for the mechanism k{i} ({_spec.Gates[i]})";
                return false;
            }

            /// <summary>
            /// Picks among <paramref name="spots"/> the one whose detour (moves added to the walk) best matches this gate's
            /// share of the moves left, staying inside the par window.
            /// </summary>
            Cell? PickBySharedDetour(List<Cell> spots, Func<Cell, int> total, int i, int current)
            {
                int max = _spec.MaxMoves - MinGateDetour * i;
                int room = max - current;
                int need = i == 0 ? _spec.MinMoves - current : 0;
                int want = i == 0 ? (Math.Max(need, MinGateDetour) + room + 1) / 2 : room / (i + 1);
                Cell? best = null;
                int bestScore = int.MaxValue;
                foreach (var c in spots)
                {
                    int t = total(c);
                    if (t < 0 || t > max || t - current < need || t - current < 2) continue;
                    int score = Math.Abs(t - current - want) * 4 + _rng.Range(0, 6);
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                return best;
            }

            /// <summary>Mechanism spots on this side of the gates: dead ends first, any maze cell when none fits.</summary>
            Cell? PickMechanism(Func<Cell, bool> ok, Func<Cell, int> total, int i, int current, bool deadEndOnly)
            {
                var spot = PickBySharedDetour(CandidatesAllFloors(c => IsCell(c) && ok(c), deadEndsOnly: true), total, i, current);
                if (spot.HasValue || deadEndOnly) return spot;
                return PickBySharedDetour(CandidatesAllFloors(c => IsCell(c) && ok(c), deadEndsOnly: false), total, i, current);
            }

            bool TryGate(int i, Cell cut, int current, ref Cell target, ref int tail)
            {
                var gate = _spec.Gates[i];
                var t = target;
                int spacing = _spec.MinPoiSpacing;

                if (gate.Kind == GateKind.Door || gate.Kind == GateKind.Laser)
                {
                    bool laser = gate.Kind == GateKind.Laser;
                    int channel = _channels;
                    _level[cut] = new Tile { Type = laser ? TileType.Barrier : TileType.Door, Channel = (byte)channel };
                    var d0 = Distances(_level.Start, 0, out _, null, NoPlacedPads);
                    var dT = Distances(t, 1 << channel, out _, null, NoPlacedPads);
                    int tl = tail;
                    Cell? button = d0[_level.IndexOf(t)] >= 0 ? null : PickMechanism(
                        c => d0[_level.IndexOf(c)] >= 0 && dT[_level.IndexOf(c)] >= 0 && c.Manhattan(cut) >= spacing && FarFromPois(c),
                        c => d0[_level.IndexOf(c)] + dT[_level.IndexOf(c)] + tl, i, current, deadEndOnly: false);
                    if (!button.HasValue) { _level[cut] = Tile.Floor; return false; }
                    _channels++;
                    Reserve(cut);
                    _level[button.Value] = new Tile { Type = laser ? TileType.Switch : TileType.Button, Channel = (byte)channel };
                    Reserve(button.Value);
                    if (laser) _lasers.Add((cut, button.Value, channel));
                    _mech[i] = button;
                    _mechChannel[i] = channel;
                    tail += dT[_level.IndexOf(button.Value)];
                    target = button.Value;
                    return true;
                }

                // Portal gate: the way is walled off; a teleporter at the end of a dead end on this side lands beyond.
                _level[cut] = Tile.Wall;
                var reach = Distances(_level.Start, 0, out _, null, NoPlacedPads);
                if (reach[_level.IndexOf(t)] >= 0) { _level[cut] = Tile.Floor; return false; }
                var beyond = Distances(t, 0, out _, null, NoPlacedPads);
                Cell? b = null;
                foreach (int maxFromTarget in new[] { 8, 14, 99 })
                {
                    int m = maxFromTarget;
                    var list = CandidatesAllFloors(c =>
                    {
                        int k = _level.IndexOf(c);
                        return IsCell(c) && reach[k] < 0 && beyond[k] >= 1 && beyond[k] <= m && FarFromPois(c);
                    }, deadEndsOnly: true);
                    if (list.Count > 0) { b = list[0]; break; }
                }
                if (!b.HasValue) { _level[cut] = Tile.Floor; return false; }
                int landing = beyond[_level.IndexOf(b.Value)] + tail;
                var pb = b.Value;
                bool locked = gate.Portal == TeleporterKind.Locked;
                // A locked portal also needs its lever: keep room for that detour too.
                int leverRoom = locked ? MinGateDetour : 0;
                var a = PickMechanism(
                    c => reach[_level.IndexOf(c)] >= 0 && c.Manhattan(cut) >= spacing && c.Manhattan(pb) >= spacing + 2 && FarFromPois(c),
                    c => reach[_level.IndexOf(c)] + landing + leverRoom, i, current, deadEndOnly: true);
                if (!a.HasValue) { _level[cut] = Tile.Floor; return false; }

                Cell? leverSpot = null;
                int toPad = 0;
                if (locked)
                {
                    var fromA = Distances(a.Value, 0, out _, null, NoPlacedPads);
                    var pa = a.Value;
                    leverSpot = PickMechanism(
                        c => reach[_level.IndexOf(c)] >= 0 && fromA[_level.IndexOf(c)] >= 0 && c.Manhattan(pa) >= spacing && c.Manhattan(pb) >= spacing
                             && c.Manhattan(cut) >= spacing && FarFromPois(c),
                        c => reach[_level.IndexOf(c)] + fromA[_level.IndexOf(c)] + landing, i, current, deadEndOnly: false);
                    if (!leverSpot.HasValue) { _level[cut] = Tile.Floor; return false; }
                    toPad = fromA[_level.IndexOf(leverSpot.Value)];
                }

                Reserve(cut, poi: false);
                byte lever = 0;
                if (leverSpot.HasValue)
                {
                    lever = (byte)_channels++;
                    _level[leverSpot.Value] = new Tile { Type = TileType.Button, Channel = lever };
                    Reserve(leverSpot.Value);
                    _mech[i] = leverSpot;
                    _mechChannel[i] = lever;
                }
                _level[a.Value] = new Tile { Type = TileType.Teleporter, Teleporter = gate.Portal, Channel = lever };
                _level[pb] = new Tile { Type = TileType.Teleporter, Teleporter = gate.Portal, Channel = lever };
                _level.LinkTeleporters(a.Value, pb);
                Reserve(a.Value);
                Reserve(pb);
                _gatePads[a.Value] = i;
                _gatePads[pb] = i;
                _padA[i] = a;
                _padB[i] = pb;
                tail = toPad + landing;
                target = leverSpot ?? a.Value;
                return true;
            }

            /// <summary>
            /// The walk the chain asks for, in play order: each button / lever, each portal jump, then the exit, along
            /// shortest paths with what is open at that point. Mechanics and pruning work on this walk.
            /// </summary>
            List<Cell> PlannedWalk()
            {
                var walk = new List<Cell> { _level.Start };
                var cur = _level.Start;
                int mask = 0;
                bool Leg(Cell to, int stage)
                {
                    var dist = Distances(cur, mask, out var parent, null, p => !_gatePads.TryGetValue(p, out int j) || j < stage);
                    if (dist[_level.IndexOf(to)] < 0) return false;
                    var path = BuildPath(parent, to);
                    for (int k = 1; k < path.Count; k++) walk.Add(path[k]);
                    cur = to;
                    return true;
                }
                for (int i = 0; i < _spec.Gates.Count; i++)
                {
                    if (_mech[i].HasValue)
                    {
                        if (!Leg(_mech[i].Value, i)) return null;
                        mask |= 1 << _mechChannel[i];
                    }
                    if (_padA[i].HasValue)
                    {
                        if (!Leg(_padA[i].Value, i)) return null;
                        walk.Add(_padB[i].Value);
                        cur = _padB[i].Value;
                    }
                }
                return Leg(_level.Exit, _spec.Gates.Count) ? walk : null;
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
                    if (_level[next].IsGate) break;
                    bool junction = Degree(next) >= 3;
                    _level[c] = Tile.Wall;
                    if (junction) break; // stop at the junction this branch hangs from (no cascade)
                    if (_level[next].Type != TileType.Floor) break;
                    c = next;
                }
            }


            /// <summary>
            /// Braiding loops long branches instead of erasing them, which can leave a whole wing no useful walk goes
            /// through. Ground off the walk the gate chain asks for is kept only where it links two separate points of
            /// the walk: a short or a long way between them, a choice to remember. A pocket entered and left through
            /// the same spot (or two spots side by side) serves nothing and goes back to rock.
            /// </summary>
            void PruneWings(List<Cell> walk)
            {
                var onWalk = new bool[_level.CellCount];
                foreach (var c in walk)
                {
                    onWalk[_level.IndexOf(c)] = true;
                    // A climb lands on the other floor: the ladder left behind is walked on too.
                    foreach (int f in new[] { c.Floor - 1, c.Floor + 1 })
                    {
                        var other = c.WithFloor(f);
                        var t = _level[c].Type;
                        if (_level.InBounds(other) && (t == TileType.LadderUp || t == TileType.LadderDown)
                            && (_level[other].Type == TileType.LadderUp || _level[other].Type == TileType.LadderDown))
                            onWalk[_level.IndexOf(other)] = true;
                    }
                }
                var seen = new bool[_level.CellCount];
                var q = new Queue<Cell>();
                foreach (var c0 in _level.AllCells())
                {
                    int i0 = _level.IndexOf(c0);
                    if (seen[i0] || onWalk[i0] || !Walkable(c0)) continue;
                    var area = new List<Cell>();
                    var touches = new List<Cell>();
                    bool special = false;
                    seen[i0] = true;
                    q.Enqueue(c0);
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        area.Add(c);
                        special |= _level[c].Type != TileType.Floor;
                        foreach (var d in DirExt.All)
                        {
                            var n = c.Step(d);
                            if (!Walkable(n)) continue;
                            int ni = _level.IndexOf(n);
                            if (onWalk[ni]) { if (!touches.Contains(n)) touches.Add(n); continue; }
                            if (seen[ni]) continue;
                            seen[ni] = true;
                            q.Enqueue(n);
                        }
                    }
                    if (special) continue;
                    if (!LinksApart(touches)) { foreach (var c in area) _level[c] = Tile.Wall; continue; }
                    if (area.Count > MaxWayTiles) ThinToOneWay(area, touches);
                }
            }

            /// <summary>
            /// A whole block of maze between two points of the walk is a place to get lost, not a choice: keep a single
            /// corridor through it, between the two links farthest apart, and give the rest back to the rock.
            /// </summary>
            void ThinToOneWay(List<Cell> area, List<Cell> touches)
            {
                Cell from = touches[0], to = touches[0];
                int best = -1;
                foreach (var a in touches)
                    foreach (var b in touches)
                        if (a.Floor == b.Floor && a.Manhattan(b) > best) { best = a.Manhattan(b); from = a; to = b; }
                var inArea = new HashSet<Cell>(area);
                var prev = new Dictionary<Cell, Cell>();
                var q = new Queue<Cell>();
                foreach (var d in DirExt.All)
                {
                    var n = from.Step(d);
                    if (inArea.Contains(n) && !prev.ContainsKey(n)) { prev[n] = n; q.Enqueue(n); }
                }
                Cell? end = null;
                while (q.Count > 0 && !end.HasValue)
                {
                    var c = q.Dequeue();
                    foreach (var d in DirExt.All)
                        if (c.Step(d) == to) { end = c; break; }
                    if (end.HasValue) break;
                    foreach (var d in DirExt.All)
                    {
                        var n = c.Step(d);
                        if (!inArea.Contains(n) || prev.ContainsKey(n)) continue;
                        prev[n] = c;
                        q.Enqueue(n);
                    }
                }
                if (!end.HasValue) return;
                var keep = new HashSet<Cell>();
                for (var c = end.Value; ; c = prev[c])
                {
                    keep.Add(c);
                    if (prev[c] == c) break;
                }
                foreach (var c in area)
                    if (!keep.Contains(c)) _level[c] = Tile.Wall;
            }

            /// <summary>Above this many tiles, a way between two points of the walk is thinned to a single corridor.</summary>
            const int MaxWayTiles = 8;

            /// <summary>True when two of these walk tiles are far enough apart for a way between them to be a real choice.</summary>
            static bool LinksApart(List<Cell> touches)
            {
                for (int a = 0; a < touches.Count; a++)
                    for (int b = a + 1; b < touches.Count; b++)
                        if (touches[a].Floor == touches[b].Floor && touches[a].Manhattan(touches[b]) >= MinLinkApart) return true;
                return false;
            }

            /// <summary>Two links closer than this make a pocket, not a way of its own.</summary>
            const int MinLinkApart = 3;

            /// <summary>
            /// Short and long ways: a new corridor dug through the rock between two spots of the same area (1 to 3 maze
            /// cells long), running beside the walk. It is never a big shortcut (the par barely moves): the choice is
            /// between ways of similar length, or a longer way around, to remember from the preview.
            /// </summary>
            void AddBypasses(int count)
            {
                if (count <= 0) return;
                var comp = FloorComponents();
                bool Rock(Cell c) => _level.InBounds(c) && IsInterior(c) && IsCell(c) && _level[c].Type == TileType.Wall;
                bool End(Cell c) => _level.InBounds(c) && IsInterior(c) && IsCell(c) && IsFloorType(c) && c != _level.Start;
                var starts = new List<Cell>();
                foreach (var c in _level.AllCells()) if (End(c)) starts.Add(c);
                _rng.Shuffle(starts);
                foreach (var a in starts)
                {
                    if (count <= 0) return;
                    if (!End(a)) continue;
                    var ground = Distances(a, AllOpen, out _);
                    // BFS over rock cells two tiles apart, up to 3 cells deep.
                    var prev = new Dictionary<Cell, Cell>();
                    var frontier = new List<Cell> { a };
                    Cell? hit = null, last = null;
                    for (int depth = 1; depth <= 3 && !hit.HasValue; depth++)
                    {
                        var next = new List<Cell>();
                        _rng.Shuffle(frontier);
                        foreach (var c in frontier)
                        {
                            foreach (var d in DirExt.All)
                            {
                                var wall = c.Step(d);
                                var n = wall.Step(d);
                                if (!_level.InBounds(n) || _level[wall].Type != TileType.Wall || prev.ContainsKey(n) || n == a) continue;
                                if (Rock(n)) { prev[n] = c; next.Add(n); continue; }
                                // Reached ground again: a bypass if it joins the same area, about as long as the way it doubles.
                                if (depth < 2 || !End(n) || comp[_level.IndexOf(n)] != comp[_level.IndexOf(a)]) continue;
                                int old = ground[_level.IndexOf(n)], fresh = 2 * depth;
                                if (old < 4 || fresh < old - 4) continue;
                                hit = n; last = c;
                                break;
                            }
                            if (hit.HasValue) break;
                        }
                        frontier = next;
                    }
                    if (!hit.HasValue) continue;
                    // Dig from the far end back to the start of the bypass.
                    var cur = hit.Value;
                    var from = last.Value;
                    while (true)
                    {
                        _level[new Cell(cur.Floor, (cur.X + from.X) / 2, (cur.Y + from.Y) / 2)] = Tile.Floor;
                        if (from == a) break;
                        _level[from] = Tile.Floor;
                        cur = from;
                        from = prev[from];
                    }
                    count--;
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
                    if (diff < 3 || IsMeaningfulDeadEnd(a) || IsMeaningfulDeadEnd(b)) continue;
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
                // The closest spot to the junction first: the player sees it close right behind them.
                for (int k = spots.Count - 1; k >= 0; k--)
                {
                    var c = route[spots[k]];
                    _level[c] = new Tile { Type = TileType.Barrier, Channel = (byte)laser.channel, Param = 1 };
                    if (LevelValidator.CheckNoDeadLock(_level) != null) { _level[c] = Tile.Floor; continue; }
                    Reserve(c);
                    return;
                }
            }

            /// <summary>
            /// A stream of 2-4 current tiles along the route, flowing towards the exit: a one-way shortcut. Kept only
            /// where the player can always walk back around it (it never strands them away from a button they need).
            /// </summary>
            bool PlaceCurrent(List<Cell> route)
            {
                // Every start along the route, each with the 3 stream lengths (a random one first).
                var starts = new List<int>();
                for (int i = 2; i < route.Count - 4; i++) starts.Add(i);
                _rng.Shuffle(starts);
                int first = _rng.Range(0, 3);
                var streams = new List<(int start, int len)>();
                foreach (int i in starts)
                    for (int l = 0; l < 3; l++) streams.Add((i, 2 + (first + l) % 3));

                foreach (var (i, len) in streams)
                {
                    if (i + len >= route.Count - 1 || _level[route[i - 1]].Type == TileType.Current) continue;
                    bool ok = true;
                    for (int j = i; j < i + len && ok; j++) ok = IsRouteCorridor(route, j);
                    var end = route[i + len];
                    if (!ok || end.Floor != route[i].Floor || !(IsFloorType(end) || _level[end].Type == TileType.Exit)) continue;
                    for (int j = i; j < i + len; j++)
                        _level[route[j]] = new Tile { Type = TileType.Current, Param = (byte)DirTo(route[j], route[j + 1]) };
                    if (LevelValidator.CheckNoDeadLock(_level) != null)
                    {
                        for (int j = i; j < i + len; j++) _level[route[j]] = Tile.Floor;
                        continue;
                    }
                    for (int j = i; j < i + len; j++)
                    {
                        _reserved[_level.IndexOf(route[j])] = true;
                        _hazards.Add(route[j]);
                    }
                    _reserved[_level.IndexOf(end)] = true; // still ground where the stream lets go
                    return true;
                }
                return false;
            }

            /// <summary>
            /// A fragile slab on the route: a one-time shortcut. Kept only where a longer way around exists, so that
            /// crossing it at the wrong time (or stepping back off it) costs moves, never the run.
            /// </summary>
            bool PlaceCrumbling(List<Cell> route)
            {
                var spots = new List<int>();
                for (int i = 2; i < route.Count - 2; i++) if (IsRouteCorridor(route, i)) spots.Add(i);
                _rng.Shuffle(spots);
                foreach (int i in spots)
                {
                    var c = route[i];
                    _level[c] = new Tile { Type = TileType.Crumbling, Param = (byte)_crumbling };
                    if (LevelValidator.CheckNoDeadLock(_level) != null) { _level[c] = Tile.Floor; continue; }
                    _crumbling++;
                    _reserved[_level.IndexOf(c)] = true;
                    _hazards.Add(c);
                    return true;
                }
                return false;
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
