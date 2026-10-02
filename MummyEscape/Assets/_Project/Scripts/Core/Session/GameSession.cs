using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    public enum SessionStatus { Playing, Won, Dead }

    /// <summary>
    /// One run through a level: wraps the pure <see cref="Rules"/> with everything the player experiences
    /// (fog of war, blindness, revealed secrets, counters). Engine agnostic so it is unit tested.
    /// </summary>
    public sealed class GameSession
    {
        public Level Level { get; }
        public RuleState State { get; private set; }
        public SessionStatus Status { get; private set; } = SessionStatus.Playing;
        public int Moves { get; private set; }
        public int Interactions { get; private set; }
        public int BlindTurnsLeft => State.Blind;
        public int TrapsTriggered { get; private set; }
        public int Teleports { get; private set; }

        readonly bool[] _explored;
        readonly bool[] _revealedHidden;
        readonly List<PlayerAction> _history = new List<PlayerAction>();

        /// <summary>Raised after every accepted action, with what happened.</summary>
        public event Action<StepResult> Stepped;

        public GameSession(Level level)
        {
            Level = level;
            State = Rules.Initial(level);
            _explored = new bool[level.CellCount];
            _revealedHidden = new bool[level.CellCount];
            RevealAround(State.Position);
        }

        public Cell Position => State.Position;
        public int Hp => State.Hp;
        public IReadOnlyList<PlayerAction> History => _history;
        public bool IsBlind => BlindTurnsLeft > 0;
        /// <summary>False after walking into dust, until the mummy passes a wall torch.</summary>
        public bool TorchLit => !State.TorchOut;

        public StepResult Move(Dir dir) => Apply(PlayerAction.Move(dir));
        public StepResult Disarm(Dir dir) => Apply(PlayerAction.Disarm(dir));

        public StepResult Apply(PlayerAction action)
        {
            if (Status != SessionStatus.Playing) return new StepResult { State = State, Flags = StepFlags.Blocked };

            // A trap can only be disarmed if the player can actually see it.
            if (action.Kind == ActionKind.Disarm && !IsVisible(Position.Step(action.Dir)))
                return new StepResult { State = State, Flags = StepFlags.Blocked };

            var r = Rules.Step(Level, State, action);
            if (r.Has(StepFlags.Blocked)) return r;

            Moves++;
            _history.Add(action);
            if (r.CountsAsInteraction) Interactions++;
            if (r.Has(StepFlags.TrapTriggered)) TrapsTriggered++;
            if (r.Has(StepFlags.Teleported)) Teleports++;

            if (r.Has(StepFlags.HiddenRevealed))
            {
                _revealedHidden[Level.IndexOf(r.SteppedOn)] = true;
                if (Level.TryGetTeleportTarget(r.SteppedOn, out var other)) _revealedHidden[Level.IndexOf(other)] = true;
            }
            if (r.Has(StepFlags.FogReset)) Array.Clear(_explored, 0, _explored.Length);

            State = r.State;
            // The tile walked on before a transport stays known (you remember the ladder you took).
            _explored[Level.IndexOf(r.SteppedOn)] = !r.Has(StepFlags.FogReset);
            RevealAround(State.Position);

            if (r.Has(StepFlags.Won)) Status = SessionStatus.Won;
            else if (r.Has(StepFlags.Died)) Status = SessionStatus.Dead;

            Stepped?.Invoke(r);
            return r;
        }

        /// <summary>Currently lit by the mummy's torch: own tile + 4 neighbours (only own tile when blind or when the torch is out).</summary>
        public bool IsVisible(Cell c)
        {
            var p = Position;
            if (c.Floor != p.Floor) return false;
            int d = Math.Abs(c.X - p.X) + Math.Abs(c.Y - p.Y);
            return d == 0 || (d == 1 && State.SeesNeighbours);
        }

        /// <summary>Seen at least once (and not wiped by a curse): drawn dimmed.</summary>
        public bool IsExplored(Cell c) => Level.InBounds(c) && _explored[Level.IndexOf(c)];

        /// <summary>What the player believes the tile is (hidden portals look like floor until found).</summary>
        public Tile PerceivedTile(Cell c)
        {
            var t = Level.Get(c);
            if (t.Type == TileType.Teleporter && t.Teleporter == TeleporterKind.Hidden && !_revealedHidden[Level.IndexOf(c)])
                return Tile.Floor;
            return t;
        }

        public bool IsDoorOpen(Cell c) => Level.Get(c).Type == TileType.Door && Rules.IsDoorOpen(Level[c], State.Pressed);
        public bool IsChannelActive(int channel) => (State.Pressed & (1 << channel)) != 0;
        public bool IsTrapArmed(Cell c) => Rules.IsTrapArmed(Level.Get(c), State.Disarmed);

        public LevelResult BuildResult() => new LevelResult
        {
            Level = Level.Id,
            Variant = Level.Variant,
            Won = Status == SessionStatus.Won,
            Moves = Moves,
            Interactions = Interactions,
            HpLeft = State.Hp,
            MaxHp = Level.MaxHp,
            Par = Level.Solution?.Moves ?? 0,
            TrapsTriggered = TrapsTriggered,
        };

        void RevealAround(Cell p)
        {
            _explored[Level.IndexOf(p)] = true;
            if (!State.SeesNeighbours) return;
            foreach (var d in DirExt.All)
            {
                var n = p.Step(d);
                if (Level.InBounds(n)) _explored[Level.IndexOf(n)] = true;
            }
        }
    }
}
