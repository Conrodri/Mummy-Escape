using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>Everything that decides how one tile looks, besides its type.</summary>
    public struct TileLook
    {
        /// <summary>Stable per-tile random number (0-255): picks slab variants and rare decorations.</summary>
        public int Variant;
        /// <summary>Wall whose front face is seen (walkable tile below it); otherwise only its top is.</summary>
        public bool WallFace;
        /// <summary>Corridor runs left-right: barriers and doors span it vertically.</summary>
        public bool Vertical;
        public bool Open, Active, Armed, Collapsed;
        /// <summary>Flame jet: 0 idle, 1 about to fire, 2 firing.</summary>
        public int Fire;
        /// <summary>Animation frame (currents).</summary>
        public int Frame;
    }

    /// <summary>
    /// Procedurally painted pixel art in an Egyptian palette, re-themed per act (sandstone, flooded, ruins, high-tech,
    /// inferno). Every sprite is 32 px = 1 world unit. Replace by real art later by swapping what these getters return.
    /// </summary>
    public sealed class ArtLibrary
    {
        public const int Ppu = 32;
        public const int CurrentFrames = 4;

        // Fixed palette (UI, gold, lapis, metal).
        static readonly Color32 Gold = new Color32(232, 195, 90, 255);
        static readonly Color32 GoldDark = new Color32(160, 120, 40, 255);
        static readonly Color32 Lapis = new Color32(38, 64, 140, 255);
        static readonly Color32 LapisDark = new Color32(22, 36, 82, 255);
        static readonly Color32 Turquoise = new Color32(64, 224, 208, 255);
        static readonly Color32 Metal = new Color32(176, 180, 186, 255);
        static readonly Color32 MetalDark = new Color32(90, 92, 98, 255);
        static readonly Color32 Black = new Color32(10, 8, 6, 255);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        // Themed palette: set by SetTheme (painters are static and read these).
        static TombTheme T = TombTheme.ForAct(1);
        static Color32 Sand => T.Floor;
        static Color32 SandDark => T.FloorDark;
        static Color32 SandLight => T.FloorLight;
        static Color32 Stone => T.Wall;
        static Color32 StoneDark => T.WallDark;
        static Color32 StoneLight => T.WallLight;
        static Color32 Accent => T.Accent;
        static TombTheme.Style S => T.Kind;

        readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        public Sprite White { get; }
        public Sprite Glow { get; }
        public Sprite Panel { get; }
        public Sprite ButtonSprite { get; }
        public Sprite Ankh { get; }
        public Sprite AnkhEmpty { get; }
        public Sprite Star { get; }
        public Sprite StarEmpty { get; }
        public Sprite Scarab { get; }
        public Sprite Lock { get; }

        // Particle shapes.
        public Sprite Spark { get; }
        public Sprite Pixel { get; }
        public Sprite Puff { get; }
        public Sprite Shard { get; }
        public Sprite Beam { get; }

        public TombTheme Theme => T;

        public ArtLibrary()
        {
            var white = new Px(4, 4); white.Fill(new Color32(255, 255, 255, 255));
            White = ToSprite(white);
            Glow = ToSprite(PaintGlow(64), 64);
            Panel = ToSprite(PaintPanel(new Color32(30, 26, 22, 255), Gold), Ppu, new Vector4(10, 10, 10, 10));
            ButtonSprite = ToSprite(PaintPanel(Lapis, Gold), Ppu, new Vector4(10, 10, 10, 10));
            Ankh = ToSprite(PaintAnkh(Gold, GoldDark), 16);
            AnkhEmpty = ToSprite(PaintAnkh(new Color32(70, 64, 58, 255), new Color32(40, 36, 32, 255)), 16);
            Star = ToSprite(PaintStar(Gold), 16);
            StarEmpty = ToSprite(PaintStar(new Color32(70, 64, 58, 255)), 16);
            Scarab = ToSprite(PaintScarabIcon(), 16);
            Lock = ToSprite(PaintLock(), 16);

            Spark = ToSprite(PaintGlow(64), 64);
            var px = new Px(2, 2); px.Fill(new Color32(255, 255, 255, 255));
            Pixel = ToSprite(px, 2);
            Puff = ToSprite(PaintPuff(), 16, default, smooth: true);
            Shard = ToSprite(PaintShard(), 4);
            Beam = ToSprite(PaintBeam(), 32, default, smooth: true);
        }

        /// <summary>Re-themes every tile sprite (cached per act).</summary>
        public void SetTheme(TombTheme theme) => T = theme ?? TombTheme.ForAct(1);

        // ------------------------------------------------------------------ public getters

        /// <summary>Sprite for a tile as the player perceives it.</summary>
        public Sprite ForTile(Tile t, TileLook l)
        {
            int v = l.Variant & 0xFF;
            switch (t.Type)
            {
                case TileType.Wall:
                    if (!l.WallFace) return Tiled($"wtop{v % 3}", () => PaintWallTop(v % 3));
                    int wd = WallDecor(v);
                    return wd >= 0 ? Tiled($"wdec{wd}_{v % 2}", () => PaintWallFace(v % 2).With(p => DecorateWall(p, wd)))
                                   : Tiled($"wall{v % 4}", () => PaintWallFace(v % 4));
                case TileType.Floor: return FloorSprite(v);
                case TileType.Exit: return Tiled("exit", PaintExit);
                case TileType.Door:
                    return l.Open ? Tiled($"door_open{(l.Vertical ? "v" : "")}", () => Turn(PaintDoor(true), l.Vertical))
                                  : Tiled($"door{(l.Vertical ? "v" : "")}", () => Turn(PaintDoor(false), l.Vertical));
                case TileType.Button: return Tiled(l.Active ? "button_on" : "button_off", () => PaintButton(l.Active));
                case TileType.Trap:
                    if (t.Trap == TrapKind.Spikes) return Tiled(l.Armed ? "spikes" : "spikes_off", () => PaintSpikes(l.Armed));
                    if (t.Trap == TrapKind.Reverse) return Tiled(l.Armed ? "mirror" : "mirror_off", () => PaintMirror(l.Armed));
                    if (t.Trap == TrapKind.Rotate)
                        return l.Armed ? Tiled(t.Param == 1 ? "turn_cw" : "turn_ccw", () => PaintTurning(true, t.Param == 1)) : Tiled("turn_off", () => PaintTurning(false, false));
                    return Tiled(l.Armed ? "dark" : "dark_off", () => PaintDarkness(l.Armed));
                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Locked && !l.Active) return Tiled("tp_locked", () => PaintPortal(new Color32(110, 110, 120, 255), true));
                    if (t.Teleporter == TeleporterKind.Cursed) return Tiled("tp_cursed", () => PaintPortal(new Color32(150, 230, 80, 255), false));
                    return Tiled("tp", () => PaintPortal(S == TombTheme.Style.Tech ? Accent : Turquoise, false));
                case TileType.BreakableFloor: return Tiled("breakable", PaintBreakable);
                case TileType.LadderUp: return Tiled("ladder_up", () => PaintLadder(true));
                case TileType.LadderDown: return Tiled("ladder_down", () => PaintLadder(false));
                case TileType.Dust: return Tiled("dust", PaintDust);
                case TileType.WallTorch: return Tiled("wall_torch", PaintWallTorch);
                case TileType.Current:
                    int f = ((l.Frame % CurrentFrames) + CurrentFrames) % CurrentFrames;
                    return Tiled($"current{t.Param}_{f}", () => PaintCurrent((Dir)t.Param, f));
                case TileType.Crumbling: return Tiled(l.Collapsed ? "rubble" : "fragile", () => PaintCrumbling(l.Collapsed));
                case TileType.Barrier:
                    string bk = $"barrier{t.Param}{(l.Open ? "o" : "c")}{(l.Vertical ? "v" : "")}";
                    return Tiled(bk, () => Turn(PaintBarrier(t.Param == 0, l.Open), l.Vertical));
                case TileType.Switch: return Tiled(l.Active ? "switch_on" : "switch_off", () => PaintSwitch(l.Active));
                case TileType.FireJet: return Tiled($"fire{l.Fire}", () => PaintFireJet(l.Fire));
            }
            return FloorSprite(v);
        }

        /// <summary>Soft contact shadow cast on a floor tile by its solid neighbours (bit 1 N, 2 E, 4 S, 8 W).</summary>
        public Sprite Shadow(int mask) => mask == 0 ? null : Cached($"shadow{mask}", () => PaintShadow(mask));

        Sprite FloorSprite(int v)
        {
            int fd = FloorDecor(v);
            return fd >= 0 ? Tiled($"fdec{fd}_{v % 2}", () => PaintFloor(v % 2).With(p => DecorateFloor(p, fd, v)))
                           : Tiled($"floor{v % 4}", () => PaintFloor(v % 4));
        }

        /// <summary>About one floor slab in nine carries a detail (bones, shards, moss, cables, lava...).</summary>
        static int FloorDecor(int v) => (v * 37 + 11) % 9 == 0 ? (v / 9) % 4 : -1;
        /// <summary>About one wall face in seven carries a detail (niche, cartouche, crack, panel...).</summary>
        static int WallDecor(int v) => (v * 53 + 5) % 7 == 0 ? (v / 7) % 3 : -1;

        /// <summary>
        /// In-game mummy in its outfit, holding the torch handle; the flame is a separate sprite so it can go out.
        /// The canvas is 32x40 (room for hats) with the pivot 16 px up, so the body sits where a 32x32 tile sprite would.
        /// </summary>
        public Sprite Mummy(Loadout look, int frame = 0)
        {
            frame = look.Animated ? frame % LegendaryFrames : 0;
            return CachedMummy("mummy_" + look.Key + (frame > 0 ? "_f" + frame : ""), () => PaintMummy(look, frame));
        }

        /// <summary>Mummy with its torch lit, for menus, shop and icon.</summary>
        public Sprite MummyPortrait(Loadout look, int frame = 0)
        {
            frame = look.Animated ? frame % LegendaryFrames : 0;
            return CachedMummy("portrait_" + look.Key + (frame > 0 ? "_f" + frame : ""),
                               () => PaintMummy(look, frame).Blit(PaintFlame(look.Light, 0), 0, 0));
        }

        /// <summary>Frames in the loop of a legendary colour (<see cref="MummyAnimator"/> plays them).</summary>
        public const int LegendaryFrames = 8;

        public const int TorchFlameFrames = 3;
        public Sprite TorchFlame(Color tint, int frame) =>
            Cached($"flame_{ColorUtility.ToHtmlStringRGB(tint)}_{frame}", () => PaintFlame(tint, frame));

        /// <summary>Glowing ember left in the cup when the torch is out.</summary>
        public Sprite TorchEmber() => Cached("ember", PaintEmber);

        /// <summary>Flame centre relative to the mummy sprite centre, in world units (mirror x when the sprite is flipped).</summary>
        public static readonly Vector2 TorchFlameOffset = new Vector2(9.5f / Ppu, 9f / Ppu);

        /// <summary>Colour of the small light a point of interest emits once discovered.</summary>
        public static Color? GlowColor(Tile t, TileLook l)
        {
            switch (t.Type)
            {
                case TileType.Exit: return new Color(1f, 0.85f, 0.45f);
                case TileType.WallTorch: return T.SconceLight;
                case TileType.Button: return l.Active ? new Color(0.3f, 1f, 0.9f) : new Color(0.35f, 0.5f, 1f);
                case TileType.Door: return l.Open ? new Color(0.3f, 1f, 0.9f) : new Color(1f, 0.35f, 0.25f);
                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Cursed) return new Color(0.6f, 1f, 0.3f);
                    if (t.Teleporter == TeleporterKind.Locked && !l.Active) return null;
                    return new Color(0.3f, 0.95f, 1f);
                case TileType.Trap:
                    if (!l.Armed) return null;
                    return t.Trap == TrapKind.Darkness ? new Color(0.55f, 0.25f, 0.9f)
                         : t.Trap == TrapKind.Reverse ? new Color(1f, 0.35f, 0.8f)
                         : t.Trap == TrapKind.Rotate ? new Color(1f, 0.8f, 0.35f)
                         : (Color?)null;
                case TileType.Barrier:
                    if (l.Open) return null;
                    return t.Param == 0 ? new Color(1f, 0.2f, 0.25f) : new Color(0.25f, 0.45f, 1f);
                case TileType.Switch: return l.Active ? new Color(0.3f, 1f, 0.9f) : new Color(1f, 0.4f, 0.3f);
                case TileType.FireJet: return l.Fire == 2 ? new Color(1f, 0.55f, 0.15f) : l.Fire == 1 ? new Color(0.9f, 0.25f, 0.05f) : (Color?)null;
                case TileType.Current: return null;
            }
            return null;
        }

        Sprite Cached(string key, System.Func<Px> paint)
        {
            if (!_cache.TryGetValue(key, out var s)) _cache[key] = s = ToSprite(paint());
            return s;
        }

        public const int MummyHeight = 40;

        Sprite CachedMummy(string key, System.Func<Px> paint)
        {
            if (!_cache.TryGetValue(key, out var s)) _cache[key] = s = ToSprite(paint(), Ppu, default, false, new Vector2(0.5f, 16f / MummyHeight));
            return s;
        }

        /// <summary>Tile sprites depend on the act's theme.</summary>
        Sprite Tiled(string key, System.Func<Px> paint) => Cached($"a{T.Act}_{key}", paint);

        static Px Turn(Px p, bool vertical) => vertical ? p.Rotated(1) : p;

        // ------------------------------------------------------------------ floors

        static Px PaintFloor(int seed)
        {
            var p = new Px(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    p.Set(x, y, Px.Shade(Sand, Px.Hash(x, y, seed + T.Act * 100) * 0.18f - 0.09f));

            switch (S)
            {
                case TombTheme.Style.Sandstone:
                {
                    int off = seed % 2 == 0 ? 0 : 8;
                    for (int i = 0; i < 32; i++)
                    {
                        p.Set(i, 0, SandDark); p.Set(i, 16, SandDark);
                        p.Set(off % 32, i, SandDark);
                        p.Set((off + 16) % 32, i, SandDark);
                    }
                    for (int i = 1; i < 32; i++) { p.Set(i, 1, SandLight); p.Set(i, 17, SandLight); }
                    Cracks(p, seed, 1);
                    break;
                }
                case TombTheme.Style.Flooded:
                {
                    // Wet flagstones: dark grout full of moss, a film of water catching the light.
                    for (int i = 0; i < 32; i++) { p.Set(i, 0, SandDark); p.Set(i, 15, SandDark); p.Set(0, i, SandDark); p.Set(15 + (seed % 2) * 4, i, SandDark); }
                    for (int k = 0; k < 10; k++) p.Set((int)(Px.Hash(k, 5, seed) * 31), (k % 2) * 15, T.Accent);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float w = Mathf.Sin(x * 0.3f + seed) * Mathf.Sin(y * 0.25f + seed * 2) + Px.Hash(x, y, seed + 9) * 0.4f;
                            if (w > 0.55f) p.Set(x, y, Px.Lerp(p.Get(x, y), new Color32(120, 170, 180, 255), 0.45f));
                        }
                    for (int k = 0; k < 5; k++) p.Set(3 + (int)(Px.Hash(k, 1, seed) * 26), 3 + (int)(Px.Hash(1, k, seed) * 26), new Color32(220, 245, 245, 255));
                    break;
                }
                case TombTheme.Style.Ruins:
                {
                    // Irregular flagstones, missing chunks, grit.
                    for (int i = 0; i < 32; i++) { p.Set(i, (11 + seed * 3) % 32, SandDark); p.Set((7 + seed * 5) % 32, i, SandDark); }
                    for (int i = 0; i < 16; i++) p.Set(20 + i / 4, i * 2 % 32, SandDark);
                    Cracks(p, seed, 3);
                    int hx = 4 + (int)(Px.Hash(7, 7, seed) * 20), hy = 4 + (int)(Px.Hash(8, 8, seed) * 20);
                    if (seed % 2 == 1) { p.Ellipse(hx, hy, 3, 2, T.AccentDark); p.Set(hx - 1, hy + 2, SandLight); }
                    for (int k = 0; k < 12; k++) p.Set((int)(Px.Hash(k, 13, seed) * 31), (int)(Px.Hash(13, k, seed) * 31), k % 3 == 0 ? SandLight : SandDark);
                    break;
                }
                case TombTheme.Style.Tech:
                {
                    // Metal deck plates, rivets, a thin light seam.
                    for (int i = 0; i < 32; i++) { p.Set(i, 0, SandDark); p.Set(i, 16, SandDark); p.Set(0, i, SandDark); p.Set(16, i, SandDark); }
                    for (int i = 1; i < 32; i++) { p.Set(i, 1, SandLight); p.Set(1, i, SandLight); }
                    foreach (int cx in new[] { 3, 13, 19, 29 }) foreach (int cy in new[] { 3, 13, 19, 29 }) p.Set(cx, cy, SandLight);
                    if (seed % 2 == 0) for (int x = 4; x < 28; x++) p.Set(x, 8, Px.Lerp(SandDark, T.Accent, 0.35f));
                    break;
                }
                case TombTheme.Style.Inferno:
                {
                    // Basalt tiles split by seams: most have cooled down (dark red), one slab in four still glows.
                    // Kept sparse so flame jets and mechanisms stand out from the floor.
                    bool hot = seed == 0;
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float n = Mathf.Abs(Mathf.Sin(x * 0.22f + seed * 1.7f) + Mathf.Sin(y * 0.19f - x * 0.07f + seed));
                            if (n < 0.05f) p.Set(x, y, hot ? Px.Lerp(T.Accent, new Color32(255, 200, 110, 255), Px.Hash(x, y, seed) * 0.5f)
                                                           : Px.Lerp(p.Get(x, y), new Color32(120, 36, 18, 255), 0.7f));
                            else if (n < 0.1f) p.Set(x, y, Px.Lerp(p.Get(x, y), new Color32(70, 24, 16, 255), 0.5f));
                        }
                    break;
                }
            }
            return p;
        }

        static void Cracks(Px p, int seed, int count)
        {
            for (int c = 0; c < count; c++)
            {
                int cx = 4 + (int)(Px.Hash(1 + c, 2, seed) * 22), cy = 4 + (int)(Px.Hash(3, 4 + c, seed) * 22);
                for (int k = 0; k < 6; k++) p.Set(cx + k, cy + (k % 3 == 0 ? 1 : 0) - c, SandDark);
            }
            for (int k = 0; k < 4; k++) p.Set((int)(Px.Hash(k, 9, seed) * 31), (int)(Px.Hash(9, k, seed) * 31), SandDark);
        }

        /// <summary>Rare floor details, chosen per theme.</summary>
        static void DecorateFloor(Px p, int kind, int seed)
        {
            var bone = new Color32(226, 214, 186, 255);
            var boneDark = new Color32(160, 148, 120, 255);
            switch (S)
            {
                case TombTheme.Style.Sandstone:
                    if (kind == 0) Bones(p, bone, boneDark);
                    else if (kind == 1) Shards(p, new Color32(170, 90, 50, 255), new Color32(120, 60, 30, 255));
                    else if (kind == 2) { for (int k = 0; k < 6; k++) { int x = 8 + (int)(Px.Hash(k, 2, seed) * 16), y = 8 + (int)(Px.Hash(2, k, seed) * 16); p.Rect(x, y, x + 1, y, Gold); p.Set(x, y + 1, GoldDark); } }
                    else SandPile(p);
                    break;
                case TombTheme.Style.Flooded:
                    if (kind == 0) { p.Circle(12, 14, 5, T.Accent, true); p.Line(12, 14, 16, 18, Px.Shade(T.Accent, -0.4f)); p.Circle(22, 22, 3, Px.Shade(T.Accent, 0.2f), true); p.Set(22, 25, new Color32(240, 170, 200, 255)); } // lotus leaves
                    else if (kind == 1) { p.Line(8, 16, 22, 16, bone); for (int x = 10; x < 22; x += 3) { p.Line(x, 13, x, 19, boneDark); } p.Rect(22, 14, 25, 18, bone); } // fish bones
                    else if (kind == 2) { for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (Px.Hash(x / 3, y / 3, seed + 4) > 0.8f) p.Set(x, y, Px.Lerp(p.Get(x, y), T.Accent, 0.6f)); } // moss
                    else Shards(p, new Color32(90, 110, 130, 255), new Color32(60, 70, 90, 255));
                    break;
                case TombTheme.Style.Ruins:
                    if (kind == 0) Bones(p, bone, boneDark);
                    else if (kind == 1) { p.Circle(14, 14, 6, StoneLight, true); p.Circle(14, 14, 6, StoneDark, false); p.Line(9, 12, 19, 16, StoneDark); } // fallen column drum
                    else if (kind == 2) { for (int k = 0; k < 9; k++) { int x = 4 + (int)(Px.Hash(k, 6, seed) * 24), y = 4 + (int)(Px.Hash(6, k, seed) * 24); p.Rect(x, y, x + 2, y + 1, k % 2 == 0 ? StoneLight : StoneDark); } } // rubble
                    else { p.Line(2, 30, 14, 18, T.Accent); p.Line(14, 18, 16, 8, T.Accent); p.Line(14, 18, 26, 22, Px.Shade(T.Accent, -0.3f)); } // roots
                    break;
                case TombTheme.Style.Tech:
                    if (kind == 0) { p.Rect(6, 6, 25, 25, SandDark); for (int x = 8; x < 24; x += 3) p.Rect(x, 8, x, 23, Black); } // vent grate
                    else if (kind == 1) { p.Line(0, 10, 31, 12, new Color32(30, 30, 36, 255)); p.Line(0, 11, 31, 13, T.Accent); } // cable
                    else if (kind == 2) { p.Circle(16, 16, 6, T.Accent, false); p.Line(16, 10, 16, 22, T.Accent); p.Line(13, 19, 19, 19, T.Accent); } // holographic ankh
                    else { p.Rect(10, 10, 21, 21, T.AccentDark); p.Rect(12, 12, 19, 19, SandDark); } // gold inlay plate
                    break;
                case TombTheme.Style.Inferno:
                    if (kind == 0) Bones(p, new Color32(60, 50, 48, 255), new Color32(30, 24, 22, 255));
                    else if (kind == 1) { p.Circle(16, 16, 6, T.Accent, true); p.Circle(16, 16, 3, new Color32(255, 230, 140, 255), true); } // lava bubble
                    else if (kind == 2) { for (int k = 0; k < 10; k++) p.Set(4 + (int)(Px.Hash(k, 3, seed) * 24), 4 + (int)(Px.Hash(3, k, seed) * 24), new Color32(255, 170, 60, 255)); } // embers
                    else Shards(p, T.AccentDark, GoldDark);
                    break;
            }
        }

        static void Bones(Px p, Color32 bone, Color32 dark)
        {
            p.Circle(11, 20, 4, bone, true); p.Rect(9, 19, 10, 20, dark); p.Rect(12, 19, 13, 20, dark); p.Rect(10, 16, 12, 16, dark); // skull
            p.Line(17, 10, 26, 15, bone); p.Rect(16, 9, 17, 11, bone); p.Rect(26, 14, 27, 16, bone);
            p.Line(6, 6, 12, 9, bone);
        }

        static void Shards(Px p, Color32 c, Color32 dark)
        {
            p.Ellipse(12, 14, 4, 3, c); p.Line(9, 14, 15, 14, dark);
            p.Rect(20, 20, 23, 22, c); p.Set(20, 20, dark);
            p.Rect(17, 8, 19, 9, dark);
        }

        static void SandPile(Px p)
        {
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = ((x - 20) * (x - 20)) / 90f + ((y - 10) * (y - 10)) / 40f;
                    if (d < 1f) p.Set(x, y, Px.Lerp(p.Get(x, y), SandLight, 0.7f * (1f - d)));
                }
        }

        // ------------------------------------------------------------------ walls

        /// <summary>Top of a wall seen from above (inside the rock): a flat, darker cap.</summary>
        static Px PaintWallTop(int seed)
        {
            var p = new Px(32, 32);
            // The top of the masonry: darker than the floor so the walls read as solid mass, cut in big blocks.
            var top = Px.Lerp(StoneDark, Stone, 0.45f);
            var seam = Px.Shade(StoneDark, -0.25f);
            int off = seed * 5;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    bool joint = y % 16 == 0 || (x + (y / 16 % 2 == 0 ? off : off + 8)) % 16 == 0;
                    bool lit = y % 16 == 15 || (x + (y / 16 % 2 == 0 ? off : off + 8)) % 16 == 1;
                    var c = joint ? seam : lit ? Px.Lerp(top, Stone, 0.5f) : top;
                    p.Set(x, y, Px.Shade(c, Px.Hash(x, y, seed + 70 + T.Act * 10) * 0.1f - 0.05f));
                }
            if (S == TombTheme.Style.Tech) { for (int i = 0; i < 32; i += 8) for (int x = 0; x < 32; x++) p.Set(x, i, Px.Shade(top, -0.2f)); }
            else if (S == TombTheme.Style.Inferno) { for (int k = 0; k < 3; k++) p.Set((int)(Px.Hash(k, 1, seed) * 31), (int)(Px.Hash(1, k, seed) * 31), T.Accent); }
            else if (S == TombTheme.Style.Ruins && seed == 1) { p.Line(4, 4, 14, 12, StoneDark); p.Line(14, 12, 20, 10, StoneDark); }
            return p;
        }

        /// <summary>Front face of a wall (a block of the tomb seen in 3/4 view) with a lit cap on top.</summary>
        static Px PaintWallFace(int seed)
        {
            var p = new Px(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    p.Set(x, y, Px.Shade(Stone, Px.Hash(x, y, seed + 50 + T.Act * 10) * 0.14f - 0.07f));

            switch (S)
            {
                case TombTheme.Style.Tech:
                    // Metal panels, a neon strip, bolts.
                    for (int x = 0; x < 32; x++) { p.Set(x, 12, StoneDark); p.Set(x, 0, StoneDark); }
                    p.Set(15, 3, StoneLight); p.Set(15, 9, StoneLight);
                    for (int y = 0; y < 25; y++) p.Set(seed % 2 == 0 ? 15 : 7, y, StoneDark);
                    for (int x = 2; x < 30; x++) p.Set(x, 18, T.Accent);
                    for (int x = 2; x < 30; x++) p.Set(x, 17, Px.Lerp(Stone, T.Accent, 0.3f));
                    break;
                case TombTheme.Style.Ruins:
                    Bricks(p);
                    // Missing bricks and a crack.
                    if (seed % 2 == 0) p.Rect(17, 9, 23, 14, T.AccentDark);
                    else p.Rect(2, 1, 8, 6, T.AccentDark);
                    p.Line(10, 24, 13, 16, StoneDark); p.Line(13, 16, 11, 9, StoneDark);
                    break;
                case TombTheme.Style.Inferno:
                    for (int row = 0; row < 3; row++) for (int x = 0; x < 32; x++) p.Set(x, row * 9, StoneDark);
                    for (int y = 0; y < 25; y++) { float n = Mathf.Sin(y * 0.4f + seed * 2f) * 3f; p.Set(16 + (int)n, y, T.Accent); if (y % 3 == 0) p.Set(17 + (int)n, y, new Color32(255, 220, 120, 255)); }
                    break;
                case TombTheme.Style.Flooded:
                    Bricks(p);
                    // Water stains, drips and moss at the foot of the wall.
                    for (int y = 0; y < 25; y++) if (Px.Hash(4 + seed, y, 3) > 0.35f) p.Set(5 + seed * 9, y, new Color32(130, 170, 175, 255));
                    for (int x = 0; x < 32; x++) for (int y = 0; y < 4; y++) if (Px.Hash(x, y, seed + 21) > 0.45f - y * 0.1f) p.Set(x, y, T.Accent);
                    break;
                default:
                    Bricks(p);
                    int g = seed % 4;
                    Color32 carve = StoneDark;
                    if (g == 0) { p.Circle(16, 13, 4, carve, false); p.Set(16, 13, carve); p.Line(12, 13, 8, 11, carve); }
                    else if (g == 1) { p.Circle(16, 17, 3, carve, false); p.Line(16, 14, 16, 5, carve); p.Line(12, 12, 20, 12, carve); }
                    else if (g == 2) { for (int k = 0; k < 3; k++) for (int x = 8; x < 24; x++) p.Set(x, 8 + k * 4 + ((x / 2) % 2), carve); }
                    else { p.Line(10, 6, 14, 18, carve); p.Line(14, 18, 20, 20, carve); p.Line(20, 20, 22, 14, carve); p.Line(14, 6, 18, 6, carve); }
                    break;
            }
            // Lit cap on top (fake 3/4 view).
            for (int y = 26; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    p.Set(x, y, Px.Shade(StoneLight, Px.Hash(x, y, seed) * 0.1f - 0.05f));
            for (int x = 0; x < 32; x++) { p.Set(x, 25, StoneDark); p.Set(x, 31, Px.Shade(StoneLight, 0.15f)); }
            return p;
        }

        static void Bricks(Px p)
        {
            for (int row = 0; row < 4; row++)
            {
                int y0 = row * 8;
                for (int x = 0; x < 32; x++) p.Set(x, y0, StoneDark);
                int shift = row % 2 == 0 ? 0 : 8;
                for (int y = y0; y < y0 + 8 && y < 25; y++) { p.Set(shift, y, StoneDark); p.Set((shift + 16) % 32, y, StoneDark); }
            }
        }

        /// <summary>Rare wall details, chosen per theme.</summary>
        static void DecorateWall(Px p, int kind)
        {
            switch (S)
            {
                case TombTheme.Style.Sandstone:
                    if (kind == 0) { p.Rect(10, 3, 21, 20, StoneDark); p.Rect(13, 4, 18, 12, new Color32(170, 120, 70, 255)); p.Ellipse(15, 14, 2, 2, Gold); } // niche with a canopic jar
                    else if (kind == 1) { p.Rect(9, 4, 22, 21, Gold); p.Rect(10, 5, 21, 20, Px.Shade(Stone, 0.1f)); p.Circle(15, 16, 2, Lapis, true); p.Line(12, 9, 19, 9, Lapis); p.Line(13, 12, 18, 12, LapisDark); } // cartouche
                    else { for (int x = 0; x < 32; x++) { p.Set(x, 20, Lapis); p.Set(x, 21, Gold); p.Set(x, 19, new Color32(170, 50, 40, 255)); } } // painted frieze
                    break;
                case TombTheme.Style.Flooded:
                    if (kind == 0) { for (int y = 0; y < 26; y++) for (int x = 12; x < 20; x++) if (Px.Hash(x, y / 2, 5) > 0.3f) p.Set(x, y, Px.Lerp(new Color32(110, 170, 190, 255), new Color32(220, 245, 250, 255), Px.Hash(x, y, 6))); } // trickling water
                    else if (kind == 1) { for (int x = 0; x < 32; x++) { p.Set(x, 20, new Color32(50, 110, 140, 255)); p.Set(x, 19, Gold); } }
                    else { p.Rect(11, 4, 20, 19, StoneDark); p.Ellipse(15, 10, 3, 5, new Color32(80, 130, 120, 255)); } // drowned statue
                    break;
                case TombTheme.Style.Ruins:
                    if (kind == 0) { p.Line(4, 2, 12, 22, Black); p.Line(12, 22, 22, 14, Black); p.Line(22, 14, 27, 24, Black); }
                    else if (kind == 1) { p.Line(6, 24, 8, 4, T.Accent); p.Line(8, 4, 16, 10, T.Accent); p.Line(16, 10, 22, 2, Px.Shade(T.Accent, -0.3f)); } // roots
                    else { p.Rect(10, 4, 22, 20, T.AccentDark); p.Circle(16, 13, 4, StoneLight, true); p.Rect(13, 12, 14, 13, StoneDark); p.Rect(17, 12, 18, 13, StoneDark); } // broken statue head
                    break;
                case TombTheme.Style.Tech:
                    if (kind == 0) { p.Rect(7, 3, 24, 15, Black); for (int y = 5; y < 14; y += 2) p.Line(9, y, 9 + (y * 3) % 14, y, T.Accent); } // screen
                    else if (kind == 1) { p.Rect(12, 2, 19, 22, T.AccentDark); p.Rect(14, 15, 17, 20, Black); p.Set(14, 17, T.Accent); p.Set(17, 17, T.Accent); } // gold Anubis
                    else { p.Circle(16, 12, 6, T.Accent, false); p.Circle(16, 12, 3, T.Accent, false); }
                    break;
                case TombTheme.Style.Inferno:
                    if (kind == 0) { for (int y = 0; y < 26; y++) for (int x = 13; x < 19; x++) p.Set(x, y, Px.Lerp(T.Accent, new Color32(255, 230, 140, 255), Px.Hash(x, y, 2))); } // lava fall
                    else if (kind == 1) { p.Rect(10, 3, 21, 20, T.AccentDark); p.Rect(12, 5, 19, 18, Black); p.Circle(15, 11, 2, T.Accent, true); } // golden sun shrine
                    else { p.Line(4, 4, 26, 22, T.Accent); }
                    break;
            }
        }

        /// <summary>Contact shadow on a floor next to solid neighbours.</summary>
        static Px PaintShadow(int mask)
        {
            var p = new Px(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float a = 0f;
                    if ((mask & 1) != 0) a = Mathf.Max(a, Mathf.Clamp01(1f - (31 - y) / 9f) * 0.62f); // the wall above casts the strongest shadow
                    if ((mask & 2) != 0) a = Mathf.Max(a, Mathf.Clamp01(1f - (31 - x) / 5f) * 0.38f);
                    if ((mask & 4) != 0) a = Mathf.Max(a, Mathf.Clamp01(1f - y / 3f) * 0.25f);
                    if ((mask & 8) != 0) a = Mathf.Max(a, Mathf.Clamp01(1f - x / 5f) * 0.38f);
                    p.Set(x, y, new Color32(0, 0, 0, (byte)(a * 255)));
                }
            return p;
        }

        // ------------------------------------------------------------------ mechanisms

        static Px PaintDoor(bool open)
        {
            var p = PaintFloor(1);
            if (S == TombTheme.Style.Tech)
            {
                p.Rect(0, 0, 3, 31, StoneDark); p.Rect(28, 0, 31, 31, StoneDark);
                if (open) { p.Rect(1, 0, 2, 31, T.Accent); p.Rect(29, 0, 30, 31, T.Accent); return p; }
                p.Rect(4, 0, 27, 31, MetalDark);
                for (int y = 2; y < 32; y += 5) p.Rect(4, y, 27, y, Metal);
                p.Rect(14, 0, 17, 31, new Color32(255, 80, 60, 255));
                return p;
            }
            if (S == TombTheme.Style.Flooded)
            {
                // Sluice gate: bronze bars over dark water.
                p.Rect(0, 0, 3, 31, StoneDark); p.Rect(28, 0, 31, 31, StoneDark);
                if (open) { p.Rect(4, 26, 27, 31, new Color32(120, 80, 40, 255)); return p; }
                p.Rect(4, 0, 27, 31, new Color32(30, 60, 80, 255));
                for (int x = 6; x < 27; x += 4) p.Rect(x, 0, x + 1, 31, new Color32(160, 110, 50, 255));
                p.Rect(4, 14, 27, 16, new Color32(120, 80, 40, 255));
                return p;
            }
            if (open)
            {
                p.Rect(0, 0, 4, 31, StoneDark); p.Rect(27, 0, 31, 31, StoneDark);
                p.Rect(5, 14, 26, 17, Black);
                return p;
            }
            p.Rect(0, 0, 31, 31, StoneDark);
            p.Rect(3, 2, 28, 29, Px.Shade(Stone, 0.15f));
            if (S == TombTheme.Style.Ruins) { p.Line(6, 4, 14, 20, StoneDark); p.Line(14, 20, 24, 26, StoneDark); }
            var eye = S == TombTheme.Style.Inferno ? T.Accent : Gold;
            p.Circle(16, 17, 5, eye, false);
            p.Circle(16, 17, 2, Lapis, true);
            p.Line(9, 20, 23, 20, eye);
            p.Line(14, 12, 12, 7, eye);
            p.Line(18, 12, 22, 9, eye);
            return p;
        }

        static Px PaintButton(bool pressed)
        {
            var p = PaintFloor(2);
            if (S == TombTheme.Style.Tech)
            {
                // Holographic pressure pad.
                for (int k = 0; k < 6; k++)
                {
                    float a0 = k * Mathf.PI / 3f, a1 = (k + 1) * Mathf.PI / 3f;
                    p.Line(16 + (int)(Mathf.Cos(a0) * 10), 16 + (int)(Mathf.Sin(a0) * 10), 16 + (int)(Mathf.Cos(a1) * 10), 16 + (int)(Mathf.Sin(a1) * 10), pressed ? Turquoise : T.Accent);
                }
                p.Circle(16, 16, 5, pressed ? Turquoise : MetalDark, true);
                return p;
            }
            p.Circle(16, 16, 10, StoneDark, true);
            p.Circle(16, 16, 9, pressed ? LapisDark : Lapis, true);
            var scarab = pressed ? Turquoise : Gold;
            p.Ellipse(16, 15, 4, 6, scarab);
            p.Circle(16, 22, 2, scarab, true);
            p.Line(11, 18, 13, 16, scarab); p.Line(21, 18, 19, 16, scarab);
            p.Line(11, 11, 13, 13, scarab); p.Line(21, 11, 19, 13, scarab);
            p.Line(16, 9, 16, 21, pressed ? LapisDark : Lapis);
            return p;
        }

        static Px PaintSpikes(bool armed)
        {
            var p = PaintFloor(3);
            for (int gy = 0; gy < 3; gy++)
                for (int gx = 0; gx < 3; gx++)
                {
                    int cx = 7 + gx * 9, cy = 6 + gy * 9;
                    if (!armed) { p.Rect(cx - 1, cy, cx + 1, cy + 1, Black); continue; }
                    for (int h = 0; h < 6; h++)
                        for (int w = -(5 - h) / 2; w <= (5 - h) / 2; w++)
                            p.Set(cx + w, cy + h, w < 0 ? MetalDark : Metal);
                }
            return p;
        }

        static Px PaintDarkness(bool armed)
        {
            var p = PaintFloor(0);
            if (!armed) return p;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 15.5f, dy = y - 15.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float swirl = Mathf.Sin(a * 3 + r * 0.6f);
                    if (r < 13 && swirl > 0.1f) p.Set(x, y, Px.Lerp(p.Get(x, y), new Color32(28, 14, 40, 255), 0.85f));
                }
            for (int k = 0; k < 8; k++) p.Set(6 + (int)(Px.Hash(k, 1, 77) * 20), 6 + (int)(Px.Hash(1, k, 77) * 20), new Color32(170, 90, 255, 255));
            return p;
        }

        /// <summary>Mirror of Seth: a polished disc set in the floor, two arrows swapping sides (dull once it has struck).</summary>
        static Px PaintMirror(bool armed)
        {
            var p = PaintFloor(0);
            p.Circle(16, 16, 11, StoneDark, true);
            p.Circle(16, 16, 10, armed ? Gold : MetalDark, false);
            p.Circle(16, 16, 8, armed ? new Color32(150, 70, 140, 255) : new Color32(70, 62, 70, 255), true);
            if (!armed) return p;
            var ink = new Color32(255, 190, 240, 255);
            // ← on top, → below.
            p.Line(10, 19, 22, 19, ink); p.Line(10, 19, 13, 22, ink); p.Line(10, 19, 13, 16, ink);
            p.Line(10, 13, 22, 13, ink); p.Line(22, 13, 19, 16, ink); p.Line(22, 13, 19, 10, ink);
            return p;
        }

        /// <summary>Turning slab: a round slab cut out of the floor, an arrow curling the way the tomb will turn.</summary>
        static Px PaintTurning(bool armed, bool clockwise)
        {
            var p = PaintFloor(0);
            p.Circle(16, 16, 12, StoneDark, false);
            p.Circle(16, 16, 11, armed ? Gold : MetalDark, false);
            if (!armed) return p;
            var ink = new Color32(255, 205, 90, 255);
            int X(float x) => clockwise ? 31 - Mathf.RoundToInt(x) : Mathf.RoundToInt(x);
            int Y(float y) => Mathf.RoundToInt(y);
            const float r = 7f, from = 0.4f, to = 5.0f;
            for (int k = 0; k <= 48; k++)
            {
                float a = Mathf.Lerp(from, to, k / 48f);
                p.Set(X(16 + Mathf.Cos(a) * r), Y(16 + Mathf.Sin(a) * r), ink);
            }
            // Arrow head at the end of the arc (drawn anticlockwise, mirrored for clockwise).
            float ex = 16 + Mathf.Cos(to) * r, ey = 16 + Mathf.Sin(to) * r;
            float tx = -Mathf.Sin(to), ty = Mathf.Cos(to), nx = Mathf.Cos(to), ny = Mathf.Sin(to);
            float hx = ex + tx * 2f, hy = ey + ty * 2f;
            p.Line(X(hx), Y(hy), X(ex - tx * 2f + nx * 3f), Y(ey - ty * 2f + ny * 3f), ink);
            p.Line(X(hx), Y(hy), X(ex - tx * 2f - nx * 3f), Y(ey - ty * 2f - ny * 3f), ink);
            return p;
        }

        static Px PaintPortal(Color32 ring, bool sealedBar)
        {
            var p = PaintFloor(1);
            p.Circle(16, 16, 12, StoneDark, false);
            p.Circle(16, 16, 11, ring, false);
            p.Circle(16, 16, 9, Black, true);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                p.Set(16 + Mathf.RoundToInt(Mathf.Cos(a) * 11), 16 + Mathf.RoundToInt(Mathf.Sin(a) * 11), Gold);
            }
            p.Circle(16, 20, 2, ring, false);
            p.Line(16, 17, 16, 10, ring);
            p.Line(13, 16, 19, 16, ring);
            if (sealedBar) { p.Rect(4, 15, 27, 17, MetalDark); p.Rect(4, 16, 27, 16, Metal); }
            return p;
        }

        /// <summary>What smothers the torch, per theme: dust, a deep puddle, ash, a smoke vent.</summary>
        static Px PaintDust()
        {
            var p = PaintFloor(1);
            Color32 a, b;
            switch (S)
            {
                case TombTheme.Style.Flooded: a = new Color32(28, 58, 80, 255); b = new Color32(70, 120, 150, 255); break;
                case TombTheme.Style.Tech: a = new Color32(80, 84, 96, 255); b = new Color32(140, 146, 160, 255); break;
                case TombTheme.Style.Inferno: a = new Color32(40, 36, 36, 255); b = new Color32(90, 84, 82, 255); break;
                default: a = new Color32(150, 140, 128, 255); b = new Color32(186, 176, 160, 255); break;
            }
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Mathf.Sin(x * 0.35f + y * 0.12f) * 0.5f + Mathf.Sin(y * 0.41f - x * 0.2f) * 0.5f + Px.Hash(x, y, 77) * 0.6f;
                    if (S == TombTheme.Style.Flooded) d = 1.2f - (((x - 15.5f) * (x - 15.5f)) + ((y - 15.5f) * (y - 15.5f))) / 180f + Px.Hash(x, y, 3) * 0.2f;
                    if (d > 0.35f) p.Set(x, y, Px.Lerp(p.Get(x, y), d > 0.8f ? b : a, 0.85f));
                }
            if (S == TombTheme.Style.Flooded) { p.Circle(16, 16, 6, b, false); p.Circle(16, 16, 10, Px.Shade(b, -0.2f), false); }
            if (S == TombTheme.Style.Tech) { p.Rect(9, 9, 22, 22, MetalDark); for (int x = 10; x < 22; x += 3) p.Rect(x, 10, x, 21, Black); }
            if (S == TombTheme.Style.Inferno) for (int k = 0; k < 5; k++) p.Set((int)(Px.Hash(k, 4, 9) * 31), (int)(Px.Hash(4, k, 9) * 31), T.Accent);
            for (int k = 0; k < 10; k++) p.Set((int)(Px.Hash(k, 3, 91) * 31), (int)(Px.Hash(3, k, 91) * 31), b);
            return p;
        }

        /// <summary>Wall with a light source: a bronze sconce, a neon lamp or a brazier.</summary>
        static Px PaintWallTorch()
        {
            var p = PaintWallFace(1);
            if (S == TombTheme.Style.Tech)
            {
                p.Rect(11, 6, 20, 20, StoneDark);
                p.Rect(13, 8, 18, 18, new Color32(160, 250, 255, 255));
                p.Rect(15, 8, 16, 18, new Color32(240, 255, 255, 255));
                return p;
            }
            var bronze = new Color32(150, 98, 42, 255);
            var bronzeDark = new Color32(96, 60, 24, 255);
            bool brazier = S == TombTheme.Style.Inferno;
            p.Rect(15, 6, 16, 14, bronzeDark);
            p.Rect(brazier ? 10 : 12, 14, brazier ? 21 : 19, 15, bronze);
            p.Rect(13, 13, 18, 13, bronzeDark);
            p.Ellipse(16, brazier ? 20 : 19, brazier ? 5 : 3, brazier ? 6 : 4, new Color32(255, 110, 30, 255));
            p.Ellipse(16, 18, 2, 3, new Color32(255, 200, 80, 255));
            p.Rect(16, 16, 16, 18, new Color32(255, 250, 220, 255));
            return p;
        }

        static Px PaintBreakable()
        {
            var p = PaintFloor(2);
            Color32 c = new Color32(70, 52, 28, 255);
            p.Line(16, 16, 4, 22, c); p.Line(16, 16, 27, 25, c); p.Line(16, 16, 20, 3, c);
            p.Line(16, 16, 6, 7, c); p.Line(9, 20, 7, 28, c); p.Line(22, 9, 29, 11, c);
            p.Circle(16, 16, 2, Black, true);
            return p;
        }

        static Px PaintLadder(bool up)
        {
            var p = PaintFloor(3);
            bool tech = S == TombTheme.Style.Tech;
            var wood = tech ? Metal : new Color32(130, 84, 40, 255);
            var woodDark = tech ? MetalDark : new Color32(84, 52, 24, 255);
            if (!up) p.Rect(4, 2, 27, 29, Black);
            p.Rect(8, 2, 9, 29, up ? wood : woodDark); p.Rect(22, 2, 23, 29, up ? wood : woodDark);
            for (int y = 5; y < 29; y += 5) p.Rect(10, y, 21, y + 1, wood);
            if (up) for (int y = 26; y < 30; y++) for (int x = 6; x < 26; x++) p.Set(x, y, Px.Lerp(p.Get(x, y), SandLight, 0.4f));
            return p;
        }

        /// <summary>Flowing water: chevrons scrolling downstream (painted pointing up, then turned).</summary>
        static Px PaintCurrent(Dir dir, int frame)
        {
            var p = new Px(32, 32);
            var deep = new Color32(26, 70, 96, 255);
            var mid = new Color32(44, 110, 140, 255);
            var foam = new Color32(200, 240, 245, 255);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float w = Mathf.Sin((y + frame * 8) * 0.39f + Mathf.Sin(x * 0.4f) * 0.8f);
                    p.Set(x, y, w > 0.6f ? mid : deep);
                }
            for (int k = 0; k < 2; k++)
            {
                int cy = ((k * 16 + frame * 4) % 32);
                for (int i = 0; i < 7; i++) { p.Set(16 - i, cy - i / 2 + 3, foam); p.Set(15 + i, cy - i / 2 + 3, foam); }
            }
            // Stone banks on both sides.
            for (int y = 0; y < 32; y++) { p.Set(0, y, SandDark); p.Set(31, y, SandDark); p.Set(1, y, Px.Shade(deep, -0.3f)); p.Set(30, y, Px.Shade(deep, -0.3f)); }
            int turns = dir == Dir.Up ? 0 : dir == Dir.Right ? 3 : dir == Dir.Down ? 2 : 1;
            return p.Rotated(turns);
        }

        static Px PaintCrumbling(bool collapsed)
        {
            var p = PaintFloor(3);
            if (collapsed)
            {
                p.Rect(2, 2, 29, 29, Black);
                for (int k = 0; k < 14; k++)
                {
                    int x = 3 + (int)(Px.Hash(k, 2, 33) * 24), y = 3 + (int)(Px.Hash(2, k, 33) * 24);
                    p.Rect(x, y, x + 2, y + 1, k % 2 == 0 ? SandDark : Px.Shade(SandDark, -0.4f));
                }
                return p;
            }
            // A slab hanging over the void: deep cracks, a dark rim, loose grit.
            var crack = Px.Shade(SandDark, -0.45f);
            for (int x = 0; x < 32; x++) { p.Set(x, 0, Black); p.Set(x, 31, Black); }
            for (int y = 0; y < 32; y++) { p.Set(0, y, Black); p.Set(31, y, Black); }
            p.Line(3, 6, 14, 15, crack); p.Line(14, 15, 28, 10, crack); p.Line(14, 15, 18, 28, crack); p.Line(6, 25, 14, 15, crack);
            p.Line(20, 4, 24, 10, crack);
            return p;
        }

        /// <summary>Laser barrier: two emitters and the beam between them (horizontal; turned for vertical corridors).</summary>
        static Px PaintBarrier(bool red, bool open)
        {
            var p = PaintFloor(0);
            var core = red ? new Color32(255, 90, 90, 255) : new Color32(110, 160, 255, 255);
            var halo = red ? new Color32(200, 30, 40, 255) : new Color32(40, 70, 220, 255);
            p.Rect(0, 11, 4, 20, MetalDark); p.Rect(27, 11, 31, 20, MetalDark);
            p.Rect(2, 14, 3, 17, open ? Px.Shade(halo, -0.5f) : core); p.Rect(28, 14, 29, 17, open ? Px.Shade(halo, -0.5f) : core);
            if (open) return p;
            for (int x = 5; x < 27; x++)
            {
                p.Set(x, 13, halo); p.Set(x, 18, halo);
                p.Set(x, 14, core); p.Set(x, 17, core);
                p.Set(x, 15, new Color32(255, 255, 255, 255)); p.Set(x, 16, new Color32(255, 255, 255, 255));
            }
            return p;
        }

        static Px PaintSwitch(bool on)
        {
            var p = PaintFloor(2);
            p.Circle(16, 14, 8, MetalDark, true);
            p.Circle(16, 14, 6, Px.Shade(MetalDark, -0.3f), true);
            var lit = on ? Turquoise : new Color32(255, 80, 60, 255);
            int tipX = on ? 23 : 9;
            p.Line(16, 14, tipX, 24, Metal);
            p.Line(17, 14, tipX + 1, 24, Metal);
            p.Circle(tipX, 25, 2, lit, true);
            p.Rect(13, 4, 19, 5, lit);
            return p;
        }

        /// <summary>Flame vent: an iron grate, glowing before it fires, a pillar of fire when it does.</summary>
        static Px PaintFireJet(int state)
        {
            var p = PaintFloor(1);
            p.Rect(5, 5, 26, 26, Px.Shade(MetalDark, -0.4f));
            var slot = state == 0 ? Black : state == 1 ? new Color32(200, 60, 20, 255) : new Color32(255, 200, 80, 255);
            for (int y = 7; y < 25; y += 4) p.Rect(7, y, 24, y + 1, slot);
            if (state == 2)
            {
                p.Ellipse(16, 18, 9, 11, new Color32(255, 100, 20, 255));
                p.Ellipse(16, 16, 6, 8, new Color32(255, 190, 60, 255));
                p.Ellipse(16, 14, 3, 5, new Color32(255, 250, 210, 255));
            }
            return p;
        }

        static Px PaintExit()
        {
            var p = new Px(32, 32);
            p.Fill(StoneDark);
            for (int y = 2; y < 30; y++)
                for (int x = 5; x < 27; x++)
                    p.Set(x, y, Px.Lerp(new Color32(255, 244, 200, 255), Gold, y / 32f));
            p.Rect(3, 0, 4, 31, GoldDark); p.Rect(27, 0, 28, 31, GoldDark);
            p.Rect(3, 29, 28, 31, GoldDark);
            p.Circle(16, 24, 3, new Color32(255, 255, 255, 255), true);
            return p;
        }

        // ------------------------------------------------------------------ particle shapes

        static Px PaintPuff()
        {
            var p = new Px(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f)) / 7.5f + (Px.Hash(x, y, 5) - 0.5f) * 0.3f;
                    p.Set(x, y, new Color32(255, 255, 255, (byte)(Mathf.Clamp01(1f - d) * 200)));
                }
            return p;
        }

        static Px PaintShard()
        {
            var p = new Px(4, 4);
            p.Fill(new Color32(255, 255, 255, 255));
            p.Set(0, 3, Clear); p.Set(3, 0, Clear);
            return p;
        }

        /// <summary>Vertical shaft of light (fades at both ends and on the sides).</summary>
        static Px PaintBeam()
        {
            var p = new Px(32, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 32; x++)
                {
                    float sx = 1f - Mathf.Abs(x - 15.5f) / 16f;
                    float sy = Mathf.Sin(y / 63f * Mathf.PI);
                    p.Set(x, y, new Color32(255, 255, 255, (byte)(sx * sx * sy * 150)));
                }
            return p;
        }

        // ------------------------------------------------------------------ the mummy and its outfit

        /// <summary>
        /// Layers, back to front: body (silhouette, colours, bandage texture), face, shoes, hat, then the torch and the
        /// fist over its grip. Pixel rows 0-31 match a tile sprite; rows 32-39 only hold tall hats and ears.
        /// </summary>
        static Px PaintMummy(Loadout look, int frame = 0)
        {
            var c = look.Color;
            var p = new Px(32, MummyHeight);
            p.Fill(Clear);
            // Shadow.
            p.Ellipse(16, 3, 8, 2, new Color32(0, 0, 0, 90));
            // Body.
            p.Rect(10, 4, 21, 18, c.Bandage);
            p.Rect(12, 2, 14, 5, c.Bandage); p.Rect(17, 2, 19, 5, c.Bandage); // legs
            // Arms reaching forward.
            p.Rect(6, 13, 9, 16, c.Bandage); p.Rect(22, 13, 25, 16, c.Bandage);
            // Head.
            p.Circle(16, 23, 6, c.Bandage, true);
            Ears(p, look.Mummy.Shape, c);
            // Bandage stripes.
            for (int y = 5; y < 29; y += 3)
                for (int x = 9; x < 23; x++)
                    if (p.Get(x, y).a == 255 && ((x + y) % 5 != 0)) p.Set(x, y, c.Shadow);
            for (int y = 13; y <= 16; y += 3) { p.Rect(6, y, 9, y, c.Shadow); p.Rect(22, y, 25, y, c.Shadow); }
            // A legendary colour covers the act textures with its own animated one.
            if (!c.Legendary) Texture(p, look.Mummy.Pattern);
            // Dark eye band + glowing eyes.
            p.Rect(11, 22, 21, 24, new Color32(30, 24, 20, 255));
            p.Rect(12, 23, 14, 23, c.Eyes); p.Rect(18, 23, 20, 23, c.Eyes);
            if (look.Mummy.Shape == MummyShape.Jackal)
            {
                // Muzzle below the eyes, black nose at its tip.
                p.Rect(14, 18, 18, 21, c.Bandage);
                p.Rect(14, 18, 18, 18, c.Shadow);
                p.Rect(15, 20, 17, 21, new Color32(24, 20, 18, 255));
            }
            else if (look.Mummy.Shape == MummyShape.Cat)
            {
                p.Set(16, 20, new Color32(230, 120, 140, 255)); // pink nose
                p.Line(8, 21, 11, 20, c.Shadow); p.Line(24, 21, 21, 20, c.Shadow); // whiskers
                p.Rect(11, 17, 21, 17, Gold); p.Set(16, 16, Turquoise); // collar and its scarab
            }

            // Shoes and hats are cut out on their own layer, so their holes never punch through the body.
            p.Blit(Layer(l => PaintShoes(l, look.Shoes.Shoes)), 0, 0);
            p.Blit(Layer(l => PaintHat(l, look.Hat.Hat)), 0, 0);
            PaintTorch(p, look.Torch.TorchStyle);
            // Bandaged fist over the grip.
            p.Rect(23, 13, 26, 16, c.Bandage);
            p.Rect(23, 14, 26, 14, c.Shadow);
            if (c.Legendary) PaintLegendary(p, c.Fx, frame);
            return p;
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        /// <summary>
        /// Repaints the marker-coloured body (<see cref="LegendarySkins.MarkBandage"/>, folds in <see cref="LegendarySkins.MarkShadow"/>)
        /// with the frame's effect, then scatters a few sparkles in the air around the mummy.
        /// </summary>
        static void PaintLegendary(Px p, LegendaryFx fx, int frame)
        {
            float t = frame / (float)LegendaryFrames;
            for (int y = 0; y < p.H; y++)
                for (int x = 0; x < p.W; x++)
                {
                    var col = p.Get(x, y);
                    bool fold = Same(col, LegendarySkins.MarkShadow);
                    if (!fold && !Same(col, LegendarySkins.MarkBandage)) continue;
                    var c = LegendaryPixel(fx, x, y, t, frame);
                    p.Set(x, y, fold ? Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.36f) : c);
                }
            Color32 glint = Color.Lerp(LegendarySkins.Accent(fx), Color.white, 0.55f);
            var halo = new Color32(glint.r, glint.g, glint.b, 170);
            for (int k = 0; k < 3; k++)
            {
                int sx = 3 + (int)(Px.Hash(k, frame, 71) * 26), sy = 4 + (int)(Px.Hash(frame, k, 13) * 32);
                if (p.Get(sx, sy).a != 0) continue;
                p.Set(sx, sy, new Color32(255, 255, 255, 255));
                if (p.Get(sx + 1, sy).a == 0) p.Set(sx + 1, sy, halo);
                if (p.Get(sx - 1, sy).a == 0) p.Set(sx - 1, sy, halo);
                if (p.Get(sx, sy + 1).a == 0) p.Set(sx, sy + 1, halo);
                if (p.Get(sx, sy - 1).a == 0) p.Set(sx, sy - 1, halo);
            }
        }

        static Color32 Hsv(float h, float s, float v) => Color.HSVToRGB(h - Mathf.Floor(h), Mathf.Clamp01(s), Mathf.Clamp01(v));

        /// <summary>Colour of one body pixel of a legendary skin at time <paramref name="t"/> (0-1 over the loop).</summary>
        static Color32 LegendaryPixel(LegendaryFx fx, int x, int y, float t, int frame)
        {
            const float Tau = Mathf.PI * 2f;
            switch (fx)
            {
                case LegendaryFx.Rainbow:
                    return Hsv(y / 28f + x / 80f - t, 0.8f, 1f);
                case LegendaryFx.Fire:
                {
                    float heat = 1.1f - y / 34f + 0.3f * Mathf.Sin(Tau * (x / 8f + t)) + 0.3f * Px.Hash(x, y / 2 - frame, 5);
                    return heat > 1f ? new Color32(255, 240, 150, 255) : heat > 0.72f ? new Color32(255, 150, 40, 255)
                         : heat > 0.45f ? new Color32(230, 70, 30, 255) : new Color32(150, 20, 34, 255);
                }
                case LegendaryFx.Galaxy:
                {
                    if (Px.Hash(x, y, frame + 100) > 0.95f) return new Color32(255, 255, 255, 255);
                    if (Px.Hash(x, y, 7) > 0.96f) return new Color32(255, 190, 255, 255);
                    float k = 0.5f + 0.5f * Mathf.Sin(Tau * (x / 12f + y / 16f + t));
                    return Color32.Lerp(new Color32(36, 18, 92, 255), new Color32(150, 50, 190, 255), k);
                }
                case LegendaryFx.Aurora:
                {
                    float wave = Mathf.Sin(Tau * (x / 14f + t) + y / 5f);
                    if (wave > 0.85f) return new Color32(255, 130, 220, 255);
                    float glow = 0.5f + 0.5f * Mathf.Sin(Tau * (y / 9f - t));
                    return Hsv(0.36f + 0.12f * wave, 0.75f, 0.45f + 0.55f * glow);
                }
                case LegendaryFx.Gold:
                {
                    float band = (x + y) / 24f - t;
                    band -= Mathf.Floor(band);
                    return band < 0.1f ? new Color32(255, 252, 220, 255) : band < 0.2f ? new Color32(255, 226, 120, 255)
                         : Color32.Lerp(new Color32(240, 186, 50, 255), new Color32(186, 124, 30, 255), y / 40f);
                }
                case LegendaryFx.Storm:
                {
                    bool strike = frame % 4 < 2;
                    int cx = 16 + Mathf.RoundToInt(Mathf.Sin(y * 0.9f + frame * 2f) * 3f);
                    if (strike && x == cx) return new Color32(235, 250, 255, 255);
                    if (strike && Mathf.Abs(x - cx) == 1) return new Color32(120, 190, 255, 255);
                    if (Px.Hash(x, y, frame) > 0.93f) return new Color32(90, 150, 255, 255);
                    return strike ? new Color32(44, 58, 150, 255) : new Color32(28, 36, 104, 255);
                }
                case LegendaryFx.Spectre:
                {
                    if (Px.Hash(x, y + frame * 3, 31) > 0.93f) return new Color32(225, 255, 250, 255);
                    float k = 0.5f + 0.5f * Mathf.Sin(Tau * (t + y / 20f));
                    return Color32.Lerp(new Color32(16, 86, 90, 255), new Color32(140, 255, 226, 255), k);
                }
                case LegendaryFx.Neon:
                {
                    int stripe = ((y + frame) / 2) % 3;
                    return stripe == 0 ? new Color32(255, 60, 200, 255) : stripe == 1 ? new Color32(60, 240, 255, 255) : new Color32(48, 20, 72, 255);
                }
                case LegendaryFx.Prism:
                    if (Px.Hash(x, y, frame + 40) > 0.95f) return new Color32(255, 255, 255, 255);
                    return Hsv((x - y) / 20f + t, 0.35f, 1f);
                default: // Magma
                {
                    float flow = Mathf.Sin(x * 0.8f + Tau * t) + Mathf.Sin(y * 0.55f - Tau * t);
                    return flow > 1.3f ? new Color32(255, 222, 90, 255) : flow > 0.85f ? new Color32(255, 120, 30, 255)
                         : flow > 0.45f ? new Color32(180, 40, 20, 255) : new Color32(42, 20, 22, 255);
                }
            }
        }

        static Px Layer(System.Action<Px> paint)
        {
            var l = new Px(32, MummyHeight);
            l.Fill(Clear);
            paint(l);
            return l;
        }

        static void Ears(Px p, MummyShape shape, SkinDef c)
        {
            if (shape == MummyShape.Cat)
            {
                p.Poly(c.Bandage, new Vector2(9.5f, 25), new Vector2(15, 27.5f), new Vector2(10.5f, 33));
                p.Poly(c.Bandage, new Vector2(22.5f, 25), new Vector2(17, 27.5f), new Vector2(21.5f, 33));
                p.Poly(new Color32(230, 140, 150, 255), new Vector2(10.6f, 27), new Vector2(13, 28.2f), new Vector2(11.2f, 31));
                p.Poly(new Color32(230, 140, 150, 255), new Vector2(21.4f, 27), new Vector2(19, 28.2f), new Vector2(20.8f, 31));
            }
            else if (shape == MummyShape.Jackal)
            {
                p.Poly(c.Bandage, new Vector2(10, 26), new Vector2(14, 28), new Vector2(11, 36.5f));
                p.Poly(c.Bandage, new Vector2(22, 26), new Vector2(18, 28), new Vector2(21, 36.5f));
                p.Line(11, 28, 11, 34, c.Shadow); p.Line(21, 28, 21, 34, c.Shadow);
            }
        }

        static bool IsBody(Px p, int x, int y) => p.Get(x, y).a == 255;

        /// <summary>Texture of the act-themed mummies, painted over the bandages in the chosen colours.</summary>
        static void Texture(Px p, BandagePattern pattern)
        {
            switch (pattern)
            {
                case BandagePattern.Hieroglyphs:
                {
                    // Gold glyphs on the chest and legs (eye of Horus, ankh, wave, feather), a broad collar.
                    var ink = new Color32(214, 170, 60, 255);
                    p.Rect(11, 18, 21, 18, Gold); p.Rect(12, 17, 20, 17, Lapis);
                    for (int x = 12; x <= 20; x += 2) p.Set(x, 17, Turquoise);
                    p.Rect(12, 13, 14, 13, ink); p.Set(13, 12, ink); p.Set(12, 11, ink);          // eye
                    p.Rect(18, 10, 18, 14, ink); p.Rect(17, 12, 19, 12, ink); p.Set(18, 15, ink);  // ankh
                    p.Set(12, 8, ink); p.Set(13, 9, ink); p.Set(14, 8, ink); p.Set(15, 9, ink);    // water
                    p.Rect(19, 6, 19, 8, ink); p.Set(20, 8, ink);                                  // feather
                    p.Set(13, 4, ink); p.Set(18, 4, ink);
                    break;
                }
                case BandagePattern.Moss:
                {
                    var moss = new Color32(78, 128, 64, 255);
                    var mossDark = new Color32(44, 84, 46, 255);
                    for (int y = 2; y < 30; y++)
                        for (int x = 5; x < 27; x++)
                        {
                            if (!IsBody(p, x, y)) continue;
                            float h = Px.Hash(x / 2, y / 2, 21);
                            if (h < 0.22f) p.Set(x, y, Px.Hash(x, y, 4) < 0.5f ? moss : mossDark);
                        }
                    // Drips running down the bandages.
                    var water = new Color32(110, 190, 210, 255);
                    p.Rect(11, 6, 11, 9, mossDark); p.Set(11, 5, water);
                    p.Rect(20, 9, 20, 12, mossDark); p.Set(20, 8, water);
                    p.Rect(15, 4, 15, 6, mossDark);
                    p.Set(7, 12, water);
                    break;
                }
                case BandagePattern.Stone:
                {
                    // Turned to stone: greyed, grainy, cracked.
                    var crack = new Color32(46, 44, 42, 255);
                    for (int y = 2; y < 30; y++)
                        for (int x = 5; x < 27; x++)
                        {
                            if (!IsBody(p, x, y)) continue;
                            var g = p.Get(x, y);
                            byte l = (byte)((g.r + g.g + g.b) / 3);
                            var grey = new Color32(l, l, (byte)Mathf.Min(255, l + 6), 255);
                            p.Set(x, y, Px.Shade(Px.Lerp(g, grey, 0.75f), (Px.Hash(x, y, 9) - 0.5f) * 0.18f));
                        }
                    p.Line(12, 16, 14, 12, crack); p.Line(14, 12, 13, 9, crack);
                    p.Line(19, 7, 21, 4, crack); p.Line(17, 28, 19, 25, crack);
                    p.Set(8, 15, crack);
                    break;
                }
                case BandagePattern.Circuit:
                {
                    // Cybernetic: metal plates and glowing circuit traces.
                    var plate = new Color32(120, 130, 146, 255);
                    var trace = new Color32(90, 240, 255, 255);
                    var node = new Color32(220, 255, 255, 255);
                    p.Rect(12, 8, 19, 11, plate);
                    p.Rect(12, 8, 19, 8, Px.Shade(plate, -0.3f));
                    p.Line(11, 13, 15, 13, trace); p.Line(15, 13, 15, 10, trace); p.Line(15, 10, 18, 10, trace);
                    p.Line(20, 15, 20, 12, trace); p.Line(20, 12, 17, 12, trace);
                    p.Line(13, 6, 13, 4, trace); p.Line(18, 7, 18, 4, trace);
                    p.Line(12, 27, 15, 27, trace);
                    p.Set(15, 13, node); p.Set(18, 10, node); p.Set(17, 12, node); p.Set(13, 4, node); p.Set(15, 27, node);
                    p.Set(7, 14, trace); p.Set(8, 14, trace);
                    break;
                }
                case BandagePattern.Lava:
                {
                    // Basalt bandages split by glowing cracks.
                    var hot = new Color32(255, 120, 30, 255);
                    var core = new Color32(255, 220, 90, 255);
                    for (int y = 2; y < 30; y++)
                        for (int x = 5; x < 27; x++)
                            if (IsBody(p, x, y)) p.Set(x, y, Px.Lerp(p.Get(x, y), new Color32(34, 28, 30, 255), 0.72f));
                    p.Line(11, 4, 13, 9, hot); p.Line(13, 9, 12, 13, hot); p.Line(12, 13, 15, 17, hot);
                    p.Line(20, 5, 18, 10, hot); p.Line(18, 10, 20, 14, hot);
                    p.Line(13, 27, 16, 26, hot); p.Line(16, 26, 18, 28, hot);
                    p.Set(13, 9, core); p.Set(18, 10, core); p.Set(16, 26, core); p.Set(12, 13, core);
                    p.Set(7, 15, hot);
                    break;
                }
            }
        }

        static void PaintShoes(Px p, ShoeStyle style)
        {
            switch (style)
            {
                case ShoeStyle.Slippers:
                {
                    var red = new Color32(190, 52, 48, 255);
                    p.Rect(11, 2, 14, 3, red); p.Rect(17, 2, 20, 3, red);
                    p.Set(10, 3, red); p.Set(10, 4, red); p.Set(21, 3, red); p.Set(21, 4, red); // curled toes
                    p.Rect(12, 3, 14, 3, Gold); p.Rect(17, 3, 19, 3, Gold);
                    break;
                }
                case ShoeStyle.GoldSandals:
                    p.Rect(11, 1, 15, 1, GoldDark); p.Rect(16, 1, 20, 1, GoldDark);
                    p.Rect(12, 2, 14, 2, Gold); p.Rect(17, 2, 19, 2, Gold);
                    p.Rect(12, 4, 14, 4, Gold); p.Rect(17, 4, 19, 4, Gold);
                    p.Set(13, 3, Gold); p.Set(18, 3, Gold); p.Set(13, 5, Turquoise); p.Set(18, 5, Turquoise);
                    break;
                case ShoeStyle.MudBoots:
                {
                    var mud = new Color32(84, 76, 48, 255);
                    var wet = new Color32(56, 70, 44, 255);
                    p.Rect(11, 2, 14, 7, mud); p.Rect(17, 2, 20, 7, mud);
                    p.Rect(11, 7, 14, 7, wet); p.Rect(17, 7, 20, 7, wet);
                    p.Set(12, 5, wet); p.Set(19, 4, wet); p.Set(11, 2, wet); p.Set(20, 2, wet);
                    p.Set(13, 1, wet); p.Set(18, 1, new Color32(110, 190, 210, 255));
                    break;
                }
                case ShoeStyle.StoneBoots:
                {
                    var stone = new Color32(128, 122, 114, 255);
                    var dark = new Color32(74, 70, 66, 255);
                    p.Rect(10, 1, 15, 6, stone); p.Rect(16, 1, 21, 6, stone);
                    p.Rect(10, 1, 15, 1, dark); p.Rect(16, 1, 21, 1, dark);
                    p.Rect(10, 6, 15, 6, Px.Shade(stone, 0.2f)); p.Rect(16, 6, 21, 6, Px.Shade(stone, 0.2f));
                    p.Line(12, 5, 13, 3, dark); p.Line(19, 5, 18, 2, dark);
                    break;
                }
                case ShoeStyle.JetBoots:
                {
                    var metal = Metal;
                    p.Rect(11, 2, 15, 6, metal); p.Rect(16, 2, 20, 6, metal);
                    p.Rect(11, 2, 15, 2, MetalDark); p.Rect(16, 2, 20, 2, MetalDark);
                    p.Rect(11, 5, 15, 5, new Color32(90, 240, 255, 255)); p.Rect(16, 5, 20, 5, new Color32(90, 240, 255, 255));
                    // Thruster flames under the soles.
                    p.Rect(12, 1, 14, 1, new Color32(160, 250, 255, 255)); p.Rect(17, 1, 19, 1, new Color32(160, 250, 255, 255));
                    p.Set(13, 0, new Color32(60, 160, 255, 255)); p.Set(18, 0, new Color32(60, 160, 255, 255));
                    break;
                }
                case ShoeStyle.ObsidianHooves:
                {
                    var obsidian = new Color32(26, 20, 30, 255);
                    var sheen = new Color32(120, 80, 150, 255);
                    p.Rect(11, 1, 15, 5, obsidian); p.Rect(16, 1, 20, 5, obsidian);
                    p.Rect(13, 1, 13, 2, Clear); p.Rect(18, 1, 18, 2, Clear); // split hooves
                    p.Set(12, 4, sheen); p.Set(17, 4, sheen);
                    p.Rect(11, 5, 15, 5, new Color32(255, 110, 30, 255)); p.Rect(16, 5, 20, 5, new Color32(255, 110, 30, 255));
                    break;
                }
                case ShoeStyle.PapyrusSandals:
                {
                    var straw = new Color32(214, 186, 116, 255);
                    var strawDark = new Color32(150, 124, 70, 255);
                    p.Rect(11, 1, 15, 1, straw); p.Rect(16, 1, 20, 1, straw);
                    p.Rect(11, 0, 15, 0, strawDark); p.Rect(16, 0, 20, 0, strawDark);
                    p.Rect(12, 3, 14, 3, strawDark); p.Rect(17, 3, 19, 3, strawDark);
                    p.Set(13, 2, straw); p.Set(18, 2, straw); p.Set(13, 5, strawDark); p.Set(18, 5, strawDark);
                    break;
                }
                case ShoeStyle.LapisSandals:
                    p.Rect(11, 1, 15, 1, Gold); p.Rect(16, 1, 20, 1, Gold);
                    p.Rect(11, 0, 15, 0, GoldDark); p.Rect(16, 0, 20, 0, GoldDark);
                    p.Rect(12, 2, 14, 2, Lapis); p.Rect(17, 2, 19, 2, Lapis);
                    p.Rect(12, 4, 14, 4, Lapis); p.Rect(17, 4, 19, 4, Lapis);
                    p.Set(13, 3, Lapis); p.Set(18, 3, Lapis); p.Set(13, 5, Gold); p.Set(18, 5, Gold);
                    break;
                case ShoeStyle.CrocBoots:
                {
                    var croc = new Color32(74, 124, 62, 255);
                    var scale = new Color32(44, 84, 40, 255);
                    var belly = new Color32(176, 190, 120, 255);
                    p.Rect(10, 1, 15, 6, croc); p.Rect(16, 1, 21, 6, croc);
                    for (int y = 2; y <= 5; y++)
                        for (int x = 10; x <= 21; x++)
                            if ((x + y) % 3 == 0) p.Set(x, y, scale);
                    p.Rect(10, 1, 15, 1, belly); p.Rect(16, 1, 21, 1, belly);
                    for (int x = 10; x <= 21; x += 2) p.Set(x, 7, new Color32(244, 240, 220, 255)); // teeth on the cuff
                    p.Set(11, 3, Gold); p.Set(20, 3, Gold); // eyes on the toes
                    break;
                }
                case ShoeStyle.WingedSandals:
                {
                    var wing = new Color32(246, 244, 236, 255);
                    var wingDark = new Color32(180, 176, 170, 255);
                    p.Rect(11, 1, 15, 1, Gold); p.Rect(16, 1, 20, 1, Gold);
                    p.Rect(12, 3, 14, 3, GoldDark); p.Rect(17, 3, 19, 3, GoldDark);
                    // Little wings at the ankles.
                    p.Rect(9, 5, 11, 5, wing); p.Rect(8, 6, 10, 6, wing); p.Set(7, 7, wing); p.Rect(9, 4, 11, 4, wingDark);
                    p.Rect(20, 5, 22, 5, wing); p.Rect(21, 6, 23, 6, wing); p.Set(24, 7, wing); p.Rect(20, 4, 22, 4, wingDark);
                    break;
                }
                case ShoeStyle.ScarabClogs:
                {
                    var shell = new Color32(40, 140, 150, 255);
                    var shellDark = new Color32(20, 80, 100, 255);
                    var sheen = new Color32(140, 230, 220, 255);
                    p.Rect(11, 1, 15, 4, shell); p.Rect(16, 1, 20, 4, shell);
                    p.Rect(11, 0, 15, 0, shellDark); p.Rect(16, 0, 20, 0, shellDark);
                    p.Rect(13, 1, 13, 4, shellDark); p.Rect(18, 1, 18, 4, shellDark); // wing case seam
                    p.Set(12, 3, sheen); p.Set(17, 3, sheen); p.Rect(12, 5, 14, 5, Gold); p.Rect(17, 5, 19, 5, Gold);
                    break;
                }
                case ShoeStyle.GuildGreaves:
                    // Royal purple plates with gold rims and a gem on each knee (guild reward).
                    p.Rect(11, 1, 15, 2, GuildGold); p.Rect(16, 1, 20, 2, GuildGold);
                    p.Rect(12, 3, 14, 7, GuildPurple); p.Rect(17, 3, 19, 7, GuildPurple);
                    p.Rect(14, 3, 14, 7, GuildPurpleDark); p.Rect(19, 3, 19, 7, GuildPurpleDark);
                    p.Rect(12, 7, 14, 7, GuildGold); p.Rect(17, 7, 19, 7, GuildGold);
                    p.Set(13, 5, GuildGem); p.Set(18, 5, GuildGem);
                    break;
                case ShoeStyle.SilverGreaves:
                    p.Rect(11, 1, 15, 2, MetalDark); p.Rect(16, 1, 20, 2, MetalDark);
                    p.Rect(12, 3, 14, 7, Metal); p.Rect(17, 3, 19, 7, Metal);
                    p.Rect(14, 3, 14, 7, MetalDark); p.Rect(19, 3, 19, 7, MetalDark);
                    p.Rect(12, 5, 14, 5, MetalDark); p.Rect(17, 5, 19, 5, MetalDark);
                    p.Set(13, 6, Turquoise); p.Set(18, 6, Turquoise);
                    break;
                case ShoeStyle.DesertBoots:
                {
                    var leather = new Color32(150, 100, 60, 255);
                    var leatherDark = new Color32(96, 60, 34, 255);
                    var lace = new Color32(236, 220, 180, 255);
                    p.Rect(11, 1, 15, 6, leather); p.Rect(16, 1, 20, 6, leather);
                    p.Rect(11, 0, 15, 0, leatherDark); p.Rect(16, 0, 20, 0, leatherDark);
                    p.Rect(11, 6, 15, 6, leatherDark); p.Rect(16, 6, 20, 6, leatherDark);
                    p.Set(13, 2, lace); p.Set(13, 4, lace); p.Set(18, 2, lace); p.Set(18, 4, lace);
                    p.Set(12, 3, lace); p.Set(14, 3, lace); p.Set(17, 3, lace); p.Set(19, 3, lace);
                    break;
                }
                case ShoeStyle.RubyPointed:
                {
                    var ruby = new Color32(178, 30, 60, 255);
                    var rubyDark = new Color32(110, 14, 34, 255);
                    p.Rect(11, 1, 14, 4, ruby); p.Rect(17, 1, 20, 4, ruby);
                    p.Rect(11, 1, 14, 1, rubyDark); p.Rect(17, 1, 20, 1, rubyDark);
                    // Long curled tips.
                    p.Rect(9, 2, 10, 2, ruby); p.Set(8, 3, ruby); p.Set(8, 4, ruby); p.Set(9, 5, Gold);
                    p.Rect(21, 2, 22, 2, ruby); p.Set(23, 3, ruby); p.Set(23, 4, ruby); p.Set(22, 5, Gold);
                    p.Set(13, 4, Gold); p.Set(18, 4, Gold);
                    break;
                }
                case ShoeStyle.LotusSlippers:
                {
                    var pink = new Color32(236, 150, 180, 255);
                    var pinkDark = new Color32(180, 90, 120, 255);
                    var white = new Color32(252, 238, 244, 255);
                    p.Rect(11, 1, 15, 3, pink); p.Rect(16, 1, 20, 3, pink);
                    p.Rect(11, 0, 15, 0, pinkDark); p.Rect(16, 0, 20, 0, pinkDark);
                    for (int x = 11; x <= 20; x += 2) p.Set(x, 4, pink); // petal tips
                    p.Set(13, 2, white); p.Set(18, 2, white); p.Set(12, 4, new Color32(90, 160, 80, 255)); p.Set(19, 4, new Color32(90, 160, 80, 255));
                    break;
                }
            }
        }

        static void PaintHat(Px p, HatStyle style)
        {
            switch (style)
            {
                case HatStyle.Explorer:
                {
                    var khaki = new Color32(206, 186, 132, 255);
                    var khakiDark = new Color32(150, 132, 88, 255);
                    p.Ellipse(16, 28, 6, 5, khaki);
                    p.Rect(10, 22, 22, 27, Clear); // keep the face: the dome only covers the crown
                    p.Rect(7, 26, 25, 27, khaki);
                    p.Rect(7, 26, 25, 26, khakiDark);
                    p.Rect(10, 28, 22, 28, new Color32(110, 70, 40, 255)); // band
                    p.Set(14, 32, Px.Shade(khaki, 0.15f)); p.Set(15, 33, Px.Shade(khaki, 0.15f));
                    p.Set(16, 34, khakiDark);
                    break;
                }
                case HatStyle.Nemes:
                {
                    // Striped headcloth: crown, lappets down to the shoulders, cobra on the brow.
                    for (int y = 15; y <= 31; y++)
                        for (int x = 8; x <= 24; x++)
                        {
                            bool crown = y >= 25 && (x - 16) * (x - 16) * 25 + (y - 25) * (y - 25) * 64 <= 25 * 64 + 40;
                            bool lappet = y <= 25 && (x <= 10 || x >= 22) && x >= 8 && x <= 24;
                            if (crown || lappet) p.Set(x, y, (y % 2 == 0) ? Gold : Lapis);
                        }
                    p.Rect(10, 25, 22, 25, Gold);
                    p.Rect(16, 26, 16, 29, Gold); p.Rect(15, 29, 17, 29, Gold); p.Set(16, 30, new Color32(200, 40, 40, 255));
                    break;
                }
                case HatStyle.Lotus:
                {
                    var teal = new Color32(40, 150, 140, 255);
                    var pink = new Color32(236, 150, 180, 255);
                    var white = new Color32(250, 236, 240, 255);
                    var leaf = new Color32(70, 150, 70, 255);
                    p.Rect(10, 27, 22, 28, teal);
                    for (int x = 11; x <= 21; x += 2) p.Set(x, 28, Gold);
                    // Petals: the middle one tallest.
                    p.Poly(pink, new Vector2(13.5f, 28.5f), new Vector2(18.5f, 28.5f), new Vector2(16, 35.5f));
                    p.Poly(white, new Vector2(14.8f, 29.5f), new Vector2(17.2f, 29.5f), new Vector2(16, 33.5f));
                    p.Poly(pink, new Vector2(10.5f, 28.5f), new Vector2(14.5f, 28.5f), new Vector2(11, 33));
                    p.Poly(pink, new Vector2(17.5f, 28.5f), new Vector2(21.5f, 28.5f), new Vector2(21, 33));
                    p.Set(9, 29, leaf); p.Set(8, 30, leaf); p.Set(23, 29, leaf); p.Set(24, 30, leaf);
                    break;
                }
                case HatStyle.BrokenHelm:
                {
                    var bronze = new Color32(176, 120, 60, 255);
                    var bronzeDark = new Color32(112, 72, 36, 255);
                    var verdigris = new Color32(90, 170, 140, 255);
                    p.Ellipse(16, 26, 7, 6, bronze);
                    p.Rect(11, 20, 21, 25, Clear); // open face
                    p.Rect(9, 19, 10, 25, bronze); p.Rect(22, 19, 23, 25, bronze); // cheek guards
                    p.Rect(9, 25, 23, 25, bronzeDark);
                    p.Rect(15, 26, 17, 32, bronzeDark); p.Rect(16, 26, 16, 32, Px.Shade(bronze, 0.25f)); // crest
                    // The broken corner: bandages show through.
                    p.Rect(20, 28, 23, 31, Clear);
                    p.Line(19, 31, 21, 27, bronzeDark); p.Set(22, 27, bronzeDark);
                    p.Set(11, 28, verdigris); p.Set(12, 29, verdigris); p.Set(13, 27, verdigris); p.Set(9, 22, verdigris);
                    break;
                }
                case HatStyle.CyberEars:
                {
                    var black = new Color32(28, 28, 36, 255);
                    var glow = new Color32(90, 240, 255, 255);
                    p.Poly(black, new Vector2(9.5f, 26), new Vector2(14.5f, 27.5f), new Vector2(10.5f, 38.5f));
                    p.Poly(black, new Vector2(22.5f, 26), new Vector2(17.5f, 27.5f), new Vector2(21.5f, 38.5f));
                    p.Line(11, 28, 11, 35, glow); p.Line(21, 28, 21, 35, glow);
                    p.Rect(10, 26, 22, 27, MetalDark); p.Rect(10, 27, 22, 27, Metal);
                    p.Set(16, 26, glow); p.Set(13, 26, Gold); p.Set(19, 26, Gold);
                    break;
                }
                case HatStyle.SunDisk:
                {
                    var sun = new Color32(230, 70, 40, 255);
                    var sunLight = new Color32(255, 150, 60, 255);
                    p.Rect(10, 27, 22, 28, Gold);
                    p.Circle(16, 34, 5, Gold, true);
                    p.Circle(16, 34, 4, sun, true);
                    p.Rect(14, 35, 15, 37, sunLight);
                    // Two cobras rising on each side of the disk.
                    p.Rect(10, 29, 10, 33, Gold); p.Set(11, 33, Gold); p.Set(10, 34, new Color32(200, 40, 40, 255));
                    p.Rect(22, 29, 22, 33, Gold); p.Set(21, 33, Gold); p.Set(22, 34, new Color32(200, 40, 40, 255));
                    break;
                }
                case HatStyle.GuildCrown:
                {
                    // Crenellated gold crown on a purple band, a gem on each point (guild reward).
                    p.Rect(10, 26, 22, 29, GuildPurple);
                    p.Rect(10, 26, 22, 26, GuildPurpleDark);
                    p.Rect(10, 29, 22, 30, GuildGold);
                    for (int x = 10; x <= 22; x += 3)
                    {
                        p.Rect(x, 31, x, 33, GuildGold);
                        p.Set(x, 34, GuildGem);
                    }
                    p.Rect(15, 27, 17, 28, GuildGold);
                    p.Set(16, 27, GuildGem);
                    break;
                }
                case HatStyle.Pschent:
                {
                    // Double crown: red deshret around the white hedjet.
                    var red = new Color32(176, 40, 40, 255);
                    var redDark = new Color32(110, 20, 24, 255);
                    var white = new Color32(244, 238, 226, 255);
                    var whiteDark = new Color32(190, 180, 164, 255);
                    p.Ellipse(15, 33, 4, 5, white);
                    p.Rect(13, 34, 13, 37, whiteDark); p.Circle(15, 38, 1, white, true);
                    p.Rect(10, 26, 22, 30, red); p.Rect(10, 26, 22, 26, redDark);
                    p.Rect(20, 30, 22, 35, red); p.Rect(22, 30, 22, 35, redDark); // tall back of the red crown
                    p.Line(12, 30, 10, 33, Gold); p.Set(11, 34, Gold); // the curl
                    p.Rect(16, 27, 16, 29, Gold); p.Set(16, 30, Gold);
                    break;
                }
                case HatStyle.Khepresh:
                {
                    var blue = new Color32(44, 74, 170, 255);
                    p.Ellipse(16, 28, 7, 7, blue);
                    p.Rect(8, 19, 24, 25, Clear);
                    for (int y = 27; y <= 34; y += 2)
                        for (int x = 10 + (y / 2) % 2; x <= 22; x += 2)
                            if (p.Get(x, y).a > 0) p.Set(x, y, Gold);
                    p.Rect(9, 26, 23, 26, Gold);
                    p.Rect(16, 27, 16, 29, Gold); p.Set(15, 29, Gold); p.Set(16, 30, new Color32(200, 40, 40, 255));
                    break;
                }
                case HatStyle.Atef:
                {
                    var white = new Color32(244, 238, 226, 255);
                    var feather = new Color32(214, 222, 236, 255);
                    p.Poly(white, new Vector2(12.5f, 26.5f), new Vector2(19.5f, 26.5f), new Vector2(16, 37.5f));
                    p.Rect(10, 28, 11, 36, feather); p.Rect(21, 28, 22, 36, feather);
                    p.Rect(10, 30, 11, 30, Lapis); p.Rect(21, 30, 22, 30, Lapis); p.Rect(10, 33, 11, 33, Lapis); p.Rect(21, 33, 22, 33, Lapis);
                    p.Rect(6, 27, 26, 27, Gold); p.Set(6, 28, Gold); p.Set(26, 28, Gold); // ram horns
                    p.Rect(10, 26, 22, 26, GoldDark);
                    p.Set(16, 38, new Color32(200, 40, 40, 255));
                    break;
                }
                case HatStyle.MaatFeather:
                {
                    var white = new Color32(248, 246, 240, 255);
                    var quill = new Color32(170, 166, 160, 255);
                    p.Rect(10, 27, 22, 28, Gold); p.Rect(10, 27, 22, 27, Lapis);
                    p.Rect(15, 29, 17, 37, white); p.Rect(16, 29, 16, 36, quill);
                    p.Set(16, 38, white); p.Set(17, 38, white); p.Set(18, 37, white); // the tip bends over
                    break;
                }
                case HatStyle.Vulture:
                {
                    p.Ellipse(16, 27, 7, 5, Gold);
                    p.Rect(8, 19, 24, 25, Clear);
                    // Wings folded down the sides of the head.
                    for (int y = 17; y <= 27; y++)
                    {
                        var c = y % 2 == 0 ? Gold : Lapis;
                        p.Rect(8, y, 10, y, c); p.Rect(22, y, 24, y, c);
                    }
                    p.Rect(9, 26, 23, 26, GoldDark);
                    p.Rect(15, 27, 17, 30, Gold); p.Set(16, 31, Gold); p.Set(15, 29, Black); p.Set(17, 29, Black); // head at the brow
                    p.Set(16, 27, new Color32(200, 40, 40, 255));
                    break;
                }
                case HatStyle.HathorHorns:
                {
                    var ivory = new Color32(240, 230, 200, 255);
                    var ivoryDark = new Color32(180, 166, 130, 255);
                    p.Rect(10, 27, 22, 28, Gold);
                    p.Line(12, 29, 9, 31, ivory); p.Line(9, 31, 9, 34, ivory); p.Line(9, 34, 11, 37, ivory); p.Line(10, 31, 10, 34, ivoryDark);
                    p.Line(20, 29, 23, 31, ivory); p.Line(23, 31, 23, 34, ivory); p.Line(23, 34, 21, 37, ivory); p.Line(22, 31, 22, 34, ivoryDark);
                    p.Circle(16, 33, 3, new Color32(210, 50, 40, 255), true);
                    p.Set(15, 34, new Color32(255, 140, 80, 255));
                    break;
                }
                case HatStyle.ScarabCirclet:
                    p.Rect(10, 27, 22, 27, Gold); p.Rect(10, 26, 22, 26, GoldDark);
                    p.Ellipse(16, 29, 2, 2, Turquoise); p.Set(16, 32, Turquoise);
                    p.Rect(16, 28, 16, 30, Lapis);
                    p.Line(13, 29, 11, 31, Gold); p.Line(19, 29, 21, 31, Gold); // wings
                    break;
                case HatStyle.Turban:
                {
                    var cream = new Color32(240, 226, 196, 255);
                    var orange = new Color32(222, 130, 50, 255);
                    p.Ellipse(16, 29, 8, 5, cream);
                    p.Rect(8, 22, 24, 25, Clear);
                    for (int y = 26; y <= 34; y++)
                        for (int x = 8; x <= 24; x++)
                            if ((x + y) % 4 == 0 && p.Get(x, y).a > 0) p.Set(x, y, orange);
                    p.Circle(16, 29, 1, new Color32(200, 30, 60, 255), true); p.Set(16, 29, new Color32(255, 140, 160, 255));
                    p.Line(16, 31, 18, 37, new Color32(250, 250, 250, 255)); p.Set(19, 37, new Color32(250, 250, 250, 255));
                    break;
                }
                case HatStyle.Nefertiti:
                {
                    var blue = new Color32(50, 84, 168, 255);
                    var blueDark = new Color32(28, 50, 110, 255);
                    p.Poly(blue, new Vector2(10.5f, 26), new Vector2(21.5f, 26), new Vector2(23.5f, 38.5f), new Vector2(8.5f, 38.5f));
                    p.Rect(9, 38, 23, 38, blueDark);
                    p.Rect(10, 26, 22, 27, Gold);
                    for (int x = 11; x <= 21; x += 3) p.Set(x, 27, x % 2 == 0 ? new Color32(200, 40, 40, 255) : Turquoise);
                    p.Line(10, 31, 22, 35, Gold); // the ribbon
                    p.Rect(16, 28, 16, 29, Gold); p.Set(16, 30, new Color32(200, 40, 40, 255));
                    break;
                }
            }
        }

        static readonly Color32 GuildPurple = new Color32(122, 70, 176, 255);
        static readonly Color32 GuildPurpleDark = new Color32(70, 36, 112, 255);
        static readonly Color32 GuildGold = new Color32(255, 206, 84, 255);
        static readonly Color32 GuildGem = new Color32(230, 160, 255, 255);

        /// <summary>Torch handle and cup in the right hand. Every style holds its flame in the same spot.</summary>
        static void PaintTorch(Px p, TorchStyle style)
        {
            switch (style)
            {
                case TorchStyle.GuildBanner:
                    // A gold staff flying the guild's purple pennant (guild reward).
                    p.Rect(25, 9, 26, 20, GuildGold); p.Rect(26, 9, 26, 20, GuildPurpleDark);
                    p.Rect(27, 13, 30, 19, GuildPurple);
                    p.Rect(27, 13, 30, 13, GuildPurpleDark);
                    p.Set(30, 14, Clear); p.Set(30, 18, Clear); // swallowtail
                    p.Set(28, 16, GuildGold); p.Set(29, 16, GuildGold); p.Set(28, 17, GuildGem);
                    p.Rect(23, 21, 28, 22, GuildGold); p.Rect(23, 21, 28, 21, GuildPurpleDark);
                    break;
                case TorchStyle.Scepter:
                    p.Rect(25, 9, 26, 20, Gold); p.Rect(26, 9, 26, 20, GoldDark);
                    p.Rect(25, 11, 26, 11, Lapis); p.Rect(25, 18, 26, 18, Lapis);
                    p.Rect(24, 8, 27, 8, GoldDark); // foot
                    p.Rect(23, 21, 28, 22, Gold); p.Rect(23, 21, 28, 21, GoldDark);
                    p.Set(25, 21, Turquoise); p.Set(26, 21, Turquoise);
                    break;
                case TorchStyle.Lantern:
                {
                    var nacre = new Color32(222, 236, 240, 255);
                    var nacreDark = new Color32(150, 170, 184, 255);
                    p.Rect(25, 9, 26, 20, nacreDark); p.Rect(25, 9, 25, 20, nacre);
                    p.Rect(23, 21, 28, 22, nacre); p.Rect(23, 21, 28, 21, nacreDark);
                    // Glass cage around the flame.
                    p.Rect(22, 22, 22, 29, nacreDark); p.Rect(29, 22, 29, 29, nacreDark);
                    p.Rect(22, 30, 29, 30, nacre); p.Rect(24, 31, 27, 31, nacreDark); p.Set(25, 32, nacre); p.Set(26, 32, nacre);
                    break;
                }
                case TorchStyle.Bone:
                {
                    var bone = new Color32(232, 224, 200, 255);
                    var boneDark = new Color32(170, 160, 132, 255);
                    p.Rect(25, 10, 26, 20, bone); p.Rect(26, 10, 26, 20, boneDark);
                    p.Rect(24, 8, 25, 9, bone); p.Rect(26, 8, 27, 9, boneDark); // knuckle
                    // Little skull as the cup.
                    p.Rect(23, 19, 28, 22, bone); p.Rect(23, 19, 28, 19, boneDark);
                    p.Set(24, 21, Black); p.Set(27, 21, Black); p.Set(25, 20, boneDark); p.Set(26, 20, boneDark);
                    break;
                }
                case TorchStyle.Plasma:
                {
                    var glow = new Color32(90, 240, 255, 255);
                    p.Rect(25, 9, 26, 20, MetalDark); p.Rect(25, 9, 25, 20, Metal);
                    p.Rect(25, 11, 26, 11, glow); p.Rect(25, 18, 26, 18, glow);
                    p.Rect(23, 20, 28, 21, Metal);
                    p.Rect(23, 22, 23, 24, Metal); p.Rect(28, 22, 28, 24, Metal); // emitter prongs
                    p.Set(23, 25, glow); p.Set(28, 25, glow);
                    break;
                }
                case TorchStyle.Obsidian:
                {
                    var obsidian = new Color32(30, 22, 34, 255);
                    var sheen = new Color32(130, 80, 160, 255);
                    var hot = new Color32(255, 110, 30, 255);
                    p.Rect(25, 9, 26, 20, obsidian); p.Rect(25, 12, 25, 16, sheen);
                    p.Set(26, 18, hot); p.Set(26, 11, hot);
                    p.Rect(23, 21, 28, 22, obsidian);
                    p.Set(23, 23, obsidian); p.Set(28, 23, obsidian); p.Set(22, 22, obsidian); p.Set(29, 22, obsidian); // jagged rim
                    p.Rect(24, 21, 27, 21, hot);
                    break;
                }
                case TorchStyle.Ankh:
                    p.Rect(25, 9, 26, 17, Gold); p.Rect(26, 9, 26, 17, GoldDark);
                    p.Rect(22, 18, 29, 18, Gold); p.Set(22, 17, GoldDark); p.Set(29, 17, GoldDark);
                    p.Rect(23, 19, 23, 22, Gold); p.Rect(28, 19, 28, 22, GoldDark); p.Rect(23, 22, 28, 22, Gold); // the loop holds the flame
                    p.Set(25, 18, Turquoise); p.Set(26, 18, Turquoise);
                    break;
                case TorchStyle.Was:
                {
                    var lapisDark = new Color32(30, 50, 110, 255);
                    p.Rect(25, 10, 26, 20, Lapis); p.Rect(26, 10, 26, 20, lapisDark);
                    p.Set(24, 9, Lapis); p.Set(27, 9, Lapis); p.Set(24, 8, lapisDark); p.Set(27, 8, lapisDark); // forked foot
                    p.Rect(25, 18, 26, 18, Gold); p.Rect(25, 11, 26, 11, Gold);
                    p.Rect(23, 21, 28, 22, Lapis); p.Rect(21, 22, 22, 22, Lapis); p.Set(28, 23, Lapis); // animal head
                    p.Set(26, 22, Gold);
                    break;
                }
                case TorchStyle.Crook:
                    for (int y = 9; y <= 20; y++) p.Rect(25, y, 26, y, (y / 2) % 2 == 0 ? Gold : Lapis);
                    p.Rect(23, 21, 28, 22, Gold); p.Rect(23, 21, 28, 21, GoldDark);
                    p.Rect(29, 22, 29, 26, Gold); p.Rect(30, 23, 30, 25, GoldDark); p.Set(28, 27, Gold); p.Set(27, 27, Lapis); // the hook
                    break;
                case TorchStyle.Papyrus:
                {
                    var stem = new Color32(96, 156, 64, 255);
                    var stemDark = new Color32(56, 100, 40, 255);
                    var tie = new Color32(206, 164, 92, 255);
                    p.Rect(25, 9, 26, 20, stem); p.Rect(26, 9, 26, 20, stemDark);
                    p.Rect(25, 18, 26, 18, tie);
                    p.Poly(stem, new Vector2(25.5f, 19.5f), new Vector2(21.5f, 23.5f), new Vector2(29.5f, 23.5f));
                    p.Rect(22, 22, 29, 22, new Color32(150, 204, 92, 255));
                    for (int x = 22; x <= 29; x += 2) p.Set(x, 23, tie);
                    break;
                }
                case TorchStyle.Cobra:
                {
                    var bronze = new Color32(176, 120, 60, 255);
                    var scale = new Color32(90, 150, 70, 255);
                    var red = new Color32(230, 40, 40, 255);
                    p.Rect(25, 9, 26, 19, bronze); p.Rect(26, 9, 26, 19, new Color32(112, 72, 36, 255));
                    p.Set(24, 10, scale); p.Set(27, 11, scale); p.Set(24, 18, scale); p.Set(27, 19, scale); // coiled around the staff
                    p.Rect(23, 20, 28, 22, scale); p.Rect(22, 21, 22, 22, scale); p.Rect(29, 21, 29, 22, scale); // the hood
                    p.Rect(24, 20, 27, 20, Gold);
                    p.Set(24, 22, red); p.Set(27, 22, red);
                    break;
                }
                case TorchStyle.Crystal:
                {
                    var violet = new Color32(150, 80, 220, 255);
                    var light = new Color32(224, 176, 255, 255);
                    p.Rect(25, 9, 26, 19, Metal); p.Rect(26, 9, 26, 19, MetalDark);
                    p.Poly(violet, new Vector2(25.5f, 18), new Vector2(22, 21.5f), new Vector2(29, 21.5f));
                    p.Rect(23, 21, 28, 22, violet);
                    p.Set(24, 22, light); p.Set(25, 21, light); p.Set(25, 20, light);
                    break;
                }
                case TorchStyle.OilLamp:
                {
                    var clay = new Color32(186, 96, 56, 255);
                    var clayDark = new Color32(126, 60, 34, 255);
                    p.Rect(25, 9, 26, 17, new Color32(116, 74, 38, 255)); p.Rect(26, 9, 26, 17, new Color32(78, 48, 24, 255));
                    p.Ellipse(25, 20, 3, 2, clay);
                    p.Rect(22, 18, 28, 18, clayDark);
                    p.Rect(28, 21, 29, 22, clay); p.Set(29, 22, clayDark); // spout
                    p.Set(23, 20, Gold); p.Set(26, 20, Gold);
                    break;
                }
                case TorchStyle.Moon:
                {
                    var moon = new Color32(214, 224, 244, 255);
                    p.Rect(25, 9, 26, 20, Metal); p.Rect(26, 9, 26, 20, MetalDark);
                    p.Rect(23, 21, 28, 22, Metal);
                    // Crescent cradling the flame.
                    p.Set(22, 22, moon); p.Rect(21, 23, 21, 27, moon); p.Set(22, 28, moon); p.Set(23, 29, moon);
                    p.Set(22, 23, moon); p.Set(22, 27, moon);
                    break;
                }
                case TorchStyle.Feather:
                {
                    var white = new Color32(248, 246, 240, 255);
                    p.Rect(25, 9, 26, 20, Gold); p.Rect(26, 9, 26, 20, GoldDark);
                    p.Rect(23, 21, 28, 22, Gold); p.Rect(23, 21, 28, 21, GoldDark);
                    // A white feather tied under the cup.
                    p.Set(27, 20, new Color32(200, 40, 40, 255));
                    p.Rect(28, 14, 29, 20, white); p.Rect(28, 14, 28, 19, new Color32(170, 166, 160, 255)); p.Set(29, 13, white);
                    break;
                }
                case TorchStyle.Sistrum:
                    p.Rect(25, 9, 26, 19, Gold); p.Rect(26, 9, 26, 19, GoldDark);
                    p.Rect(23, 20, 28, 21, Gold);
                    p.Rect(22, 21, 22, 28, Gold); p.Rect(29, 21, 29, 28, GoldDark); p.Rect(23, 29, 28, 29, Gold); // U frame
                    p.Rect(21, 24, 30, 24, Metal); p.Rect(21, 27, 30, 27, Metal); // rattling rods
                    p.Set(21, 25, MetalDark); p.Set(30, 25, MetalDark);
                    break;
                default:
                {
                    var wood = new Color32(116, 74, 38, 255);
                    var woodDark = new Color32(78, 48, 24, 255);
                    p.Rect(25, 9, 26, 20, wood);
                    p.Rect(26, 9, 26, 20, woodDark);
                    p.Rect(24, 21, 27, 22, new Color32(186, 128, 52, 255));
                    p.Rect(24, 21, 27, 21, new Color32(120, 78, 32, 255));
                    break;
                }
            }
        }

        static Px PaintFlame(Color tint, int frame)
        {
            var p = new Px(32, 32);
            p.Fill(Clear);
            Color32 outer = Color.Lerp(tint, new Color(1f, 0.35f, 0.05f), 0.35f);
            Color32 mid = Color.Lerp(tint, new Color(1f, 0.85f, 0.3f), 0.6f);
            Color32 core = Color.Lerp(tint, Color.white, 0.8f);
            // Three hand-placed frames: lean right, upright, lean left (a little shorter).
            int lean = frame == 0 ? 1 : frame == 1 ? 0 : -1;
            int height = frame == 2 ? 2 : 3;
            p.Ellipse(25, 25, 2, height, outer);
            p.Set(25 + lean, 26 + height, outer);
            p.Set(25 + lean, 25 + height, mid);
            p.Ellipse(25, 24, 1, 2, mid);
            p.Rect(25, 23, 25, 24, core);
            if (frame != 2) p.Set(26, 24, core);
            return p;
        }

        static Px PaintEmber()
        {
            var p = new Px(32, 32);
            p.Fill(Clear);
            p.Rect(25, 23, 26, 23, new Color32(255, 90, 30, 255));
            p.Set(24, 23, new Color32(150, 40, 20, 255));
            p.Set(27, 23, new Color32(150, 40, 20, 255));
            p.Set(25, 25, new Color32(120, 110, 100, 110)); // wisp of smoke
            p.Set(26, 27, new Color32(120, 110, 100, 70));
            return p;
        }

        static Px PaintGlow(int size)
        {
            var p = new Px(size, size);
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255);
                    p.Set(x, y, new Color32(255, 255, 255, a));
                }
            return p;
        }

        static Px PaintPanel(Color32 fill, Color32 border)
        {
            var p = new Px(32, 32);
            p.Fill(fill);
            for (int i = 0; i < 32; i++)
            {
                p.Set(i, 0, border); p.Set(i, 31, border); p.Set(0, i, border); p.Set(31, i, border);
                p.Set(i, 1, border); p.Set(i, 30, border); p.Set(1, i, border); p.Set(30, i, border);
            }
            var inner = Px.Shade(border, -0.35f);
            for (int i = 4; i < 28; i++) { p.Set(i, 4, inner); p.Set(i, 27, inner); p.Set(4, i, inner); p.Set(27, i, inner); }
            // Corner studs.
            p.Rect(2, 2, 3, 3, border); p.Rect(28, 2, 29, 3, border); p.Rect(2, 28, 3, 29, border); p.Rect(28, 28, 29, 29, border);
            return p;
        }

        static Px PaintAnkh(Color32 c, Color32 dark)
        {
            var p = new Px(16, 16);
            p.Fill(Clear);
            p.Ellipse(8, 11, 3, 4, c);
            p.Ellipse(8, 11, 1, 2, Clear);
            p.Rect(2, 7, 13, 8, c);
            p.Rect(7, 0, 9, 7, c);
            p.Rect(9, 0, 9, 7, dark);
            return p;
        }

        static Px PaintStar(Color32 c)
        {
            var p = new Px(16, 16);
            p.Fill(Clear);
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 5;
                float r = i % 2 == 0 ? 7.5f : 3.2f;
                pts[i] = new Vector2(7.5f + Mathf.Cos(a) * r, 7.5f + Mathf.Sin(a) * r);
            }
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    if (Px.InPolygon(new Vector2(x, y), pts)) p.Set(x, y, c);
            return p;
        }

        static Px PaintScarabIcon()
        {
            var p = new Px(16, 16);
            p.Fill(Clear);
            p.Ellipse(8, 7, 4, 5, Turquoise);
            p.Circle(8, 13, 2, Gold, true);
            p.Line(8, 2, 8, 11, LapisDark);
            p.Line(2, 9, 4, 8, Gold); p.Line(14, 9, 12, 8, Gold);
            p.Line(2, 4, 4, 5, Gold); p.Line(14, 4, 12, 5, Gold);
            return p;
        }

        static Px PaintLock()
        {
            var p = new Px(16, 16);
            p.Fill(Clear);
            p.Rect(3, 1, 12, 8, Gold);
            p.Circle(8, 10, 4, GoldDark, false);
            p.Rect(7, 3, 8, 6, Black);
            return p;
        }

        // ------------------------------------------------------------------ texture helpers

        static Sprite ToSprite(Px p, int ppu = Ppu, Vector4 border = default, bool smooth = false, Vector2? pivot = null)
        {
            var tex = new Texture2D(p.W, p.H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (smooth || p.W == 64) tex.filterMode = FilterMode.Bilinear; // soft glows, beams, puffs
            tex.SetPixels32(p.C);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, p.W, p.H), pivot ?? new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
        }

        /// <summary>Minimal pixel canvas.</summary>
        sealed class Px
        {
            public readonly int W, H;
            public readonly Color32[] C;

            public Px(int w, int h) { W = w; H = h; C = new Color32[w * h]; }

            public void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) C[y * W + x] = c; }
            public Color32 Get(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? C[y * W + x] : default;
            public void Fill(Color32 c) { for (int i = 0; i < C.Length; i++) C[i] = c; }

            /// <summary>Draws the opaque pixels of another canvas of the same size on top of this one.</summary>
            public Px Overlay(Px top)
            {
                for (int i = 0; i < C.Length && i < top.C.Length; i++)
                    if (top.C[i].a > 0) C[i] = top.C[i];
                return this;
            }

            /// <summary>Draws the opaque pixels of any canvas at an offset on top of this one.</summary>
            public Px Blit(Px top, int dx, int dy)
            {
                for (int y = 0; y < top.H; y++)
                    for (int x = 0; x < top.W; x++)
                        if (top.C[y * top.W + x].a > 0) Set(x + dx, y + dy, top.C[y * top.W + x]);
                return this;
            }

            /// <summary>Fills a polygon (pixel centres inside it).</summary>
            public void Poly(Color32 c, params Vector2[] pts)
            {
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var v in pts) { x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y); x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y); }
                for (int y = Mathf.FloorToInt(y0); y <= Mathf.CeilToInt(y1); y++)
                    for (int x = Mathf.FloorToInt(x0); x <= Mathf.CeilToInt(x1); x++)
                        if (InPolygon(new Vector2(x, y), pts)) Set(x, y, c);
            }

            /// <summary>Applies extra painting and returns this canvas (decorations).</summary>
            public Px With(System.Action<Px> paint) { paint(this); return this; }

            /// <summary>Copy turned by quarter turns counter-clockwise (square canvases only).</summary>
            public Px Rotated(int quarterTurns)
            {
                var r = this;
                for (int q = 0; q < ((quarterTurns % 4) + 4) % 4; q++)
                {
                    var n = new Px(r.H, r.W);
                    for (int y = 0; y < r.H; y++)
                        for (int x = 0; x < r.W; x++)
                            n.Set(r.H - 1 - y, x, r.Get(x, y));
                    r = n;
                }
                return r;
            }

            public void Rect(int x0, int y0, int x1, int y1, Color32 c)
            {
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c);
            }

            public void Circle(int cx, int cy, int r, Color32 c, bool filled)
            {
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        int d = x * x + y * y;
                        if (filled ? d <= r * r : d <= r * r && d > (r - 1) * (r - 1)) Set(cx + x, cy + y, c);
                    }
            }

            public void Ellipse(int cx, int cy, int rx, int ry, Color32 c)
            {
                for (int y = -ry; y <= ry; y++)
                    for (int x = -rx; x <= rx; x++)
                        if (x * x * ry * ry + y * y * rx * rx <= rx * rx * ry * ry) Set(cx + x, cy + y, c);
            }

            public void Line(int x0, int y0, int x1, int y1, Color32 c)
            {
                int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    Set(x0, y0, c);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            public static float Hash(int x, int y, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return (h ^ (h >> 16)) / (float)uint.MaxValue;
                }
            }

            public static Color32 Shade(Color32 c, float amount)
            {
                float f = 1f + amount;
                return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);
            }

            public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

            public static bool InPolygon(Vector2 p, Vector2[] poly)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                        p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                        inside = !inside;
                return inside;
            }
        }
    }
}
