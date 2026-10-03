using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MummyEscape.Core;

// Usage: dotnet run -c Release --project tools/LevelLab              -> stats over 20 mazes (variants) of every level
//        dotnet run -c Release --project tools/LevelLab -- --variants 100 [--act 2]
//        dotnet run -c Release --project tools/LevelLab -- 2-5 [variant] -> ASCII map + optimal path of one maze
if (args.Length >= 1 && args[0].Contains("-") && !args[0].StartsWith("--"))
{
    var parts = args[0].Split('-');
    var id = new LevelId(int.Parse(parts[0]), int.Parse(parts[1]));
    int variant = args.Length > 1 ? int.Parse(args[1]) : 0;
    var level = LevelGenerator.Generate(id, variant);
    var sol = level.Solution;
    Console.WriteLine(DifficultyTable.Spec(id));
    Console.WriteLine($"variant {variant} | par {sol.Moves} | buttons {sol.ButtonsPressed} portals {sol.Teleports} disarms {sol.Disarms} | hp left {sol.HpLeft} | attempt {level.Attempt}");
    Console.WriteLine("solution: " + string.Join(" ", sol.Actions));
    var histogram = new SortedDictionary<string, int>();
    foreach (var c in level.AllCells())
    {
        var type = level[c].Type;
        if (type == TileType.Wall || type == TileType.Floor) continue;
        histogram[type.ToString()] = histogram.TryGetValue(type.ToString(), out int n) ? n + 1 : 1;
    }
    Console.WriteLine("tiles: " + string.Join(", ", histogram.Select(kv => $"{kv.Key} {kv.Value}")));
    var path = new HashSet<Cell>();
    var s = Rules.Initial(level);
    foreach (var a in sol.Actions) { s = Rules.Step(level, s, a).State; path.Add(s.Position); }
    Console.WriteLine("legend: S start E exit A-P doors a-p buttons @ portal ? hidden & locked % cursed ^ spikes ~ darkness , dust ! wall torch U/D ladders v hole {>}< currents x fragile | red = blue barrier $ switch 0-2 flame * route");
    Console.WriteLine(level.ToAscii(path));
    return;
}

if (args.Length >= 2 && args[0] == "--why")
{
    var p = args[1].Split('-');
    var wid = new LevelId(int.Parse(p[0]), int.Parse(p[1]));
    var wspec = DifficultyTable.Spec(wid);
    for (int a = 0; a < (args.Length > 3 ? int.Parse(args[3]) : 40); a++)
    {
        var l = LevelGenerator.TryAttempt(wspec, DifficultyTable.Seed(wid, 0), a, out string why);
        Console.WriteLine($"attempt {a}: {(l != null ? "OK par " + l.Solution.Moves : why)}");
        if (l == null && args.Length > 2 && why.StartsWith(args[2]))
        {
            Console.WriteLine(LevelGenerator.BuildUnchecked(wspec, DifficultyTable.Seed(wid, 0), a, out _).ToAscii());
            return;
        }
    }
    return;
}

if (args.Length >= 3 && args[0] == "--human-why")
{
    // --human-why 3-8 attentif [runs]: outcome of each run of one level, to look into losses and give-ups.
    var hp = args[1].Split('-');
    var hid = new LevelId(int.Parse(hp[0]), int.Parse(hp[1]));
    var prof = args[2] == "omniscient" ? LevelLab.HumanProfile.Omniscient : LevelLab.HumanProfile.All.First(x => x.Name == args[2]);
    var rng = new Random(1);
    for (int v = 0; v < (args.Length > 3 ? int.Parse(args[3]) : 20); v++)
    {
        var level = LevelGenerator.Generate(hid, v);
        var r = LevelLab.HumanSim.Play(level, prof, rng);
        if (!r.Won && args.Length > 4) // --human-why 4-6 moyen 20 map: where the run ended
            Console.WriteLine($"ended at {r.Final.Position} pressed {r.Final.Pressed} crumbled {r.Final.Crumbled} hp {r.Final.Hp}\n" + level.ToAscii(new[] { r.Final.Position }));
        Console.WriteLine($"variant {v,3} par {level.Solution.Moves,3}: {(r.Won ? "won" : r.GaveUp ? "gave up (" + r.Reason + ")" : r.Defeat.ToString()),-20} moves {r.Moves,4} {r.Seconds,5:0} s wrong turns {r.WrongTurns} bumps {r.Bumps}");
    }
    return;
}

if (args.Length >= 1 && args[0] == "--compact")
{
    // --compact [variants]: how much of each tomb the ideal walk actually uses, per act.
    int n = args.Length > 1 ? int.Parse(args[1]) : 10;
    Console.WriteLine("act | ground/grid | far>3 % | max reach | junctions | wings (levels with far>3)");
    for (int act = 1; act <= DifficultyTable.ActCount; act++)
    {
        double ground = 0, far = 0, junctions = 0; int maxReach = 0, wings = 0, count = 0;
        for (int i = 1; i <= DifficultyTable.GetAct(act).Levels; i++)
            for (int v = 0; v < n; v++)
            {
                var level = LevelGenerator.Generate(new LevelId(act, i), v);
                var reach = LevelLab.Compactness.ReachFromSolution(level);
                int walk = 0, farTiles = 0, junc = 0, grid = 0;
                foreach (var c in level.AllCells())
                {
                    if (c.X == 0 || c.Y == 0 || c.X == level.Width - 1 || c.Y == level.Height - 1) continue;
                    grid++;
                    if (level[c].IsSolid && level[c].Type == TileType.Wall) continue;
                    walk++;
                    int r = reach[level.IndexOf(c)];
                    if (r > 3 || r < 0) farTiles++;
                    maxReach = Math.Max(maxReach, r);
                    int deg = 0;
                    foreach (var d in DirExt.All) { var s = c.Step(d); if (level.InBounds(s) && level[s].Type != TileType.Wall) deg++; }
                    if (deg >= 3) junc++;
                }
                ground += (double)walk / grid; far += (double)farTiles / walk; junctions += junc; count++;
                if (farTiles > 0) wings++;
            }
        Console.WriteLine($"{act,3} | {ground / count * 100,8:0}% | {far / count * 100,6:0.0}% | {maxReach,9} | {junctions / count,9:0.0} | {wings}/{count}");
    }
    return;
}

if (args.Length >= 1 && args[0] == "--human")
{
    LevelLab.HumanReport.Run(args);
    return;
}

int variants = 20, onlyAct = 0;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--variants") variants = int.Parse(args[i + 1]);
    if (args[i] == "--act") onlyAct = int.Parse(args[i + 1]);
}

Console.WriteLine($"{variants} mazes per level");
Console.WriteLine("level | window  | par min-avg-max | mech | attempts avg/max | ms avg/max | top rejections");
var total = Stopwatch.StartNew();
foreach (var id in DifficultyTable.AllLevels())
{
    if (onlyAct != 0 && id.Act != onlyAct) continue;
    var spec = DifficultyTable.Spec(id);
    var pars = new List<int>();
    var attempts = new List<int>();
    var times = new List<long>();
    var mech = new List<int>();
    var failures = new Dictionary<string, int>();
    int failed = 0;
    for (int v = 0; v < variants; v++)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var level = LevelGenerator.Generate(spec, DifficultyTable.Seed(id, v), failures);
            pars.Add(level.Solution.Moves);
            attempts.Add(level.Attempt + 1);
            mech.Add(level.Solution.Mechanics);
        }
        catch (LevelGenerationException) { failed++; }
        times.Add(sw.ElapsedMilliseconds);
    }
    string top = string.Join(", ", failures.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}"));
    if (pars.Count == 0) { Console.WriteLine($"{id,-5} | ALL FAILED | {top}"); continue; }
    Console.WriteLine($"{id,-5} | {spec.MinMoves,2}-{spec.MaxMoves,-3} | {pars.Min(),3} {pars.Average(),5:0.0} {pars.Max(),3}   | {mech.Min()}-{mech.Max()}  | {attempts.Average(),6:0.0} {attempts.Max(),4}   | {times.Average(),5:0} {times.Max(),5} | {(failed > 0 ? $"FAILED {failed} " : "")}{top}");
}
Console.WriteLine($"total {total.ElapsedMilliseconds} ms");
