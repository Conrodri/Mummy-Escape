using System.Text;
using MummyEscape.Core;
using Unity.Pipeline.Commands;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Project commands exposed to the Unity CLI and the Unity MCP server (so an AI agent can inspect levels):
    ///   unity command mummy_levels           -> difficulty report of every level
    ///   unity command mummy_level --id 2-5   -> ASCII map + optimal solution of one level
    ///   unity command mummy_setup            -> (re)build the Main scene and player settings
    /// </summary>
    public static class MummyCliCommands
    {
        [CliCommand("mummy_levels", "Mummy Escape: generate every level and report par, interactions and floors.", Tags = new[] { "mummy" })]
        public static string Levels()
        {
            var sb = new StringBuilder("level | par [window] | buttons interactions hp | floors size\n");
            foreach (var id in DifficultyTable.AllLevels())
            {
                var spec = DifficultyTable.Spec(id);
                var l = LevelGenerator.Generate(id);
                var s = l.Solution;
                sb.AppendLine($"{id} | {s.Moves} [{spec.MinMoves}-{spec.MaxMoves}] | {s.ButtonsPressed} {s.Interactions} {s.HpLeft} | {spec.Floors} {spec.Width}x{spec.Height}");
            }
            return sb.ToString();
        }

        [CliCommand("mummy_level", "Mummy Escape: ASCII map and optimal solution of one level (e.g. --id 2-5).", Tags = new[] { "mummy" })]
        public static string Level([CliArg("id", "Level id act-index, e.g. 1-3")] string id = "1-1",
                                   [CliArg("variant", "Maze number (every run of a level draws the next one)")] int variant = 0)
        {
            var parts = id.Split('-');
            var lid = new LevelId(int.Parse(parts[0]), int.Parse(parts[1]));
            var l = LevelGenerator.Generate(lid, variant);
            return $"{l.Spec}\npar {l.Solution.Moves}, interactions {l.Solution.Interactions}, hp left {l.Solution.HpLeft}\n" +
                   $"solution: {string.Join(" ", l.Solution.Actions)}\n\n{l.ToAscii()}";
        }

        [CliCommand("mummy_setup", "Mummy Escape: create/refresh the Main scene, lit sprite material and mobile player settings.", Tags = new[] { "mummy" })]
        public static string Setup()
        {
            ProjectSetup.Run();
            return "Setup done: " + ProjectSetup.ScenePath;
        }

        [CliCommand("mummy_build_play", "Mummy Escape: build the signed Google Play App Bundle (Builds/Android/MummyEscape.aab).", Tags = new[] { "mummy" })]
        public static string BuildPlay()
        {
            BuildScript.GooglePlay();
            return "AAB: " + System.IO.Path.GetFullPath(BuildScript.BundlePath);
        }

        [CliCommand("mummy_signing_create", "Mummy Escape: create the Google Play upload key in ~/.mummyescape (never overwrites).", Tags = new[] { "mummy" })]
        public static string CreateSigning() => Signing.Create();

        [CliCommand("mummy_build_android", "Mummy Escape: build a development APK to Builds/Android/MummyEscape.apk.", Tags = new[] { "mummy" })]
        public static string BuildAndroid()
        {
            BuildScript.AndroidDev();
            return "APK: " + System.IO.Path.GetFullPath(BuildScript.AndroidPath);
        }
    }
}
