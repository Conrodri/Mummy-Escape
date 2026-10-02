using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>
    /// Draws the current floor with fog of war: unknown tiles are pitch black, tiles the player remembers are
    /// dimmed in a cold tint, tiles lit by the torch are drawn at full colour. Points of interest emit a small
    /// coloured light once discovered.
    /// </summary>
    public sealed class MazeView : MonoBehaviour
    {
        static readonly Color MemoryTint = new Color(0.42f, 0.44f, 0.55f, 1f);
        const float FadeSpeed = 5f;

        sealed class TileView
        {
            public Cell Cell;
            public SpriteRenderer Renderer;
            public Light2D Glow;
            public int Variant;
            public float Visibility;
        }

        ArtLibrary _art;
        Material _material;
        GameSession _session;
        readonly List<TileView[]> _floors = new List<TileView[]>();
        readonly List<GameObject> _floorRoots = new List<GameObject>();
        int _shownFloor = -1;

        /// <summary>When >= 0, forces which floor is drawn (used while a climb/fall animation plays).</summary>
        public int FloorOverride { get; set; } = -1;

        public void Init(ArtLibrary art, Material spriteMaterial)
        {
            _art = art;
            _material = spriteMaterial;
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
                        tiles[y * level.Width + x] = new TileView { Cell = cell, Renderer = sr, Variant = (x * 7 + y * 13 + f * 3) & 0xFF };
                    }
                _floors.Add(tiles);
            }
            _shownFloor = -1;
            RefreshSprites();
        }

        public void Clear()
        {
            foreach (var r in _floorRoots) Destroy(r);
            _floorRoots.Clear();
            _floors.Clear();
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
                    if (!_session.IsExplored(tv.Cell) && !_session.IsVisible(tv.Cell)) continue;
                    var t = _session.PerceivedTile(tv.Cell);
                    bool active = (t.Type == TileType.Door || t.Type == TileType.Button || t.Type == TileType.Teleporter) && _session.IsChannelActive(t.Channel);
                    tv.Renderer.sprite = _art.ForTile(t, tv.Variant, _session.IsDoorOpen(tv.Cell), active, _session.IsTrapArmed(tv.Cell));
                    UpdateGlow(tv, t, active);
                }
        }

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

        void UpdateGlow(TileView tv, Tile t, bool active)
        {
            var color = ArtLibrary.GlowColor(t, active);
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
                tv.Glow.pointLightOuterRadius = t.Type == TileType.Exit ? 3.5f : 1.6f;
                tv.Glow.falloffIntensity = 0.7f;
            }
            tv.Glow.enabled = true;
            tv.Glow.color = color.Value;
            tv.Glow.intensity = 0f;
        }

        void Update()
        {
            if (_session == null) return;
            int f = FloorOverride >= 0 ? FloorOverride : _session.Position.Floor;
            if (f != _shownFloor)
            {
                for (int i = 0; i < _floorRoots.Count; i++) _floorRoots[i].SetActive(i == f);
                _shownFloor = f;
            }

            float dt = Time.deltaTime;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f);
            foreach (var tv in _floors[f])
            {
                bool visible = _session.IsVisible(tv.Cell);
                float target = visible ? 1f : _session.IsExplored(tv.Cell) ? 0.5f : 0f;
                if (Mathf.Approximately(tv.Visibility, target) && tv.Glow == null) continue;
                tv.Visibility = Mathf.MoveTowards(tv.Visibility, target, dt * FadeSpeed);

                // 0 = black, 0.5 = remembered (cold, dim), 1 = lit by the torch.
                float v = tv.Visibility;
                Color c = v <= 0.5f
                    ? new Color(MemoryTint.r, MemoryTint.g, MemoryTint.b, v * 2f)
                    : Color.Lerp(MemoryTint, Color.white, (v - 0.5f) * 2f);
                tv.Renderer.color = c;

                if (tv.Glow != null && tv.Glow.enabled)
                    tv.Glow.intensity = Mathf.Clamp01(v * 2f) * (visible ? 1.1f : 0.55f) * pulse;
            }
        }
    }
}
