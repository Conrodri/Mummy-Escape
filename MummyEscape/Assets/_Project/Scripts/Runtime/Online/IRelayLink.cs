using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    public enum RelayMessageKind : byte { Vote = 1, Input = 2, Quit = 3, Start = 4, Ready = 5 }

    /// <summary>What the four phones of a 2v2 tell each other: votes, actions (with their time on the race clock), departures.</summary>
    public struct RelayMessage
    {
        public RelayMessageKind Kind;
        public string From;
        public int Tick, Maze, Direction;
        /// <summary>Vote: the player designated to start. Start: the starter decided by the duo.</summary>
        public string Vote;

        public static RelayMessage Input(string from, RelayInput i) =>
            new RelayMessage { Kind = RelayMessageKind.Input, From = from, Tick = i.Tick, Maze = i.Maze, Direction = i.Direction };

        public RelayInput AsInput => new RelayInput { Tick = Tick, Maze = Maze, Direction = Direction };

        /// <summary>Compact text form for the network: "kind|from|tick|maze|direction|vote".</summary>
        public string Encode() => $"{(int)Kind}|{From}|{Tick}|{Maze}|{Direction}|{Vote}"; // noloc

        public static bool TryDecode(string text, out RelayMessage m)
        {
            m = default;
            var parts = text?.Split('|');
            if (parts == null || parts.Length != 6) return false;
            if (!int.TryParse(parts[0], out int kind) || !int.TryParse(parts[2], out m.Tick) || !int.TryParse(parts[3], out m.Maze)
                || !int.TryParse(parts[4], out m.Direction)) return false;
            m.Kind = (RelayMessageKind)kind;
            m.From = parts[1];
            m.Vote = parts[5].Length == 0 ? null : parts[5];
            return m.Kind >= RelayMessageKind.Vote && m.Kind <= RelayMessageKind.Ready && m.Maze >= 0 && m.Maze < RelayConfig.Mazes;
        }
    }

    /// <summary>
    /// The live connection of a 2v2 match: the match the server created, and the messages of the other players. A duo of
    /// bots sends nothing: its relay is in the match (<see cref="RelaySide.Inputs"/>) and is replayed on the race clock.
    /// </summary>
    public interface IRelayLink : IDisposable
    {
        RelayMatch Match { get; }
        string Me { get; }
        /// <summary>A message from another player (never one's own).</summary>
        event Action<RelayMessage> Received;
        /// <summary>A player's connection dropped: for the game, he quit the match.</summary>
        event Action<string> Left;
        void Send(RelayMessage message);
        /// <summary>Every frame: network pump, and the race clock for a simulated teammate.</summary>
        void Tick(int raceMs, RelayRace myDuo, int myMaze);
    }

    /// <summary>What a duo looks for.</summary>
    public sealed class RelaySearch
    {
        public Duo Duo;
        public string Me;
        public RelayRunner MeRunner;
        /// <summary>The player's own duel Elo: the duo's division is its best player's league.</summary>
        public int MyDuelElo = PvpConfig.StartingElo;
        public bool AllowBots;
        public IPvpService Pvp;
    }

    /// <summary>Finds the other duo (or a duo of bots) and connects the four phones.</summary>
    public interface IRelayMatchmaker
    {
        /// <summary>The connected match, or null (cancelled, error: <paramref name="status"/> said why).</summary>
        Task<IRelayLink> FindAsync(RelaySearch search, Action<string> status, CancellationToken cancel);
    }

    public static class RelayMatchmakerFactory
    {
        /// <summary>Set by the UGS module when Lobby and Relay are installed.</summary>
        public static Func<IRelayMatchmaker> CreateOnline;

        public static IRelayMatchmaker For(IPvpService pvp) =>
            pvp == null ? null : pvp.IsDemo ? new LocalRelayMatchmaker() : CreateOnline?.Invoke();
    }

    // ------------------------------------------------------------------ offline demo

    /// <summary>Offline: the teammate and the rival duo are simulated (editor and development builds).</summary>
    public sealed class LocalRelayMatchmaker : IRelayMatchmaker
    {
        public async Task<IRelayLink> FindAsync(RelaySearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Recherche d'un duo adverse…"));
            try { await Task.Delay(1500, cancel); }
            catch (TaskCanceledException) { return null; }
            var mine = new RelaySide { DuoId = search.Duo.Id };
            foreach (var id in search.Duo.Members)
                mine.Runners.Add(id == search.Me ? search.MeRunner : new RelayRunner { PlayerId = id, Name = search.Duo.Names[search.Duo.Members.IndexOf(id)] });
            var r = await search.Pvp.StartRelayBotsAsync("demo_" + Guid.NewGuid().ToString("N"), mine); // noloc
            if (cancel.IsCancellationRequested) return null;
            if (r?.Match == null) { status(Loc.T("Impossible de créer le match.")); return null; }
            // The teammate is simulated too: mark him so the link plays his legs.
            foreach (var runner in r.Match.SideOf(search.Me).Runners)
                if (runner.PlayerId != search.Me) runner.Bot = true;
            return new LocalRelayLink(r.Match, search.Me);
        }
    }

    /// <summary>
    /// Offline match: the simulated teammate votes after the player and runs his legs like a person
    /// (<see cref="HumanPlayer"/>) as soon as he is freed.
    /// </summary>
    public sealed class LocalRelayLink : IRelayLink
    {
        public RelayMatch Match { get; }
        public string Me { get; }
        public event Action<RelayMessage> Received;
        /// <summary>Nobody drops out offline.</summary>
        public event Action<string> Left { add { } remove { } }

        readonly RelaySide _side;
        readonly RelayRunner _mate;
        readonly System.Random _rng = new System.Random();
        readonly HumanPlayer _player;
        readonly Queue<RelayMessage> _due = new Queue<RelayMessage>();
        int _plannedSegment = -1;
        float _voteAt = -1f;
        string _vote;

        public LocalRelayLink(RelayMatch match, string me)
        {
            Match = match;
            Me = me;
            _side = match.SideOf(me);
            _mate = _side.Runners.Find(r => r.PlayerId != me);
            _player = new HumanPlayer(HumanPlayer.SkillForElo(_side.Elo + _rng.Next(-100, 101)), _rng);
        }

        public void Send(RelayMessage message)
        {
            // The teammate answers the vote a moment later, usually agreeing.
            if (message.Kind == RelayMessageKind.Vote && _mate != null && _vote == null)
            {
                _vote = _rng.NextDouble() < 0.7 ? message.Vote : (message.Vote == Me ? _mate.PlayerId : Me);
                _voteAt = Time.unscaledTime + 0.6f + (float)_rng.NextDouble() * 1.2f;
            }
        }

        public void Tick(int raceMs, RelayRace myDuo, int myMaze)
        {
            if (_voteAt >= 0f && Time.unscaledTime >= _voteAt)
            {
                _voteAt = -1f;
                Received?.Invoke(new RelayMessage { Kind = RelayMessageKind.Vote, From = _mate.PlayerId, Vote = _vote });
            }
            if (myDuo == null || _mate == null || myDuo.IsOver) return;
            int mateMaze = 1 - myMaze;
            if (myDuo.ActiveMaze == mateMaze && _plannedSegment != myDuo.Segment && _due.Count == 0)
            {
                _plannedSegment = myDuo.Segment;
                Plan(myDuo, mateMaze);
            }
            while (_due.Count > 0 && RunActions.MsOf(_due.Peek().Tick) <= raceMs)
                Received?.Invoke(_due.Dequeue());
        }

        /// <summary>The teammate's next leg, from where his mummy stands, freed at the earliest the hand-off allows.</summary>
        void Plan(RelayRace race, int maze)
        {
            var copy = RelayRace.Verify(race.Map, new List<RelayInput>(race.Inputs));
            if (copy == null) return;
            double reaction = 300 + _rng.NextDouble() * 500;
            double start = RunActions.MsOf(copy.MinTick(maze)) + reaction - 150;
            var leg = race.Map.Mazes[maze].WithGoal(race.Goal);
            var (inputs, _, _) = _player.Play(leg, _rng, race.Session(maze).State, Math.Max(0, start), RelayConfig.TimeLimitMs);
            foreach (var input in inputs)
            {
                if (!RunActions.TryDecode(input.Direction, out var action)) break;
                var stamped = new RelayInput { Tick = Math.Max(input.Tick, copy.MinTick(maze)), Maze = maze, Direction = input.Direction };
                var r = copy.Apply(maze, action, stamped.Tick);
                if (!r.HasValue || r.Value.Has(StepFlags.Blocked)) break;
                _due.Enqueue(RelayMessage.Input(_mate.PlayerId, stamped));
                if (copy.IsOver || copy.ActiveMaze != maze) break;
            }
        }

        public void Dispose() => Received = null;
    }
}
