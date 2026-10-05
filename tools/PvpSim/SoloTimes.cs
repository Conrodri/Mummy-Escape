using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;

namespace MummyEscape.PvpSim
{
    /// <summary>
    /// Solo tombs: the perfect time (RunTiming, no mistake, every swipe the instant the game allows) next to what
    /// simulated players of a few levels take, to set the "time to beat" and the suspicion threshold.
    /// </summary>
    public static class SoloTimes
    {
        public static void Run(int variants)
        {
            double[] skills = { 0.4, 0.7, 1.0 };
            Console.WriteLine("level  par  perfect   " + string.Join("  ", skills.Select(s => $"skill {s:0.0} (x perfect)")));
            foreach (int act in new[] { 1, 2, 3, 4, 5 })
            foreach (int n in new[] { 1, 5, 10 })
            {
                var id = new LevelId(act, n);
                var pars = new List<int>();
                var perfect = new List<int>();
                var ratios = skills.Select(_ => new List<double>()).ToArray();
                for (int v = 0; v < variants; v++)
                {
                    var level = LevelGenerator.Generate(id, v);
                    int min = RunTiming.MinFinishMs(level) ?? 0;
                    if (min <= 0) continue;
                    pars.Add(level.Solution.Moves);
                    perfect.Add(min);
                    var rng = new Random(act * 1000 + n * 50 + v);
                    for (int k = 0; k < skills.Length; k++)
                    {
                        var (inputs, outcome, _) = new HumanPlayer(skills[k], rng).Play(level, rng);
                        if (outcome == RunOutcome.Finished) ratios[k].Add(RunActions.MsOf(inputs[inputs.Count - 1].Tick) / (double)min);
                    }
                }
                string Cell(List<double> r) => r.Count == 0 ? "      -      " : $"min {r.Min(),4:0.00} med {Median(r),4:0.00}";
                Console.WriteLine($"{act}-{n,-3} {Median(pars.Select(x => (double)x).ToList()),4:0}  {Median(perfect.Select(x => (double)x).ToList()) / 1000,5:0.0}s   "
                                  + string.Join("    ", ratios.Select(Cell)));
            }
        }

        static double Median(List<double> xs) => xs.Count == 0 ? 0 : xs.OrderBy(x => x).ElementAt(xs.Count / 2);
    }
}
