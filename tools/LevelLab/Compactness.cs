using System.Collections.Generic;
using MummyEscape.Core;

namespace LevelLab
{
    /// <summary>How far each tile lies from the ideal walk (plain steps, floor by floor): finds wings nobody needs.</summary>
    public static class Compactness
    {
        public static int[] ReachFromSolution(Level level)
        {
            var reach = new int[level.CellCount];
            for (int i = 0; i < reach.Length; i++) reach[i] = -1;
            var q = new Queue<Cell>();
            void Seed(Cell c)
            {
                if (reach[level.IndexOf(c)] >= 0) return;
                reach[level.IndexOf(c)] = 0;
                q.Enqueue(c);
            }
            var s = Rules.Initial(level);
            Seed(s.Position);
            foreach (var a in level.Solution.Actions)
            {
                var r = Rules.Step(level, s, a);
                if (r.Has(StepFlags.Teleported)) Seed(r.SteppedOn);
                s = r.State;
                Seed(s.Position);
            }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in DirExt.All)
                {
                    var n = c.Step(d);
                    if (!level.InBounds(n) || level[n].Type == TileType.Wall || reach[level.IndexOf(n)] >= 0) continue;
                    reach[level.IndexOf(n)] = reach[level.IndexOf(c)] + 1;
                    q.Enqueue(n);
                }
            }
            return reach;
        }
    }
}
