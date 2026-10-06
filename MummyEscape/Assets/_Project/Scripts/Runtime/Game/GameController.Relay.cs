using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using MummyEscape.World;
using UnityEngine;

namespace MummyEscape.Game
{
    public enum RelayPhase { None, Loading, Versus, Preview, Vote, Racing, Over }

    /// <summary>How a 2v2 match ended, as the phone saw it (the server's verdict comes on the result screen).</summary>
    public sealed class RelayOutcome
    {
        public RelayMatch Match;
        public string Me;
        public string Starter;
        public DuelResult Result;
        public RelaySummary Mine, Rival;
        public List<RelayInput> Inputs;
        /// <summary>The rival duo's relay as it was received (the server's copy replaces it in the replays).</summary>
        public List<RelayInput> RivalInputs;
        public List<string> Quitters;
        /// <summary>The teammate left the match: the player may quit and report him.</summary>
        public string PartnerQuit;
    }

    /// <summary>
    /// The live 2v2 relay: both mazes previewed one floor after the other, the vote for who starts, then the legs in turn.
    /// The camera follows whoever of the duo is running: the player when it is his leg, his teammate otherwise. The rival
    /// duo is only followed on the progress dots.
    /// </summary>
    public sealed partial class GameController
    {
        public IRelayLink Relay { get; private set; }
        public bool InRelay => Relay != null;
        public RelayPhase Phase { get; private set; }
        public RelayMap RelayMap { get; private set; }
        public RelayRace MyRace { get; private set; }
        public RelayRace RivalRace { get; private set; }
        public RelaySide MySide { get; private set; }
        public RelaySide RivalSide { get; private set; }
        /// <summary>The player's maze (0: he starts), known once the vote is over.</summary>
        public int MyMaze { get; private set; }
        /// <summary>Maze on screen (the running teammate's).</summary>
        public int ShownMaze { get; private set; }
        /// <summary>The player who runs maze 0, null before the vote.</summary>
        public string Starter { get; private set; }
        /// <summary>Who each teammate voted for (player id → player id).</summary>
        public readonly Dictionary<string, string> Votes = new Dictionary<string, string>();
        public readonly List<string> Quitters = new List<string>();
        /// <summary>Seconds left in the vote.</summary>
        public float VoteLeft { get; private set; }
        public int RelayClockMs => (int)Math.Min(int.MaxValue, _relayClock);
        public int RelayTimeLeftMs => Math.Max(0, RelayConfig.TimeLimitMs - RelayClockMs);
        /// <summary>Maze shown by the preview right now.</summary>
        public int PreviewMaze { get; private set; }
        /// <summary>It is the player's leg: his swipes count.</summary>
        public bool MyTurn => Phase == RelayPhase.Racing && MyRace != null && !MyRace.IsOver && MyRace.ActiveMaze == MyMaze && ShownMaze == MyMaze;

        /// <summary>Votes, legs, departures: the HUD refreshes.</summary>
        public event Action RelayChanged;
        public event Action<RelayOutcome> RelayEnded;

        double _relayClock;
        readonly Queue<RelayMessage> _mateSteps = new Queue<RelayMessage>();
        readonly Queue<RelayInput> _botSteps = new Queue<RelayInput>();

        /// <summary>
        /// Draws the two mazes of the match and shows maze 0 behind the VS screen. The caller then plays the VS screen and
        /// calls <see cref="BeginRelayPreview"/>. False when the match cannot be prepared.
        /// </summary>
        public async Task<bool> StartRelay(IRelayLink link)
        {
            int token = ++_loadToken;
            ResetForLoad();
            ClearRelay();
            Session = null;
            Match = null;
            Pace = null;
            Relay = link;
            Phase = RelayPhase.Loading;
            link.Received += OnRelayMessage;
            link.Left += OnRelayLeft;
            var match = link.Match;
            MySide = match.SideOf(link.Me);
            RivalSide = match.OtherSide(link.Me);
            var id = RelayArena.LevelFor(match.Seed);
            CurrentLevel = id;
            _app.Audio.PlayMusic(id.Act);
            RelayMap map;
            try
            {
                map = await Task.Run(() => RelayArena.Generate(match.Seed));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not generate relay {match.Seed}: {e}");
                return false;
            }
            if (token != _loadToken || this == null || Relay != link) return false;
            RelayMap = map;
            MyRace = new RelayRace(map);
            RivalRace = new RelayRace(map);
            if (RivalSide.Bot && RivalSide.Inputs != null)
                foreach (var i in RivalSide.Inputs) _botSteps.Enqueue(i);
            ApplyTheme(TombTheme.ForAct(id.Act));
            _app.Lighting.SetMood(true);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _paused = false;
            ShowMaze(0);
            Phase = RelayPhase.Versus;
            RelayChanged?.Invoke();
            return true;
        }

        /// <summary>After the VS screen: every floor of both mazes, 7 s each, nothing to skip; then the vote, then the race.</summary>
        public void BeginRelayPreview()
        {
            if (Phase != RelayPhase.Versus) return;
            _relayPreview = StartCoroutine(RelayPreviewRoutine());
        }

        Coroutine _relayPreview;
        /// <summary>The starter decided by the teammate's phone.</summary>
        string _announcedStarter;
        /// <summary>How long the other phone waits for that decision before drawing itself (the teammate dropped).</summary>
        const float StarterWaitSeconds = 6f;

        IEnumerator RelayPreviewRoutine()
        {
            Phase = RelayPhase.Preview;
            Previewing = true;
            _input.Enabled = false;
            _app.Guard.Arm(true); // the map must not leave the phone (screenshot / recording)
            _app.Lighting.SetPreview(true);
            int total = RelayMap.PreviewFloors, shown = 0;
            for (int m = 0; m < RelayConfig.Mazes; m++)
            {
                PreviewMaze = m;
                ShowMaze(m);
                var level = RelayMap.Mazes[m];
                for (int f = 0; f < level.Floors; f++, shown++)
                {
                    PreviewFloor = f;
                    PreviewFloorsLeft = total - 1 - shown;
                    PreviewLeft = RelayConfig.PreviewSecondsPerFloor;
                    _maze.SetPreview(true, f);
                    _player.gameObject.SetActive(f == level.Start.Floor);
                    _app.Camera.ShowArea(_maze.PreviewBounds());
                    PreviewChanged?.Invoke();
                    while (PreviewLeft > 0f)
                    {
                        // Recorded or mirrored: the tomb stays dark, but the clock runs on (the four phones keep in step).
                        bool captured = _app.Guard.IsCaptured;
                        if (captured != _maze.Concealed)
                        {
                            _maze.Concealed = captured;
                            PreviewChanged?.Invoke();
                        }
                        PreviewLeft -= Time.deltaTime;
                        yield return null;
                    }
                }
            }
            PreviewLeft = 0f;
            _player.gameObject.SetActive(true);
            EndPreviewVisuals();
            _app.Audio.Play(Sfx.Darkness);
            ShowMaze(0);
            PreviewChanged?.Invoke();

            // Who starts? Both teammates vote; agreement decides, a conflict (or silence) is drawn at random.
            Phase = RelayPhase.Vote;
            VoteLeft = RelayConfig.VoteSeconds;
            RelayChanged?.Invoke();
            while (VoteLeft > 0f && !BothVoted()) { VoteLeft -= Time.deltaTime; yield return null; }
            VoteLeft = 0f;
            var a = MySide.Runners[0].PlayerId;
            var b = MySide.Runners[1].PlayerId;
            // One phone of the duo decides (the first member, or the player when his teammate is simulated) and tells the
            // other: a vote arriving just after one phone's timeout must not give the two phones different starters.
            var mate = MySide.Runners.Find(r => r.PlayerId != Relay.Me);
            bool decides = mate == null || mate.Bot || a == Relay.Me;
            if (decides)
            {
                Votes.TryGetValue(a, out var va);
                Votes.TryGetValue(b, out var vb);
                Starter = RelayVote.Resolve(a, b, va, vb, new System.Random(RelayMatchSeed(MySide.DuoId)));
                Relay.Send(new RelayMessage { Kind = RelayMessageKind.Start, From = Relay.Me, Vote = Starter });
            }
            else
            {
                float wait = StarterWaitSeconds;
                while (_announcedStarter == null && wait > 0f) { wait -= Time.deltaTime; yield return null; }
                Votes.TryGetValue(a, out var va);
                Votes.TryGetValue(b, out var vb);
                Starter = _announcedStarter ?? RelayVote.Resolve(a, b, va, vb, new System.Random(RelayMatchSeed(MySide.DuoId)));
            }
            MySide.Starter = Starter;
            MyMaze = Starter == Relay.Me ? 0 : 1;

            _relayClock = 0;
            Phase = RelayPhase.Racing;
            ShowMaze(0);
            _app.Audio.Play(Sfx.Versus);
            RelayChanged?.Invoke();
        }

        int RelayMatchSeed(string duoId)
        {
            int h = Relay.Match.Seed;
            foreach (char c in duoId ?? "") h = unchecked(h * 31 + c);
            return h;
        }

        bool BothVoted() => MySide.Runners.TrueForAll(r => Votes.ContainsKey(r.PlayerId));

        /// <summary>The player's vote: <paramref name="starter"/> runs maze 0.</summary>
        public void VoteStarter(string starter)
        {
            if (Phase != RelayPhase.Vote || Votes.ContainsKey(Relay.Me)) return;
            Votes[Relay.Me] = starter;
            Relay.Send(new RelayMessage { Kind = RelayMessageKind.Vote, From = Relay.Me, Vote = starter });
            RelayChanged?.Invoke();
        }

        /// <summary>Puts this maze on screen: its fog, its runner's mummy where it stands, the camera on it.</summary>
        void ShowMaze(int maze)
        {
            ShownMaze = maze;
            Session = MyRace.Session(maze);
            IndexMechanisms(Session.Level);
            _maze.Build(Session);
            _player.gameObject.SetActive(true);
            _player.ResetVisual();
            var runner = Starter == null ? null : MySide.RunnerOf(maze);
            _player.SetSkin(runner == null ? SkinCatalog.Classic : runner.PlayerId == Relay.Me ? _app.Save.Loadout : PvpSkins.Loadout(runner.Look));
            _player.Place(Session.Position);
            _player.SetBlind(Session.IsBlind);
            _player.SetReversed(Session.State.Reversed);
            _player.SetTorchLit(Session.TorchLit);
            _app.Camera.SnapTo(MazeView.CellToWorld(Session.Position));
            _app.Camera.SetTurn(Session.State.Rotation, true);
            _input.Enabled = MyTurn && !_paused;
            Changed?.Invoke();
            RelayChanged?.Invoke();
        }

        void OnRelayMessage(RelayMessage m)
        {
            if (Relay == null || MySide == null) return;
            bool mate = MySide.Has(m.From) && m.From != Relay.Me;
            switch (m.Kind)
            {
                case RelayMessageKind.Vote:
                    if (mate && (m.Vote == null || MySide.Has(m.Vote))) Votes[m.From] = m.Vote;
                    RelayChanged?.Invoke();
                    break;
                case RelayMessageKind.Start:
                    if (mate && MySide.Has(m.Vote)) _announcedStarter = m.Vote;
                    break;
                case RelayMessageKind.Input:
                    if (mate) _mateSteps.Enqueue(m);
                    // The rival duo's clock started when its own vote ended: its steps are played on ours, like a ghost.
                    else if (RivalSide.Has(m.From) && RivalRace != null && !RivalRace.IsOver) _botSteps.Enqueue(m.AsInput);
                    break;
                case RelayMessageKind.Quit:
                    PlayerQuit(m.From);
                    break;
            }
        }

        void OnRelayLeft(string playerId) => PlayerQuit(playerId);

        void PlayerQuit(string playerId)
        {
            if (Phase == RelayPhase.Over || Quitters.Contains(playerId)) return;
            Quitters.Add(playerId);
            if (MySide.Has(playerId)) MyRace?.Lose(RelayDefeat.Abandoned);
            else if (RivalSide.Has(playerId)) RivalRace?.Lose(RelayDefeat.Abandoned);
            else return;
            RelayChanged?.Invoke();
            if (Phase == RelayPhase.Racing || Phase == RelayPhase.Vote || Phase == RelayPhase.Preview) EndRelay();
        }

        void ApplyRival(RelayInput input)
        {
            if (!RunActions.TryDecode(input.Direction, out var action) || input.Maze != RivalRace.ActiveMaze) return;
            var r = RivalRace.Apply(input.Maze, action, Math.Max(input.Tick, RivalRace.MinTick(input.Maze)));
            if (r.HasValue) RelayChanged?.Invoke();
        }

        void UpdateRelay()
        {
            if (Relay == null) return;
            if (Phase == RelayPhase.Racing) _relayClock += Time.deltaTime * 1000.0;
            Relay.Tick(RelayClockMs, Phase == RelayPhase.Racing ? MyRace : null, MyMaze);
            if (Phase != RelayPhase.Racing) return;

            // The bots' relay, played on the race clock like a ghost.
            while (_botSteps.Count > 0 && RunActions.MsOf(_botSteps.Peek().Tick) <= RelayClockMs && !RivalRace.IsOver)
                ApplyRival(_botSteps.Dequeue());

            // The teammate's steps, animated in order once his maze is on screen.
            if (!_busy && _mateSteps.Count > 0 && _mateSteps.Peek().Maze == ShownMaze && ShownMaze == MyRace.ActiveMaze && !MyRace.IsOver)
                StartCoroutine(PlayMate(_mateSteps.Dequeue()));

            if (RelayClockMs >= RelayConfig.TimeLimitMs)
            {
                MyRace.Lose(RelayDefeat.TimedOut);
                RivalRace.Lose(RelayDefeat.TimedOut);
                EndRelay();
            }
            else if (RivalRace.IsOver && !_busy) EndRelay(); // first home, or the rival duo lost a mummy: it is over
        }

        IEnumerator PlayRelay(PlayerAction action)
        {
            if (!MyTurn) yield break;
            int tick = Math.Max(RunActions.TickOf(RelayClockMs), MyRace.MinTick(MyMaze));
            var before = Session.State;
            var from = Session.Position;
            var r = MyRace.Apply(MyMaze, action, tick);
            if (!r.HasValue) yield break;
            if (r.Value.Has(StepFlags.Blocked))
            {
                if (action.Kind == ActionKind.Move)
                {
                    _app.Audio.Play(Sfx.Bump);
                    _app.Fx.Bump(MazeView.CellToWorld(from), r.Value.Dir);
                    _busy = true;
                    yield return _player.Bump(r.Value.Dir);
                    _busy = false;
                }
                yield break;
            }
            var inputs = MyRace.Inputs;
            Relay.Send(RelayMessage.Input(Relay.Me, inputs[inputs.Count - 1]));
            yield return Present(before, from, action, r.Value);
        }

        IEnumerator PlayMate(RelayMessage m)
        {
            if (!RunActions.TryDecode(m.Direction, out var action)) yield break;
            var before = Session.State;
            var from = Session.Position;
            var r = MyRace.Apply(m.Maze, action, m.Tick);
            if (!r.HasValue || r.Value.Has(StepFlags.Blocked))
            {
                Debug.LogWarning($"[Relay] Teammate step refused (tick {m.Tick}, maze {m.Maze}): out of step");
                yield break;
            }
            yield return Present(before, from, action, r.Value);
        }

        /// <summary>After a step of the duo: a leg ends (the camera goes to the teammate), or the relay is over.</summary>
        IEnumerator RelayStepDone()
        {
            if (MyRace.IsOver)
            {
                var here = MazeView.CellToWorld(Session.Position);
                if (MyRace.Status == RelayStatus.Finished)
                {
                    _app.Fx.Win(here);
                    _app.Audio.Play(Sfx.Win);
                    _app.Lighting.Flash(new Color(1f, 0.8f, 0.3f), 0.8f);
                    yield return _player.Vanish(0.5f);
                }
                else if (MyRace.Defeat == RelayDefeat.Died)
                {
                    _app.Fx.Death(here);
                    _app.Audio.Play(Sfx.Death);
                    _app.Camera.Shake(0.8f);
                    yield return _player.Die();
                }
                yield return new WaitForSeconds(0.35f);
                _busy = false;
                EndRelay();
                yield break;
            }
            if (MyRace.ActiveMaze != ShownMaze)
            {
                // Relay plate pressed: a flash, then the camera crosses over to the teammate, who is freed.
                _app.Lighting.Flash(new Color(0.3f, 1f, 0.9f), 0.6f);
                _app.Audio.Play(Sfx.Teleport);
                yield return new WaitForSeconds(RelayConfig.HandoffMs / 1000f - 0.1f);
                ShowMaze(MyRace.ActiveMaze);
                if (MyTurn) _app.Audio.Play(Sfx.Versus);
                _buffered = null;
            }
            _busy = false;
            if (_buffered.HasValue && MyTurn)
            {
                var next = _buffered.Value;
                _buffered = null;
                Submit(next);
            }
        }

        /// <summary>The player leaves the match: his duo loses, his teammate is told.</summary>
        public void ForfeitRelay()
        {
            if (Relay == null || Phase == RelayPhase.Over) return;
            Relay.Send(new RelayMessage { Kind = RelayMessageKind.Quit, From = Relay.Me });
            Quitters.Add(Relay.Me);
            MyRace?.Lose(RelayDefeat.Abandoned);
            EndRelay();
        }

        void EndRelay()
        {
            if (Phase == RelayPhase.Over || Relay == null) return;
            Phase = RelayPhase.Over;
            _input.Enabled = false;
            _buffered = null;
            if (_relayPreview != null) StopCoroutine(_relayPreview);
            _relayPreview = null;
            if (Previewing) EndPreviewVisuals();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            bool quitMine = Quitters.Exists(MySide.Has), quitRival = Quitters.Exists(RivalSide.Has);
            var mine = RelaySummary.Of(MyRace, quitMine);
            var rival = RelaySummary.Of(RivalRace, quitRival);
            var outcome = new RelayOutcome
            {
                Match = Relay.Match,
                Me = Relay.Me,
                Starter = Starter,
                Mine = mine,
                Rival = rival,
                Result = RelayJudge.Resolve(mine, rival),
                Inputs = MyRace == null ? new List<RelayInput>() : new List<RelayInput>(MyRace.Inputs),
                RivalInputs = RivalRace == null ? new List<RelayInput>() : new List<RelayInput>(RivalRace.Inputs),
                Quitters = new List<string>(Quitters),
                PartnerQuit = Quitters.Find(q => q != Relay.Me && MySide.Has(q)),
            };
            RelayChanged?.Invoke();
            RelayEnded?.Invoke(outcome);
        }

        /// <summary>Back to the menus: the connection is closed (a match still running counts as a departure).</summary>
        public void LeaveRelay()
        {
            if (Relay == null) return;
            if (Phase != RelayPhase.Over) ForfeitRelay();
            Abandon();
        }

        void ClearRelay()
        {
            if (Relay != null)
            {
                Relay.Received -= OnRelayMessage;
                Relay.Left -= OnRelayLeft;
                Relay.Dispose();
            }
            Relay = null;
            Phase = RelayPhase.None;
            RelayMap = null;
            MyRace = RivalRace = null;
            MySide = RivalSide = null;
            Starter = null;
            _announcedStarter = null;
            Votes.Clear();
            Quitters.Clear();
            _mateSteps.Clear();
            _botSteps.Clear();
            _relayClock = 0;
        }
    }
}
