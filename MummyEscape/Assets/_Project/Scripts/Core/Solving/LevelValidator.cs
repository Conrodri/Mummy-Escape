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
            var deadEnd = CheckDeadEnds(level);
            if (deadEnd != null) return deadEnd;
            var pads = CheckDeadEndPortals(level);
            if (pads != null) return pads;
            var unused = CheckEverythingUsed(level, solution);
            if (unused != null) return unused;
            var spacing = CheckSpacing(level, spec);
            if (spacing != null) return spacing;
            return CheckNoDeadLock(level, hazards: true);
        }

        /// <summary>
        /// Every element of the tomb serves the ideal route: each button / switch is pressed, each portal and ladder
        /// taken, each door, barrier, current, fragile slab, trap, dust patch and flame jet lies on the way, and each
        /// wall torch relights the torch. A button with a door that leads nowhere is a design error, not a lure.
        /// </summary>
        public static string CheckEverythingUsed(Level level, Solution solution)
        {
            var used = new HashSet<Cell>();
            var relit = new HashSet<Cell>();
            var s = Rules.Initial(level);
            used.Add(s.Position);
            foreach (var a in solution.Actions)
            {
                var r = Rules.Step(level, s, a);
                used.Add(s.Position.Step(a.Dir)); // tile walked into (or trap disarmed)
                used.Add(r.SteppedOn);
                used.Add(r.State.Position);
                if (r.Has(StepFlags.TorchRelit)) relit.Add(r.State.Position);
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
                        bool lit = false;
                        foreach (var d in DirExt.All) lit |= relit.Contains(c.Step(d));
                        if (!lit) return $"unused: wall torch {c} never relights the torch";
                        continue;
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
        /// </summary>
        public static string CheckNoDeadLock(Level level, bool hazards = false, int maxStates = 400_000)
        {
            bool tick = false;
            if (hazards)
                foreach (var c in level.AllCells()) tick |= level[c].Type == TileType.FireJet;
            var ids = new Dictionary<long, int>();
            var states = new List<RuleState>();
            var preds = new List<List<int>>();
            var canWin = new List<bool>();
            RuleState Norm(RuleState x)
            {
                if (!hazards) return new RuleState { Position = x.Position, Pressed = x.Pressed, Crumbled = x.Crumbled, Disarmed = -1, Hp = 7 };
                if (!tick) x.Tick = 0;
                return x;
            }
            long Key(RuleState x) =>
                (long)level.IndexOf(x.Position) | ((long)(x.Pressed & 0xFFF) << 12) | ((long)(x.Crumbled & 0xFFF) << 24)
                | (hazards ? ((long)(x.Disarmed & 0xFFF) << 36) | ((long)(x.Hp & 0x7) << 48) | ((long)(x.Blind & 0x3) << 51)
                             | (x.TorchOut ? 1L << 53 : 0) | ((long)(x.Tick & 0x3) << 54) : 0);
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
                    if (hazards && adj.Type == TileType.Trap && adj.Trap == TrapKind.Spikes && Rules.IsTrapArmed(adj, st.Disarmed))
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
                if (!canWin[i])
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
                if (t.IsSolid || t.Type != TileType.Floor || c == level.Start || IsMeaningfulFloor(level, c) || !reach[level.IndexOf(c)]) continue;
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
        /// next to a ladder / portal (needed to step back onto it).
        /// </summary>
        public static bool IsMeaningfulFloor(Level level, Cell c)
        {
            if (IsHoleLanding(level, c)) return true;
            foreach (var d in DirExt.All)
            {
                var t = level.Get(c.Step(d)).Type;
                if (t == TileType.LadderUp || t == TileType.LadderDown || t == TileType.Teleporter) return true;
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
