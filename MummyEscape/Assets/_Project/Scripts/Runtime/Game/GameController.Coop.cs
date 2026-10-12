using System;
using System.Collections;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using MummyEscape.World;
using UnityEngine;

namespace MummyEscape.Game
{
    /// <summary>How a try of the 2v2 campaign ended on this phone.</summary>
    public sealed class CoopOutcome
    {
        public CoopStart Info;
        public CoopTomb Tomb;
        public bool Won;
        /// <summary>The recap of the pair: moves of both, the joint par, this phone's clock.</summary>
        public LevelResult Result;
        /// <summary>The pair's actions in the host's order (what the server replays).</summary>
        public List<CoopRunInput> Inputs;
        /// <summary>The mummy that died (-1: none).</summary>
        public int Fallen = -1;
        /// <summary>The teammate left (or his connection dropped): the campaign session is over.</summary>
        public bool MateLeft;
    }

    /// <summary>
    /// The 2v2 campaign: both mummies in the same tomb, live. Each phone runs the same <see cref="CoopSession"/>: the host
    /// applies every action (its own at once, the guest's as they arrive) and sends each accepted one, numbered, to the
    /// guest, who applies them in that order (its own swipes wait for the host's answer, one at a time). The player's
    /// mummy is <see cref="Session"/> (its fog sees what both torches light), the teammate's is a second
    /// <see cref="PlayerView"/>. A death ends the try; the exit lets the pair out once both stand on it. A new try starts
    /// once both players asked for the same tomb (<see cref="CoopWish"/>).
    /// </summary>
    public sealed partial class GameController
    {
        public CoopStart CoopInfo { get; private set; }
        public CoopSession Coop { get; private set; }
        public bool InCoop => CoopInfo != null;
        /// <summary>The preview is over here and the teammate's phone is not done with its own yet.</summary>
        public bool CoopWaiting { get; private set; }
        /// <summary>The tomb the teammate asked for (0: none yet).</summary>
        public int MateWish { get; private set; }
        /// <summary>The tomb the player asked for (0: none yet).</summary>
        public int MyWish { get; private set; }
        /// <summary>A try ended, or the teammate left.</summary>
        public event Action<CoopOutcome> CoopEnded;

        PlayerView _mate;
        int _myMummy;
        bool _mateReady, _coopAwait, _coopOver, _mateBusy;
        float _awaitSince;

        struct MateStep
        {
            public PlayerAction Action;
            public StepResult R;
            public Cell From;
            /// <summary>The player's own state before (gates the step moved).</summary>
            public RuleState MineBefore;
        }

        readonly Queue<MateStep> _coopMateSteps = new Queue<MateStep>();

        /// <summary>The teammate's mummy (created once by the app).</summary>
        public void InitCoop(PlayerView mate)
        {
            _mate = mate;
            _mate.gameObject.SetActive(false);
        }

        int MateMummy => 1 - _myMummy;
        GameSession MateSession => Coop?.Mummy(MateMummy);

        /// <summary>The two phones are connected: both ask for the chosen tomb, and the first try starts.</summary>
        public void StartCoop(CoopStart start)
        {
            ++_loadToken;
            ResetForLoad();
            ClearRelay();
            DetachDuelLink();
            ClearCoop();
            Match = null;
            Pace = null;
            Session = null;
            CoopInfo = start;
            _myMummy = start.MyMummy;
            start.Link.Received += OnCoopMessage;
            start.Link.Left += OnCoopLeft;
            CoopWish(start.Tomb);
        }

        /// <summary>The player wants to play this tomb (again): it starts once the teammate wants the same one.</summary>
        public void CoopWish(int tomb)
        {
            if (!InCoop || CoopCampaign.Get(tomb) == null) return;
            MyWish = tomb;
            CoopInfo.Link.Send(new RelayMessage { Kind = RelayMessageKind.Start, From = CoopInfo.Me, Vote = tomb.ToString() });
            TryBeginCoop();
            Changed?.Invoke();
        }

        void TryBeginCoop()
        {
            if (MyWish <= 0 || MyWish != MateWish) return;
            int tomb = MyWish;
            MyWish = MateWish = 0;
            BeginCoopTry(tomb);
        }

        void BeginCoopTry(int number)
        {
            ++_loadToken;
            ResetForLoad();
            var tomb = CoopCampaign.Get(number);
            Coop = new CoopSession(tomb);
            _coopOver = _coopAwait = _mateReady = _mateBusy = false;
            _coopMateSteps.Clear();
            CurrentLevel = tomb.Level.Id;
            LevelLoading?.Invoke(CurrentLevel);
            Setup(tomb.Level, Coop.Mummy(_myMummy));

            var theme = TombTheme.ForAct(CurrentLevel.Act);
            _mate.gameObject.SetActive(true);
            _mate.SetTheme(theme, _app.Fx.Unlit);
            _mate.ResetVisual();
            _mate.SetSkin(PvpSkins.Loadout(CoopInfo.MateLook));
            _mate.Place(MateSession.Position);
            _mate.SetBlind(false);
            _mate.SetReversed(0);
            _mate.SetTorchLit(true);

            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        /// <summary>Tells the teammate the preview is over and waits for his phone (both clocks start together).</summary>
        IEnumerator WaitForMate()
        {
            if (!InCoop) yield break;
            _input.Enabled = false;
            CoopInfo.Link.Send(new RelayMessage { Kind = RelayMessageKind.Ready, From = CoopInfo.Me });
            CoopWaiting = true;
            PreviewChanged?.Invoke();
            float left = 10f;
            while (!_mateReady && InCoop && left > 0f)
            {
                left -= Time.unscaledDeltaTime;
                yield return null;
            }
            CoopWaiting = false;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ actions

        IEnumerator PlayCoop(PlayerAction action)
        {
            if (Coop == null || _coopOver || CoopWaiting) yield break;
            if (!CoopInfo.Host)
            {
                // The guest asks; the host's answer plays the step (OnCoopMessage).
                _coopAwait = true;
                _awaitSince = Time.unscaledTime;
                _busy = true;
                CoopInfo.Link.Send(new RelayMessage
                {
                    Kind = RelayMessageKind.Input, From = CoopInfo.Me, Tick = CoopWire.Wanted, Maze = _myMummy, Direction = RunActions.Encode(action),
                });
                yield break;
            }
            var before = Session.State;
            var from = Session.Position;
            var r = Coop.Apply(_myMummy, action);
            if (r.Has(StepFlags.Blocked))
            {
                yield return CoopBump(action, from, r);
                yield break;
            }
            SendCoopStep(_myMummy, action);
            yield return Present(before, from, action, r);
        }

        IEnumerator CoopBump(PlayerAction action, Cell from, StepResult r)
        {
            _busy = false;
            if (action.Kind != ActionKind.Move) yield break;
            if (_held == action.Dir) _heldBlocked = true;
            _app.Audio.Play(Sfx.Bump);
            _app.Fx.Bump(MazeView.CellToWorld(from), r.Dir);
            _busy = true;
            yield return _player.Bump(r.Dir);
            _busy = false;
        }

        /// <summary>Host: the action just accepted, numbered, to the guest.</summary>
        void SendCoopStep(int mummy, PlayerAction action) =>
            CoopInfo.Link.Send(new RelayMessage
            {
                Kind = RelayMessageKind.Input, From = CoopInfo.Me, Tick = Coop.History.Count - 1, Maze = mummy, Direction = RunActions.Encode(action),
            });

        void OnCoopMessage(RelayMessage m)
        {
            if (!InCoop || m.From == CoopInfo.Me) return;
            switch (m.Kind)
            {
                case RelayMessageKind.Ready:
                    _mateReady = true;
                    break;
                case RelayMessageKind.Start:
                    if (int.TryParse(m.Vote, out int tomb) && CoopCampaign.Get(tomb) != null)
                    {
                        MateWish = tomb;
                        TryBeginCoop();
                        Changed?.Invoke();
                    }
                    break;
                case RelayMessageKind.Quit:
                    MateGone();
                    break;
                case RelayMessageKind.Input:
                    if (RunActions.TryDecode(m.Direction, out var action)) OnCoopInput(m, action);
                    break;
            }
        }

        void OnCoopLeft(string playerId) => MateGone();

        void OnCoopInput(RelayMessage m, PlayerAction action)
        {
            if (Coop == null) return;
            if (CoopInfo.Host)
            {
                // The guest's wish: applied now, in the order it came, then told back.
                if (m.Maze != MateMummy || m.Tick != CoopWire.Wanted) return;
                var mateFrom = MateSession.Position;
                var mineBefore = Session.State;
                var r = Coop.Apply(m.Maze, action);
                if (r.Has(StepFlags.Blocked))
                {
                    CoopInfo.Link.Send(new RelayMessage { Kind = RelayMessageKind.Input, From = CoopInfo.Me, Tick = CoopWire.Refused, Maze = m.Maze, Direction = m.Direction });
                    return;
                }
                SendCoopStep(m.Maze, action);
                _coopMateSteps.Enqueue(new MateStep { Action = action, R = r, From = mateFrom, MineBefore = mineBefore });
                return;
            }

            // Guest: the host's answer.
            bool mine = m.Maze == _myMummy;
            if (m.Tick == CoopWire.Refused)
            {
                if (!mine || !_coopAwait) return;
                _coopAwait = false;
                StartCoroutine(CoopBump(action, Session.Position, new StepResult { Flags = StepFlags.Blocked, Dir = Session.State.WorldDir(action.Dir) }));
                return;
            }
            if (m.Tick < 0) return;
            if (m.Tick != Coop.History.Count) Debug.LogWarning($"[Coop] Step {m.Tick} arrived, {Coop.History.Count} expected: out of step");
            var before = Session.State;
            var from = Session.Position;
            var matePos = MateSession.Position;
            var step = Coop.Apply(m.Maze, action);
            if (step.Has(StepFlags.Blocked))
            {
                Debug.LogWarning($"[Coop] Step {m.Tick} refused here: out of step");
                if (mine) { _coopAwait = false; _busy = false; }
                return;
            }
            if (mine)
            {
                _coopAwait = false;
                StartCoroutine(Present(before, from, action, step));
            }
            else _coopMateSteps.Enqueue(new MateStep { Action = action, R = step, From = matePos, MineBefore = before });
        }

        void UpdateCoop()
        {
            if (!InCoop) return;
            CoopInfo.Link.Pump();
            if (!_mateBusy && _coopMateSteps.Count > 0) StartCoroutine(PresentMate(_coopMateSteps.Dequeue()));
            // The host never answered (it would have left): stop waiting.
            if (_coopAwait && Time.unscaledTime - _awaitSince > 5f) { _coopAwait = false; _busy = false; }
        }

        /// <summary>After one of the player's steps: the end of the try, or the joystick's next step.</summary>
        IEnumerator CoopStepDone()
        {
            if (Coop.Status != SessionStatus.Playing)
            {
                yield return FinishCoop();
                _busy = false;
                yield break;
            }
            _busy = false;
            RepeatHeld();
        }

        /// <summary>The teammate's step: its mummy walks, the gates it moved open or close, its traps go off.</summary>
        IEnumerator PresentMate(MateStep s)
        {
            _mateBusy = true;
            var fx = _app.Fx;
            var audio = _app.Audio;
            var r = s.R;
            var landed = MazeView.CellToWorld(r.SteppedOn);
            if (s.Action.Kind == ActionKind.Disarm)
            {
                audio.Play(Sfx.Disarm);
                fx.Disarm(MazeView.CellToWorld(s.From.Step(s.Action.Dir)));
                _maze.RefreshSprites();
                yield return new WaitForSeconds(RunTiming.DisarmMs / 1000f);
            }
            else
            {
                if (r.Has(StepFlags.Collapsed))
                {
                    audio.Play(Sfx.Crumble);
                    fx.Collapse(MazeView.CellToWorld(s.From));
                    _maze.RefreshSprites();
                }
                yield return _mate.WalkTo(s.From.Step(r.Dir));
                if (r.Has(StepFlags.ButtonPressed))
                {
                    audio.Play(r.Has(StepFlags.Switched) ? Sfx.Laser : Sfx.Button);
                    if (r.Has(StepFlags.Switched)) fx.Switched(landed, Session.IsChannelActive(r.Channel));
                    else fx.ButtonPressed(landed, new Color(0.3f, 1f, 0.9f));
                }
                if (r.Has(StepFlags.Damaged))
                {
                    audio.Play(Sfx.Spikes);
                    fx.Spikes(landed);
                    yield return _mate.Hurt();
                }
                if (r.Has(StepFlags.Teleported))
                {
                    audio.Play(Sfx.Teleport);
                    fx.TeleportOut(landed, false);
                    yield return _mate.Vanish();
                    var arrival = MateSession.Position;
                    _mate.Place(arrival);
                    _mate.ResetVisual();
                    fx.TeleportIn(MazeView.CellToWorld(arrival), false);
                    yield return _mate.Appear();
                }
            }
            // Held plates and levers: the gates the teammate moved, seen from here.
            GatesMoved(s.MineBefore);
            _mate.SetBlind(MateSession.IsBlind);
            _mate.SetTorchLit(MateSession.TorchLit);
            _maze.RefreshSprites();
            Changed?.Invoke();
            _mateBusy = false;
            if (Coop != null && Coop.Status != SessionStatus.Playing) yield return FinishCoop();
        }

        IEnumerator FinishCoop()
        {
            if (_coopOver || Coop == null) yield break;
            _coopOver = true;
            _input.Enabled = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            bool won = Coop.Status == SessionStatus.Won;
            if (won)
            {
                _app.Fx.Win(MazeView.CellToWorld(Session.Position));
                _app.Audio.Play(Sfx.Win);
                _app.Lighting.Flash(new Color(1f, 0.8f, 0.3f), 0.8f);
                StartCoroutine(_mate.Vanish(0.5f));
                yield return _player.Vanish(0.5f);
            }
            else
            {
                bool me = Coop.Fallen == _myMummy;
                var at = me ? Session.Position : MateSession.Position;
                _app.Fx.Death(MazeView.CellToWorld(at));
                _app.Audio.Play(Sfx.Death);
                _app.Camera.Shake(0.8f);
                yield return me ? _player.Die() : _mate.Die();
            }
            yield return new WaitForSeconds(0.35f);
            var inputs = new List<CoopRunInput>();
            foreach (var (mummy, action) in Coop.History) inputs.Add(new CoopRunInput { Mummy = mummy, Direction = RunActions.Encode(action) });
            CoopEnded?.Invoke(new CoopOutcome
            {
                Info = CoopInfo, Tomb = Coop.Tomb, Won = won, Result = Coop.BuildResult(), Inputs = inputs, Fallen = Coop.Fallen,
            });
        }

        void MateGone()
        {
            if (!InCoop) return;
            var info = CoopInfo;
            _input.Enabled = false;
            CoopEnded?.Invoke(new CoopOutcome { Info = info, Tomb = Coop?.Tomb, MateLeft = true });
        }

        /// <summary>Back to the menus: the teammate is told, the connection closes.</summary>
        public void LeaveCoop()
        {
            if (!InCoop) return;
            CoopInfo.Link.Send(new RelayMessage { Kind = RelayMessageKind.Quit, From = CoopInfo.Me });
            Abandon();
        }

        void ClearCoop()
        {
            if (CoopInfo != null)
            {
                CoopInfo.Link.Received -= OnCoopMessage;
                CoopInfo.Link.Left -= OnCoopLeft;
                CoopInfo.Link.Dispose();
            }
            CoopInfo = null;
            Coop = null;
            CoopWaiting = false;
            MyWish = MateWish = 0;
            _coopMateSteps.Clear();
            _coopAwait = _mateBusy = false;
            if (_mate != null) _mate.gameObject.SetActive(false);
        }
    }
}
