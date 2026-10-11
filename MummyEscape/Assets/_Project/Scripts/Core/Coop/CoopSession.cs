using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// One try of a two-player tomb, the same on both phones: every action of either mummy goes through
    /// <see cref="Apply"/> in the same order (the host's phone decides the order). Each mummy has its
    /// <see cref="GameSession"/> (its fog, its counters) that sees what both torches light. The pair wins once both stand
    /// on the exit; it loses as soon as one mummy dies.
    /// </summary>
    public sealed class CoopSession
    {
        public CoopTomb Tomb { get; }
        public Level Level => Tomb.Level;
        public CoopRules Rules { get; }
        public CoopState State { get; private set; }
        public SessionStatus Status { get; private set; } = SessionStatus.Playing;
        /// <summary>The mummy that died (-1: none).</summary>
        public int Fallen { get; private set; } = -1;
        readonly GameSession[] _mummies = new GameSession[2];
        readonly List<(int mummy, PlayerAction action)> _history = new List<(int, PlayerAction)>();

        public CoopSession(CoopTomb tomb)
        {
            Tomb = tomb;
            Rules = new CoopRules(tomb.Level);
            State = Rules.Initial(tomb.Starts[0], tomb.Starts[1]);
            for (int i = 0; i < 2; i++)
            {
                _mummies[i] = new GameSession(tomb.Level, State[i]);
                _mummies[i].ShareTomb();
            }
            for (int i = 0; i < 2; i++)
            {
                var other = _mummies[1 - i];
                _mummies[i].SharedLight = other.IsLitByTorch;
            }
            Sync();
        }

        /// <summary>Mummy 0 or 1: its own view of the run (fog, health, counters).</summary>
        public GameSession Mummy(int i) => _mummies[i];

        /// <summary>Actions of both mummies (the par counts them all).</summary>
        public int Moves => _mummies[0].Moves + _mummies[1].Moves;
        public IReadOnlyList<(int mummy, PlayerAction action)> History => _history;
        public int ElapsedMs => _mummies[0].ElapsedMs;

        /// <summary>Both mummies' clocks (they run together).</summary>
        public void Tick(double seconds)
        {
            _mummies[0].Tick(seconds);
            _mummies[1].Tick(seconds);
        }

        /// <summary>
        /// Applies one mummy's action. Blocked (wall, closed door, nothing to disarm in reach, run over): nothing changes.
        /// </summary>
        public StepResult Apply(int mummy, PlayerAction action)
        {
            var blocked = new StepResult { State = State[mummy], Flags = StepFlags.Blocked };
            if (Status != SessionStatus.Playing || mummy < 0 || mummy > 1) return blocked;
            var session = _mummies[mummy];
            if (action.Kind == ActionKind.Disarm && !session.CanReach(session.Position.Step(action.Dir))) return blocked;

            var r = Rules.Step(State, mummy, action, out var next);
            if (r.Has(StepFlags.Blocked)) return r;
            State = next;
            _history.Add((mummy, action));
            r.State = View(mummy);
            session.Commit(action, r);
            Sync();

            if (r.Has(StepFlags.Won)) Finish(SessionStatus.Won, DefeatCause.None);
            else if (r.Has(StepFlags.Died))
            {
                Fallen = mummy;
                Finish(SessionStatus.Dead, DefeatCause.Wounds);
            }
            return r;
        }

        /// <summary>A mummy's state as its view shows it: with the channels the pair holds right now.</summary>
        RuleState View(int mummy)
        {
            var s = State[mummy];
            s.Pressed = Rules.Pressed(State);
            return s;
        }

        void Sync()
        {
            for (int i = 0; i < 2; i++) _mummies[i].Sync(View(i));
            // What one torch lights, the other mummy remembers too.
            for (int i = 0; i < 2; i++)
            {
                var lit = _mummies[1 - i];
                var p = lit.Position;
                _mummies[i].Reveal(p);
                if (lit.State.SeesNeighbours)
                    foreach (var d in DirExt.All) _mummies[i].Reveal(p.Step(d));
            }
        }

        void Finish(SessionStatus status, DefeatCause cause)
        {
            Status = status;
            _mummies[0].End(status, cause);
            _mummies[1].End(status, cause);
        }

        /// <summary>The recap of the pair's try (moves of both, the joint par, the clock).</summary>
        public LevelResult BuildResult() => Tomb.Result(Status == SessionStatus.Won, Moves, ElapsedMs);
    }
}
