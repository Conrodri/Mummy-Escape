using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MummyEscape.Core;

// Usage: dotnet run --project tools/LevelLab            -> table of every level (full search, no baked hints)
//        dotnet run --project tools/LevelLab -- 2-5     -> ASCII map + optimal path of level 2-5
//        dotnet run --project tools/LevelLab -- --bake  -> regenerate Core/Generation/LevelAttemptTable.cs
if (args.Length == 1 && args[0] == "--bake")
{
    var sb = new StringBuilder();
    foreach (var id in DifficultyTable.AllLevels())
    {
        var level = LevelGenerator.GenerateFromScratch(id);
        sb.AppendLine($"            {{ {id.Act * 1000 + id.Index}, {level.Attempt} }}, // {id} par {level.Solution.Moves}");
    }
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../MummyEscape/Assets/_Project/Scripts/Core/Generation/LevelAttemptTable.cs"));
    string src = File.ReadAllText(path);
    int open = src.IndexOf("        {\n", src.IndexOf("Attempts = new", StringComparison.Ordinal), StringComparison.Ordinal) + "        {\n".Length;
    int close = src.IndexOf("        };", open, StringComparison.Ordinal);
    src = src.Substring(0, open) + sb + src.Substring(close);
    src = System.Text.RegularExpressions.Regex.Replace(src, @"GeneratorVersion = \d+;", $"GeneratorVersion = {DifficultyTable.GeneratorVersion};");
    File.WriteAllText(path, src);
    Console.WriteLine($"Baked {path}");
    return;
}

if (args.Length == 1 && args[0].Contains("-"))
{
    var parts = args[0].Split('-');
    var id = new LevelId(int.Parse(parts[0]), int.Parse(parts[1]));
    var level = LevelGenerator.Generate(id);
    Console.WriteLine(DifficultyTable.Spec(id));
    Console.WriteLine($"par {level.Solution.Moves} | interactions {level.Solution.Interactions} | hp left {level.Solution.HpLeft} | attempt {level.Attempt}");
    Console.WriteLine("solution: " + string.Join(" ", level.Solution.Actions));
    var path = new System.Collections.Generic.HashSet<Cell>();
    var s = Rules.Initial(level);
    foreach (var a in level.Solution.Actions) { s = Rules.Step(level, s, a).State; path.Add(s.Position); }
    Console.WriteLine(level.ToAscii(path));
    return;
}

Console.WriteLine("level | par  window  | btn int hp | floors size | tp | attempt | ms");
var total = Stopwatch.StartNew();
foreach (var id in DifficultyTable.AllLevels())
{
    var sw = Stopwatch.StartNew();
    var spec = DifficultyTable.Spec(id);
    Level level;
    try { level = LevelGenerator.GenerateFromScratch(id); }
    catch (LevelGenerationException e) { Console.WriteLine($"{id,-5} | FAILED {e.Message}"); continue; }
    var sol = level.Solution;
    Console.WriteLine($"{id,-5} | {sol.Moves,3} [{spec.MinMoves,2}-{spec.MaxMoves,2}] | {sol.ButtonsPressed,3} {sol.Interactions,3} {sol.HpLeft,2} | {spec.Floors,6} {spec.Width,2}x{spec.Height,-2} | {spec.Teleporters.Count,2} | {level.Attempt,7} | {sw.ElapsedMilliseconds}");
}
Console.WriteLine($"total {total.ElapsedMilliseconds} ms");
