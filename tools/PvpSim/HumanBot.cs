using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;

namespace MummyEscape.PvpSim
{
    /// <summary>
    /// A player who plays like a person: remembers the preview imperfectly (takes a wrong branch at some crossings,
    /// more often as the minutes pass), sometimes swipes the wrong way, is thrown by a turned tomb or reversed controls,
    /// thinks before each step and hesitates at crossings. Skill 0..1 sets all of it.
    /// </summary>
    public sealed class HumanBot
    {
        public readonly string Id;
        public readonly string Name;
        public readonly double Skill;
        /// <summary>Duels a day this player tends to play.</summary>
        public readonly int Activity;

        readonly double _memory;       // chance to pick the right branch at a crossing, just after the preview
        readonly double _misswipe;     // chance a swipe goes the wrong way
        readonly double _reactionMs;   // usual time to decide a step
        readonly double _decayPerMin;  // memory fading, per minute of run

        public HumanBot(string id, string name, double skill, int activity, Random rng)
        {
            Id = id; Name = name; Skill = skill; Activity = activity;
            _memory = 0.70 + 0.28 * skill + (rng.NextDouble() - 0.5) * 0.06;
            _misswipe = 0.075 - 0.065 * skill;
            _reactionMs = 750 - 480 * skill + (rng.NextDouble() - 0.5) * 80;
            _decayPerMin = 0.10 - 0.07 * skill;
        }

        public sealed class RunStats
        {
            public int Lapses, Misswipes, Confusions, Bumps, Actions;
            public bool Quit;
        }

        /// <summary>Plays the duel tomb; returns the timestamped inputs the game would send, and how it went.</summary>
        public (List<RunInput> inputs, RunOutcome outcome, RunStats stats) Play(Level level, Random rng)
        {
            var stats = new RunStats();
            var session = new GameSession(level);
            var inputs = new List<RunInput>();
            double t = 400 + rng.NextDouble() * 700 * (1.2 - Skill); // getting one's bearings as the fog falls
            List<PlayerAction> plan = null;
            int planAt = 0;
            var wrongTried = new HashSet<Cell>();
            Cell? previous = null;
            int confusedSteps = 0;
            bool quitter = rng.NextDouble() < 0.015; // rage-quits now and then
            int quitAfter = rng.Next(5, 40);

            // False once the clock has run out.
            bool Apply(PlayerAction action, double thinkMs)
            {
                t += Math.Max(130, thinkMs); // a step is animated for 0.13 s: the next swipe waits for it
                if (t > PvpConfig.TimeLimitMs) return false;
                var before = session.State;
                var r = session.Apply(action);
                if (r.Has(StepFlags.Blocked)) { stats.Bumps++; t += 140; return true; }
                int tick = RunActions.TickOf((int)t);
                if (inputs.Count > 0) tick = Math.Max(tick, inputs[inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
                inputs.Add(new RunInput { Tick = tick, Direction = RunActions.Encode(action) });
                stats.Actions++;
                if (!before.Position.Equals(session.Position)) previous = before.Position;
                if (r.Has(StepFlags.Rotated)) { confusedSteps = 6; t += 900 + 900 * (1 - Skill); }
                if (r.Has(StepFlags.Reversed)) t += 500;
                if (r.Has(StepFlags.TrapTriggered) || r.Has(StepFlags.Teleported) || r.Has(StepFlags.Climbed) || r.Has(StepFlags.Fell))
                    t += 300 + rng.NextDouble() * 500;
                return true;
            }

            double Think()
            {
                double ms = _reactionMs * Math.Exp((rng.NextDouble() - 0.5) * 0.6);
                if (!session.State.SeesNeighbours) ms *= 1.5; // in the dark
                if (rng.NextDouble() < 0.08) ms += _reactionMs * (1 + rng.NextDouble() * 3); // a look at the map, a doubt
                return ms;
            }

            Dir SwipeFor(Dir world) // the swipe that walks this way in the tomb now
            {
                var s = world.Turn(session.State.Rotation);
                return session.State.Reversed > 0 ? s.Opposite() : s;
            }

            while (session.Status == SessionStatus.Playing)
            {
                if (quitter && stats.Actions >= quitAfter) { stats.Quit = true; return (inputs, RunOutcome.Abandoned, stats); }
                if (plan == null || planAt >= plan.Count)
                {
                    var sol = Solver.Solve(level, session.State, SolverOptions.Default);
                    if (sol == null || sol.Actions.Count == 0) break; // nothing left to do: the clock runs out
                    plan = sol.Actions;
                    planAt = 0;
                }
                var next = plan[planAt];
                var pos = session.Position;
                double think = Think();

                if (next.Kind != ActionKind.Move)
                {
                    if (!Apply(next, think)) return (inputs, RunOutcome.TimedOut, stats);
                    planAt++;
                    continue;
                }

                var intended = session.State.WorldDir(next.Dir);
                var open = new List<Dir>();
                foreach (var d in DirExt.All)
                {
                    var n = pos.Step(d);
                    if (Rules.CanEnter(level, n, session.State) && (!previous.HasValue || !n.Equals(previous.Value))) open.Add(d);
                }
                double recall = Math.Max(0.4, _memory - _decayPerMin * t / 60000.0);

                // A crossing: the memory of the preview may point down the wrong corridor.
                if (open.Count >= 2)
                {
                    think += _reactionMs * 0.4 * rng.NextDouble();
                    var wrong = open.FindAll(d => d != intended && !wrongTried.Contains(pos.Step(d)));
                    if (wrong.Count > 0 && rng.NextDouble() > recall)
                    {
                        var d = wrong[rng.Next(wrong.Count)];
                        wrongTried.Add(pos.Step(d));
                        stats.Lapses++;
                        int depth = 1 + rng.Next(4);
                        for (int k = 0; k < depth && session.Status == SessionStatus.Playing; k++)
                        {
                            if (!Rules.CanEnter(level, session.Position.Step(d), session.State)) break;
                            if (!Apply(PlayerAction.Move(SwipeFor(d)), k == 0 ? think : Think() * 0.6))
                                return (inputs, RunOutcome.TimedOut, stats);
                        }
                        t += 400 + rng.NextDouble() * 800; // "not this way"
                        plan = null;
                        continue;
                    }
                }

                // Turned tomb or reversed controls: the hand swipes the old way.
                var swipe = next.Dir;
                bool confused = (confusedSteps > 0 && rng.NextDouble() < 0.10 + 0.30 * (1 - Skill))
                                || (session.State.Reversed > 0 && rng.NextDouble() < 0.08 + 0.25 * (1 - Skill));
                if (confused)
                {
                    // The swipe the player would make if nothing had changed.
                    swipe = session.State.Reversed > 0 ? swipe.Opposite() : intended;
                    if (swipe != next.Dir) stats.Confusions++;
                }
                else if (rng.NextDouble() < _misswipe)
                {
                    swipe = swipe.Turn(rng.Next(2) == 0 ? 1 : -1);
                    stats.Misswipes++;
                }
                if (confusedSteps > 0) confusedSteps--;

                var stateBefore = session.State;
                if (!Apply(PlayerAction.Move(swipe), think)) return (inputs, RunOutcome.TimedOut, stats);
                if (swipe != next.Dir)
                {
                    if (!session.State.Equals(stateBefore)) plan = null; // went astray: plan again from here
                    continue;
                }
                planAt++;
            }
            var outcome = session.Status == SessionStatus.Won ? RunOutcome.Finished
                        : session.Status == SessionStatus.Dead ? RunOutcome.Died
                        : RunOutcome.TimedOut;
            return (inputs, outcome, stats);
        }
    }
}
