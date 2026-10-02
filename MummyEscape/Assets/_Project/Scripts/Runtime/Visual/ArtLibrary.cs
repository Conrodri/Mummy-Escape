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

        /// <summary>In-game mummy holding an unlit torch handle; the flame is a separate sprite so it can go out.</summary>
        public Sprite Mummy(SkinDef skin) => Cached("mummy_" + skin.Id, () => PaintMummy(skin));

        /// <summary>Mummy with its torch lit, for menus, shop and icon.</summary>
        public Sprite MummyPortrait(SkinDef skin) => Cached("portrait_" + skin.Id, () => PaintMummy(skin).Overlay(PaintFlame(skin, 0)));

        public const int TorchFlameFrames = 3;
        public Sprite TorchFlame(SkinDef skin, int frame) => Cached($"flame_{skin.Id}_{frame}", () => PaintFlame(skin, frame));

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
                case TileType.Trap: return t.Trap == TrapKind.Darkness && l.Armed ? new Color(0.55f, 0.25f, 0.9f) : (Color?)null;
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

        static Px PaintMummy(SkinDef s)
        {
            var p = new Px(32, 32);
            p.Fill(Clear);
            // Shadow.
            p.Ellipse(16, 3, 8, 2, new Color32(0, 0, 0, 90));
            // Body.
            p.Rect(10, 4, 21, 18, s.Bandage);
            p.Rect(12, 2, 14, 5, s.Bandage); p.Rect(17, 2, 19, 5, s.Bandage); // legs
            // Arms reaching forward.
            p.Rect(6, 13, 9, 16, s.Bandage); p.Rect(22, 13, 25, 16, s.Bandage);
            // Head.
            p.Circle(16, 23, 6, s.Bandage, true);
            // Bandage stripes.
            for (int y = 5; y < 29; y += 3)
                for (int x = 9; x < 23; x++)
                    if (p.Get(x, y).a > 0 && ((x + y) % 5 != 0)) p.Set(x, y, s.Shadow);
            for (int y = 13; y <= 16; y += 3) { p.Rect(6, y, 9, y, s.Shadow); p.Rect(22, y, 25, y, s.Shadow); }
            // Dark eye band + glowing eyes.
            p.Rect(11, 22, 21, 24, new Color32(30, 24, 20, 255));
            p.Rect(12, 23, 14, 23, s.Eyes); p.Rect(18, 23, 20, 23, s.Eyes);

            // Torch held upright in the right hand: wooden handle, bronze cup, bandaged fist over the grip.
            var wood = new Color32(116, 74, 38, 255);
            var woodDark = new Color32(78, 48, 24, 255);
            p.Rect(25, 9, 26, 20, wood);
            p.Rect(26, 9, 26, 20, woodDark);
            p.Rect(24, 21, 27, 22, new Color32(186, 128, 52, 255));
            p.Rect(24, 21, 27, 21, new Color32(120, 78, 32, 255));
            p.Rect(23, 13, 26, 16, s.Bandage);
            p.Rect(23, 14, 26, 14, s.Shadow);
            return p;
        }

        static Px PaintFlame(SkinDef s, int frame)
        {
            var p = new Px(32, 32);
            p.Fill(Clear);
            Color tint = s.Torch;
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

        static Sprite ToSprite(Px p, int ppu = Ppu, Vector4 border = default, bool smooth = false)
        {
            var tex = new Texture2D(p.W, p.H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (smooth || p.W == 64) tex.filterMode = FilterMode.Bilinear; // soft glows, beams, puffs
            tex.SetPixels32(p.C);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, p.W, p.H), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
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
