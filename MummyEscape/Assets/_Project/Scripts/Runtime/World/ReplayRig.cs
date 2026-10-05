using System.Collections.Generic;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>
    /// One side of a duel replay: its own copy of the tomb (far from the game's, so the two never meet), the mummy
    /// replaying that player's actions, the rival's ghost when that player could see it, and a camera drawing it all
    /// into a part of the screen. The fog is that player's own: what they had seen, what they remembered.
    /// </summary>
    public sealed class ReplayView : MonoBehaviour
    {
        const float TilesAcross = 7.5f;
        const float MinTilesHigh = 5.5f;

        MazeView _maze;
        PlayerView _player;
        GhostView _ghost;
        Camera _cam;
        Material _unlit;
        Level _level;
        IList<RunInput> _inputs;
        Loadout _look;
        Coroutine _anim;
        float _angle;
        bool _ended;

        public RunReplay Replay { get; private set; }
        public GameSession Session => Replay?.Session;
        public Camera Cam => _cam;
        /// <summary>Actions played so far (the input strip lights them up).</summary>
        public int Played { get; private set; }

        public static ReplayView Create(Transform parent, string name, Vector3 offset, GameApp app)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var view = go.AddComponent<ReplayView>();
            view.Init(app);
            return view;
        }

        void Init(GameApp app)
        {
            _maze = Child<MazeView>("Maze"); // noloc
            _maze.Init(app.Art, app.SpriteMaterial, app.Fx, app.Settings);
            _player = Child<PlayerView>("Player"); // noloc
            _player.Init(app.Art, app.SpriteMaterial, app.Settings);
            _ghost = Child<GhostView>("Ghost"); // noloc
            _ghost.Init(app.Art, app.Fx.Unlit);
            _unlit = app.Fx.Unlit;

            _cam = Child<Transform>("Camera").gameObject.AddComponent<Camera>(); // noloc
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.015f, 0.01f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 50f;
            _cam.depth = 1;
            _cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }

        /// <summary>Sets up the run to replay (<paramref name="inputs"/> null: nobody to show on this side).</summary>
        public void Load(Level level, IList<RunInput> inputs, Loadout look, Loadout rivalLook, TombTheme theme, bool spectator = false)
        {
            _maze.Spectator = spectator;
            _player.SetTheme(theme, _unlit);
            _level = level;
            _inputs = inputs;
            _look = look;
            _ghost.SetLook(rivalLook);
            Seek(0);
        }

        /// <summary>Jumps to <paramref name="ms"/>: the run is replayed from the start, without animations.</summary>
        public void Seek(int ms)
        {
            StopAllCoroutines();
            _anim = null;
            Replay = new RunReplay(_level, _inputs ?? new List<RunInput>());
            Replay.AdvanceTo(RunActions.TickOf(ms));
            Played = CountPlayed(ms);
            _maze.Build(Replay.Session);
            _player.gameObject.SetActive(_inputs != null);
            _player.ResetVisual();
            _player.SetSkin(_look);
            _player.Place(Session.Position);
            _player.SetBlind(Session.IsBlind);
            _player.SetTorchLit(Session.TorchLit);
            _ended = false;
            ShowEnd(true);
            _angle = 90f * Session.State.Rotation;
            var p = MazeView.CellToWorld(Session.Position);
            _cam.transform.localPosition = new Vector3(p.x, p.y, -10f);
            _ghost.Hide();
        }

        int CountPlayed(int ms)
        {
            if (_inputs == null) return 0;
            int tick = RunActions.TickOf(ms), n = 0;
            while (n < _inputs.Count && _inputs[n].Tick <= tick) n++;
            return n;
        }

        /// <summary>Plays the actions up to <paramref name="ms"/>, animated at <paramref name="speed"/>.</summary>
        public void PlayTo(int ms, float speed)
        {
            if (Replay == null || _inputs == null) return;
            var before = Session.Position;
            var r = Replay.AdvanceTo(RunActions.TickOf(ms));
            Played = CountPlayed(ms);
            if (r == null) return;

            _maze.RefreshSprites();
            _player.SetBlind(Session.IsBlind);
            _player.SetTorchLit(Session.TorchLit);
            var now = Session.Position;
            if (_anim != null) StopCoroutine(_anim);
            _anim = null;
            bool step = now.Floor == before.Floor && Mathf.Abs(now.X - before.X) + Mathf.Abs(now.Y - before.Y) == 1;
            if (step) _anim = StartCoroutine(_player.WalkTo(now, 0.13f / Mathf.Max(1f, speed)));
            else if (now != before)
            {
                _player.Place(now);
                var p = MazeView.CellToWorld(now);
                _cam.transform.localPosition = new Vector3(p.x, p.y, -10f);
            }
            ShowEnd(false);
        }

        /// <summary>The run's end: the mummy leaves through the exit, or falls.</summary>
        void ShowEnd(bool snap)
        {
            if (_ended || Session.Status == SessionStatus.Playing) return;
            _ended = true;
            if (Session.Status == SessionStatus.Won) StartCoroutine(_player.Vanish(snap ? 0.01f : 0.25f));
            else StartCoroutine(_player.Die());
        }

        /// <summary>The rival's ghost, where that player could have seen it.</summary>
        public void ShowRival(GameSession rival)
        {
            if (rival == null || Session == null) { _ghost.Hide(); return; }
            var c = rival.Position;
            bool gone = rival.Status == SessionStatus.Won;
            bool seen = !gone && c.Floor == Session.Position.Floor && Session.IsVisible(c);
            _ghost.Show(c, seen);
        }

        RenderTexture _target;

        /// <summary>
        /// The picture of this view, <paramref name="width"/> × <paramref name="height"/> pixels, shown by the UI. (A camera
        /// drawing straight into part of the screen comes out upside down with URP post-processing on some devices.)
        /// Zero size switches the view off.
        /// </summary>
        public RenderTexture Render(int width, int height)
        {
            _cam.enabled = width > 1 && height > 1;
            if (!_cam.enabled) return null;
            if (_target == null || _target.width != width || _target.height != height)
            {
                _cam.targetTexture = null;
                if (_target != null) { _target.Release(); Destroy(_target); }
                _target = new RenderTexture(width, height, 24) { name = name + " replay" }; // noloc
                _cam.targetTexture = _target;
            }
            return _target;
        }

        void LateUpdate()
        {
            if (Session == null) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            var target = _player.transform.localPosition;
            var pos = Vector3.Lerp(_cam.transform.localPosition, new Vector3(target.x, target.y, -10f), k);
            _cam.transform.localPosition = new Vector3(pos.x, pos.y, -10f);
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            _cam.orthographicSize = Mathf.Max(TilesAcross / aspect, MinTilesHigh) * 0.5f;

            // The turning slab turns this player's view only.
            float goal = _angle + Mathf.DeltaAngle(_angle, 90f * Session.State.Rotation);
            _angle = Mathf.Lerp(_angle, goal, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
            var turn = Quaternion.Euler(0f, 0f, _angle);
            _cam.transform.localRotation = turn;
            _player.transform.localRotation = turn;
            _ghost.transform.localRotation = turn;
        }

        public void Clear()
        {
            StopAllCoroutines();
            _maze.Clear();
            Replay = null;
            _ghost.Hide();
            _cam.enabled = false;
            _cam.targetTexture = null;
            if (_target != null) { _target.Release(); Destroy(_target); }
            _target = null;
        }
    }

    /// <summary>
    /// A duel watched again: both runs on one shared clock, each in its own view (split screen). Play, pause, x2 and
    /// a timeline; seeking replays both runs from the start (the rules are deterministic).
    /// </summary>
    public sealed class ReplayRig : MonoBehaviour
    {
        /// <summary>Far from the game's tomb (at the origin) and from each other: each camera only sees its own.</summary>
        static readonly Vector3 TopOffset = new Vector3(1000f, 0f, 0f), BottomOffset = new Vector3(2000f, 0f, 0f);
        /// <summary>The end of the replay lingers on the result.</summary>
        const int TailMs = 1500;

        ReplayView _top, _bottom;
        DuelRecord _duel;
        float _timeMs;

        public ReplayView Top => _top;
        public ReplayView Bottom => _bottom;
        public int TimeMs => (int)_timeMs;
        public int EndMs { get; private set; }
        public bool Playing { get; set; }
        public float Speed { get; set; } = 1f;
        public bool HasRival => _duel?.Rival != null;

        public static ReplayRig Create(Transform parent, GameApp app)
        {
            var go = new GameObject("Replay"); // noloc
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<ReplayRig>();
            rig._top = ReplayView.Create(go.transform, "Me", TopOffset, app); // noloc
            rig._bottom = ReplayView.Create(go.transform, "Rival", BottomOffset, app); // noloc
            go.SetActive(false);
            return rig;
        }

        /// <summary>Shows the duel from the start, paused (<paramref name="spectator"/>: traps and points of interest hidden).</summary>
        public void Open(DuelRecord duel, Level level, TombTheme theme, bool spectator = false)
        {
            _duel = duel;
            gameObject.SetActive(true);
            var me = PvpSkins.Loadout(duel.Me?.Look);
            var rival = PvpSkins.Loadout(duel.Rival?.Look);
            _top.Load(level, duel.Me?.Inputs, me, rival, theme, spectator);
            _bottom.Load(level, duel.Rival?.Inputs, rival, me, theme, spectator);
            EndMs = Mathf.Max(EndOf(duel.Me), EndOf(duel.Rival)) + TailMs;
            _timeMs = 0;
            Playing = false;
        }

        /// <summary>When a run stops: its time (exit, death, time limit), or its last action for a forfeit.</summary>
        static int EndOf(DuelRun run)
        {
            if (run == null) return 0;
            int last = run.Inputs != null && run.Inputs.Count > 0 ? RunActions.MsOf(run.Inputs[run.Inputs.Count - 1].Tick) : 0;
            return run.Outcome == RunOutcome.Abandoned ? last : Mathf.Max(last, run.TimeMs);
        }

        public void Seek(int ms)
        {
            _timeMs = Mathf.Clamp(ms, 0, EndMs);
            _top.Seek(TimeMs);
            _bottom.Seek(TimeMs);
            ShowGhosts();
        }

        public void Close()
        {
            Playing = false;
            _top.Clear();
            _bottom.Clear();
            gameObject.SetActive(false);
            _duel = null;
        }

        void Update()
        {
            if (_duel == null || !Playing) return;
            _timeMs = Mathf.Min(EndMs, _timeMs + Time.unscaledDeltaTime * 1000f * Speed);
            _top.PlayTo(TimeMs, Speed);
            _bottom.PlayTo(TimeMs, Speed);
            ShowGhosts();
            if (_timeMs >= EndMs) Playing = false;
        }

        void ShowGhosts()
        {
            _top.ShowRival(HasRival ? _bottom.Session : null);
            _bottom.ShowRival(HasRival ? _top.Session : null);
        }
    }
}
