using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    public enum SessionStatus { Playing, Won, Dead }

    /// <summary>Why a run was lost.</summary>
    public enum DefeatCause { None, Wounds, Trapped }

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
        /// <summary>Play time in milliseconds (the host ticks it while the player can act; frozen once the run ends).</summary>
        public int ElapsedMs => (int)Math.Min(int.MaxValue, _elapsed * 1000.0);
        double _elapsed;

        readonly bool[] _explored;
        readonly bool[] _revealedHidden;
        readonly List<PlayerAction> _history = new List<PlayerAction>();

        /// <summary>Currents, fragile slabs or barriers: a careless move can leave no way out.</summary>
        readonly bool _canGetStuck;
        public DefeatCause Defeat { get; private set; }

        /// <summary>Raised after every accepted action, with what happened.</summary>
        public event Action<StepResult> Stepped;

        public GameSession(Level level)
        {
            Level = level;
            State = Rules.Initial(level);
            _explored = new bool[level.CellCount];
            _revealedHidden = new bool[level.CellCount];
            for (int i = 0; i < level.CellCount && !_canGetStuck; i++) _canGetStuck = level[level.CellAt(i)].IsIrreversible;
            RevealAround(State.Position);
        }

        /// <summary>Advances the run clock. Ignored once the run is over.</summary>
        public void Tick(double seconds)
        {
            if (Status == SessionStatus.Playing && seconds > 0) _elapsed += seconds;
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
            else if (r.Has(StepFlags.Died)) { Status = SessionStatus.Dead; Defeat = DefeatCause.Wounds; }
            else if (_canGetStuck && (r.Flags & (StepFlags.Swept | StepFlags.Collapsed | StepFlags.Switched)) != 0 && !Solver.CanEscape(Level, State))
            {
                // Walled in for good: no point letting the player wander, the run is over.
                r.Flags |= StepFlags.Trapped;
                Status = SessionStatus.Dead;
                Defeat = DefeatCause.Trapped;
            }

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

        public bool IsDoorOpen(Cell c) => Level.Get(c).IsGate && Rules.IsGateOpen(Level[c], State.Pressed);
        public bool IsCollapsed(Cell c) => Rules.IsCollapsed(Level.Get(c), State.Crumbled);
        /// <summary>Flame jet blasting right now.</summary>
        public bool IsFiring(Cell c) => Rules.IsFiring(Level.Get(c), State.Tick);
        /// <summary>Flame jet that will blast on the next move: stepping onto it now burns.</summary>
        public bool IsAboutToFire(Cell c) => Rules.IsFiring(Level.Get(c), (State.Tick + 1) % Rules.FlameCycle);
        public bool IsChannelActive(int channel) => (State.Pressed & (1 << channel)) != 0;
        public bool IsTrapArmed(Cell c) => Rules.IsTrapArmed(Level.Get(c), State.Disarmed);

        /// <summary>
        /// Visible armed spikes next to the mummy (traps are spaced out, so there is at most one): what the disarm
        /// button acts on. False in the dark, while blinded, and next to cursed sand (it cannot be disarmed).
        /// </summary>
        public bool CanDisarm(out Dir dir)
        {
            foreach (var d in DirExt.All)
            {
                var c = Position.Step(d);
                if (IsVisible(c) && Rules.IsDisarmable(Level.Get(c), State.Disarmed)) { dir = d; return true; }
            }
            dir = default;
            return false;
        }

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
            TimeMs = ElapsedMs,
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
