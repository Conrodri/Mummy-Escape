using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MummyEscape.Core;

namespace LevelLab
{
    /// <summary>
    /// `--human [--sessions 30] [--act 2] [--profile moyen]`: plays every level with human-like players.
    /// A session = play the level until escaping, a new maze after each death (as in the game), preview included.
    /// </summary>
    static class HumanReport
    {
        static double Pct(List<double> v, double q)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        }

        public static void Run(string[] args)
        {
            int sessions = 30, act = 0;
            string only = null;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--sessions") sessions = int.Parse(args[i + 1]);
                if (args[i] == "--act") act = int.Parse(args[i + 1]);
                if (args[i] == "--profile") only = args[i + 1];
            }
            var total = Stopwatch.StartNew();
            var profiles = only == "omniscient" ? new[] { HumanProfile.Omniscient } : HumanProfile.All;
            foreach (var prof in profiles)
            {
                if (only != null && prof.Name != only) continue;
                Console.WriteLine($"\n=== joueur {prof.Name} (mémoire {prof.Memory:P0} / repères {prof.PoiMemory:P0}, mauvais embranchement {prof.WrongTurn:P0}) - {sessions} sessions/niveau ===");
                Console.WriteLine("level | par | gagné 1er essai | coups/par méd p75 | chrono méd | total méd  p90 | >2min | morts emmuré abandon | erreurs/run");
                var actRatios = new SortedDictionary<int, List<double>>();
                var actTotals = new SortedDictionary<int, List<double>>();
                var actChronos = new SortedDictionary<int, List<double>>();
                foreach (var id in DifficultyTable.AllLevels())
                {
                    if (act != 0 && id.Act != act) continue;
                    var rng = new Random(id.Act * 1000 + id.Index + prof.Name.Length * 7919);
                    var pars = new List<double>(); var ratios = new List<double>(); var chronos = new List<double>(); var totals = new List<double>();
                    int firstWins = 0, deaths = 0, trapped = 0, gaveUp = 0, over2 = 0, runs = 0, wrong = 0;
                    for (int k = 0; k < sessions; k++)
                    {
                        double t = 0;
                        for (int attempt = 0; ; attempt++)
                        {
                            var level = LevelGenerator.Generate(id, k * 13 + attempt);
                            t += level.Floors * 10 + (attempt > 0 ? 3 : 0); // preview (10 s per floor) + restart
                            var r = HumanSim.Play(level, prof, rng);
                            runs++; wrong += r.WrongTurns;
                            t += r.Seconds;
                            if (r.Won)
                            {
                                if (attempt == 0) firstWins++;
                                pars.Add(level.Solution.Moves);
                                ratios.Add((double)r.Moves / level.Solution.Moves);
                                chronos.Add(r.Seconds);
                                break;
                            }
                            if (r.GaveUp) gaveUp++; else if (r.Defeat == DefeatCause.Trapped) trapped++; else deaths++;
                            if (t > 900 || attempt >= 8) break;
                        }
                        totals.Add(t);
                        if (t > 120) over2++;
                    }
                    Add(actRatios, id.Act, ratios); Add(actTotals, id.Act, totals); Add(actChronos, id.Act, chronos);
                    Console.WriteLine($"{id,-5} | {(pars.Count > 0 ? pars.Average() : 0),3:0} | {firstWins * 100 / sessions,14}% | {Pct(ratios, 0.5),6:0.00} {Pct(ratios, 0.75),5:0.00}     | {Pct(chronos, 0.5),7:0} s | {Pct(totals, 0.5),5:0} s {Pct(totals, 0.9),4:0} s | {over2 * 100 / sessions,4}% | {deaths,5} {trapped,7} {gaveUp,7} | {(double)wrong / runs,5:0.0}");
                }
                Console.WriteLine("acte | coups/par p10  p25  p50  p75  p90 | chrono p50 p90 | total p50 p90 | >2min");
                foreach (var a in actRatios.Keys)
                {
                    var rr = actRatios[a]; var tt = actTotals[a]; var cc = actChronos[a];
                    Console.WriteLine($"{a,4} | {Pct(rr, .1),13:0.00} {Pct(rr, .25),4:0.00} {Pct(rr, .5),4:0.00} {Pct(rr, .75),4:0.00} {Pct(rr, .9),4:0.00} | {Pct(cc, .5),6:0} s {Pct(cc, .9),3:0} s | {Pct(tt, .5),5:0} s {Pct(tt, .9),3:0} s | {tt.Count(x => x > 120) * 100 / tt.Count,4}%");
                }
            }
            Console.WriteLine($"total {total.ElapsedMilliseconds} ms");
        }

        static void Add(SortedDictionary<int, List<double>> d, int key, List<double> values)
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = new List<double>();
            list.AddRange(values);
        }
    }
}
