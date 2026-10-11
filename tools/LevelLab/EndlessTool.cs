using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace LevelLab
{
    /// <summary>
    /// --endless [count] [--mode F|N|X]: draws the next tombs of each endless ladder with today's generator and appends
    /// them to the stored file (never rewrites a line). Each mode's ladder grows to <c>count</c> tombs (default 300).
    /// </summary>
    static class EndlessTool
    {
        public static void Run(string[] args)
        {
            int count = args.Length > 1 && int.TryParse(args[1], out int n) ? n : 300;
            var modes = DifficultyExt.All.ToList();
            int m = Array.IndexOf(args, "--mode");
            if (m >= 0 && m + 1 < args.Length && DifficultyExt.TryParse(args[m + 1][0], out var only)) modes = new List<Difficulty> { only };

            string path = FindFile();
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            if (text.Length > 0 && !text.EndsWith("\n")) text += "\n";
            if (text.Length == 0) text = "# Mummy Rush : tombeaux stockés du mode sans fin (TombArchive, format 1). Ajout seulement : ne jamais modifier ni retirer une ligne.\n";
            File.WriteAllText(path, text);

            foreach (var mode in modes)
            {
                int have = TombArchive.Lines(text, mode).Count;
                if (have >= count) { Console.WriteLine($"{mode}: {have} tombs already"); continue; }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var lines = new string[count - have];
                Parallel.For(have, count, depth =>
                {
                    var id = Endless.Source(mode, depth, out int variant);
                    var level = LevelGenerator.Generate(id, variant);
                    string line = TombArchive.Write(level);
                    // The stored line must give back the very same tomb.
                    var back = TombArchive.Load(line);
                    if (back.ComputeHash() != level.ComputeHash() || back.Solution.Moves != level.Solution.Moves)
                        throw new InvalidOperationException($"{mode} tomb {depth + 1}: the stored line does not round-trip");
                    lines[depth - have] = line;
                });
                // Appended in depth order, after the lines already there.
                File.AppendAllText(path, string.Join("\n", lines) + "\n");
                text = File.ReadAllText(path);
                Console.WriteLine($"{mode}: tombs {have + 1}-{count} appended in {watch.Elapsed.TotalSeconds:0.0} s");
            }
            Console.WriteLine(path);
        }

        /// <summary>--halloween: draws the Halloween act's tombs once (an existing file is never rewritten).</summary>
        public static void RunHalloween()
        {
            string path = FindFile("halloween.txt");
            if (File.Exists(path)) { Console.WriteLine(path + " already exists: stored tombs never change"); return; }
            var lines = new string[Halloween.TombCount];
            Parallel.For(0, lines.Length, i =>
            {
                var level = LevelGenerator.Generate(Halloween.Source(i, out int variant), variant);
                string line = TombArchive.Write(level);
                if (TombArchive.Load(line).ComputeHash() != level.ComputeHash()) throw new InvalidOperationException($"Halloween tomb {i + 1}: the stored line does not round-trip");
                lines[i] = line;
            });
            File.WriteAllText(path, "# Mummy Rush : tombeaux stockés de l'acte Halloween (TombArchive, format 1), dans l'ordre de jeu. Ne jamais modifier ni retirer une ligne.\n"
                                    + string.Join("\n", lines) + "\n");
            for (int i = 0; i < lines.Length; i++)
            {
                var level = TombArchive.Load(lines[i]);
                Console.WriteLine($"{i + 1,2}. {level.Id.Mode.Letter()}{level.Id.Short,-5} {level.Width}x{level.Height}x{level.Floors}  par {level.Solution.Moves}");
            }
            Console.WriteLine(path);
        }

        /// <summary>The stored file, found from the repository root whatever the working directory.</summary>
        static string FindFile(string name = "endless.txt")
        {
            string rel = "MummyEscape/Assets/_Project/Resources/Tombs/" + name;
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "MummyEscape")))
                {
                    string path = Path.Combine(dir.FullName, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    return path;
                }
            throw new DirectoryNotFoundException("Run from inside the repository");
        }
    }
}
