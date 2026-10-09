using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Checks a generated level against its spec. Returns null when valid, otherwise "bucket: details".
    /// This is the contract that guarantees every player gets a fair, interesting level whatever the variant.
    /// </summary>
    public static class LevelValidator
    {
        public static string Validate(Level level, LevelSpec spec, Solution solution)
        {
            if (solution == null) return "unsolvable: no path to the exit";
            if (solution.Moves < spec.MinMoves) return $"too-short: par {solution.Moves} < {spec.MinMoves}";
            if (solution.Moves > spec.MaxMoves) return $"too-long: par {solution.Moves} > {spec.MaxMoves}";
            if (solution.HpLeft < spec.MinHpLeftForPar) return $"too-deadly: par leaves {solution.HpLeft} hp";

            // Every level asks for at least one mechanic (a button or a portal) on the optimal route.
            if (solution.Mechanics < spec.MinMechanics)
                return $"interactions: par uses {solution.Mechanics} mechanics < {spec.MinMechanics}";
            int buttons = spec.RequiredButtons, portals = spec.RequiredPortals;
            if (solution.ButtonsPressed < buttons) return $"interactions: par presses {solution.ButtonsPressed} < {buttons} buttons";
            if (solution.Teleports < portals) return $"interactions: par takes {solution.Teleports} < {portals} portals";
            if (buttons > 0)
            {
                var noButtons = SolverOptions.Default;
                noButtons.AllowButtons = false;
                if (Solver.Solve(level, noButtons) != null) return "interactions: exit reachable without buttons";
            }
            if (portals > 0)
            {
                var noPortals = SolverOptions.Default;
                noPortals.AllowTeleporters = false;
                if (Solver.Solve(level, noPortals) != null) return "interactions: exit reachable without portals";
            }

            var far = CheckExitDistance(level, spec);
            if (far != null) return far;
            var floors = CheckFloors(level, spec);
            if (floors != null) return floors;
            var deadEnd = CheckDeadEnds(level);
            if (deadEnd != null) return deadEnd;
            var pads = CheckDeadEndPortals(level);
            if (pads != null) return pads;
            var unused = CheckEverythingUsed(level, solution);
            if (unused != null) return unused;
            var pocket = CheckNoPocket(level, solution);
            if (pocket != null) return pocket;
            var torches = CheckTorches(level, solution);
            if (torches != null) return torches;
            var spacing = CheckSpacing(level, spec);
            if (spacing != null) return spacing;
            var spikes = CheckSpikeShortcuts(level, solution, spec);
            if (spikes != null) return spikes;
            var score = CheckScore(level, spec);
            if (score != null) return score;
            return CheckNoDeadLock(level, hazards: true, darkTolls: HasTollSpikes(level));
        }

        /// <summary>
        /// The tomb's difficulty (<see cref="DifficultyScore"/>, read from its tiles) lands in the band of its act and mode:
        /// a drawn pattern laying more spikes than asked must not push a Facile tomb into Normal.
        /// </summary>
        public static string CheckScore(Level level, LevelSpec spec)
        {
            if (spec.MinScore <= 0 && spec.MaxScore == int.MaxValue) return null;
            int score = DifficultyScore.Of(level).Score;
            if (score < spec.MinScore) return $"score: {score} < {spec.MinScore}";
            if (score > spec.MaxScore) return $"score: {score} > {spec.MaxScore}";
            return null;
        }

        /// <summary>
        /// Every floor is worth the climb: at least <see cref="LevelSpec.MinFloorTiles"/> tiles of ground and
        /// <see cref="LevelSpec.MinFloorInterests"/> things to remember besides its ladders (a corridor from the start to a ladder is
        /// not a floor).
        /// </summary>
        public static string CheckFloors(Level level, LevelSpec spec = null)
        {
            int minTiles = spec?.MinFloorTiles ?? 12, minInterests = spec?.MinFloorInterests ?? 2;
            if (level.Floors < 2) return null;
            var ground = new int[level.Floors];
            var interests = new int[level.Floors];
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                if (!t.IsSolid) ground[c.Floor]++;
                if (t.Type != TileType.Floor && t.Type != TileType.Wall && t.Type != TileType.LadderUp && t.Type != TileType.LadderDown) interests[c.Floor]++;
            }
            for (int f = 0; f < level.Floors; f++)
            {
                if (ground[f] < minTiles) return $"floor: {f} has {ground[f]} tiles of ground < {minTiles}";
                if (interests[f] < minInterests) return $"floor: {f} has {interests[f]} points of interest < {minInterests}";
            }
            return null;
        }

        /// <summary>The way round a spike trap costs at least this many moves more than walking across it.</summary>
        public const int MinSpikeDetour = 4;

        /// <summary>Spikes with no way round that a tomb with dust puts past it: they are why its wall torch is worth lighting.</summary>
        public const int TollSpikes = 2;

        /// <summary>
        /// Spikes guard a shortcut: the walk across them is the fast way, and a spike-free way round costs at least
        /// <see cref="MinSpikeDetour"/> more moves <b>on the walk the player makes</b> (<see cref="SpikeSaving"/>), not just
        /// between the two sides of the trap: on a ring entered and left at opposite corners both halves are as long, and
        /// spikes on one half would save nothing. Each tomb holds at least <see cref="LevelSpec.SpikeTraps"/> of them.
        /// Only in a tomb with a wall torch, spikes may bar the way (<see cref="TollSpikes"/>): a lit torch shows them to
        /// disarm. Either way the whole tomb can be cleared without a hit: losing a life is a choice, never a toll.
        /// </summary>
        public static string CheckSpikeShortcuts(Level level, Solution solution, LevelSpec spec)
        {
            bool any = false, toll = false;
            int shortcuts = 0, barring = 0;
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                if (t.Type != TileType.Trap || t.Trap != TrapKind.Spikes) continue;
                any = true;
                int extra = SpikeDetour(level, c);
                if (extra < 0) { toll = true; barring++; continue; }
                int saving = SpikeSaving(level, c, solution.Moves);
                if (saving < MinSpikeDetour) return $"spikes: {c} saves only {saving} moves on the walk";
                shortcuts++;
            }
            if (shortcuts < spec.SpikeShortcuts) return $"spikes: {shortcuts} shortcuts < {spec.SpikeShortcuts}";
            if (barring < spec.SpikeTraps) return $"spikes: {barring} barring the walk < {spec.SpikeTraps}";
            if (!any) return null;
            var safe = SolverOptions.Default;
            safe.AvoidSpikes = true;
            safe.DisarmSpikes = toll;
            return Solver.Solve(level, safe) == null ? "spikes: the exit cannot be reached without a spike" : null;
        }

        /// <summary>
        /// Moves the best walk loses when these spikes turn to rock (<paramref name="par"/> = the best walk with them): what
        /// crossing them saves over the spike-free way round, from wherever the walk leaves and rejoins. -1 when the exit
        /// cannot be reached without them.
        /// </summary>
        public static int SpikeSaving(Level level, Cell spike, int par)
        {
            var tile = level[spike];
            level[spike] = Tile.Wall;
            try
            {
                var without = Solver.Solve(level);
                return without == null ? -1 : without.Moves - par;
            }
            finally { level[spike] = tile; }
        }

        static bool HasWallTorch(Level level)
        {
            foreach (var c in level.AllCells())
                if (level[c].Type == TileType.WallTorch) return true;
            return false;
        }

        /// <summary>Spikes barring the way (no way round): only a tomb with a wall torch has them.</summary>
        public static bool HasTollSpikes(Level level)
        {
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                if (t.Type == TileType.Trap && t.Trap == TrapKind.Spikes && SpikeDetour(level, c) < 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Extra moves the shortest spike-free way between the two sides of this spike costs over walking across it
        /// (every channel on, both directions, the worse one counts); -1 when either direction has no way round.
        /// </summary>
        public static int SpikeDetour(Level level, Cell spike)
        {
            var sides = new List<Cell>();
            foreach (var d in DirExt.All)
                if (!level.Get(spike.Step(d)).IsSolid) sides.Add(spike.Step(d));
            if (sides.Count != 2) return -1;
            int there = SpikeFreeDistance(level, sides[0], sides[1]);
            int back = SpikeFreeDistance(level, sides[1], sides[0]);
            return there < 0 || back < 0 ? -1 : Math.Min(there, back) - 2;
        }

        /// <summary>A tile next to this wall (a sconce) can be reached from <paramref name="from"/> without crossing spikes (every channel on).</summary>
        public static bool ReachableWithoutSpikes(Level level, Cell from, Cell wall)
        {
            foreach (var d in DirExt.All)
            {
                var n = wall.Step(d);
                if (level.InBounds(n) && !level[n].IsSolid && SpikeFreeDistance(level, from, n) >= 0) return true;
            }
            return false;
        }

        static int SpikeFreeDistance(Level level, Cell from, Cell to)
        {
            var dist = new Dictionary<Cell, int> { [from] = 0 };
            var q = new Queue<Cell>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == to) return dist[c];
                if (level[c].Type == TileType.Exit) continue;
                var s = new RuleState { Position = c, Pressed = -1, Hp = 99 };
                foreach (var d in DirExt.All)
                {
                    var r = Rules.Step(level, s, PlayerAction.Move(d));
                    if (r.Has(StepFlags.Blocked)) continue;
                    var stepped = level[r.SteppedOn];
                    if (stepped.Type == TileType.Trap && stepped.Trap == TrapKind.Spikes) continue;
                    var p = r.State.Position;
                    if (dist.ContainsKey(p)) continue;
                    dist[p] = dist[c] + 1;
                    q.Enqueue(p);
                }
            }
            return -1;
        }

        /// <summary>
        /// Ground off the ideal walk links two separate points of it (a short or a long way to choose between), never a
        /// pocket entered and left through the same spot. The generator prunes pockets against the walk it planned; this
        /// catches the rare tomb whose shortest walk, as the solver finds it, goes another way. Only exception: the
        /// alcove of a wall torch, a dead end worth its few moves to see again.
        /// </summary>
        public static string CheckNoPocket(Level level, Solution solution)
        {
            var onWalk = new bool[level.CellCount];
            var s = Rules.Initial(level);
            onWalk[level.IndexOf(s.Position)] = true;
            foreach (var a in solution.Actions)
            {
                var r = Rules.Step(level, s, a);
                onWalk[level.IndexOf(r.SteppedOn)] = true; // a ladder or a pad, before the move carries on
                s = r.State;
                onWalk[level.IndexOf(s.Position)] = true;
            }
            bool Ground(Cell c) => level.InBounds(c) && !level[c].IsSolid;
            var seen = new bool[level.CellCount];
            var touches = new List<Cell>();
            var queue = new Queue<Cell>();
            foreach (var c0 in level.AllCells())
            {
                if (seen[level.IndexOf(c0)] || onWalk[level.IndexOf(c0)] || !Ground(c0)) continue;
                touches.Clear();
                bool touchesTorch = false, decoy = false;
                seen[level.IndexOf(c0)] = true;
                queue.Enqueue(c0);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    decoy |= level.IsDecoy(c);
                    foreach (var d in DirExt.All)
                    {
                        var n = c.Step(d);
                        if (level.Get(n).Type == TileType.WallTorch) touchesTorch = true;
                        if (!Ground(n)) continue;
                        if (onWalk[level.IndexOf(n)]) { touches.Add(n); continue; }
                        if (seen[level.IndexOf(n)]) continue;
                        seen[level.IndexOf(n)] = true;
                        queue.Enqueue(n);
                    }
                }
                // 2 apart is the way around a fragile slab or a current, kept so they never wall the player in.
                // A decoy alcove is a pocket on purpose (see Level.IsDecoy).
                bool apart = touchesTorch || decoy;
                foreach (var a in touches)
                    foreach (var b in touches)
                        apart |= a.Floor == b.Floor && a.Manhattan(b) >= 2;
                if (!apart) return $"pocket: {c0} is off the ideal walk and leads nowhere";
            }
            return null;
        }

        /// <summary>Moves a lit torch costs, at most: past this, the dark is always the better bet.</summary>
        public const int MinTorchDetour = 2, MaxTorchDetour = 6;

        /// <summary>
        /// Dust and the wall torch that relights the mummy's torch are a choice: the sconce is never by the ideal walk but
        /// in another corridor, a few moves away (<see cref="MinTorchDetour"/> to <see cref="MaxTorchDetour"/>): finish
        /// in the dark and save time, or pay the detour to see the rest of the tomb.
        /// </summary>
        public static string CheckTorches(Level level, Solution solution)
        {
            // Spikes barring the way past the dust: relighting is part of the ideal walk (the solution proves it reachable).
            if (HasTollSpikes(level)) return null;
            foreach (var c in level.AllCells())
            {
                if (level[c].Type != TileType.WallTorch) continue;
                int detour = TorchDetour(level, solution, c);
                if (detour == 0) return $"torch: wall torch {c} is by the ideal walk";
                if (detour < 0) return $"torch: wall torch {c} cannot be reached in the dark";
                if (detour < MinTorchDetour || detour > MaxTorchDetour) return $"torch: wall torch {c} costs {detour} moves";
            }
            return null;
        }

        /// <summary>
        /// Extra moves to relight at this sconce once the dust has put the torch out: leave the ideal walk, stand by the
        /// sconce, come back to it (here or further on). 0 when the walk itself passes by it, -1 when it cannot be reached.
        /// </summary>
        public static int TorchDetour(Level level, Solution solution, Cell sconce)
        {
            var walk = new List<Cell>();
            var states = new List<RuleState>();
            var s = Rules.Initial(level);
            walk.Add(s.Position);
            states.Add(s);
            foreach (var a in solution.Actions)
            {
                s = Rules.Step(level, s, a).State;
                walk.Add(s.Position);
                states.Add(s);
            }
            var spots = new List<Cell>();
            foreach (var d in DirExt.All)
            {
                var n = sconce.Step(d);
                if (level.InBounds(n) && !level[n].IsSolid) spots.Add(n);
            }
            foreach (var p in walk)
                if (spots.Contains(p)) return 0;

            int best = -1;
            for (int k = 0; k < walk.Count; k++)
            {
                if (!states[k].TorchOut || walk[k].Floor != sconce.Floor) continue;
                // Plain walking from where the walk stands (no current to ride, gates as they are now).
                var state = states[k];
                var dist = new Dictionary<Cell, int>();
                var queue = new Queue<Cell>();
                foreach (var spot in spots)
                    if (Rules.CanEnter(level, spot, state) && level[spot].Type != TileType.Current) { dist[spot] = 0; queue.Enqueue(spot); }
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var d in DirExt.All)
                    {
                        var n = c.Step(d);
                        if (dist.ContainsKey(n) || !Rules.CanEnter(level, n, state) || level[n].Type == TileType.Current) continue;
                        dist[n] = dist[c] + 1;
                        queue.Enqueue(n);
                    }
                }
                if (!dist.TryGetValue(walk[k], out int there)) continue;
                for (int m = k; m < walk.Count; m++)
                {
                    if (walk[m].Floor != sconce.Floor || !dist.TryGetValue(walk[m], out int back)) continue;
                    int extra = there + back - (m - k);
                    if (best < 0 || extra < best) best = extra;
                }
            }
            return best;
        }

        /// <summary>
        /// Every element of the tomb serves the ideal route: each button / switch is pressed, each portal and ladder
        /// taken, each door, barrier, current, fragile slab, trap, dust patch and flame jet lies on the way. Wall torches
        /// are the exception, see <see cref="CheckTorches"/>. A button with a door that leads nowhere is a design error,
        /// not a lure.
        /// </summary>
        public static string CheckEverythingUsed(Level level, Solution solution)
        {
            var used = new HashSet<Cell>();
            var s = Rules.Initial(level);
            used.Add(s.Position);
            foreach (var a in solution.Actions)
            {
                var r = Rules.Step(level, s, a);
                used.Add(s.Position.Step(r.Dir)); // tile walked into (or trap disarmed)
                used.Add(r.SteppedOn);
                used.Add(r.State.Position);
                s = r.State;
            }
            // A stream of current is ridden as a whole: entering it counts for every tile it carries the player along.
            var queue = new Queue<Cell>();
            foreach (var c in used) if (level.InBounds(c) && level[c].Type == TileType.Current) queue.Enqueue(c);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var next = c.Step((Dir)level[c].Param);
                if (level.InBounds(next) && level[next].Type == TileType.Current && used.Add(next)) queue.Enqueue(next);
            }

            foreach (var c in level.AllCells())
            {
                var t = level[c];
                switch (t.Type)
                {
                    case TileType.Wall:
                    case TileType.Floor:
                        continue;
                    case TileType.WallTorch:
                        continue; // off the walk on purpose (CheckTorches)
                    default:
                        if (!used.Contains(c)) return $"unused: {t.Type} {c} is off the ideal route";
                        continue;
                }
            }
            return null;
        }

        /// <summary>
        /// The tomb can never wall the player in: from every situation a player can reach (position, channels on,
        /// collapsed slabs), whatever the moves that led there, a way to the exit is left. Currents, fragile slabs and
        /// barriers may cost a detour, never the run.
        /// With <paramref name="hazards"/>, life, torch, blindness, disarmed traps and the flame beat count too: no
        /// situation may leave death as the only way on (say 1 life left, the torch smothered by dust, and spikes that
        /// can't be seen to disarm across the only way back).
        /// With <paramref name="darkTolls"/> (spikes barring the way past the dust, <see cref="TollSpikes"/>), the dark is the
        /// exception: whoever crosses them without relighting the torch is doomed, by design.
        /// </summary>
        public static string CheckNoDeadLock(Level level, bool hazards = false, int maxStates = 400_000, bool darkTolls = false)
        {
            bool tick = false;
            if (hazards)
                foreach (var c in level.AllCells()) tick |= level[c].Type == TileType.FireJet;
            var ids = new Dictionary<long, int>();
            var states = new List<RuleState>();
            var preds = new List<List<int>>();
            var canWin = new List<bool>();
            // Every way is tried from each state, so the turned screen and the mirror change nothing here.
            RuleState Norm(RuleState x)
            {
                if (!hazards) return new RuleState { Position = x.Position, Pressed = x.Pressed, Crumbled = x.Crumbled, Disarmed = -1, Hp = 7 };
                if (!tick) x.Tick = 0;
                x.Rotation = 0;
                x.Reversed = 0;
                return x;
            }
            var layout = Solver.Layout(level, tick);
            long Key(RuleState x) => layout.Pack(x);
            int Id(RuleState x)
            {
                long k = Key(x);
                if (ids.TryGetValue(k, out int id)) return id;
                id = states.Count;
                ids[k] = id;
                states.Add(x);
                preds.Add(new List<int>());
                canWin.Add(false);
                return id;
            }

            Id(Norm(Rules.Initial(level)));
            var actions = new List<PlayerAction>(8);
            for (int i = 0; i < states.Count; i++)
            {
                if (states.Count > maxStates) return null; // too big to prove: let the other checks decide
                var st = states[i];
                actions.Clear();
                foreach (var d in DirExt.All)
                {
                    actions.Add(PlayerAction.Move(d));
                    var adj = level.Get(st.Position.Step(d));
                    if (hazards && Rules.IsDisarmable(adj, st.Disarmed))
                        actions.Add(PlayerAction.Disarm(d));
                }
                foreach (var a in actions)
                {
                    var r = Rules.Step(level, st, a);
                    if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Died)) continue;
                    if (r.Has(StepFlags.Won)) { canWin[i] = true; continue; }
                    preds[Id(Norm(r.State))].Add(i);
                }
            }

            var queue = new Queue<int>();
            for (int i = 0; i < states.Count; i++) if (canWin[i]) queue.Enqueue(i);
            while (queue.Count > 0)
                foreach (int p in preds[queue.Dequeue()])
                    if (!canWin[p]) { canWin[p] = true; queue.Enqueue(p); }
            for (int i = 0; i < states.Count; i++)
                if (!canWin[i] && !(darkTolls && states[i].TorchOut))
                    return hazards && CheckNoDeadLock(level) == null
                        ? $"dead-end-hazard: only death left at {states[i].Position} ({states[i].Hp} hp{(states[i].TorchOut ? ", torch out" : "")})"
                        : $"dead-lock: walled in at {states[i].Position}";
            return null;
        }

        /// <summary>The exit is far from the entrance, or else behind a door whose button lies far away.</summary>
        static string CheckExitDistance(Level level, LevelSpec spec)
        {
            int d = level.Start.Manhattan(level.Exit);
            if (d >= spec.MinExitDistance) return null;
            if (spec.RequiredButtons > 0)
                foreach (var c in level.AllCells())
                    if (level[c].IsTrigger && level.Start.Manhattan(c) >= spec.MinExitDistance)
                        return null;
            return $"too-close: exit {d} from start (< {spec.MinExitDistance})";
        }

        /// <summary>
        /// Dead ends must mean something: a button, a portal, a ladder, a hole, the exit or the start.
        /// </summary>
        public static string CheckDeadEnds(Level level)
        {
            foreach (var c in level.AllCells())
                if (level[c].Type == TileType.BreakableFloor && (c.Floor == 0 || level[c.WithFloor(c.Floor - 1)].IsSolid))
                    return $"hole: {c} lands in rock";
            var reach = Reachable(level, -1);
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                if (t.IsSolid || t.Type != TileType.Floor || c == level.Start || IsMeaningfulFloor(level, c) || level.IsDecoy(c) || !reach[level.IndexOf(c)]) continue;
                int degree = 0;
                foreach (var d in DirExt.All) if (!level.Get(c.Step(d)).IsSolid) degree++;
                if (degree == 1) return $"dead-end: nothing to find at {c}";
            }
            return null;
        }

        /// <summary>
        /// Every teleporter at the end of a dead end: arriving, there is a single way out, and stepping back takes the
        /// portal again. A pad with several exits would force a double trip through the portal to change direction.
        /// </summary>
        public static string CheckDeadEndPortals(Level level)
        {
            foreach (var c in level.AllCells())
            {
                if (level[c].Type != TileType.Teleporter) continue;
                int degree = 0;
                foreach (var d in DirExt.All) if (!level.Get(c.Step(d)).IsSolid) degree++;
                if (degree != 1) return $"portal: {c} has {degree} exits (dead end required)";
            }
            return null;
        }

        /// <summary>Where a player falling through a cracked floor lands (meaningful even at the end of a corridor).</summary>
        public static bool IsHoleLanding(Level level, Cell c) =>
            c.Floor + 1 < level.Floors && level[c.WithFloor(c.Floor + 1)].Type == TileType.BreakableFloor;

        /// <summary>
        /// Plain floor that still serves a purpose at the end of a corridor: where a hole drops the player, or the step
        /// next to a ladder / portal (needed to step back onto it), or by a wall torch (its alcove).
        /// </summary>
        public static bool IsMeaningfulFloor(Level level, Cell c)
        {
            if (IsHoleLanding(level, c)) return true;
            foreach (var d in DirExt.All)
            {
                var t = level.Get(c.Step(d)).Type;
                if (t == TileType.LadderUp || t == TileType.LadderDown || t == TileType.Teleporter || t == TileType.WallTorch) return true;
            }
            return false;
        }

        static bool[] Reachable(Level level, int pressed)
        {
            var seen = new bool[level.CellCount];
            var q = new Queue<Cell>();
            seen[level.IndexOf(level.Start)] = true;
            q.Enqueue(level.Start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                var s = new RuleState { Position = c, Pressed = pressed, Disarmed = -1, Hp = 99 };
                foreach (var d in DirExt.All)
                {
                    var r = Rules.Step(level, s, PlayerAction.Move(d));
                    if (r.Has(StepFlags.Blocked)) continue;
                    // The tile walked onto counts as visited even when it transports the player away.
                    foreach (var p in new[] { r.SteppedOn, r.State.Position })
                    {
                        int i = level.IndexOf(p);
                        if (seen[i]) continue;
                        seen[i] = true;
                        if (level[p].Type != TileType.Exit) q.Enqueue(p);
                    }
                }
            }
            return seen;
        }

        static string CheckSpacing(Level level, LevelSpec spec)
        {
            var pois = new List<Cell>();
            foreach (var c in level.AllCells())
                if (level[c].IsPointOfInterest) pois.Add(c);

            // Doors sit in corridors and are allowed one tile of slack (see generator).
            for (int i = 0; i < pois.Count; i++)
                for (int j = i + 1; j < pois.Count; j++)
                {
                    var a = pois[i];
                    var b = pois[j];
                    if (a.Floor != b.Floor) continue;
                    int d = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                    int min = spec.MinPoiSpacing;
                    if (level[a].IsGate || level[b].IsGate) min--;
                    if (d < min) return $"spacing: {level[a].Type}{a} and {level[b].Type}{b} are {d} apart (< {min})";
                }
            return null;
        }
    }
}
