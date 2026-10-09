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
        [CliCommand("mummy_levels", "Mummy Rush: generate every level and report par, interactions and floors.", Tags = new[] { "mummy" })]
        public static string Levels()
        {
            var sb = new StringBuilder("level | par [window] | buttons interactions hp | floors size\n");
            foreach (var id in DifficultyTable.AllLevels())
            {
                var spec = DifficultyTable.Spec(id);
                var l = LevelGenerator.Generate(id);
                var s = l.Solution;
                sb.AppendLine($"{id} | {s.Moves} [{spec.MinMoves}-{spec.MaxMoves}] | {s.ButtonsPressed} {s.Interactions} {s.HpLeft} | {spec.Floors} {spec.Width}x{spec.Height} | {DifficultyScore.Of(l)}");
            }
            return sb.ToString();
        }

        [CliCommand("mummy_level", "Mummy Rush: ASCII map and optimal solution of one level (e.g. --id N2-5: mode F, N or X, then act-index).", Tags = new[] { "mummy" })]
        public static string Level([CliArg("id", "Level id mode+act-index, e.g. F1-3, N2-5, X5-1 (F when the letter is left out)")] string id = "F1-1",
                                   [CliArg("variant", "Maze number (every run of a level draws the next one)")] int variant = 0)
        {
            if (!LevelId.TryParse(id, out var lid)) return $"Unknown level id {id} (expected F1-3, N2-5, X5-1)";
            var l = LevelGenerator.Generate(lid, variant);
            return $"{l.Spec}\ndifficulty {DifficultyScore.Of(l)}\npar {l.Solution.Moves}, interactions {l.Solution.Interactions}, hp left {l.Solution.HpLeft}\n" +
                   $"solution: {string.Join(" ", l.Solution.Actions)}\n\n{l.ToAscii()}";
        }

        [CliCommand("mummy_setup", "Mummy Rush: create/refresh the Main scene, lit sprite material and mobile player settings.", Tags = new[] { "mummy" })]
        public static string Setup()
        {
            ProjectSetup.Run();
            return "Setup done: " + ProjectSetup.ScenePath;
        }

        [CliCommand("mummy_build_play", "Mummy Rush: build the signed Google Play App Bundle (Builds/Android/MummyRush.aab).", Tags = new[] { "mummy" })]
        public static string BuildPlay()
        {
            BuildScript.GooglePlay();
            return "AAB: " + System.IO.Path.GetFullPath(BuildScript.BundlePath);
        }

        [CliCommand("mummy_signing_create", "Mummy Rush: create the Google Play upload key in ~/.mummyescape (never overwrites).", Tags = new[] { "mummy" })]
        public static string CreateSigning() => Signing.Create();

        [CliCommand("mummy_loc_check", "Mummy Rush: list player-facing texts without translation and interpolations to wrap in Loc.F.", Tags = new[] { "mummy" })]
        public static string LocCheck() => EditorTools.LocCheck.Report();

        [CliCommand("mummy_build_android", "Mummy Rush: build a development APK to Builds/Android/MummyRush.apk.", Tags = new[] { "mummy" })]
        public static string BuildAndroid()
        {
            BuildScript.AndroidDev();
            return "APK: " + System.IO.Path.GetFullPath(BuildScript.AndroidPath);
        }
    }
}
