using System.Collections;
using MummyEscape.App;
using MummyEscape.Core;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Plays the optimal solution of the current run in Play mode, to reach a situation quickly before a capture:
    ///   unity command mummy_autoplay --until torch_out
    /// </summary>
    public static class MummyAutoplayCommand
    {
        [CliCommand("mummy_autoplay", "Mummy Escape (Play mode): skip the map preview and play the optimal route (optionally stop at an event).", Tags = new[] { "mummy" })]
        public static string Autoplay(
            [CliArg("steps", "Maximum number of actions to play")] int steps = 9999,
            [CliArg("until", "Stop after: torch_out, torch_relit, button, teleport, or empty to play everything")] string until = "")
        {
            if (!Application.isPlaying || GameApp.I == null || GameApp.I.Game.Session == null) return "Start a level in Play mode first.";
            Application.runInBackground = true;
            var runner = GameApp.I.gameObject.GetComponent<AutoplayRunner>() ?? GameApp.I.gameObject.AddComponent<AutoplayRunner>();
            runner.StopAllCoroutines();
            runner.StartCoroutine(runner.Run(steps, until));
            return $"Autoplay started ({GameApp.I.Game.Session.Level.Solution.Moves} moves in the solution).";
        }

        sealed class AutoplayRunner : MonoBehaviour
        {
            public IEnumerator Run(int steps, string until)
            {
                var game = GameApp.I.Game;
                game.SkipPreview();
                while (game.Previewing) yield return null;
                yield return new WaitForSeconds(0.4f);
                var session = game.Session;
                var actions = session.Level.Solution.Actions;
                for (int i = session.Moves; i < actions.Count && steps-- > 0; i++)
                {
                    bool lit = session.TorchLit;
                    int pressed = session.State.Pressed, teleports = session.Teleports;
                    game.Submit(actions[i]);
                    yield return new WaitForSeconds(0.45f);
                    if (until == "torch_out" && lit && !session.TorchLit) break;
                    if (until == "torch_relit" && !lit && session.TorchLit) break;
                    if (until == "button" && session.State.Pressed != pressed) break;
                    if (until == "teleport" && session.Teleports != teleports) break;
                }
            }
        }
    }
}
