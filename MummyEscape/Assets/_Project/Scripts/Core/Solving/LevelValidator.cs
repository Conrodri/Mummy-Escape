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
            return CheckSpacing(level, spec);
        }

        /// <summary>The exit is far from the entrance, or else behind a door whose button lies far away.</summary>
        static string CheckExitDistance(Level level, LevelSpec spec)
        {
            int d = level.Start.Manhattan(level.Exit);
            if (d >= spec.MinExitDistance) return null;
            if (spec.RequiredButtons > 0)
                foreach (var c in level.AllCells())
                    if (level[c].Type == TileType.Button && (level.DecoyChannels & (1 << level[c].Channel)) == 0
                        && level.Start.Manhattan(c) >= spec.MinExitDistance)
                        return null;
            return $"too-close: exit {d} from start (< {spec.MinExitDistance})";
        }

        /// <summary>
        /// Dead ends must mean something: a button, a portal, a ladder, a hole, the exit or the start. Pointless ones are
        /// only tolerated behind a decoy door (the decoy is the lure).
        /// </summary>
        public static string CheckDeadEnds(Level level)
        {
            foreach (var c in level.AllCells())
                if (level[c].Type == TileType.BreakableFloor && (c.Floor == 0 || level[c.WithFloor(c.Floor - 1)].IsSolid))
                    return $"hole: {c} lands in rock";
            var reach = Reachable(level, ~level.DecoyChannels);
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
                    if (level[a].Type == TileType.Door || level[b].Type == TileType.Door) min--;
                    if (d < min) return $"spacing: {level[a].Type}{a} and {level[b].Type}{b} are {d} apart (< {min})";
                }
            return null;
        }
    }
}
