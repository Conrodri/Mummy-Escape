using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>
    /// Draws the current floor with fog of war: unknown tiles are pitch black, tiles the player remembers are
    /// dimmed in a cold tint, tiles lit by the torch are drawn at full colour. Points of interest emit a small
    /// coloured light once discovered, and the living parts of the tomb (sconces, portals, currents, flame jets,
    /// the exit) carry looping particles while they are in sight.
    /// </summary>
    public sealed class MazeView : MonoBehaviour
    {
        static readonly Color MemoryTint = new Color(0.42f, 0.44f, 0.55f, 1f);
        const float FadeSpeed = 5f;
        /// <summary>Frames per second of the flowing water.</summary>
        const float CurrentFps = 6f;

        sealed class TileView
        {
            public Cell Cell;
            public SpriteRenderer Renderer;
            public SpriteRenderer Shadow;
            public SpriteRenderer Beam;
            public Light2D Glow;
            public ParticleSystem Loop;
            public FxRig.Loop LoopKind;
            public bool HasLoop;
            public TileLook Look;
            public float Visibility;
            public float FlickerSeed;
        }

        ArtLibrary _art;
        Material _material;
        FxRig _fx;
        SettingsService _settings;
        GameSession _session;
        readonly List<TileView[]> _floors = new List<TileView[]>();
        readonly List<GameObject> _floorRoots = new List<GameObject>();
        readonly List<TileView> _animated = new List<TileView>();
        int _shownFloor = -1;
        int _currentFrame = -1;

        /// <summary>When >= 0, forces which floor is drawn (used while a climb/fall animation plays).</summary>
        public int FloorOverride { get; set; } = -1;

        public void Init(ArtLibrary art, Material spriteMaterial, FxRig fx, SettingsService settings)
        {
            _art = art;
            _material = spriteMaterial;
            _fx = fx;
            _settings = settings;
        }

        public void Build(GameSession session)
        {
            Clear();
            _session = session;
            var level = session.Level;
            for (int f = 0; f < level.Floors; f++)
            {
                var root = new GameObject($"Floor {f}");
                root.transform.SetParent(transform, false);
                _floorRoots.Add(root);
                var tiles = new TileView[level.Width * level.Height];
                for (int y = 0; y < level.Height; y++)
                    for (int x = 0; x < level.Width; x++)
                    {
                        var cell = new Cell(f, x, y);
                        var go = new GameObject($"T{x}_{y}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.localPosition = CellToWorld(cell);
                        var sr = go.AddComponent<SpriteRenderer>();
                        if (_material != null) sr.sharedMaterial = _material;
                        sr.color = Color.clear;
                        sr.sortingOrder = 0;
                        var tv = new TileView { Cell = cell, Renderer = sr };
                        tv.Look = StaticLook(level, cell);
                        tv.FlickerSeed = Hash(x, y, f, 99) * 100f;
                        tiles[y * level.Width + x] = tv;

                        var tile = level[cell];
                        if (!tile.IsSolid)
                        {
                            var shadow = _art.Shadow(ShadowMask(level, cell));
                            if (shadow != null)
                            {
                                var sgo = new GameObject("Shadow");
                                sgo.transform.SetParent(go.transform, false);
                                tv.Shadow = sgo.AddComponent<SpriteRenderer>();
                                if (_material != null) tv.Shadow.sharedMaterial = _material;
                                tv.Shadow.sprite = shadow;
                                tv.Shadow.sortingOrder = 1;
                                tv.Shadow.color = Color.clear;
                            }
                        }
                        if (tile.Type == TileType.Current || tile.Type == TileType.WallTorch) _animated.Add(tv);
                        if (tile.Type == TileType.Exit)
                        {
                            var bgo = new GameObject("Beam");
                            bgo.transform.SetParent(go.transform, false);
                            bgo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                            tv.Beam = bgo.AddComponent<SpriteRenderer>();
                            tv.Beam.sprite = _art.Beam;
                            tv.Beam.sortingOrder = 11;
                            if (_fx != null && _fx.Unlit != null) tv.Beam.sharedMaterial = _fx.Unlit;
                            tv.Beam.color = Color.clear;
                        }
                    }
                _floors.Add(tiles);
            }
            _shownFloor = -1;
            _currentFrame = -1;
            RefreshSprites();
        }

        static float Hash(int x, int y, int f, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(f * 83492791) ^ (uint)(seed * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>The parts of a tile's look that never change during a run (variant, orientation...).</summary>
        static TileLook StaticLook(Level level, Cell c)
        {
            bool Solid(int dx, int dy) => level.Get(new Cell(c.Floor, c.X + dx, c.Y + dy)).IsSolid;
            return new TileLook
            {
                // The maze variant reshuffles details, so two runs of the same level never look identical.
                Variant = (int)(Hash(c.X, c.Y, c.Floor, level.Variant * 31 + level.Id.Act) * 255f),
                // A wall shows its carved face when the tile south of it is walkable (top-down 3/4 view).
                WallFace = !Solid(0, -1),
                // Gates are drawn for a north-south passage; turn them when the corridor runs east-west.
                Vertical = Solid(0, 1) && Solid(0, -1),
            };
        }

        static int ShadowMask(Level level, Cell c)
        {
            bool Solid(int dx, int dy) => level.Get(new Cell(c.Floor, c.X + dx, c.Y + dy)).IsSolid;
            return (Solid(0, 1) ? 1 : 0) | (Solid(1, 0) ? 2 : 0) | (Solid(0, -1) ? 4 : 0) | (Solid(-1, 0) ? 8 : 0);
        }

        /// <summary>
        /// Start-of-run preview: every floor is drawn fully lit (as the player perceives it: hidden portals stay
        /// hidden), floors stacked bottom to top so the whole tomb fits on screen. Turning it off lets the fog fall back.
        /// </summary>
        public bool Preview { get; private set; }

        /// <summary>Hides the whole map at once (screen capture detected during the preview).</summary>
        public bool Concealed { get; set; }

        /// <summary>Vertical gap between stacked floors in the preview, in tiles.</summary>
        const float PreviewFloorGap = 2f;

        public void SetPreview(bool on)
        {
            if (_session == null) return;
            Preview = on;
            float step = _session.Level.Height + PreviewFloorGap;
            for (int f = 0; f < _floorRoots.Count; f++)
            {
                _floorRoots[f].transform.localPosition = on ? new Vector3(0f, f * step, 0f) : Vector3.zero;
                _floorRoots[f].SetActive(on || f == _shownFloor);
            }
            if (!on)
            {
                // Other floors vanish at once (no fade that would leak them later); the current one sinks slowly.
                int current = _session.Position.Floor;
                foreach (var tiles in _floors)
                    foreach (var tv in tiles)
                        if (tv.Cell.Floor != current && !_session.IsExplored(tv.Cell))
                        {
                            tv.Visibility = 0f;
                            ApplyColor(tv, Color.clear);
                            if (tv.Glow != null) tv.Glow.intensity = 0f;
                            SetLoop(tv, false);
                        }
                _shownFloor = -1;
            }
            RefreshSprites();
        }

        /// <summary>World bounds of the stacked preview.</summary>
        public Bounds PreviewBounds()
        {
            var level = _session.Level;
            float step = level.Height + PreviewFloorGap;
            float h = level.Floors * step - PreviewFloorGap;
            return new Bounds(new Vector3((level.Width - 1) * 0.5f, (h - 1) * 0.5f, 0f), new Vector3(level.Width, h, 0f));
        }

        public void Clear()
        {
            foreach (var r in _floorRoots) Destroy(r);
            _floorRoots.Clear();
            _floors.Clear();
            _animated.Clear();
            Preview = false;
            Concealed = false;
            _session = null;
        }

        public static Vector3 CellToWorld(Cell c) => new Vector3(c.X, c.Y, 0f);

        public static Cell WorldToCell(Vector3 p, int floor) => new Cell(floor, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));

        /// <summary>Re-picks sprites after a state change (door opened, trap disarmed, portal revealed...).</summary>
        public void RefreshSprites()
        {
            if (_session == null) return;
            foreach (var tiles in _floors)
                foreach (var tv in tiles)
                {
                    if (!Preview && !_session.IsExplored(tv.Cell) && !_session.IsVisible(tv.Cell)) continue;
                    Refresh(tv);
                }
        }

        void Refresh(TileView tv)
        {
            var t = _session.PerceivedTile(tv.Cell);
            var l = tv.Look;
            l.Open = _session.IsDoorOpen(tv.Cell);
            l.Active = (t.Type == TileType.Door || t.Type == TileType.Button || t.Type == TileType.Teleporter || t.Type == TileType.Switch)
                       && _session.IsChannelActive(t.Channel);
            l.Armed = _session.IsTrapArmed(tv.Cell);
            l.Collapsed = t.Type == TileType.Crumbling && _session.IsCollapsed(tv.Cell);
            l.Fire = t.Type != TileType.FireJet ? 0 : _session.IsFiring(tv.Cell) ? 2 : _session.IsAboutToFire(tv.Cell) ? 1 : 0;
            l.Frame = CurrentFrame();
            tv.Look = l;
            tv.Renderer.sprite = _art.ForTile(t, l);
            UpdateGlow(tv, t);
            UpdateLoop(tv, t);
        }

        int CurrentFrame() => (int)(Time.time * CurrentFps) % ArtLibrary.CurrentFrames;

        /// <summary>Instantly hides everything the player forgot (cursed portal).</summary>
        public void ForgetAll()
        {
            foreach (var tiles in _floors)
                foreach (var tv in tiles)
                    if (!_session.IsExplored(tv.Cell)) tv.Visibility = 0f;
        }

        public Bounds ExploredBounds(int floor)
        {
            var b = new Bounds();
            bool any = false;
            if (floor < 0 || floor >= _floors.Count) return b;
            foreach (var tv in _floors[floor])
            {
                if (!_session.IsExplored(tv.Cell)) continue;
                if (!any) { b = new Bounds(CellToWorld(tv.Cell), Vector3.one); any = true; }
                else b.Encapsulate(CellToWorld(tv.Cell));
            }
            return b;
        }

        void UpdateGlow(TileView tv, Tile t)
        {
            var color = ArtLibrary.GlowColor(t, tv.Look);
            if (color == null)
            {
                if (tv.Glow != null) tv.Glow.enabled = false;
                return;
            }
            if (tv.Glow == null)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(tv.Renderer.transform, false);
                tv.Glow = go.AddComponent<Light2D>();
                tv.Glow.lightType = Light2D.LightType.Point;
                tv.Glow.pointLightInnerRadius = 0.1f;
                tv.Glow.falloffIntensity = 0.7f;
                tv.Glow.intensity = 0f;
            }
            tv.Glow.pointLightOuterRadius = t.Type == TileType.Exit ? 3.5f
                : t.Type == TileType.WallTorch ? 2.8f
                : t.Type == TileType.FireJet ? (tv.Look.Fire == 2 ? 2.6f : 1.3f)
                : t.Type == TileType.Barrier ? 1.8f
                : 1.6f;
            tv.Glow.enabled = true;
            tv.Glow.color = color.Value;
        }

        /// <summary>Picks the looping particle effect a tile carries in its current state (if any).</summary>
        void UpdateLoop(TileView tv, Tile t)
        {
            FxRig.Loop kind;
            Color color;
            var theme = _art.Theme;
            switch (t.Type)
            {
                case TileType.WallTorch: kind = FxRig.Loop.TorchEmbers; color = theme.SconceLight; break;
                case TileType.Exit: kind = FxRig.Loop.ExitMotes; color = new Color(1f, 0.85f, 0.45f); break;
                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Locked && !tv.Look.Active) { SetLoop(tv, false); return; }
                    kind = FxRig.Loop.PortalSwirl;
                    color = t.Teleporter == TeleporterKind.Cursed ? new Color(0.6f, 1f, 0.3f) : new Color(0.4f, 0.95f, 1f);
                    break;
                case TileType.Trap when t.Trap == TrapKind.Darkness && tv.Look.Armed:
                    kind = FxRig.Loop.DarkWisps; color = new Color(0.4f, 0.15f, 0.65f, 0.7f); break;
                case TileType.Current: kind = FxRig.Loop.CurrentFoam; color = new Color(0.85f, 0.97f, 1f, 0.85f); break;
                case TileType.FireJet: kind = FxRig.Loop.FireSmoke; color = new Color(0.3f, 0.26f, 0.24f, 0.55f); break;
                case TileType.Barrier when !tv.Look.Open:
                    kind = FxRig.Loop.BarrierSparks;
                    color = t.Param == 0 ? new Color(1f, 0.4f, 0.4f) : new Color(0.5f, 0.7f, 1f);
                    break;
                default: SetLoop(tv, false); tv.HasLoop = false; return;
            }
            if (_fx == null) return;
            if (tv.Loop != null && tv.LoopKind != kind) { Destroy(tv.Loop.gameObject); tv.Loop = null; }
            if (tv.Loop == null)
            {
                tv.Loop = _fx.CreateLoop(kind, tv.Renderer.transform, color, (Dir)t.Param);
                tv.LoopKind = kind;
                SetLoop(tv, false);
            }
            else
            {
                var main = tv.Loop.main;
                main.startColor = color;
            }
            tv.HasLoop = true;
        }

        static void SetLoop(TileView tv, bool on)
        {
            if (tv.Loop == null) return;
            var em = tv.Loop.emission;
            if (em.enabled == on) return;
            em.enabled = on;
            if (!on) tv.Loop.Clear();
        }

        void Update()
        {
            if (_session == null) return;
            float dt = Time.deltaTime;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f);

            // Flowing water: swap frames on every current in sight.
            int frame = CurrentFrame();
            if (frame != _currentFrame)
            {
                _currentFrame = frame;
                foreach (var tv in _animated)
                    if (tv.Visibility > 0f && _session.Level[tv.Cell].Type == TileType.Current)
                    {
                        var l = tv.Look; l.Frame = frame; tv.Look = l;
                        tv.Renderer.sprite = _art.ForTile(_session.PerceivedTile(tv.Cell), l);
                    }
            }

            if (Preview)
            {
                foreach (var tiles in _floors)
                    foreach (var tv in tiles) Paint(tv, true, dt, pulse);
                return;
            }

            int f = FloorOverride >= 0 ? FloorOverride : _session.Position.Floor;
            if (f != _shownFloor)
            {
                for (int i = 0; i < _floorRoots.Count; i++) _floorRoots[i].SetActive(i == f);
                _shownFloor = f;
            }
            foreach (var tv in _floors[f]) Paint(tv, false, dt, pulse);
        }

        void Paint(TileView tv, bool preview, float dt, float pulse)
        {
            bool visible = !Concealed && (preview || _session.IsVisible(tv.Cell));
            float target = Concealed ? 0f : visible ? 1f : _session.IsExplored(tv.Cell) ? 0.5f : 0f;
            bool steady = Mathf.Approximately(tv.Visibility, target);
            if (steady && tv.Glow == null && tv.Beam == null && !tv.HasLoop) return;
            // The preview fades out slowly so the player sees the tomb sink back into darkness.
            float speed = Concealed ? 1000f : target < tv.Visibility && target == 0f ? FadeSpeed * 0.4f : FadeSpeed;
            tv.Visibility = Mathf.MoveTowards(tv.Visibility, target, dt * speed);

            // 0 = black, 0.5 = remembered (cold, dim), 1 = lit by the torch.
            float v = tv.Visibility;
            if (!steady)
            {
                Color c = v <= 0.5f
                    ? new Color(MemoryTint.r, MemoryTint.g, MemoryTint.b, v * 2f)
                    : Color.Lerp(MemoryTint, Color.white, (v - 0.5f) * 2f);
                ApplyColor(tv, c);
            }

            bool rich = _settings == null || _settings.AdvancedLighting;
            if (tv.Glow != null && tv.Glow.enabled)
            {
                float k = pulse;
                if (_session.Level[tv.Cell].Type == TileType.WallTorch)
                    k = 0.8f + 0.35f * Mathf.PerlinNoise(tv.FlickerSeed, Time.time * 5f); // live fire flickers
                tv.Glow.intensity = Mathf.Clamp01(v * 2f) * (visible ? 1.1f : 0.55f) * k;
            }
            if (tv.Beam != null)
            {
                float a = Mathf.Clamp01(v * 2f - 0.6f) * (0.28f + 0.08f * Mathf.Sin(Time.time * 1.7f));
                tv.Beam.color = new Color(1f, 0.85f, 0.5f, rich ? a : 0f);
            }
            // Particles only while the tile is truly in sight: unlit sparks would shine through the fog otherwise.
            if (tv.HasLoop) SetLoop(tv, rich && visible && v > 0.75f);
        }

        static void ApplyColor(TileView tv, Color c)
        {
            tv.Renderer.color = c;
            if (tv.Shadow != null) tv.Shadow.color = c;
        }
    }
}
