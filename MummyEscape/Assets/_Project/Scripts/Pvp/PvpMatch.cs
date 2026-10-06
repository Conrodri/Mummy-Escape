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

        /// <summary>
        /// A live duel: the rival runs at the same time. His actions arrive as he plays them (<see cref="AddRivalInput"/>)
        /// and are replayed on the player's clock like a ghost; against a bot, its run is known in advance.
        /// </summary>
        public PvpMatch(LiveDuel live, string me)
        {
            Live = live;
            var mine = live.SideOf(me);
            var other = live.OtherSide(me);
            var rival = live.VsBot ? live.BotRun : new GhostRun { PlayerId = other.PlayerId, Inputs = new List<RunInput>(), TimeMs = int.MaxValue };
            rival.PlayerName = other.Name;
            rival.Elo = other.Elo;
            rival.Look = other.Look;
            Duel = new FindDuelResponse { MatchId = live.Id, Seed = live.Seed, MyElo = mine.Elo, Ghost = rival };
        }

        /// <summary>The live duel, null for a duel against a recorded ghost or a team battle round.</summary>
        public LiveDuel Live { get; }
        public bool IsLive => Live != null;
        /// <summary>The rival left the live duel (forfeit or lost connection): the player wins.</summary>
        public bool RivalQuit { get; set; }

        /// <summary>An action of the live rival, as he played it (in order, on his clock).</summary>
        public void AddRivalInput(int tick, int direction)
        {
            if (!IsLive || Live.VsBot) return;
            var inputs = Ghost.Inputs;
            if (inputs.Count > 0 && tick < inputs[inputs.Count - 1].Tick) return;
            inputs.Add(new RunInput { Tick = tick, Direction = direction });
        }

        /// <summary>The rival is out of the tomb or dead, on the player's clock: in a live duel, it is over.</summary>
        public bool RivalOver => GhostReplay != null && GhostReplay.IsOver;

        /// <summary>The rival's run as far as the player saw it (outcome, time, progress).</summary>
        public (RunOutcome Outcome, int TimeMs, float Progress) RivalSoFar()
        {
            if (RivalQuit) return (RunOutcome.Abandoned, 0, GhostProgress);
            if (GhostReplay == null) return (RunOutcome.TimedOut, PvpConfig.TimeLimitMs, 0f);
            var status = GhostReplay.Session.Status;
            int time = RunActions.MsOf(GhostReplay.LastTick);
            if (status == SessionStatus.Won) return (RunOutcome.Finished, time, 1f);
            if (status == SessionStatus.Dead) return (RunOutcome.Died, time, GhostReplay.Progress);
            return (RunOutcome.TimedOut, time, GhostReplay.Progress);
        }

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
        readonly RunClock _clock = new RunClock();

        public void Record(PlayerAction action, int elapsedMs, RuleState before, GameSession session)
        {
            if (Over) return;
            int tick = RunActions.TickOf(elapsedMs);
            if (Inputs.Count > 0) tick = System.Math.Max(tick, Inputs[Inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
            // Jamais avant la fin de l'animation précédente, même à un arrondi près : le serveur la vérifie (RunTiming).
            tick = System.Math.Max(tick, _clock.MinTick);
            _clock.Accept(tick, RunTiming.MinGapMs(Level, before, action, Rules.Step(Level, before, action)));
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
