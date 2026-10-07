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
    /// What a replay view plays: one run, or a duo's relay (whose maze on screen follows whoever is running). Built
    /// afresh from the start on every seek: the rules are deterministic.
    /// </summary>
    public interface IReplayTrack
    {
        /// <summary>The maze on screen.</summary>
        GameSession Session { get; }
        /// <summary>The mummy running it.</summary>
        Loadout Look { get; }
        /// <summary>Plays every action up to <paramref name="tick"/>; the last step played, if any.</summary>
        StepResult? AdvanceTo(int tick);
        /// <summary>Actions played by <paramref name="tick"/>.</summary>
        int CountTo(int tick);
    }

    /// <summary>A duel run.</summary>
    public sealed class RunTrack : IReplayTrack
    {
        readonly RunReplay _replay;
        readonly IList<RunInput> _inputs;

        public RunTrack(Level level, IList<RunInput> inputs, Loadout look)
        {
            _inputs = inputs ?? new List<RunInput>();
            _replay = new RunReplay(level, _inputs);
            Look = look;
        }

        public GameSession Session => _replay.Session;
        public Loadout Look { get; }
        public StepResult? AdvanceTo(int tick) => _replay.AdvanceTo(tick);

        public int CountTo(int tick)
        {
            int n = 0;
            while (n < _inputs.Count && _inputs[n].Tick <= tick) n++;
            return n;
        }
    }

    /// <summary>A duo's relay: both mazes on one clock, the one on screen being the runner's of the moment.</summary>
    public sealed class RelayTrack : IReplayTrack
    {
        readonly RelayRace _race;
        readonly IList<RelayInput> _inputs;
        readonly Loadout[] _looks;
        int _next;

        /// <param name="looks">The runner of each maze.</param>
        public RelayTrack(RelayMap map, IList<RelayInput> inputs, Loadout[] looks)
        {
            _race = new RelayRace(map);
            _inputs = inputs ?? new List<RelayInput>();
            _looks = looks;
        }

        public RelayRace Race => _race;
        /// <summary>The maze running, or the one where the relay ended.</summary>
        public int Maze => _race.Status == RelayStatus.Finished ? RelayMap.MazeOf(_race.Segment - 1) : _race.ActiveMaze;
        public GameSession Session => _race.Session(Maze);
        public Loadout Look => _looks[Maze];

        public StepResult? AdvanceTo(int tick)
        {
            StepResult? last = null;
            while (_next < _inputs.Count && _inputs[_next].Tick <= tick && !_race.IsOver && !_race.Invalid)
            {
                var input = _inputs[_next++];
                if (!RunActions.TryDecode(input.Direction, out var action)) break;
                var r = _race.Apply(input.Maze, action, input.Tick);
                if (r.HasValue) last = r;
            }
            return last;
        }

        public int CountTo(int tick)
        {
            int n = 0;
            while (n < _inputs.Count && _inputs[n].Tick <= tick) n++;
            return n;
        }
    }

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
        System.Func<IReplayTrack> _make;
        bool _hasRunner;
        Coroutine _anim;
        float _angle;
        bool _ended;
        bool _map;

        public IReplayTrack Track { get; private set; }
        public GameSession Session => Track?.Session;
        public Camera Cam => _cam;
        /// <summary>Actions played so far (the input strip lights them up).</summary>
        public int Played { get; private set; }
        /// <summary>The whole floor shown, fog lifted and the camera pulled back (the replay's map).</summary>
        public bool MapOpen => _map;

        public void ShowMap(bool on)
        {
            _map = on;
            if (Session != null) _maze.SetPreview(on, Session.Position.Floor);
        }

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
        public void Load(Level level, IList<RunInput> inputs, Loadout look, Loadout rivalLook, TombTheme theme, bool spectator = false) =>
            Load(() => new RunTrack(level, inputs, look), inputs != null, rivalLook, theme, spectator);

        /// <summary>Sets up any track (<paramref name="make"/> builds it from the start; <paramref name="hasRunner"/> false: nobody to show).</summary>
        public void Load(System.Func<IReplayTrack> make, bool hasRunner, Loadout rivalLook, TombTheme theme, bool spectator = false)
        {
            _maze.Spectator = spectator;
            _player.SetTheme(theme, _unlit);
            _make = make;
            _hasRunner = hasRunner;
            _ghost.SetLook(rivalLook);
            Seek(0);
        }

        /// <summary>Jumps to <paramref name="ms"/>: the run is replayed from the start, without animations.</summary>
        public void Seek(int ms)
        {
            StopAllCoroutines();
            _anim = null;
            Track = _make();
            Track.AdvanceTo(RunActions.TickOf(ms));
            Played = _hasRunner ? Track.CountTo(RunActions.TickOf(ms)) : 0;
            ShowMaze();
        }

        /// <summary>The maze on screen built afresh, its runner where he stands, the camera on him.</summary>
        void ShowMaze()
        {
            _maze.Build(Session);
            if (_map) _maze.SetPreview(true, Session.Position.Floor);
            _player.gameObject.SetActive(_hasRunner);
            _player.ResetVisual();
            _player.SetSkin(Track.Look);
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

        /// <summary>Plays the actions up to <paramref name="ms"/>, animated at <paramref name="speed"/>.</summary>
        public void PlayTo(int ms, float speed)
        {
            if (Track == null || !_hasRunner) return;
            var shown = Session;
            var before = Session.Position;
            var r = Track.AdvanceTo(RunActions.TickOf(ms));
            Played = Track.CountTo(RunActions.TickOf(ms));
            if (r == null) return;
            if (Session != shown)
            {
                // A relay plate pressed: over to the teammate's maze.
                StopAllCoroutines();
                _anim = null;
                ShowMaze();
                return;
            }

            _maze.RefreshSprites();
            _player.SetBlind(Session.IsBlind);
            _player.SetTorchLit(Session.TorchLit);
            var now = Session.Position;
            if (_map && _maze.PreviewFloor != now.Floor) _maze.SetPreview(true, now.Floor); // the map follows the runner up or down
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
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            float size = Mathf.Max(TilesAcross / aspect, MinTilesHigh) * 0.5f;
            if (_map)
            {
                // The whole floor in the view (its sides swapped while the turning slab has the view on its side).
                var b = _maze.PreviewBounds();
                bool side = (Session.State.Rotation & 1) == 1;
                float w = side ? b.size.y : b.size.x, h = side ? b.size.x : b.size.y;
                target = b.center;
                size = Mathf.Max(h * 0.5f, w * 0.5f / aspect) + 0.4f;
            }
            var pos = Vector3.Lerp(_cam.transform.localPosition, new Vector3(target.x, target.y, -10f), k);
            _cam.transform.localPosition = new Vector3(pos.x, pos.y, -10f);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, size, k);

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
            _map = false;
            _maze.Clear();
            Track = null;
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
        bool _relay;
        float _timeMs;

        public ReplayView Top => _top;
        public ReplayView Bottom => _bottom;
        public int TimeMs => (int)_timeMs;
        public int EndMs { get; private set; }
        public bool Playing { get; set; }
        public float Speed { get; set; } = 1f;
        public bool HasRival => _duel?.Rival != null;
        /// <summary>Both views show their whole floor (the map), or their own fog.</summary>
        public bool Map
        {
            get => _top.MapOpen;
            set { _top.ShowMap(value); _bottom.ShowMap(value); }
        }

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
            _relay = false;
            gameObject.SetActive(true);
            var me = PvpSkins.Loadout(duel.Me?.Look);
            var rival = PvpSkins.Loadout(duel.Rival?.Look);
            _top.Load(level, duel.Me?.Inputs, me, rival, theme, spectator);
            _bottom.Load(level, duel.Rival?.Inputs, rival, me, theme, spectator);
            // A live duel stops at its first exit, death or forfeit: the rest of the other run (a bot's is computed whole)
            // never happened. A ghost duel shows both runs to their end.
            int mine = EndOf(duel.Me, duel.Live), theirs = EndOf(duel.Rival, duel.Live);
            EndMs = (duel.Live && mine > 0 && theirs > 0 ? Mathf.Min(mine, theirs) : Mathf.Max(mine, theirs)) + TailMs;
            _timeMs = 0;
            Playing = false;
        }

        /// <summary>
        /// Shows a 2v2 match from the start, paused: one duo's relay on top, the other's below, each view following the
        /// duo's runner of the moment. No ghosts: the duos run separate copies of the mazes.
        /// </summary>
        public void OpenRelay(RelayMap map, RelaySide top, RelaySide bottom, TombTheme theme)
        {
            _duel = null;
            _relay = true;
            gameObject.SetActive(true);
            var nobody = PvpSkins.Loadout(null);
            _top.Load(() => new RelayTrack(map, top?.Inputs, Looks(top)), top?.Inputs != null, nobody, theme);
            _bottom.Load(() => new RelayTrack(map, bottom?.Inputs, Looks(bottom)), bottom?.Inputs != null, nobody, theme);
            EndMs = Mathf.Max(EndOf(top), EndOf(bottom)) + TailMs;
            _timeMs = 0;
            Playing = false;
        }

        static Loadout[] Looks(RelaySide side)
        {
            var looks = new Loadout[RelayConfig.Mazes];
            for (int m = 0; m < looks.Length; m++) looks[m] = PvpSkins.Loadout(side?.RunnerOf(m)?.Look);
            return looks;
        }

        /// <summary>When a relay stops: its last action (the arrival, the fatal step), or its verified time.</summary>
        static int EndOf(RelaySide side)
        {
            if (side?.Inputs == null) return 0;
            int last = side.Inputs.Count > 0 ? RunActions.MsOf(side.Inputs[side.Inputs.Count - 1].Tick) : 0;
            return Mathf.Max(last, side.Verified?.TimeMs ?? 0);
        }

        /// <summary>When a run stops: its time (exit, death, time limit), or its last action for a ghost duel forfeit.</summary>
        static int EndOf(DuelRun run, bool live)
        {
            if (run == null) return 0;
            int last = run.Inputs != null && run.Inputs.Count > 0 ? RunActions.MsOf(run.Inputs[run.Inputs.Count - 1].Tick) : 0;
            return run.Outcome == RunOutcome.Abandoned && !(live && run.TimeMs > 0) ? last : Mathf.Max(last, run.TimeMs);
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
            _relay = false;
        }

        void Update()
        {
            if (_duel == null && !_relay || !Playing) return;
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
