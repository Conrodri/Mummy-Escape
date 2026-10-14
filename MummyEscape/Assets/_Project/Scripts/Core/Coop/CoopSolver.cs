using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>The pair's shortest way out: who acts, and how, one action after the other.</summary>
    public sealed class CoopSolution
    {
        public List<(int mummy, PlayerAction action)> Actions = new List<(int, PlayerAction)>();
        public int Moves => Actions.Count;
    }

    /// <summary>
    /// Exact breadth-first search over the pair's states (<see cref="CoopRules"/>): every action of either mummy costs one
    /// move, so the result is the fewest actions the pair needs (the par of a two-player tomb). Also proves that a tomb
    /// needs both players: one mummy alone, the other standing still at its start, never reaches the exit.
    /// </summary>
    public static class CoopSolver
    {
        public const int DefaultMaxStates = 3_000_000;

        public static CoopSolution Solve(CoopRules rules, CoopState start, int maxStates = DefaultMaxStates) =>
            Search(rules, start, -1, maxStates);

        /// <summary>
        /// Can <paramref name="mummy"/> reach the exit on its own while the other one never moves? False when the tomb
        /// truly needs both (null result).
        /// </summary>
        public static bool CanReachExitAlone(CoopRules rules, CoopState start, int mummy, int maxStates = DefaultMaxStates) =>
            Search(rules, start, mummy, maxStates) != null;

        /// <param name="alone">-1: both act, the goal is both on the exit. 0 or 1: only that mummy acts, the goal is its reaching the exit.</param>
        static CoopSolution Search(CoopRules rules, CoopState start, int alone, int maxStates)
        {
            var level = rules.Level;
            var layout = Solver.Layout(level);
            (long, long) Key(CoopState s) => (layout.Pack(s.A), layout.Pack(s.B));

            var parent = new Dictionary<(long, long), ((long, long) prev, int mummy, PlayerAction action)>();
            var states = new Dictionary<(long, long), CoopState>();
            var queue = new Queue<(long, long)>();
            var none = (-1L, -1L);
            var startKey = Key(start);
            parent[startKey] = (none, -1, default);
            states[startKey] = start;
            queue.Enqueue(startKey);

            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                var s = states[key];
                for (int m = 0; m < 2; m++)
                {
                    if (alone >= 0 && m != alone) continue;
                    var me = s[m];
                    for (int k = 0; k < 8; k++)
                    {
                        var dir = DirExt.All[k & 3];
                        PlayerAction a;
                        if (k < 4) a = PlayerAction.Move(dir);
                        else if (Rules.IsDisarmable(level.Get(me.Position.Step(dir)), me.Disarmed)) a = PlayerAction.Disarm(dir);
                        else continue;
                        var r = rules.Step(s, m, a, out var next);
                        if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Died)) continue;
                        var nk = Key(next);
                        if (parent.ContainsKey(nk)) continue;
                        parent[nk] = (key, m, a);
                        bool goal = alone < 0 ? r.Has(StepFlags.Won) : rules.OnExit(next[alone].Position);
                        if (goal) return Rebuild(parent, nk, none);
                        states[nk] = next;
                        queue.Enqueue(nk);
                    }
                }
                if (parent.Count > maxStates) return null;
            }
            return null;
        }

        static CoopSolution Rebuild(Dictionary<(long, long), ((long, long) prev, int mummy, PlayerAction action)> parent, (long, long) goal, (long, long) none)
        {
            var sol = new CoopSolution();
            for (var k = goal; parent[k].prev != none; k = parent[k].prev) sol.Actions.Add((parent[k].mummy, parent[k].action));
            sol.Actions.Reverse();
            return sol;
        }
    }
}
