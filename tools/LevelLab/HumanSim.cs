using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace LevelLab
{
    /// <summary>How a human plays: imperfect memory of the preview, wrong turns, time spent per swipe and per hesitation.</summary>
    sealed class HumanProfile
    {
        public string Name;
        /// <summary>Chance to remember a plain tile (wall or floor) after the preview. A forgotten wall is imagined open.</summary>
        public double Memory;
        /// <summary>Chance to remember a point of interest (button, door, portal, ladder, trap, current...).</summary>
        public double PoiMemory;
        /// <summary>Memory penalty for floors other than the starting one (shown earlier in the preview).</summary>
        public double OtherFloor = 0.75;
        /// <summary>Chance to take a wrong branch at a junction (then follow it a few tiles before noticing).</summary>
        public double WrongTurn;
        /// <summary>Seconds per confident swipe (input + 0.13 s walk animation + glance).</summary>
        public double MoveSec;
        /// <summary>Seconds of hesitation at a junction, or when something unexpected forces a new plan.</summary>
        public double ThinkSec;

        /// <summary>Sanity check of the agent: perfect memory, no wrong turn (should play at par). Only with --profile omniscient.</summary>
        public static readonly HumanProfile Omniscient = new HumanProfile { Name = "omniscient", Memory = 1, PoiMemory = 1, OtherFloor = 1, WrongTurn = 0, MoveSec = 0.45, ThinkSec = 1.2 };

        public static readonly HumanProfile[] All =
        {
            new HumanProfile { Name = "attentif", Memory = 0.65, PoiMemory = 0.85, WrongTurn = 0.30, MoveSec = 0.45, ThinkSec = 1.2 },
            new HumanProfile { Name = "moyen", Memory = 0.45, PoiMemory = 0.70, WrongTurn = 0.50, MoveSec = 0.55, ThinkSec = 1.8 },
            new HumanProfile { Name = "distrait", Memory = 0.25, PoiMemory = 0.50, WrongTurn = 0.65, MoveSec = 0.65, ThinkSec = 2.5 },
        };
    }

    sealed class RunStats
    {
        public bool Won;
        public DefeatCause Defeat;
        public bool GaveUp;
        public string Reason;
        public RuleState Final;
        public int Moves;
        public double Seconds;
        public int WrongTurns;
        public int Bumps;
    }

    /// <summary>
    /// Plays one run with a human-like agent driving the real <see cref="GameSession"/>. The agent plans on its own
    /// belief of the tomb (what it remembers of the preview + what its torch has shown since; anything forgotten is
    /// imagined as open floor), re-plans whenever reality contradicts it, and at each junction may take a wrong branch.
    /// </summary>
    sealed class HumanSim
    {
        const int MaxMoves = 1500;
        const double MaxSeconds = 900;

        readonly Level _real;
        readonly Level _belief;
        readonly bool[] _known;
        readonly bool[] _visited;
        /// <summary>Seen with the torch during the run (not just remembered from the preview).</summary>
        readonly bool[] _seen;
        readonly HashSet<long> _triedBranches = new HashSet<long>();
        readonly HumanProfile _p;
        readonly Random _rng;
        readonly bool _tick;
        bool _dirty = true;

        HumanSim(Level level, HumanProfile p, Random rng)
        {
            _real = level;
            _p = p;
            _rng = rng;
            _belief = new Level(level.Width, level.Height, level.Floors)
            {
                Id = level.Id, Spec = level.Spec, Start = level.Start, Exit = level.Exit, MaxHp = level.MaxHp,
            };
            _known = new bool[level.CellCount];
            _visited = new bool[level.CellCount];
            _seen = new bool[level.CellCount];
            for (int i = 0; i < level.CellCount; i++)
            {
                var c = level.CellAt(i);
                var t = level[c];
                if (t.Type == TileType.FireJet) _tick = true;
                bool border = c.X == 0 || c.Y == 0 || c.X == level.Width - 1 || c.Y == level.Height - 1;
                bool plain = t.Type == TileType.Wall || t.Type == TileType.WallTorch || t.Type == TileType.Floor;
                double keep = plain ? p.Memory : p.PoiMemory;
                if (c.Floor != level.Start.Floor) keep *= p.OtherFloor;
                if (border || c == level.Start || c == level.Exit || _rng.NextDouble() < keep) Learn(c, Perceived(t));
                else _belief[c] = Tile.Floor;
            }
        }

        /// <summary>Hidden portals look like floor on the preview.</summary>
        static Tile Perceived(Tile t) =>
            t.Type == TileType.Teleporter && t.Teleporter == TeleporterKind.Hidden ? Tile.Floor : t;

        void Learn(Cell c, Tile t)
        {
            int i = _real.IndexOf(c);
            var b = _belief[c];
            if (!_known[i] || b.Type != t.Type || b.Channel != t.Channel || b.Param != t.Param || b.Teleporter != t.Teleporter || b.Trap != t.Trap)
            {
                _belief[c] = t;
                _dirty = true;
            }
            _known[i] = true;
            if (t.Type == TileType.Teleporter && _real.TryGetTeleportTarget(c, out var other)
                && _known[_real.IndexOf(other)] && _belief[other].Type == TileType.Teleporter)
                _belief.LinkTeleporters(c, other);
        }

        void Observe(GameSession s, StepResult r)
        {
            var pos = s.Position;
            _visited[_real.IndexOf(pos)] = true;
            if (r.Has(StepFlags.Teleported) || r.Has(StepFlags.HiddenRevealed))
            {
                Learn(r.SteppedOn, s.PerceivedTile(r.SteppedOn));
                Learn(pos, s.PerceivedTile(pos));
            }
            Learn(pos, s.PerceivedTile(pos));
            _seen[_real.IndexOf(pos)] = true;
            foreach (var d in DirExt.All)
            {
                var n = pos.Step(d);
                if (_real.InBounds(n) && s.IsVisible(n)) { Learn(n, s.PerceivedTile(n)); _seen[_real.IndexOf(n)] = true; }
            }
        }

        /// <summary>The curse of a cursed portal: the drawn map is wiped, and memory gets shaky too.</summary>
        void Curse()
        {
            for (int i = 0; i < _known.Length; i++)
            {
                var c = _real.CellAt(i);
                bool border = c.X == 0 || c.Y == 0 || c.X == _real.Width - 1 || c.Y == _real.Height - 1;
                // What the torch showed stays in mind (the curse wipes the drawn map, not what the player lived).
                if (!_known[i] || border || c == _real.Exit || _seen[i] || _rng.NextDouble() < _p.Memory) continue;
                _known[i] = false;
                _belief[c] = Tile.Floor;
            }
            _dirty = true;
        }

        bool Enterable(RuleState s, Cell c) => Rules.CanEnter(_belief, c, s);

        bool Dangerous(GameSession s, Cell c)
        {
            var t = _belief.Get(c);
            return Rules.IsTrapArmed(t, s.State.Disarmed) || Rules.IsFiring(t, (s.State.Tick + 1) % Rules.FlameCycle);
        }

        List<PlayerAction> Plan(RuleState from)
        {
            // 1. Best route to the exit on what the player believes (forgotten tiles imagined as open).
            var plan = Bfs(from, r => r.Has(StepFlags.Won));
            // 2. No idea how to get out: go and look at what is still unknown.
            //    (in the dark, only by walking onto it: the torch shows nothing around)
            plan ??= Bfs(from, r => r.State.SeesNeighbours ? HasUnknownNeighbour(r.State.Position) : !_known[_real.IndexOf(r.State.Position)]);
            // 2b. A portal whose other end is forgotten: try it (once its lever is pulled, for a locked one).
            plan ??= Bfs(from, r => IsUnlinkedPad(r.SteppedOn, r.State));
            // 3. Everything seen: a dead end always hides something, check the empty ones first (hidden portals),
            //    then wander to places not yet walked on.
            plan ??= Bfs(from, r => !_visited[_real.IndexOf(r.State.Position)] && IsDeadEnd(r.State));
            plan ??= Bfs(from, r => !_visited[_real.IndexOf(r.State.Position)]);
            return plan;
        }

        /// <summary>A pad the player knows is a portal, open, but whose destination they don't remember.</summary>
        bool IsUnlinkedPad(Cell c, RuleState s)
        {
            var t = _belief.Get(c);
            return t.Type == TileType.Teleporter && !_belief.TryGetTeleportTarget(c, out _)
                   && (t.Teleporter != TeleporterKind.Locked || Rules.IsDoorOpen(t, s.Pressed));
        }

        bool IsDeadEnd(RuleState s)
        {
            int open = 0;
            foreach (var d in DirExt.All)
                if (!_belief.Get(s.Position.Step(d)).IsSolid) open++;
            return open == 1;
        }

        bool HasUnknownNeighbour(Cell c)
        {
            foreach (var d in DirExt.All)
            {
                var n = c.Step(d);
                if (_real.InBounds(n) && !_known[_real.IndexOf(n)]) return true;
            }
            return false;
        }

        List<PlayerAction> Bfs(RuleState start, Func<StepResult, bool> goal, int maxStates = 400_000)
        {
            var parent = new Dictionary<long, (long prev, PlayerAction action)>();
            var states = new Dictionary<long, RuleState>();
            var queue = new Queue<long>();
            var candidates = new List<PlayerAction>(8);
            long sk = Pack(start);
            parent[sk] = (-1, default);
            states[sk] = start;
            queue.Enqueue(sk);
            while (queue.Count > 0)
            {
                long key = queue.Dequeue();
                var s = states[key];
                candidates.Clear();
                foreach (var dir in DirExt.All)
                {
                    candidates.Add(PlayerAction.Move(dir));
                    var adj = _belief.Get(s.Position.Step(dir));
                    if (adj.Type == TileType.Trap && adj.Trap == TrapKind.Spikes && Rules.IsTrapArmed(adj, s.Disarmed))
                        candidates.Add(PlayerAction.Disarm(dir));
                }
                foreach (var a in candidates)
                {
                    var r = Rules.Step(_belief, s, a);
                    if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Died)) continue;
                    long nk = Pack(r.State);
                    if (parent.ContainsKey(nk)) continue;
                    parent[nk] = (key, a);
                    if (goal(r))
                    {
                        var actions = new List<PlayerAction>();
                        for (long k = nk; parent[k].prev >= 0; k = parent[k].prev) actions.Add(parent[k].action);
                        actions.Reverse();
                        return actions;
                    }
                    states[nk] = r.State;
                    queue.Enqueue(nk);
                }
                if (parent.Count > maxStates) return null;
            }
            return null;
        }

        long Pack(RuleState s) =>
            (long)_belief.IndexOf(s.Position)
            | ((long)(s.Pressed & 0xFFF) << 12)
            | ((long)(s.Disarmed & 0xFFF) << 24)
            | ((long)(s.Hp & 0x7) << 36)
            | ((long)(s.Blind & 0x3) << 39)
            | (s.TorchOut ? 1L << 41 : 0)
            | ((long)(s.Crumbled & 0xFFF) << 42)
            | (_tick ? (long)(s.Tick & 0x3) << 54 : 0);

        long BranchKey(Cell c, Dir d) => (long)_real.IndexOf(c) * 4 + (int)d;

        /// <summary>Open neighbours the player could walk into, apart from where they come from.</summary>
        List<Dir> Openings(GameSession s, Dir? cameFrom)
        {
            var list = new List<Dir>(4);
            foreach (var d in DirExt.All)
            {
                if (cameFrom.HasValue && d == cameFrom.Value.Opposite()) continue;
                var n = s.Position.Step(d);
                if (Enterable(s.State, n) && !Dangerous(s, n)) list.Add(d);
            }
            return list;
        }

        public static RunStats Play(Level level, HumanProfile p, Random rng)
        {
            var sim = new HumanSim(level, p, rng);
            var session = new GameSession(level);
            sim.Observe(session, new StepResult { SteppedOn = level.Start });
            var stats = new RunStats();
            List<PlayerAction> plan = null;
            int next = 0, detourLeft = 0, blockedInRow = 0;
            Dir? cameFrom = null;
            double Jitter() => 0.7 + rng.NextDouble() * 0.6;

            while (session.Status == SessionStatus.Playing)
            {
                if (session.Moves >= MaxMoves || stats.Seconds >= MaxSeconds || blockedInRow > 4) { stats.GaveUp = true; stats.Reason = session.Moves >= MaxMoves ? "moves" : stats.Seconds >= MaxSeconds ? "time" : "bumps"; break; }
                PlayerAction action;
                double think = 0;

                if (detourLeft > 0)
                {
                    // Lost in a wrong branch: keep following the gallery (straight on if possible) until it ends.
                    var open = sim.Openings(session, cameFrom);
                    open.RemoveAll(d => sim._visited[level.IndexOf(session.Position.Step(d))] && open.Count > 1);
                    if (open.Count == 0) { detourLeft = 0; sim._dirty = true; stats.Seconds += p.ThinkSec * Jitter(); continue; }
                    var dir = cameFrom.HasValue && open.Contains(cameFrom.Value) ? cameFrom.Value : open[rng.Next(open.Count)];
                    action = PlayerAction.Move(dir);
                    if (--detourLeft == 0) sim._dirty = true; // "this is not the way": rethink
                }
                else
                {
                    if (plan == null || next >= plan.Count || sim._dirty)
                    {
                        bool surprise = plan != null && sim._dirty;
                        plan = sim.Plan(session.State);
                        next = 0;
                        sim._dirty = false;
                        if (surprise) think += p.ThinkSec * Jitter();
                        if (plan == null) { stats.GaveUp = true; stats.Reason = "noplan"; break; }
                    }
                    action = plan[next++];

                    if (action.Kind == ActionKind.Move)
                    {
                        var open = sim.Openings(session, cameFrom);
                        if (open.Count >= 2)
                        {
                            think += 0.5 * p.ThinkSec * Jitter();
                            var wrong = open.FindAll(d => d != action.Dir
                                && !sim._triedBranches.Contains(sim.BranchKey(session.Position, d))
                                && !sim._visited[level.IndexOf(session.Position.Step(d))]);
                            if (wrong.Count > 0 && rng.NextDouble() < p.WrongTurn)
                            {
                                var d = wrong[rng.Next(wrong.Count)];
                                sim._triedBranches.Add(sim.BranchKey(session.Position, d));
                                action = PlayerAction.Move(d);
                                detourLeft = 1 + rng.Next(2, 7);
                                stats.WrongTurns++;
                                plan = null;
                            }
                        }
                    }
                }

                var target = session.Position.Step(action.Dir);
                var r = session.Apply(action);
                if (r.Has(StepFlags.Blocked))
                {
                    // Walked into something unseen (torch out, blind): now the player knows it is solid.
                    stats.Bumps++;
                    blockedInRow++;
                    if (level.InBounds(target)) sim.Learn(target, session.PerceivedTile(target));
                    sim._dirty = true;
                    detourLeft = 0;
                    stats.Seconds += 0.8 * p.MoveSec * Jitter() + think;
                    continue;
                }
                blockedInRow = 0;

                double t = p.MoveSec * Jitter() + think;
                if ((r.Flags & (StepFlags.Teleported | StepFlags.Climbed | StepFlags.Fell)) != 0) t += 0.6;
                if (r.Has(StepFlags.Damaged)) t += 0.8;
                stats.Seconds += t;

                if (r.Has(StepFlags.FogReset)) sim.Curse();
                bool transported = (r.Flags & (StepFlags.Teleported | StepFlags.Climbed | StepFlags.Fell | StepFlags.Swept)) != 0;
                cameFrom = action.Kind == ActionKind.Move && !transported ? action.Dir : (Dir?)null;
                sim.Observe(session, r);
            }

            stats.Won = session.Status == SessionStatus.Won;
            stats.Defeat = session.Defeat;
            stats.Moves = session.Moves;
            stats.Final = session.State;
            return stats;
        }
    }
}
