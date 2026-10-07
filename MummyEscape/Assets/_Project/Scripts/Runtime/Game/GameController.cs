using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Input;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.UI.Screens;
using MummyEscape.Visual;
using MummyEscape.World;
using UnityEngine;

namespace MummyEscape.Game
{
    /// <summary>
    /// Runs one level: generates it (off the main thread), feeds input to the <see cref="GameSession"/> and turns
    /// each <see cref="StepResult"/> into animation, sound, light and haptics.
    /// </summary>
    public sealed partial class GameController : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public LevelId CurrentLevel { get; private set; }

        public event Action Changed;
        public event Action<LevelId> LevelLoading;
        public event Action LevelStarted;

        GameApp _app;
        MazeView _maze;
        PlayerView _player;
        GhostView _ghost;
        SwipeInput _input;
        readonly Dictionary<(LevelId, int), Task<Level>> _pending = new Dictionary<(LevelId, int), Task<Level>>();

        public bool Previewing { get; private set; }
        public float PreviewLeft { get; private set; }
        /// <summary>Floor shown by the preview right now (0 = bottom).</summary>
        public int PreviewFloor { get; private set; }
        /// <summary>The preview's pages (one per floor; in a 2v2, every floor of both mazes) and the one on screen.</summary>
        public int PreviewPage { get; private set; }
        public int PreviewPages { get; private set; }
        public event Action PreviewChanged;
        bool _skipPreview;
        /// <summary>Puts a page of the preview on screen (solo and duel: a floor; 2v2: a maze and a floor).</summary>
        Action<int> _showPreviewPage;
        /// <summary>Swipe made on a one-floor preview: played as soon as the fog falls.</summary>
        Dir? _pendingMove;

        bool _busy;
        bool _paused;
        PlayerAction? _buffered;
        int _loadToken;

        public void Init(GameApp app, MazeView maze, PlayerView player, GhostView ghost, SwipeInput input)
        {
            _app = app;
            _maze = maze;
            _player = player;
            _ghost = ghost;
            _input = input;
            _input.Swiped += OnSwipe;
            _input.Tapped += OnTap;
            _app.Guard.ScreenshotTaken += OnPreviewScreenshot;
            _player.gameObject.SetActive(false);
        }

        public async Task StartLevel(LevelId id)
        {
            int token = ++_loadToken;
            ResetForLoad();
            DetachDuelLink();
            Match = null;
            Pace = null;
            CurrentLevel = id;
            _app.Audio.PlayMusic(id.Act); // renders (if needed) while the maze is generated
            LevelLoading?.Invoke(id);

            // Every run draws a new maze of the level (variant), usually prefetched while the previous run was played.
            int variant = _app.Save.NextVariant(id);
            Level level;
            try
            {
                level = await GetLevel(id, variant);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not generate level {id} maze {variant}: {e}");
                return;
            }
            finally
            {
                _pending.Remove((id, variant));
            }
            if (token != _loadToken || this == null) return; // another level was requested meanwhile
            Setup(level);
            Pace = Task.Run(() => TombPace.Of(level));
            Prefetch(id); // the replay's maze, ready before the player needs it
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        /// <summary>The tutorial corridor is being played (no preview, no record, no energy).</summary>
        public bool InTutorial { get; private set; }
        /// <summary>The tutorial run is over: true when the mummy got out.</summary>
        public event Action<bool> TutorialEnded;

        /// <summary>Starts the tutorial: a hand-made corridor that meets every mechanic, lit and without preview.</summary>
        public void StartTutorial()
        {
            ++_loadToken;
            ResetForLoad();
            DetachDuelLink();
            Match = null;
            Pace = null;
            InTutorial = true;
            CurrentLevel = Tutorial.Id;
            _app.Audio.PlayMusic(Tutorial.Id.Act);
            LevelLoading?.Invoke(Tutorial.Id);
            Setup(Tutorial.Build());
            LevelStarted?.Invoke();
            _input.Enabled = !_paused;
            PreviewChanged?.Invoke();
            Changed?.Invoke();
        }

        void ResetForLoad()
        {
            InTutorial = false;
            StopAllCoroutines();
            EndPreviewVisuals();
            _busy = false;
            _buffered = null;
            _input.Enabled = false;
            ScreenshotRedraw = false;
            _ghost.Hide();
        }

        // ------------------------------------------------------------------ duels

        /// <summary>The duel being played, null in solo.</summary>
        public PvpMatch Match { get; private set; }

        /// <summary>
        /// Solo tomb: the expert mummy's time (to beat) and the perfect minimum, worked out off the main thread while
        /// the map is previewed. Null in a duel.
        /// </summary>
        public Task<TombPace> Pace { get; private set; }
        public bool InDuel => Match != null;
        /// <summary>Raised when a duel run ends (exit, death, time out, forfeit) with what goes to the server.</summary>
        public event Action<PvpMatch, RunSubmission> DuelEnded;

        /// <summary>Time left in the duel, in milliseconds.</summary>
        public int DuelTimeLeftMs => Session == null ? PvpConfig.TimeLimitMs : Math.Max(0, PvpConfig.TimeLimitMs - Session.ElapsedMs);

        /// <summary>
        /// Starts a duel on the tomb the server drew. Same rules as solo, with three differences: the preview is always
        /// shown, the clock never stops (not even in the pause menu) and the run ends after 3 minutes.
        /// </summary>
        public async Task StartDuel(PvpMatch match, IPeerLink link = null)
        {
            int token = ++_loadToken;
            ResetForLoad();
            Session = null; // the last run's clock must not reach the new duel while its tomb is generated
            Match = match;
            AttachDuelLink(link);
            Pace = null;
            var id = PvpArena.LevelFor(match.Seed);
            CurrentLevel = id;
            _app.Audio.PlayMusic(id.Act);
            LevelLoading?.Invoke(id);
            Level level;
            try
            {
                level = await Task.Run(() => PvpServer.Arena(match.Seed));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not generate duel tomb {match.Seed}: {e}");
                return;
            }
            if (token != _loadToken || this == null) return;
            match.Begin(level);
            Setup(level);
            if (match.HasGhost)
            {
                _ghost.SetLook(PvpSkins.Loadout(match.Ghost.Look));
                _ghost.Snap(level.Start);
            }
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        /// <summary>Leaves the duel: the run counts as a forfeit (a loss).</summary>
        public void ForfeitDuel()
        {
            if (Match == null || Match.Over) return;
            _duelLink?.Send(new RelayMessage { Kind = RelayMessageKind.Quit, From = _duelLink.Me });
            EndDuel(RunOutcome.Abandoned);
        }

        void EndDuel(RunOutcome outcome)
        {
            var match = Match;
            match.Over = true;
            _input.Enabled = false;
            _buffered = null;
            var run = match.BuildRun(outcome);
            run.Look = PvpSkins.Look(_app.Save.Loadout);
            run.Look.Title = TitleBook.Equipped(_app);
            DuelEnded?.Invoke(match, run);
        }

        void UpdateDuel()
        {
            _duelLink?.Pump();
            var match = Match;
            if (match == null || Session == null || match.Level == null || match.Over) return;
            int elapsed = Session.ElapsedMs;
            if (match.AdvanceGhost(elapsed)) Changed?.Invoke();
            if (match.GhostReplay != null)
            {
                var g = match.GhostReplay.Session;
                bool gone = g.Status == SessionStatus.Won; // out of the tomb
                bool seen = !Previewing && !gone && g.Position.Floor == Session.Position.Floor && Session.IsVisible(g.Position);
                _ghost.Show(g.Position, seen);
            }
            UpdateLiveDuel(match);
            if (match.Over) return;
            // Out of time: the run stops where it is (an action being animated has already counted).
            if (!Previewing && Session.Status == SessionStatus.Playing && elapsed >= PvpConfig.TimeLimitMs)
            {
                _app.Audio.Play(Sfx.Death);
                EndDuel(RunOutcome.TimedOut);
            }
        }

        void Setup(Level level)
        {
            Session = new GameSession(level);
            ApplyTheme(TombTheme.ForAct(level.Id.Act));
            IndexMechanisms(level);
            _maze.Build(Session);
            _player.gameObject.SetActive(true);
            _player.ResetVisual();
            _player.SetSkin(_app.Save.Loadout);
            _player.Place(level.Start);
            _player.SetBlind(false);
            _player.SetReversed(0);
            _player.SetTorchLit(true);
            _app.Camera.SnapTo(MazeView.CellToWorld(level.Start));
            _app.Camera.SetTurn(0, true);
            _app.Lighting.SetMood(true);
            _paused = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        void ApplyTheme(TombTheme theme)
        {
            _app.Art.SetTheme(theme);
            _app.Lighting.SetTheme(theme);
            _player.SetTheme(theme, _app.Fx.Unlit);
        }

        /// <summary>Gates and flame jets, so a step can tell which of them changed and play their effects.</summary>
        readonly List<Cell> _gates = new List<Cell>();
        readonly List<Cell> _fireJets = new List<Cell>();

        void IndexMechanisms(Level level)
        {
            _gates.Clear();
            _fireJets.Clear();
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                if (t.IsGate) _gates.Add(c);
                else if (t.Type == TileType.FireJet) _fireJets.Add(c);
            }
        }

        /// <summary>Starts generating the maze the next run of <paramref name="id"/> will use (no-op when already done).</summary>
        public void Prefetch(LevelId id) => _ = GetLevel(id, _app.Save.PeekVariant(id));

        Task<Level> GetLevel(LevelId id, int variant)
        {
            if (_pending.TryGetValue((id, variant), out var task)) return task;
            // Keep the cache tiny: drop finished mazes nobody is going to play.
            var stale = new List<(LevelId, int)>();
            foreach (var kv in _pending)
                if (kv.Value.IsCompleted && _app.Save.PeekVariant(kv.Key.Item1) != kv.Key.Item2) stale.Add(kv.Key);
            foreach (var k in stale) _pending.Remove(k);
            task = Task.Run(() => LevelGenerator.Generate(id, variant));
            _pending[(id, variant)] = task;
            return task;
        }

        // ------------------------------------------------------------------ map preview

        /// <summary>
        /// Shows the tomb before the run: <see cref="Level.PreviewSeconds"/> (7 s per floor) pooled, the first floor first,
        /// and a swipe goes up or down a floor as often as the player likes until the time is spent. "Ready" starts at
        /// once; on a one-floor tomb a swipe does too, as the first move. Turned off for good in the settings, the run
        /// starts at once.
        /// </summary>
        IEnumerator PreviewRoutine()
        {
            if (!_app.Settings.ShowPreview && Match == null) // a duel always shows the tomb: both rivals get the same look
            {
                _input.Enabled = !_paused && Session != null && Session.Status == SessionStatus.Playing;
                PreviewChanged?.Invoke();
                yield break;
            }
            Previewing = true;
            _pendingMove = null;
            _skipPreview = false;
            _input.Enabled = !_paused; // a swipe changes floor
            _app.Guard.Arm(true); // the map must not leave the phone (screenshot / recording)
            _app.Lighting.SetPreview(true);
            int startFloor = Session.Level.Start.Floor;
            PreviewPages = Session.Level.Floors;
            _showPreviewPage = floor =>
            {
                PreviewPage = PreviewFloor = floor;
                _maze.SetPreview(true, floor);
                _player.gameObject.SetActive(floor == startFloor); // the mummy stands on the start floor only
                _app.Camera.ShowArea(_maze.PreviewBounds());
                PreviewChanged?.Invoke();
            };
            PreviewLeft = Session.Level.PreviewSeconds;
            _showPreviewPage(0);
            // Let the camera settle on the floor before the clock starts.
            yield return new WaitForSeconds(0.35f);
            while (PreviewLeft > 0f && !_skipPreview)
            {
                // Screen being recorded or mirrored: the tomb stays dark, and the clock waits for it to stop (as in the pause menu).
                bool captured = _app.Guard.IsCaptured;
                if (captured != _maze.Concealed)
                {
                    _maze.Concealed = captured;
                    PreviewChanged?.Invoke();
                }
                // In a duel the pause menu does not stop the preview either (no studying the map at leisure).
                if (!captured && (!_paused || Match != null)) PreviewLeft -= Time.deltaTime;
                yield return null;
            }
            PreviewLeft = 0f;
            _showPreviewPage = null;
            _player.gameObject.SetActive(true);
            EndPreviewVisuals();
            // Live duel: both phones start together.
            if (InLiveDuel) yield return WaitForRival();
            _app.Audio.Play(Sfx.Darkness);
            _input.Enabled = !_paused && Session != null && Session.Status == SessionStatus.Playing;
            PreviewChanged?.Invoke();
            Changed?.Invoke();
            // The swipe that ended a one-floor preview is the first move of the run.
            if (_pendingMove.HasValue) Submit(PlayerAction.Move(_pendingMove.Value));
            _pendingMove = null;
        }

        /// <summary>Up or right: the next page of the preview (the floor above); down or left: the one before.</summary>
        public void TurnPreviewPage(int delta)
        {
            if (!Previewing || _showPreviewPage == null) return;
            int page = Mathf.Clamp(PreviewPage + delta, 0, PreviewPages - 1);
            if (page != PreviewPage) _showPreviewPage(page);
        }

        /// <summary>Set when the last tomb was thrown away because of a screenshot (the HUD tells the player why).</summary>
        public bool ScreenshotRedraw { get; private set; }

        /// <summary>The screen is being recorded during the preview: the map is hidden and the countdown paused.</summary>
        public bool ScreenCaptured => Previewing && _maze.Concealed;

        /// <summary>
        /// A screenshot of the map was taken (iOS, where it cannot be blocked): that tomb is burned, a new maze of the
        /// same level is drawn and its preview starts over. The screenshot shows a layout nobody will ever play.
        /// </summary>
        void OnPreviewScreenshot()
        {
            // A duel's tomb is drawn by the server and shared with the rival: it cannot be swapped.
            if (!Previewing || Session == null || Match != null) return;
            _ = RedrawAfterScreenshot();
        }

        async Task RedrawAfterScreenshot()
        {
            await StartLevel(CurrentLevel);
            ScreenshotRedraw = true;
            PreviewChanged?.Invoke();
        }

        /// <summary>"Ready": the run starts now, whatever floor the preview shows.</summary>
        public void SkipPreview()
        {
            if (!InLiveDuel) _skipPreview = true; // a live duel shows the tomb as long to both rivals
        }

        void EndPreviewVisuals()
        {
            if (!Previewing) return;
            Previewing = false;
            _app.Guard.Arm(false);
            _maze.Concealed = false;
            _maze.SetPreview(false);
            _app.Lighting.SetPreview(false);
            if (Session != null) _app.Camera.EndArea(MazeView.CellToWorld(Session.Position));
        }

        public void Restart()
        {
            if (InTutorial) StartTutorial();
            else             if (Match == null) _ = StartLevel(CurrentLevel);
        }

        public void Abandon()
        {
            _loadToken++;
            ClearRelay();
            DetachDuelLink();
            EndPreviewVisuals();
            StopAllCoroutines();
            InTutorial = false;
            Session = null;
            Match = null;
            _ghost.Hide();
            _busy = false;
            _input.Enabled = false;
            _maze.Clear();
            _player.gameObject.SetActive(false);
            _app.Lighting.SetMood(false);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        void Update()
        {
            // The clock runs once the tomb is hidden, never while paused. Scaled delta: Unity caps it after a hitch or
            // when the app comes back from the background, so a phone call does not ruin a run.
            // In a duel the clock never stops: the rival's ghost did not pause either.
            bool duel = Match != null;
            UpdateRelay();
            // A step being animated keeps the clock running even in pause: pausing on every swipe must not hide the
            // animations from the time (nobody beats RunTiming's minimum).
            if (Session != null && Relay == null && !Previewing && !DuelWaiting && (!_paused || duel || _busy) && !(duel && Match.Over)) Session.Tick(Time.deltaTime);
            if (duel) UpdateDuel();
            RepeatHeld();
            // A floor may have its own music (Resources/Music/act{n}_f{floor}); no-op while the track playing fits.
            if (Session != null && Session.Status == SessionStatus.Playing && !Previewing)
                _app.Audio.PlayLevelMusic(CurrentLevel.Act, Session.Position.Floor + 1);
        }

        void LateUpdate()
        {
            // The tomb may be turned on screen (turning slab): the mummy and the ghost stay upright.
            var turn = _app.Camera.Turn;
            _player.transform.rotation = turn;
            _ghost.transform.rotation = turn;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _input.Enabled = !paused && Session != null && Session.Status == SessionStatus.Playing && (Relay == null || MyTurn);
        }

        public void SetMapView(bool on)
        {
            if (Session == null || Previewing) return;
            int floor = Session.Position.Floor;
            var b = _maze.ExploredBounds(floor);
            _app.Camera.SetMapView(on, b.center, Mathf.Max(b.extents.x, b.extents.y) + 1f);
            if (!on) _app.Camera.Follow(MazeView.CellToWorld(Session.Position));
        }

        // ------------------------------------------------------------------ input

        Dir? _held;
        bool _heldBlocked;

        /// <summary>A direction pressed on the on-screen pad or joystick: the same as a swipe.</summary>
        public void PressMove(Dir d)
        {
            if (_input.Enabled) OnSwipe(d);
        }

        /// <summary>
        /// The joystick held in a direction (null: let go). That move repeats at the game's own pace, each step as soon as
        /// the last one is shown, until a wall stops it (pushing the other way, or again, goes on).
        /// </summary>
        public void HoldMove(Dir? d)
        {
            if (d != _held) _heldBlocked = false;
            _held = d;
        }

        void RepeatHeld()
        {
            if (!_held.HasValue || _heldBlocked || _busy || _buffered.HasValue || !_input.Enabled || Previewing) return;
            if (Session == null || Session.Status != SessionStatus.Playing) return;
            Submit(PlayerAction.Move(_held.Value));
        }

        void OnSwipe(Dir d)
        {
            // During the map preview, a swipe changes floor; on a one-floor tomb it starts the run with that move.
            if (Previewing)
            {
                if (PreviewPages > 1) TurnPreviewPage(d == Dir.Up || d == Dir.Right ? 1 : -1);
                else if (!InLiveDuel && !InRelay)
                {
                    _pendingMove = d;
                    SkipPreview();
                }
                return;
            }
            Submit(PlayerAction.Move(d));
        }

        void OnTap(Vector2 screenPos)
        {
            if (Session == null || Previewing) return;
            var world = _app.Camera.Cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
            var cell = MazeView.WorldToCell(world, Session.Position.Floor);
            int dx = cell.X - Session.Position.X, dy = cell.Y - Session.Position.Y;
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1) return;
            var toward = dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Up : Dir.Down;
            // A tap is a swipe toward that tile on screen: the turned tomb and the mirror apply to it as well.
            var swipe = toward.Turn(Session.State.Rotation);
            var dest = Session.Position.Step(Session.State.WorldDir(swipe));

            // Tapping a neighbour moves there, except onto visible spikes: no life lost to a stray tap (swipe to take
            // the hit on purpose, or use the disarm button).
            if (Session.IsVisible(dest) && Rules.IsDisarmable(Session.Level.Get(dest), Session.State.Disarmed)) return;
            Submit(PlayerAction.Move(swipe));
        }

        /// <summary>The HUD disarm button: disarms the visible spikes next to the mummy, if any.</summary>
        public void DisarmAdjacent()
        {
            if (Session != null && Session.CanDisarm(out var dir)) Submit(PlayerAction.Disarm(dir));
        }

        /// <summary>Feeds one action to the game (swipe / tap handlers, and the editor autoplay tool).</summary>
        public void Submit(PlayerAction action)
        {
            if (Session == null || _paused || Previewing || Session.Status != SessionStatus.Playing) return;
            if (Match != null && Match.Over) return;
            if (Relay != null && !MyTurn) return;
            if (_busy) { _buffered = action; return; }
            StartCoroutine(Play(action));
        }

        // ------------------------------------------------------------------ step presentation

        IEnumerator Play(PlayerAction action)
        {
            if (Relay != null) { yield return PlayRelay(action); yield break; }
            var before = Session.State;
            var from = Session.Position;
            var r = Session.Apply(action);
            var fx = _app.Fx;
            if (r.Has(StepFlags.Blocked))
            {
                if (action.Kind == ActionKind.Move)
                {
                    if (_held == action.Dir) _heldBlocked = true; // the joystick stops repeating into a wall
                    _app.Audio.Play(Sfx.Bump);
                    fx.Bump(MazeView.CellToWorld(from), r.Dir);
                    _busy = true;
                    yield return _player.Bump(r.Dir);
                    _busy = false;
                }
                yield break;
            }
            // Duel: every accepted action is recorded with its time, for the server's replay and the rival's ghost.
            Match?.Record(action, Session.ElapsedMs, before, Session);
            SendDuelInput();
            yield return Present(before, from, action, r);
        }

        /// <summary>Animation, sound, light and haptics of an accepted step (the player's, or a teammate's in the 2v2).</summary>
        IEnumerator Present(RuleState before, Cell from, PlayerAction action, StepResult r)
        {
            var fx = _app.Fx;
            _busy = true;
            Changed?.Invoke();
            var audio = _app.Audio;
            var cam = _app.Camera;

            if (action.Kind == ActionKind.Disarm)
            {
                audio.Play(Sfx.Disarm);
                fx.Disarm(MazeView.CellToWorld(from.Step(action.Dir)));
                Haptic();
                _maze.RefreshSprites();
                yield return new WaitForSeconds(RunTiming.DisarmMs / 1000f);
            }
            else
            {
                bool transport = r.Has(StepFlags.Teleported) || r.Has(StepFlags.Fell) || r.Has(StepFlags.Climbed);
                if (transport) _maze.FloorOverride = r.SteppedOn.Floor;

                var level = Session.Level;
                var first = from.Step(r.Dir);
                var landed = MazeView.CellToWorld(r.SteppedOn);

                audio.Play(Sfx.Step, 0.12f);
                fx.Footstep(MazeView.CellToWorld(from));
                if (r.Has(StepFlags.Collapsed))
                {
                    // The fragile slab gives way right behind the mummy.
                    audio.Play(Sfx.Crumble);
                    fx.Collapse(MazeView.CellToWorld(from));
                    cam.Shake(0.25f);
                    _maze.RefreshSprites();
                }
                cam.Follow(landed);
                yield return _player.WalkTo(first);

                if (r.Has(StepFlags.Swept))
                {
                    // Carried downstream tile after tile (same walk as the rules).
                    audio.Play(Sfx.Splash);
                    var c = first;
                    for (int guard = 0; guard < 64 && !c.Equals(r.SteppedOn) && level[c].Type == TileType.Current; guard++)
                    {
                        var d = (Dir)level[c].Param;
                        fx.Splash(MazeView.CellToWorld(c), d);
                        c = c.Step(d);
                        yield return _player.Slide(c);
                    }
                    fx.Splash(landed, null);
                }
                else if (level[r.SteppedOn].Type == TileType.Current) fx.Splash(landed, null);

                if (r.Has(StepFlags.ButtonPressed))
                {
                    if (r.Has(StepFlags.Switched))
                    {
                        audio.Play(Sfx.Laser);
                        fx.Switched(landed, Session.IsChannelActive(r.Channel));
                    }
                    else
                    {
                        audio.Play(Sfx.Button);
                        audio.Play(Sfx.Door);
                        fx.ButtonPressed(landed, new Color(0.3f, 1f, 0.9f));
                    }
                    GatesMoved(before);
                    cam.Shake(0.3f);
                    Haptic();
                }
                if (r.Has(StepFlags.Damaged))
                {
                    if (r.Has(StepFlags.Burned))
                    {
                        audio.Play(Sfx.Fire);
                        fx.FireBlast(landed, true);
                        _app.Lighting.Flash(new Color(1f, 0.45f, 0.1f));
                    }
                    else
                    {
                        audio.Play(Sfx.Spikes);
                        fx.Spikes(landed);
                        _app.Lighting.Flash(new Color(0.9f, 0.1f, 0.05f));
                    }
                    cam.Shake(0.7f);
                    Haptic();
                    _maze.RefreshSprites();
                    yield return _player.Hurt();
                }
                if (r.Has(StepFlags.Blinded))
                {
                    audio.Play(Sfx.Darkness);
                    fx.Darkness(landed);
                    _app.Lighting.Flash(new Color(0.4f, 0.1f, 0.6f));
                }
                if (r.Has(StepFlags.Reversed))
                {
                    audio.Play(Sfx.Mirror);
                    fx.ButtonPressed(landed, new Color(1f, 0.35f, 0.75f));
                    _app.Lighting.Flash(new Color(0.9f, 0.2f, 0.6f));
                    Haptic();
                }
                if (r.Has(StepFlags.Rotated))
                {
                    audio.Play(Sfx.Turn);
                    fx.ButtonPressed(landed, new Color(1f, 0.8f, 0.3f));
                    cam.SetTurn(Session.State.Rotation);
                    cam.Shake(0.4f);
                    Haptic();
                    _maze.RefreshSprites();
                    yield return new WaitForSeconds(RunTiming.TombTurnMs / 1000f); // let the tomb turn before the next step
                }
                if (r.Has(StepFlags.PortalSealed)) audio.Play(Sfx.Bump);
                if (r.Has(StepFlags.TorchSmothered))
                {
                    audio.Play(Sfx.Darkness);
                    fx.TorchSmothered(landed);
                    _player.SetTorchLit(false);
                }
                if (r.Has(StepFlags.TorchRelit))
                {
                    audio.Play(Sfx.Teleport, 0.1f);
                    var torch = landed + (Vector3)ArtLibrary.TorchFlameOffset;
                    fx.TorchRelit(MazeView.CellToWorld(FindSconce(r.SteppedOn)), torch, _app.Art.Theme.SconceLight);
                    _player.SetTorchLit(true);
                    _app.Lighting.Flash(new Color(1f, 0.6f, 0.2f), 0.5f);
                }

                if (transport)
                {
                    bool cursed = r.Has(StepFlags.FogReset);
                    if (r.Has(StepFlags.Teleported))
                    {
                        audio.Play(cursed ? Sfx.Curse : Sfx.Teleport);
                        fx.TeleportOut(landed, cursed);
                        yield return _player.Vanish();
                    }
                    else if (r.Has(StepFlags.Fell))
                    {
                        audio.Play(Sfx.Fall);
                        audio.Play(Sfx.Crumble);
                        fx.Fall(landed);
                        cam.Shake(0.5f);
                        yield return _player.FallThrough();
                    }
                    else
                    {
                        audio.Play(Sfx.Climb);
                        fx.Climb(landed);
                        yield return _player.Vanish(RunTiming.ClimbMs / 1000f);
                    }

                    _maze.FloorOverride = -1;
                    if (r.Has(StepFlags.FogReset))
                    {
                        _maze.ForgetAll();
                        _app.Lighting.Flash(new Color(0.4f, 0.9f, 0.2f));
                    }
                    _player.Place(Session.Position);
                    _player.ResetVisual();
                    var arrival = MazeView.CellToWorld(Session.Position);
                    cam.SnapTo(arrival);
                    if (r.Has(StepFlags.Teleported)) fx.TeleportIn(arrival, cursed);
                    else if (r.Has(StepFlags.Fell)) fx.Fall(arrival);
                    else fx.Climb(arrival);
                    yield return _player.Appear();
                }
            }

            FlameJetsBeat(r);
            _player.SetBlind(Session.IsBlind);
            _player.SetReversed(Session.State.Reversed);
            _player.SetTorchLit(Session.TorchLit);
            _maze.RefreshSprites();
            Changed?.Invoke();

            if (Relay != null) { yield return RelayStepDone(); yield break; }
            if (Session.Status != SessionStatus.Playing)
            {
                yield return Finish();
                _busy = false;
                yield break;
            }

            _busy = false;
            if (_buffered.HasValue)
            {
                var next = _buffered.Value;
                _buffered = null;
                Submit(next);
            }
            else RepeatHeld();
        }

        /// <summary>Dust or sparks on every known gate of this floor that opened or closed with the last press.</summary>
        void GatesMoved(RuleState before)
        {
            var level = Session.Level;
            bool laser = false;
            foreach (var c in _gates)
            {
                if (c.Floor != Session.Position.Floor) continue;
                var t = level[c];
                bool was = Rules.IsGateOpen(t, before.Pressed), now = Session.IsDoorOpen(c);
                if (was == now || !(Session.IsExplored(c) || Session.IsVisible(c))) continue;
                bool isLaser = t.Type == TileType.Barrier;
                laser |= isLaser;
                var color = t.Param == 0 || !isLaser ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 0.6f, 1f);
                _app.Fx.GateMoved(MazeView.CellToWorld(c), isLaser, color);
            }
            if (laser) _app.Audio.Play(Sfx.Laser, 0.15f);
        }

        Cell FindSconce(Cell at)
        {
            foreach (Dir d in new[] { Dir.Up, Dir.Right, Dir.Down, Dir.Left })
            {
                var n = at.Step(d);
                if (Session.Level.Get(n).Type == TileType.WallTorch) return n;
            }
            return at;
        }

        /// <summary>Flame jets in sight blast on their beat, whether or not the mummy stands in them.</summary>
        void FlameJetsBeat(StepResult r)
        {
            bool any = false;
            foreach (var c in _fireJets)
            {
                if (c.Floor != Session.Position.Floor || !Session.IsVisible(c) || !Session.IsFiring(c)) continue;
                if (r.Has(StepFlags.Burned) && c.Equals(r.SteppedOn)) continue; // already shown, bigger
                _app.Fx.FireBlast(MazeView.CellToWorld(c), false);
                any = true;
            }
            if (any) _app.Audio.Play(Sfx.Fire, 0.2f);
        }

        IEnumerator Finish()
        {
            _input.Enabled = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            var result = Session.BuildResult();
            var here = MazeView.CellToWorld(Session.Position);
            if (result.Won)
            {
                _app.Fx.Win(here);
                _app.Audio.Play(Sfx.Win);
                _app.Lighting.Flash(new Color(1f, 0.8f, 0.3f), 0.8f);
                yield return _player.Vanish(0.5f);
            }
            else
            {
                if (Session.Defeat == DefeatCause.Trapped)
                {
                    // Walled in: the tomb rumbles shut around the mummy.
                    _app.Audio.Play(Sfx.Crumble);
                    _app.Fx.Collapse(here);
                    _app.Lighting.Flash(new Color(0.35f, 0.3f, 0.25f));
                    yield return new WaitForSeconds(0.4f);
                }
                _app.Fx.Death(here);
                _app.Audio.Play(Sfx.Death);
                _app.Camera.Shake(0.8f);
                yield return _player.Die();
            }
            yield return new WaitForSeconds(0.35f);

            if (InTutorial)
            {
                if (result.Won && !_app.Save.Data.TutorialDone)
                {
                    _app.Save.Data.TutorialDone = true;
                    _app.Save.Save();
                }
                TutorialEnded?.Invoke(result.Won);
                yield break;
            }
            if (Match != null)
            {
                // A duel touches neither the solo records nor the scarabs: its result comes from the server.
                if (!Match.Over) EndDuel(result.Won ? RunOutcome.Finished : RunOutcome.Died);
                yield break;
            }
            // Judge the time against the tomb's perfect minimum (rarely still being computed after a very quick run).
            var pace = Pace;
            if (pace != null)
            {
                while (!pace.IsCompleted) yield return null;
                if (pace.Status == TaskStatus.RanToCompletion)
                {
                    result.TargetMs = pace.Result.TargetMs ?? 0;
                    result.PerfectMs = pace.Result.PerfectMs ?? 0;
                    result.Pace = (int)pace.Result.Judge(result.TimeMs, result.Par);
                }
            }
            var outcome = _app.Save.Apply(result);
            // Faster than the game's animations allow: a modified game, kept off the leaderboard.
            if (result.Pace != (int)PaceVerdict.Impossible) _ = _app.Online.SubmitScoreAsync(result);
            else Debug.LogWarning($"[Game] {result.Level} run of {result.TimeMs} ms under the perfect {result.PerfectMs} ms: not submitted");
            if (result.Won)
            {
                _ = _app.PublishProgress();
                _ = _app.SyncSoloStars();
            }
            _app.UI.Open<RecapScreen>().Show(result, outcome);
        }

        void Haptic()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (_app.Settings.Haptics) Handheld.Vibrate();
#endif
        }
    }
}
