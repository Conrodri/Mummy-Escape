using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Two mummies in the same tomb (the 2v2 campaign). Each keeps its own position, health, sight and rhythm; the tomb
    /// is shared: levers and buttons pressed, spikes disarmed and slabs collapsed are the same for both (the shared
    /// fields are kept equal in <see cref="A"/> and <see cref="B"/>). <see cref="RuleState.Pressed"/> holds the latched
    /// channels only: held plates count while a mummy stands on them (<see cref="CoopRules.Pressed"/>).
    /// </summary>
    public struct CoopState : IEquatable<CoopState>
    {
        public RuleState A, B;

        public RuleState this[int mummy] => mummy == 0 ? A : B;

        public CoopState With(int mummy, RuleState s)
        {
            var c = this;
            if (mummy == 0) c.A = s; else c.B = s;
            return c;
        }

        public bool Equals(CoopState o) => A.Equals(o.A) && B.Equals(o.B);
        public override bool Equals(object obj) => obj is CoopState s && Equals(s);
        public override int GetHashCode() => A.GetHashCode() * 31 ^ B.GetHashCode();
    }

    /// <summary>
    /// The two-player extension of <see cref="Rules"/>: a step of one mummy is the solo step, played with the channels
    /// the pair holds, then shared with the other mummy. Three things differ from solo:
    /// a held plate (a <see cref="TileType.Button"/> with <see cref="HeldPlate"/>) keeps its channel on only while a
    /// mummy stands on it; the tomb's changes reach both mummies; and the exit lets the pair out only once both stand on it.
    /// Mummies do not block each other (they may share a tile).
    /// </summary>
    public sealed class CoopRules
    {
        /// <summary><see cref="Tile.Param"/> of a button that is a held plate (0: an ordinary button, which latches).</summary>
        public const byte HeldPlate = 1;

        public readonly Level Level;
        /// <summary>Channels driven by held plates (never latched).</summary>
        public readonly int HeldChannels;
        readonly List<Cell> _plates = new List<Cell>();

        public CoopRules(Level level)
        {
            Level = level;
            foreach (var c in level.AllCells())
            {
                if (!IsHeldPlate(level[c])) continue;
                _plates.Add(c);
                HeldChannels |= 1 << level[c].Channel;
            }
        }

        public static bool IsHeldPlate(Tile t) => t.Type == TileType.Button && t.Param == HeldPlate;

        public CoopState Initial(Cell startA, Cell startB) => new CoopState
        {
            A = new RuleState { Position = startA, Hp = Level.MaxHp },
            B = new RuleState { Position = startB, Hp = Level.MaxHp },
        };

        /// <summary>Channels on in this state: latched ones, and the held plates a mummy stands on.</summary>
        public int Pressed(CoopState s) => s.A.Pressed | Held(s.A.Position, s.B.Position);

        int Held(Cell a, Cell b)
        {
            int held = 0;
            foreach (var p in _plates)
                if (p == a || p == b) held |= 1 << Level[p].Channel;
            return held;
        }

        public bool OnExit(Cell c) => Level.Get(c).Type == TileType.Exit;

        /// <summary>
        /// One mummy's action. The result is the solo step (its flags, its walk), with <see cref="StepFlags.Won"/> only
        /// when it leaves both mummies on the exit. Blocked: the state is unchanged.
        /// </summary>
        public StepResult Step(CoopState s, int mummy, PlayerAction action, out CoopState next)
        {
            var me = s[mummy];
            var mate = s[1 - mummy];
            var input = me;
            input.Pressed = me.Pressed | Held(me.Position, mate.Position);
            var r = Rules.Step(Level, input, action);
            if (r.Has(StepFlags.Blocked))
            {
                r.State = me;
                next = s;
                return r;
            }
            var mine = r.State;
            mine.Pressed &= ~HeldChannels; // a plate never stays down by itself
            mate.Pressed = mine.Pressed;
            mate.Disarmed = mine.Disarmed;
            mate.Crumbled = mine.Crumbled;
            if (OnExit(mine.Position) && OnExit(mate.Position)) r.Flags |= StepFlags.Won;
            else r.Flags &= ~StepFlags.Won;
            next = s.With(mummy, mine).With(1 - mummy, mate);
            r.State = mine;
            return r;
        }
    }
}
