using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// Records the actions of a run with their time, the way the server replays them (<see cref="RunReplay"/>): never
    /// before the previous action's animation is over, even by a rounding. Used by duels and by solo runs sent for the pass.
    /// </summary>
    public sealed class RunRecorder
    {
        readonly Level _level;
        readonly RunClock _clock = new RunClock();
        public readonly List<RunInput> Inputs;

        public RunRecorder(Level level, List<RunInput> inputs = null)
        {
            _level = level;
            Inputs = inputs ?? new List<RunInput>();
        }

        /// <summary>An action the rules accepted, at the run time it was played.</summary>
        public void Record(PlayerAction action, int elapsedMs, RuleState before)
        {
            int tick = RunActions.TickOf(elapsedMs);
            if (Inputs.Count > 0) tick = System.Math.Max(tick, Inputs[Inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
            tick = System.Math.Max(tick, _clock.MinTick);
            _clock.Accept(tick, RunTiming.MinGapMs(_level, before, action, Rules.Step(_level, before, action)));
            Inputs.Add(new RunInput { Tick = tick, Direction = RunActions.Encode(action) });
        }
    }
}
