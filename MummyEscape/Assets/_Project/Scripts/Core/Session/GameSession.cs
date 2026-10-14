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
        bool _canGetStuck;
        public DefeatCause Defeat { get; private set; }

        /// <summary>Raised after every accepted action, with what happened.</summary>
        public event Action<StepResult> Stepped;

        public GameSession(Level level) : this(level, Rules.Initial(level)) { }

        /// <summary>A run picked up from <paramref name="start"/> (the 2v2 relay: the next leg of the same mummy).</summary>
        public GameSession(Level level, RuleState start)
        {
            Level = level;
            State = start;
            _explored = new bool[level.CellCount];
            _revealedHidden = new bool[level.CellCount];
            _blasted = Rules.BlastedTiles(level);
            for (int i = 0; i < level.CellCount && !_canGetStuck; i++) _canGetStuck = level[level.CellAt(i)].IsIrreversible;
            RevealAround(State.Position);
        }

        /// <summary>Raised when a periodic trap (flame jet, idol gaze) hits the mummy in real time, with what happened.</summary>
        public event Action<StepResult> Struck;

        /// <summary>Tiles the periodic traps blast (<see cref="Rules.BlastedTiles"/>), null in a tomb without any.</summary>
        readonly bool[] _blasted;
        /// <summary>The last blast that hit the mummy: one hit per blast at most.</summary>
        long _lastHitSalvo = -1;

        /// <summary>
        /// Advances the run clock (and with it the periodic traps, on the beat of <see cref="Rules.TrapCycleSeconds"/> from
        /// the fog's fall). Ignored once the run is over.
        /// </summary>
        public void Tick(double seconds)
        {
            if (Status != SessionStatus.Playing || seconds <= 0) return;
            double before = _elapsed;
            _elapsed += seconds;
            if (_blasted == null) return;
            // Every blast that overlaps (before, now]: a long frame, or a replay jumping to the next action, stood through them all.
            long last = Rules.TrapSalvo(_elapsed);
            if (!Rules.IsTrapFiring(_elapsed)) last--;
            for (long k = Math.Max(_lastHitSalvo + 1, Rules.TrapSalvo(before)); k <= last && Status == SessionStatus.Playing; k++)
                Strike(k, Math.Max(before, k * Rules.TrapCycleSeconds + Rules.TrapChargeSeconds));
        }

        /// <summary>Seconds of play since the fog fell (pauses excluded): the clock of the periodic traps.</summary>
        public double Clock => _elapsed;
        /// <summary>The periodic traps of the tomb are blasting right now.</summary>
        public bool TrapsFiring => Rules.IsTrapFiring(_elapsed);
        /// <summary>The charge of the next blast, 0 to 1 (1 while blasting).</summary>
        public float TrapCharge => Rules.TrapCharge(_elapsed);
        /// <summary>A periodic trap blasts this tile (flame jet, or the line an idol stares along).</summary>
        public bool IsBlasted(Cell c) => _blasted != null && Level.InBounds(c) && _blasted[Level.IndexOf(c)];
        /// <summary>Flame jet or idol gaze blasting this tile right now.</summary>
        public bool IsFiring(Cell c) => TrapsFiring && IsBlasted(c);

        /// <summary>The mummy stands in a blast of number <paramref name="salvo"/> at <paramref name="at"/> s: -1 HP, once per blast.</summary>
        void Strike(long salvo, double at)
        {
            if (Status != SessionStatus.Playing || salvo == _lastHitSalvo || !IsBlasted(Position)) return;
            _lastHitSalvo = salvo;
            var s = State;
            s.Hp--;
            State = s;
            TrapsTriggered++;
            var r = new StepResult { State = s, SteppedOn = Position, Flags = StepFlags.TrapTriggered | StepFlags.Damaged | StepFlags.Burned };
            if (s.Hp <= 0)
            {
                r.Flags |= StepFlags.Died;
                Status = SessionStatus.Dead;
                Defeat = DefeatCause.Wounds;
                _elapsed = at; // the run's time is the blast that ended it (a long frame or a replay may have gone past)
            }
            Struck?.Invoke(r);
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

            // A trap can only be disarmed if the player can see it, or has walked on it before (in the dark).
            if (action.Kind == ActionKind.Disarm && !CanReach(Position.Step(action.Dir)))
                return new StepResult { State = State, Flags = StepFlags.Blocked };

            var r = Rules.Step(Level, State, action);
            if (r.Has(StepFlags.Blocked)) return r;
            return Commit(action, r);
        }

        /// <summary>Records an accepted step (its rules already applied): counters, fog, end of the run.</summary>
        internal StepResult Commit(PlayerAction action, StepResult r)
        {
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
            // Walked into a blast.
            if (TrapsFiring) Strike(Rules.TrapSalvo(_elapsed), _elapsed);
            return r;
        }

        /// <summary>Currently lit: by the mummy's torch, or (two-player campaign) by the other mummy's.</summary>
        public bool IsVisible(Cell c) => IsLitByTorch(c) || (SharedLight != null && SharedLight(c));

        /// <summary>Two-player campaign: the tiles the other mummy's torch lights, seen as well.</summary>
        public Func<Cell, bool> SharedLight { get; internal set; }

        /// <summary>Lit by the mummy's own torch: own tile + 4 neighbours (only own tile when blind or when the torch is out).</summary>
        public bool IsLitByTorch(Cell c)
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
        /// <summary>Wrappings still blocking this tile.</summary>
        public bool IsWrapped(Cell c) => Rules.IsWrapped(Level.Get(c), State.Crumbled);
        public bool IsChannelActive(int channel) => (State.Pressed & (1 << channel)) != 0;
        public bool IsTrapArmed(Cell c) => Rules.IsTrapArmed(Level.Get(c), State.Disarmed);

        /// <summary>
        /// Armed spikes next to the mummy (traps are spaced out, so there is at most one): what the disarm button acts on.
        /// Seen by torchlight, or, in the dark or while blinded, already walked on. False next to cursed sand (it cannot
        /// be disarmed).
        /// </summary>
        public bool CanDisarm(out Dir dir)
        {
            foreach (var d in DirExt.All)
            {
                var c = Position.Step(d);
                if (CanReach(c) && Rules.IsDisarmable(Level.Get(c), State.Disarmed)) { dir = d; return true; }
            }
            dir = default;
            return false;
        }

        internal bool CanReach(Cell c) => c.Floor == Position.Floor && (IsVisible(c) || Rules.CanFeelFor(State, Level.Get(c)));

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

        // ------------------------------------------------------------------ two-player campaign (CoopSession)

        /// <summary>The pair's own rules decide when a run is lost: no single-mummy "walled in" check.</summary>
        internal void ShareTomb() => _canGetStuck = false;

        /// <summary>The other mummy changed the shared tomb (doors, slabs, disarmed traps): this mummy's state follows.</summary>
        internal void Sync(RuleState s) => State = s;

        /// <summary>The pair won or lost on the other mummy's step.</summary>
        internal void End(SessionStatus status, DefeatCause cause)
        {
            if (Status != SessionStatus.Playing) return;
            Status = status;
            Defeat = cause;
        }

        /// <summary>A tile the other mummy's torch lights is remembered too.</summary>
        internal void Reveal(Cell c)
        {
            if (Level.InBounds(c)) _explored[Level.IndexOf(c)] = true;
        }

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
