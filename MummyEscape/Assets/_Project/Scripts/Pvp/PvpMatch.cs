using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// One duel being played: the tomb drawn by the server, the rival's ghost replayed on the player's clock, and the
    /// player's own actions recorded with their time so the server can replay them.
    /// </summary>
    public sealed class PvpMatch
    {
        public readonly FindDuelResponse Duel;
        public readonly List<RunInput> Inputs = new List<RunInput>();

        public PvpMatch(FindDuelResponse duel) => Duel = duel;

        public string MatchId => Duel.MatchId;
        public int Seed => Duel.Seed;
        public GhostRun Ghost => Duel.Ghost;
        public bool HasGhost => Duel.Ghost != null;

        public Level Level { get; private set; }
        /// <summary>The rival's run, played up to the player's clock (null when the player runs first).</summary>
        public RunReplay GhostReplay { get; private set; }
        public float MyProgress { get; private set; }
        public float GhostProgress { get; private set; }
        /// <summary>The run has ended (exit, death, time out or forfeit): no more actions are recorded.</summary>
        public bool Over { get; set; }

        public void Begin(Level level)
        {
            Level = level;
            if (HasGhost) GhostReplay = new RunReplay(level, Ghost.Inputs);
        }

        /// <summary>Records an action the rules accepted, at the run time it was played (at least the minimum gap after the previous one).</summary>
        public void Record(PlayerAction action, int elapsedMs, RuleState before, GameSession session)
        {
            if (Over) return;
            int tick = RunActions.TickOf(elapsedMs);
            if (Inputs.Count > 0) tick = System.Math.Max(tick, Inputs[Inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
            Inputs.Add(new RunInput { Tick = tick, Direction = RunActions.Encode(action) });
            MyProgress = session.Status == SessionStatus.Won ? 1f
                       : PvpProgress.Of(Level, session.Status == SessionStatus.Dead ? before : session.State);
        }

        /// <summary>Moves the ghost on to the player's clock; true when it took a step.</summary>
        public bool AdvanceGhost(int elapsedMs)
        {
            if (GhostReplay == null || GhostReplay.Invalid) return false;
            var step = GhostReplay.AdvanceTo(RunActions.TickOf(elapsedMs));
            if (!step.HasValue) return false;
            GhostProgress = GhostReplay.Progress;
            return true;
        }

        /// <summary>The ghost reached the exit (or died) before this time: its final state is known.</summary>
        public bool GhostDone(int elapsedMs) => HasGhost && elapsedMs >= Ghost.TimeMs && (GhostReplay == null || !GhostReplay.HasPendingInputs);

        /// <summary>
        /// What is sent to the server. Outcome, time and progress are the ones the server will find when it replays the
        /// actions (the same code runs on both sides), so the result screen can show them before the answer comes.
        /// </summary>
        public RunSubmission BuildRun(RunOutcome outcome)
        {
            var run = new RunSubmission { MatchId = MatchId, Outcome = outcome, Inputs = new List<RunInput>(Inputs) };
            if (outcome == RunOutcome.Abandoned || Level == null) { run.Outcome = RunOutcome.Abandoned; return run; }
            run.TimeMs = outcome == RunOutcome.TimedOut || Inputs.Count == 0 ? PvpConfig.TimeLimitMs : RunActions.MsOf(Inputs[Inputs.Count - 1].Tick);
            run.Progress = MyProgress;
            var verified = RunReplay.Verify(Level, run);
            if (verified != null)
            {
                run.Outcome = verified.Outcome;
                run.TimeMs = verified.TimeMs;
                run.Progress = verified.Progress;
            }
            return run;
        }
    }
}
