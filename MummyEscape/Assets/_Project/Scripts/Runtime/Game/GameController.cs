using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Input;
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
    public sealed class GameController : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public LevelId CurrentLevel { get; private set; }

        public event Action Changed;
        public event Action<LevelId> LevelLoading;
        public event Action LevelStarted;

        GameApp _app;
        MazeView _maze;
        PlayerView _player;
        SwipeInput _input;
        readonly Dictionary<(LevelId, int), Task<Level>> _pending = new Dictionary<(LevelId, int), Task<Level>>();

        /// <summary>Each floor of the tomb is shown this long at the start of every run (one after the other), then the fog falls.</summary>
        public const float PreviewSecondsPerFloor = 10f;
        public bool Previewing { get; private set; }
        public float PreviewLeft { get; private set; }
        /// <summary>Floor shown by the preview right now (0 = bottom), and how many floors are still to come.</summary>
        public int PreviewFloor { get; private set; }
        public int PreviewFloorsLeft { get; private set; }
        public bool PreviewOnLastFloor => PreviewFloorsLeft <= 0;
        public event Action PreviewChanged;
        bool _skipPreview;

        bool _busy;
        bool _paused;
        PlayerAction? _buffered;
        int _loadToken;

        public void Init(GameApp app, MazeView maze, PlayerView player, SwipeInput input)
        {
            _app = app;
            _maze = maze;
            _player = player;
            _input = input;
            _input.Swiped += OnSwipe;
            _input.Tapped += OnTap;
            _app.Guard.ScreenshotTaken += OnPreviewScreenshot;
            _player.gameObject.SetActive(false);
        }

        public async Task StartLevel(LevelId id)
        {
            int token = ++_loadToken;
            StopAllCoroutines();
            EndPreviewVisuals();
            _busy = false;
            _buffered = null;
            _input.Enabled = false;
            ScreenshotRedraw = false;
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

            Session = new GameSession(level);
            ApplyTheme(TombTheme.ForAct(level.Id.Act));
            IndexMechanisms(level);
            _maze.Build(Session);
            _player.gameObject.SetActive(true);
            _player.ResetVisual();
            _player.SetSkin(SkinCatalog.Get(_app.Save.Data.SelectedSkin));
            _player.Place(level.Start);
            _player.SetBlind(false);
            _player.SetTorchLit(true);
            _app.Camera.SnapTo(MazeView.CellToWorld(level.Start));
            _app.Lighting.SetMood(true);
            _paused = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Prefetch(id); // the replay's maze, ready before the player needs it
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
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
        /// Shows the floors one after the other (the start floor last), <see cref="PreviewSecondsPerFloor"/> each; "Ready" moves on
        /// to the next floor (or starts on the last one). Turned off for good in the settings, the run starts at once.
        /// </summary>
        IEnumerator PreviewRoutine()
        {
            if (!_app.Settings.ShowPreview)
            {
                _input.Enabled = !_paused && Session != null && Session.Status == SessionStatus.Playing;
                PreviewChanged?.Invoke();
                yield break;
            }
            Previewing = true;
            _input.Enabled = false;
            _app.Guard.Arm(true); // the map must not leave the phone (screenshot / recording)
            _app.Lighting.SetPreview(true);
            // The start floor comes last: the preview ends where the mummy stands, and that floor sinks into the dark.
            var order = new List<int>();
            int startFloor = Session.Level.Start.Floor;
            for (int f = 0; f < Session.Level.Floors; f++) if (f != startFloor) order.Add(f);
            order.Add(startFloor);
            for (int step = 0; step < order.Count; step++)
            {
                PreviewFloor = order[step];
                PreviewFloorsLeft = order.Count - 1 - step;
                _skipPreview = false;
                PreviewLeft = PreviewSecondsPerFloor;
                _maze.SetPreview(true, PreviewFloor);
                _player.gameObject.SetActive(PreviewFloor == startFloor); // the mummy stands on the start floor only
                _app.Camera.ShowArea(_maze.PreviewBounds());
                PreviewChanged?.Invoke();
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
                    if (!captured && !_paused) PreviewLeft -= Time.deltaTime;
                    yield return null;
                }
            }
            PreviewLeft = 0f;
            _player.gameObject.SetActive(true);
            EndPreviewVisuals();
            _app.Audio.Play(Sfx.Darkness);
            _input.Enabled = !_paused && Session != null && Session.Status == SessionStatus.Playing;
            PreviewChanged?.Invoke();
            Changed?.Invoke();
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
            if (!Previewing || Session == null) return;
            _ = RedrawAfterScreenshot();
        }

        async Task RedrawAfterScreenshot()
        {
            await StartLevel(CurrentLevel);
            ScreenshotRedraw = true;
            PreviewChanged?.Invoke();
        }

        /// <summary>"Ready": next floor of the preview, or the start of the run on the last one.</summary>
        public void SkipPreview() => _skipPreview = true;

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

        public void Restart() => _ = StartLevel(CurrentLevel);

        public void Abandon()
        {
            _loadToken++;
            EndPreviewVisuals();
            StopAllCoroutines();
            Session = null;
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
            if (Session != null && !Previewing && !_paused) Session.Tick(Time.deltaTime);
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _input.Enabled = !paused && !Previewing && Session != null && Session.Status == SessionStatus.Playing;
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

        void OnSwipe(Dir d) => Submit(PlayerAction.Move(d));

        void OnTap(Vector2 screenPos)
        {
            if (Session == null || Previewing) return;
            var world = _app.Camera.Cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
            var cell = MazeView.WorldToCell(world, Session.Position.Floor);
            int dx = cell.X - Session.Position.X, dy = cell.Y - Session.Position.Y;
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1) return;
            var dir = dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Up : Dir.Down;

            // Tapping a visible armed trap disarms it; tapping any other neighbour moves there.
            var t = Session.PerceivedTile(cell);
            bool disarm = t.Type == TileType.Trap && Session.IsTrapArmed(cell) && Session.IsVisible(cell);
            Submit(disarm ? PlayerAction.Disarm(dir) : PlayerAction.Move(dir));
        }

        /// <summary>Feeds one action to the game (swipe / tap handlers, and the editor autoplay tool).</summary>
        public void Submit(PlayerAction action)
        {
            if (Session == null || _paused || Previewing || Session.Status != SessionStatus.Playing) return;
            if (_busy) { _buffered = action; return; }
            StartCoroutine(Play(action));
        }

        // ------------------------------------------------------------------ step presentation

        IEnumerator Play(PlayerAction action)
        {
            var before = Session.State;
            var from = Session.Position;
            var r = Session.Apply(action);
            var fx = _app.Fx;
            if (r.Has(StepFlags.Blocked))
            {
                if (action.Kind == ActionKind.Move)
                {
                    _app.Audio.Play(Sfx.Bump);
                    fx.Bump(MazeView.CellToWorld(from), action.Dir);
                    _busy = true;
                    yield return _player.Bump(action.Dir);
                    _busy = false;
                }
                yield break;
            }

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
                yield return new WaitForSeconds(0.15f);
            }
            else
            {
                bool transport = r.Has(StepFlags.Teleported) || r.Has(StepFlags.Fell) || r.Has(StepFlags.Climbed);
                if (transport) _maze.FloorOverride = r.SteppedOn.Floor;

                var level = Session.Level;
                var first = from.Step(action.Dir);
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
                        yield return _player.Vanish(0.2f);
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
            _player.SetTorchLit(Session.TorchLit);
            _maze.RefreshSprites();
            Changed?.Invoke();

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

            var outcome = _app.Save.Apply(result);
            _ = _app.Online.SubmitScoreAsync(result);
            if (result.Won) _ = _app.PublishProgress();
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
