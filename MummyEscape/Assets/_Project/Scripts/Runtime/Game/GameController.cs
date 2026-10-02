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
        readonly Dictionary<LevelId, Level> _cache = new Dictionary<LevelId, Level>();

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
            _player.gameObject.SetActive(false);
        }

        public async Task StartLevel(LevelId id)
        {
            int token = ++_loadToken;
            StopAllCoroutines();
            _busy = false;
            _buffered = null;
            _input.Enabled = false;
            CurrentLevel = id;
            LevelLoading?.Invoke(id);

            if (!_cache.TryGetValue(id, out var level))
            {
                try
                {
                    level = await Task.Run(() => LevelGenerator.Generate(id));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Game] Could not generate level {id}: {e}");
                    return;
                }
                _cache[id] = level;
            }
            if (token != _loadToken || this == null) return; // another level was requested meanwhile

            Session = new GameSession(level);
            _maze.Build(Session);
            _player.gameObject.SetActive(true);
            _player.ResetVisual();
            _player.SetSkin(SkinCatalog.Get(_app.Save.Data.SelectedSkin));
            _player.Place(level.Start);
            _player.SetBlind(false);
            _app.Camera.SnapTo(MazeView.CellToWorld(level.Start));
            _app.Lighting.SetMood(true);
            _paused = false;
            _input.Enabled = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            LevelStarted?.Invoke();
            Changed?.Invoke();
        }

        public void Restart() => _ = StartLevel(CurrentLevel);

        public void Abandon()
        {
            _loadToken++;
            StopAllCoroutines();
            Session = null;
            _busy = false;
            _input.Enabled = false;
            _maze.Clear();
            _player.gameObject.SetActive(false);
            _app.Lighting.SetMood(false);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _input.Enabled = !paused && Session != null && Session.Status == SessionStatus.Playing;
        }

        public void SetMapView(bool on)
        {
            if (Session == null) return;
            int floor = Session.Position.Floor;
            var b = _maze.ExploredBounds(floor);
            _app.Camera.SetMapView(on, b.center, Mathf.Max(b.extents.x, b.extents.y) + 1f);
            if (!on) _app.Camera.Follow(MazeView.CellToWorld(Session.Position));
        }

        // ------------------------------------------------------------------ input

        void OnSwipe(Dir d) => Submit(PlayerAction.Move(d));

        void OnTap(Vector2 screenPos)
        {
            if (Session == null) return;
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

        void Submit(PlayerAction action)
        {
            if (Session == null || _paused || Session.Status != SessionStatus.Playing) return;
            if (_busy) { _buffered = action; return; }
            StartCoroutine(Play(action));
        }

        // ------------------------------------------------------------------ step presentation

        IEnumerator Play(PlayerAction action)
        {
            var r = Session.Apply(action);
            if (r.Has(StepFlags.Blocked))
            {
                if (action.Kind == ActionKind.Move)
                {
                    _app.Audio.Play(Sfx.Bump);
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
                Haptic();
                _maze.RefreshSprites();
                yield return new WaitForSeconds(0.15f);
            }
            else
            {
                bool transport = r.Has(StepFlags.Teleported) || r.Has(StepFlags.Fell) || r.Has(StepFlags.Climbed);
                if (transport) _maze.FloorOverride = r.SteppedOn.Floor;

                audio.Play(Sfx.Step, 0.12f);
                cam.Follow(MazeView.CellToWorld(r.SteppedOn));
                yield return _player.WalkTo(r.SteppedOn);

                if (r.Has(StepFlags.ButtonPressed))
                {
                    audio.Play(Sfx.Button);
                    audio.Play(Sfx.Door);
                    cam.Shake(0.3f);
                    Haptic();
                }
                if (r.Has(StepFlags.Damaged))
                {
                    audio.Play(Sfx.Spikes);
                    cam.Shake(0.7f);
                    _app.Lighting.Flash(new Color(0.9f, 0.1f, 0.05f));
                    Haptic();
                    _maze.RefreshSprites();
                    yield return _player.Hurt();
                }
                if (r.Has(StepFlags.Blinded))
                {
                    audio.Play(Sfx.Darkness);
                    _app.Lighting.Flash(new Color(0.4f, 0.1f, 0.6f));
                }
                if (r.Has(StepFlags.PortalSealed)) audio.Play(Sfx.Bump);

                if (transport)
                {
                    if (r.Has(StepFlags.Teleported))
                    {
                        audio.Play(r.Has(StepFlags.FogReset) ? Sfx.Curse : Sfx.Teleport);
                        yield return _player.Vanish();
                    }
                    else if (r.Has(StepFlags.Fell))
                    {
                        audio.Play(Sfx.Fall);
                        cam.Shake(0.5f);
                        yield return _player.FallThrough();
                    }
                    else
                    {
                        audio.Play(Sfx.Climb);
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
                    cam.SnapTo(MazeView.CellToWorld(Session.Position));
                    yield return _player.Appear();
                }
            }

            _player.SetBlind(Session.IsBlind);
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

        IEnumerator Finish()
        {
            _input.Enabled = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            var result = Session.BuildResult();
            if (result.Won)
            {
                _app.Audio.Play(Sfx.Win);
                _app.Lighting.Flash(new Color(1f, 0.8f, 0.3f), 0.8f);
                yield return _player.Vanish(0.5f);
            }
            else
            {
                _app.Audio.Play(Sfx.Death);
                _app.Camera.Shake(0.8f);
                yield return _player.Die();
            }
            yield return new WaitForSeconds(0.35f);

            var outcome = _app.Save.Apply(result);
            _ = _app.Online.SubmitScoreAsync(result);
            if (result.Won) _ = _app.Online.PublishProgressAsync(_app.BuildProgressSnapshot());
            _app.UI.Open<RecapScreen>().Show(result, outcome);
        }

        void Haptic()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (_app.Settings.Haptics) Handheld.Vibrate();
#endif
        }
    }
}
