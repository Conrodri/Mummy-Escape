using System.Collections.Generic;

namespace MummyEscape.Core
{
    public sealed class Solution
    {
        public int Moves;
        public int ButtonsPressed;
        public int Disarms;
        public int HpLeft;
        /// <summary>Teleporters taken by the optimal route.</summary>
        public int Teleports;
        /// <summary>Total cost of the route, for <see cref="Solver.SolveFastest"/> (0 for the shortest route).</summary>
        public int Cost;
        public List<PlayerAction> Actions = new List<PlayerAction>();
        public int Interactions => ButtonsPressed + Disarms;
        /// <summary>Mechanics the optimal route relies on (buttons pressed + portals taken): the "1 interaction minimum" contract.</summary>
        public int Mechanics => ButtonsPressed + Teleports;
    }

    public struct SolverOptions
    {
        /// <summary>When false, buttons never activate: used to prove that doors are mandatory.</summary>
        public bool AllowButtons;
        /// <summary>When false, teleporters never transport: used to prove that a portal is mandatory.</summary>
        public bool AllowTeleporters;
        /// <summary>When true, armed spikes are never walked onto: used to prove the long way round them exists.</summary>
        public bool AvoidSpikes;
        /// <summary>With <see cref="AvoidSpikes"/>: spikes may still be disarmed (those barring the way, seen by torchlight).</summary>
        public bool DisarmSpikes;
        /// <summary>Safety cap on explored states.</summary>
        public int MaxStates;

        public static SolverOptions Default => new SolverOptions { AllowButtons = true, AllowTeleporters = true, MaxStates = 2_000_000 };
    }

    /// <summary>
    /// Exact breadth-first search over (position, activated channels, disarmed traps, hp, blindness, torch, collapsed
    /// slabs, flame rhythm).
    /// Every action costs one move, so BFS gives the true minimum number of moves for an omniscient player.
    /// Darkness traps are never worth disarming for an omniscient player (blindness has no cost), so the solver
    /// only considers disarming spikes; this keeps the state space small without changing the optimum.
    /// </summary>
    public static class Solver
    {
        public static Solution Solve(Level level) => Solve(level, SolverOptions.Default);

        public static Solution Solve(Level level, SolverOptions options) => Solve(level, Rules.Initial(level), options);

        /// <summary>Shortest way to the exit from any situation of a run (PvP progress: moves still to go).</summary>
        public static Solution Solve(Level level, RuleState start, SolverOptions options)
        {
            bool tick = HasFireJets(level);
            var startKey = Pack(level, start, tick);
            var parent = new Dictionary<long, (long prev, PlayerAction action)>();
            var states = new Dictionary<long, RuleState>();
            var queue = new Queue<long>();
            var candidates = new List<PlayerAction>(8);

            parent[startKey] = (-1, default);
            states[startKey] = start;
            queue.Enqueue(startKey);

            while (queue.Count > 0)
            {
                long key = queue.Dequeue();
                var s = states[key];

                candidates.Clear();
                foreach (var dir in DirExt.All)
                {
                    candidates.Add(PlayerAction.Move(dir));
                    var adj = level.Get(s.Position.Step(dir));
                    if ((!options.AvoidSpikes || options.DisarmSpikes) && Rules.IsDisarmable(adj, s.Disarmed))
                        candidates.Add(PlayerAction.Disarm(dir));
                }

                foreach (var a in candidates)
                {
                    var r = Rules.Step(level, s, a);
                    if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Died)) continue;
                    if (options.AvoidSpikes && r.Has(StepFlags.TrapTriggered) && r.Has(StepFlags.Damaged) && !r.Has(StepFlags.Burned)) continue;
                    if (!options.AllowTeleporters && r.Has(StepFlags.Teleported)) r.State.Position = r.SteppedOn; // stand on the pad, no transport
                    var ns = r.State;
                    if (!options.AllowButtons) ns.Pressed = s.Pressed;
                    long nk = Pack(level, ns, tick);
                    if (parent.ContainsKey(nk)) continue;
                    parent[nk] = (key, a);
                    if (r.Has(StepFlags.Won)) return Rebuild(level, start, parent, nk, options);
                    states[nk] = ns;
                    queue.Enqueue(nk);
                }

                if (parent.Count > options.MaxStates) return null;
            }
            return null;
        }

        /// <summary>
        /// Fastest way out when actions take different times (<paramref name="cost"/>: what an action delays the next one):
        /// Dijkstra over the same states as <see cref="Solve(Level, RuleState, SolverOptions)"/>. The winning action itself
        /// costs nothing (a run's time is the instant of its last action). <see cref="Solution.Cost"/> holds the total.
        /// </summary>
        public static Solution SolveFastest(Level level, System.Func<RuleState, PlayerAction, StepResult, int> cost, SolverOptions options)
        {
            var start = Rules.Initial(level);
            bool tick = HasFireJets(level);
            var startKey = Pack(level, start, tick);
            var parent = new Dictionary<long, (long prev, PlayerAction action)>();
            var best = new Dictionary<long, int>();
            var states = new Dictionary<long, RuleState>();
            var done = new HashSet<long>();
            var open = new SortedSet<(int cost, long key)>();
            var candidates = new List<PlayerAction>(8);

            parent[startKey] = (-1, default);
            best[startKey] = 0;
            states[startKey] = start;
            open.Add((0, startKey));

            while (open.Count > 0)
            {
                var (g, key) = open.Min;
                open.Remove(open.Min);
                if (!done.Add(key)) continue;
                var s = states[key];

                candidates.Clear();
                foreach (var dir in DirExt.All)
                {
                    candidates.Add(PlayerAction.Move(dir));
                    if (Rules.IsDisarmable(level.Get(s.Position.Step(dir)), s.Disarmed)) candidates.Add(PlayerAction.Disarm(dir));
                }

                foreach (var a in candidates)
                {
                    var r = Rules.Step(level, s, a);
                    if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Died)) continue;
                    long nk = Pack(level, r.State, tick);
                    if (r.Has(StepFlags.Won))
                    {
                        // States leave the queue by increasing time: the first way out found is the fastest.
                        parent[nk] = (key, a);
                        var sol = Rebuild(level, start, parent, nk, options);
                        sol.Cost = g;
                        return sol;
                    }
                    int ng = g + cost(s, a, r);
                    if (done.Contains(nk) || (best.TryGetValue(nk, out int known) && known <= ng)) continue;
                    if (best.ContainsKey(nk)) open.Remove((best[nk], nk));
                    best[nk] = ng;
                    parent[nk] = (key, a);
                    states[nk] = r.State;
                    open.Add((ng, nk));
                }

                if (best.Count > options.MaxStates) return null;
            }
            return null;
        }

        static Solution Rebuild(Level level, RuleState start, Dictionary<long, (long prev, PlayerAction action)> parent, long goal, SolverOptions options)
        {
            var actions = new List<PlayerAction>();
            for (long k = goal; parent[k].prev >= 0; k = parent[k].prev) actions.Add(parent[k].action);
            actions.Reverse();

            // Replay to collect stats.
            var sol = new Solution { Actions = actions };
            var s = start;
            foreach (var a in actions)
            {
                var r = Rules.Step(level, s, a);
                if (!options.AllowButtons) r.State.Pressed = s.Pressed;
                else if (r.Has(StepFlags.ButtonPressed)) sol.ButtonsPressed++;
                if (r.Has(StepFlags.Disarmed)) sol.Disarms++;
                if (!options.AllowTeleporters && r.Has(StepFlags.Teleported)) r.State.Position = r.SteppedOn;
                else if (r.Has(StepFlags.Teleported)) sol.Teleports++;
                sol.Moves++;
                s = r.State;
            }
            sol.HpLeft = s.Hp;
            return sol;
        }

        /// <summary>Limits of the packed state: tiles, channels, traps and fragile slabs.</summary>
        public const int MaxCells = 1 << 12, MaxChannels = 12, MaxTraps = 12, MaxCrumbling = 12;

        /// <summary>
        /// 12 bits position, 12 channels, 12 traps, 3 hp, 2 blind, 1 torch, 12 collapsed slabs, 2 flame tick, 2 turns of the tomb,
        /// 4 reversed steps = 62 bits.
        /// The tick only matters when the tomb has flame jets (otherwise it would triple the states for nothing).
        /// </summary>
        static long Pack(Level level, RuleState s, bool tick)
        {
            return (long)level.IndexOf(s.Position)
                   | ((long)(s.Pressed & 0xFFF) << 12)
                   | ((long)(s.Disarmed & 0xFFF) << 24)
                   | ((long)(s.Hp & 0x7) << 36)
                   | ((long)(s.Blind & 0x3) << 39)
                   | (s.TorchOut ? 1L << 41 : 0)
                   | ((long)(s.Crumbled & 0xFFF) << 42)
                   | (tick ? (long)(s.Tick & 0x3) << 54 : 0)
                   | ((long)(s.Rotation & 0x3) << 56)
                   | ((long)(s.Reversed & 0xF) << 58);
        }

        static bool HasFireJets(Level level)
        {
            for (int i = 0; i < level.CellCount; i++)
                if (level[level.CellAt(i)].Type == TileType.FireJet) return true;
            return false;
        }

        /// <summary>
        /// Can the exit still be reached from this state, whatever it costs? Traps are ignored (a hurt mummy is not a
        /// stuck one): only what changes the layout counts (currents, collapsed slabs, barriers, doors).
        /// </summary>
        public static bool CanEscape(Level level, RuleState from, int maxStates = 200_000)
        {
            var seen = new HashSet<long>();
            var queue = new Queue<RuleState>();
            RuleState Norm(RuleState s) => new RuleState { Position = s.Position, Pressed = s.Pressed, Crumbled = s.Crumbled, Disarmed = -1, Hp = 7 };
            var start = Norm(from);
            seen.Add(Pack(level, start, false));
            queue.Enqueue(start);
            while (queue.Count > 0 && seen.Count < maxStates)
            {
                var s = queue.Dequeue();
                if (level[s.Position].Type == TileType.Exit) return true;
                foreach (var dir in DirExt.All)
                {
                    var r = Rules.Step(level, s, PlayerAction.Move(dir));
                    if (r.Has(StepFlags.Blocked)) continue;
                    if (r.Has(StepFlags.Won)) return true;
                    var ns = Norm(r.State);
                    if (seen.Add(Pack(level, ns, false))) queue.Enqueue(ns);
                }
            }
            return queue.Count > 0; // state cap hit: give the player the benefit of the doubt
        }
    }
}
