using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MummyEscape.Core;
using MummyEscape.Pvp;

namespace MummyEscape.PvpSim
{
    /// <summary>
    /// Replays real duel runs (the game's duel_replays.json) on their tombs and measures how a person plays: time between
    /// swipes, swipes that bump a wall, steps that take the player further from the exit, pauses after a mistake.
    /// </summary>
    public static class RunAnalysis
    {
        sealed class Stored
        {
            public List<DuelRecord> Duels = new List<DuelRecord>();
            public List<DuelRecord> Saved = new List<DuelRecord>();
        }

        public sealed class Profile
        {
            public int Runs, Actions, Good, Bumps, Astray, Neutral;
            public readonly List<int> GapsMs = new List<int>();
            public readonly List<int> GapsAfterGoodMs = new List<int>();
            public readonly List<int> GapsAfterMistakeMs = new List<int>();
            public readonly List<int> GapsStraightMs = new List<int>();
            public readonly List<int> GapsTurnMs = new List<int>();
            public readonly List<int> FirstInputMs = new List<int>();
            public readonly List<double> MovesPerPar = new List<double>();
            public readonly List<int> MistakeStreaks = new List<int>();
            public readonly List<double> MovesPerSecond = new List<double>();
            public readonly List<double> OpeningMovesPerSecond = new List<double>(); // the first 8 actions
        }

        public static void Run(string path, string who)
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
            var duels = stored.Duels.Concat(stored.Saved).GroupBy(d => d.MatchId).Select(g => g.First()).ToList();
            var me = new Profile();
            var rivals = new Profile();
            foreach (var d in duels)
            {
                if (d.GeneratorVersion != DifficultyTable.GeneratorVersion)
                {
                    Console.WriteLine($"skip {d.MatchId}: generator v{d.GeneratorVersion}");
                    continue;
                }
                var level = PvpArena.Generate(d.Seed);
                Console.WriteLine($"duel {d.MatchId[..8]} seed {d.Seed} par {level.Solution.Moves}  fastest possible {RunTiming.MinFinishMs(level) / 1000.0:0.00}s");
                if (d.Me != null) Measure(level, d.Me, me, "  me   ");
                if (d.Rival != null) Measure(level, d.Rival, rivals, "  rival");
            }
            Print(who, me);
            if (rivals.Runs > 0) Print("rivals (bots)", rivals);
        }

        static void Measure(Level level, DuelRun run, Profile p, string label)
        {
            var (good, bumps, astray) = Measure(level, run.Inputs, p);
            var replay = new RunReplay(level, run.Inputs);
            replay.AdvanceTo(int.MaxValue);
            Console.WriteLine($"{label} {run.PlayerName,-12} {run.Outcome,-9} {run.TimeMs / 1000.0,5:0.0}s  {run.Inputs.Count,3} actions  good {good}  bumps {bumps}  astray {astray}  {(replay.TooFast ? "TOO FAST" : "timing ok")}  x{run.TimeMs / (double)(RunTiming.MinFinishMs(level) ?? 1):0.00} perfect");
        }

        /// <summary>Same measures on offline ghosts (PvpBots) of this Elo, to set HumanPlayer against real runs.</summary>
        public static void Calibrate(int elo, int count)
        {
            var rng = new Random(elo);
            var p = new Profile();
            int finished = 0;
            var times = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var ghost = PvpBots.Make(rng, elo, 0);
                Measure(PvpArena.Generate(ghost.Seed), ghost.Inputs, p);
                if (ghost.Outcome == RunOutcome.Finished) { finished++; times.Add(ghost.TimeMs); }
            }
            Print($"bots around {elo} Elo", p);
            Console.WriteLine($"   finished {Pct(finished, count)}  time ms {Stats(times)}");
            Console.WriteLine($"   swipe gap straight {Stats(p.GapsStraightMs)}");
            Console.WriteLine($"   swipe gap turn     {Stats(p.GapsTurnMs)}");
        }

        static (int good, int bumps, int astray) Measure(Level level, IList<RunInput> inputs, Profile p)
        {
            var session = new GameSession(level);
            int dist = Solver.Solve(level, session.State, SolverOptions.Default)?.Moves ?? 0;
            int lastMs = -1, streak = 0;
            bool lastWasMistake = false;
            int good = 0, bumps = 0, astray = 0;
            int lastDir = 0;
            foreach (var input in inputs)
            {
                if (session.Status != SessionStatus.Playing) break;
                if (!RunActions.TryDecode(input.Direction, out var action)) continue;
                int ms = RunActions.MsOf(input.Tick);
                if (lastMs < 0) p.FirstInputMs.Add(ms);
                else
                {
                    int gap = ms - lastMs;
                    p.GapsMs.Add(gap);
                    (lastWasMistake ? p.GapsAfterMistakeMs : p.GapsAfterGoodMs).Add(gap);
                    (input.Direction == lastDir ? p.GapsStraightMs : p.GapsTurnMs).Add(gap);
                }
                lastDir = input.Direction;
                lastMs = ms;

                var r = session.Apply(action);
                p.Actions++;
                if (r.Has(StepFlags.Blocked))
                {
                    p.Bumps++; bumps++; streak++; lastWasMistake = true;
                    continue;
                }
                int after = session.Status == SessionStatus.Won ? 0
                          : Solver.Solve(level, session.State, SolverOptions.Default)?.Moves ?? dist + 1;
                if (after < dist) { p.Good++; good++; if (streak > 0) p.MistakeStreaks.Add(streak); streak = 0; lastWasMistake = false; }
                else if (after > dist) { p.Astray++; astray++; streak++; lastWasMistake = true; }
                else { p.Neutral++; lastWasMistake = false; }
                dist = after;
            }
            p.Runs++;
            if (level.Solution.Moves > 0) p.MovesPerPar.Add(inputs.Count / (double)level.Solution.Moves);
            if (inputs.Count > 1)
                p.MovesPerSecond.Add(inputs.Count / Math.Max(0.5, RunActions.MsOf(inputs[inputs.Count - 1].Tick) / 1000.0));
            if (inputs.Count > 8)
                p.OpeningMovesPerSecond.Add(8 / Math.Max(0.5, RunActions.MsOf(inputs[7].Tick) / 1000.0));
            return (good, bumps, astray);
        }

        static void Print(string who, Profile p)
        {
            Console.WriteLine();
            Console.WriteLine($"== {who}: {p.Runs} runs, {p.Actions} actions");
            if (p.Actions == 0) return;
            Console.WriteLine($"   good steps {Pct(p.Good, p.Actions)}  wall bumps {Pct(p.Bumps, p.Actions)}  steps astray {Pct(p.Astray, p.Actions)}  other {Pct(p.Neutral, p.Actions)}");
            Console.WriteLine($"   swipe gap ms     {Stats(p.GapsMs)}");
            Console.WriteLine($"   after good step  {Stats(p.GapsAfterGoodMs)}");
            Console.WriteLine($"   after a mistake  {Stats(p.GapsAfterMistakeMs)}");
            Console.WriteLine($"   straight swipe   {Stats(p.GapsStraightMs)}");
            Console.WriteLine($"   turning swipe    {Stats(p.GapsTurnMs)}");
            Console.WriteLine($"   first swipe ms   {Stats(p.FirstInputMs)}");
            Console.WriteLine($"   moves / second   {Rates(p.MovesPerSecond)}");
            Console.WriteLine($"   opening (8 acts) {Rates(p.OpeningMovesPerSecond)}");
            Console.WriteLine(p.MovesPerPar.Count <= 10
                ? $"   actions / par    {string.Join(" ", p.MovesPerPar.Select(x => x.ToString("0.00")))}"
                : $"   actions / par    mean {p.MovesPerPar.Average():0.00}  max {p.MovesPerPar.Max():0.00}");
            Console.WriteLine(p.MistakeStreaks.Count <= 20
                ? $"   mistake streaks  {string.Join(" ", p.MistakeStreaks)}"
                : $"   mistake streaks  n {p.MistakeStreaks.Count}  mean {p.MistakeStreaks.Average():0.0}  max {p.MistakeStreaks.Max()}");
        }

        static string Rates(List<double> xs) => xs.Count == 0 ? "-"
            : xs.Count <= 10 ? string.Join(" ", xs.Select(x => x.ToString("0.00")))
            : $"median {xs.OrderBy(x => x).ElementAt(xs.Count / 2):0.00}  mean {xs.Average():0.00}";

        static string Pct(int n, int total) => $"{n} ({100.0 * n / total:0}%)";

        static string Stats(List<int> xs)
        {
            if (xs.Count == 0) return "-";
            var s = xs.OrderBy(x => x).ToList();
            int Q(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
            return $"n {s.Count,3}  p10 {Q(0.1),5}  median {Q(0.5),5}  p90 {Q(0.9),5}  mean {s.Average(),6:0}";
        }
    }
}
