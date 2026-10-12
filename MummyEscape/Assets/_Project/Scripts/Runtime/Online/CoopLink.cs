using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// What the two phones of the 2v2 campaign tell each other over the live link (<see cref="IPeerLink"/>), as
    /// <see cref="RelayMessage"/>s. The host's phone orders every action, so both phones apply them in the same order:
    /// <list type="bullet">
    /// <item>guest → host, <see cref="RelayMessageKind.Input"/>: an action the guest wants (Maze = its mummy, Tick = -1);</item>
    /// <item>host → guest, Input with Tick ≥ 0: the action number Tick of the try, accepted (Maze = the mummy);</item>
    /// <item>host → guest, Input with Tick = <see cref="Refused"/>: the guest's action was blocked (it bumps);</item>
    /// <item><see cref="RelayMessageKind.Ready"/>: the preview is over on this phone (both start together);</item>
    /// <item><see cref="RelayMessageKind.Start"/>, Vote = a tomb number: this player wants to play that tomb (again); the
    /// try starts once both want the same one;</item>
    /// <item><see cref="RelayMessageKind.Quit"/>: the player left.</item>
    /// </list>
    /// </summary>
    public static class CoopWire
    {
        public const int Wanted = -1, Refused = -2;
    }

    /// <summary>The two phones are connected: who is who, and the tomb to start with.</summary>
    public sealed class CoopStart
    {
        public IPeerLink Link;
        public Duo Duo;
        public string Me;
        /// <summary>This phone orders the actions.</summary>
        public bool Host;
        public int Tomb;
        public string MateName;
        public PlayerLook MateLook;

        /// <summary>Mummy i is played by the duo's member i.</summary>
        public int MyMummy => Math.Max(0, Duo.Members.IndexOf(Me));
        public string MateId => Duo.Members.Find(m => m != Me);
    }

    public sealed class CoopSearch
    {
        public Duo Duo;
        public string Me;
        public string MyName;
        public PlayerLook Look;
        /// <summary>The tomb this player chose (the first of the two to come decides).</summary>
        public int Tomb;
        /// <summary>The group's mate: never opens a lobby, only joins the leader's (who picks the tomb).</summary>
        public bool FollowOnly;
        public IPvpService Pvp;
    }

    /// <summary>Brings the two members of a duo together (both press "Play") and connects their phones.</summary>
    public interface ICoopMatchmaker
    {
        /// <summary>The connection, or null (cancelled, error: <paramref name="status"/> said why).</summary>
        Task<CoopStart> FindAsync(CoopSearch search, Action<string> status, CancellationToken cancel);
    }

    public static class CoopMatchmakerFactory
    {
        /// <summary>Set by the UGS module when Lobby and Relay are installed.</summary>
        public static Func<ICoopMatchmaker> CreateOnline;

        public static ICoopMatchmaker For(IPvpService pvp) =>
            pvp == null ? null : pvp.IsDemo ? new LocalCoopMatchmaker() : CreateOnline?.Invoke();
    }

    // ------------------------------------------------------------------ offline demo

    /// <summary>Offline: the teammate is simulated on this phone (editor and development builds).</summary>
    public sealed class LocalCoopMatchmaker : ICoopMatchmaker
    {
        public async Task<CoopStart> FindAsync(CoopSearch search, Action<string> status, CancellationToken cancel)
        {
            int mate = search.Duo.Members.FindIndex(m => m != search.Me);
            string name = mate >= 0 && mate < search.Duo.Names.Count ? search.Duo.Names[mate] : "?";
            status(Loc.F("En attente de {0}…", name));
            try { await Task.Delay(1200, cancel); }
            catch (TaskCanceledException) { return null; }
            var start = new CoopStart { Duo = search.Duo, Me = search.Me, Host = true, Tomb = search.Tomb, MateName = name };
            start.Link = new LocalCoopLink(start);
            return start;
        }
    }

    /// <summary>
    /// Offline teammate: it follows the pair's shortest way out from wherever the two mummies stand, playing its own
    /// actions at a human pace and waiting while the next one is the player's (after a few idle seconds it moves on
    /// anyway). It answers the "ready" and "again" of the player at once.
    /// </summary>
    public sealed class LocalCoopLink : IPeerLink
    {
        public string Me { get; }
        public event Action<RelayMessage> Received;
        public event Action<string> Left { add { } remove { } }

        readonly string _mate;
        readonly int _mummy;
        CoopSession _copy;
        Task<CoopSolution> _plan;
        CoopState _plannedFor;
        float _actAt = -1f, _idleSince;
        readonly System.Random _rng = new System.Random();

        public LocalCoopLink(CoopStart start)
        {
            Me = start.Me;
            _mate = start.MateId;
            _mummy = 1 - start.MyMummy;
        }

        public void Send(RelayMessage m)
        {
            switch (m.Kind)
            {
                case RelayMessageKind.Ready:
                    Received?.Invoke(new RelayMessage { Kind = RelayMessageKind.Ready, From = _mate });
                    break;
                case RelayMessageKind.Start:
                    if (int.TryParse(m.Vote, out int tomb) && CoopCampaign.Get(tomb) != null)
                    {
                        _copy = new CoopSession(CoopCampaign.Get(tomb));
                        _plan = null;
                        _idleSince = Time.unscaledTime;
                    }
                    Received?.Invoke(new RelayMessage { Kind = RelayMessageKind.Start, From = _mate, Vote = m.Vote });
                    break;
                case RelayMessageKind.Input:
                    // The host's accepted actions, in order: the teammate's copy of the try follows.
                    if (_copy != null && m.Tick >= 0 && RunActions.TryDecode(m.Direction, out var action))
                    {
                        _copy.Apply(m.Maze, action);
                        if (m.Maze != _mummy) _idleSince = Time.unscaledTime;
                        _actAt = -1f;
                    }
                    break;
            }
        }

        public void Pump()
        {
            if (_copy == null || _copy.Status != SessionStatus.Playing) return;
            var state = _copy.State;
            if (_plan == null || !state.Equals(_plannedFor))
            {
                _plannedFor = state;
                var rules = _copy.Rules;
                _plan = Task.Run(() => CoopSolver.Solve(rules, state, 400_000));
                return;
            }
            if (!_plan.IsCompleted || _plan.Result == null || _plan.Result.Actions.Count == 0) return;
            var actions = _plan.Result.Actions;
            // The next action of the way out is the player's: wait for it, a few seconds at most.
            int next = actions[0].mummy == _mummy ? 0 : Time.unscaledTime - _idleSince > 4f ? actions.FindIndex(a => a.mummy == _mummy) : -1;
            if (next < 0) return;
            if (_actAt < 0f) _actAt = Time.unscaledTime + 0.3f + (float)_rng.NextDouble() * 0.4f;
            if (Time.unscaledTime < _actAt) return;
            _actAt = -1f;
            Received?.Invoke(new RelayMessage
            {
                Kind = RelayMessageKind.Input, From = _mate, Tick = CoopWire.Wanted, Maze = _mummy, Direction = RunActions.Encode(actions[next].action),
            });
        }

        public void Dispose() => Received = null;
    }
}
