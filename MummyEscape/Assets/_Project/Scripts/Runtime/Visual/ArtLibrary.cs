using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// Procedurally painted pixel-art placeholders in an Egyptian palette (sandstone, lapis, turquoise, gold).
    /// Every sprite is 32 px = 1 world unit. Replace by real art later by swapping what these getters return.
    /// </summary>
    public sealed class ArtLibrary
    {
        public const int Ppu = 32;

        // Palette
        static readonly Color32 Sand = new Color32(201, 166, 107, 255);
        static readonly Color32 SandDark = new Color32(156, 122, 69, 255);
        static readonly Color32 SandLight = new Color32(222, 192, 138, 255);
        static readonly Color32 Stone = new Color32(120, 92, 56, 255);
        static readonly Color32 StoneDark = new Color32(82, 60, 34, 255);
        static readonly Color32 StoneLight = new Color32(168, 132, 84, 255);
        static readonly Color32 Gold = new Color32(232, 195, 90, 255);
        static readonly Color32 GoldDark = new Color32(160, 120, 40, 255);
        static readonly Color32 Lapis = new Color32(38, 64, 140, 255);
        static readonly Color32 LapisDark = new Color32(22, 36, 82, 255);
        static readonly Color32 Turquoise = new Color32(64, 224, 208, 255);
        static readonly Color32 Metal = new Color32(176, 180, 186, 255);
        static readonly Color32 MetalDark = new Color32(90, 92, 98, 255);
        static readonly Color32 Black = new Color32(10, 8, 6, 255);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        readonly Sprite[] _floors = new Sprite[4];
        readonly Sprite[] _walls = new Sprite[4];
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

        public ArtLibrary()
        {
            for (int i = 0; i < _floors.Length; i++) _floors[i] = ToSprite(PaintFloor(i));
            for (int i = 0; i < _walls.Length; i++) _walls[i] = ToSprite(PaintWall(i));

            var white = new Px(4, 4); white.Fill(new Color32(255, 255, 255, 255));
            White = ToSprite(white);
            Glow = ToSprite(PaintGlow(64), 64);
            Panel = ToSprite(PaintPanel(new Color32(30, 26, 22, 240), Gold), Ppu, new Vector4(10, 10, 10, 10));
            ButtonSprite = ToSprite(PaintPanel(Lapis, Gold), Ppu, new Vector4(10, 10, 10, 10));
            Ankh = ToSprite(PaintAnkh(Gold, GoldDark), 16);
            AnkhEmpty = ToSprite(PaintAnkh(new Color32(70, 64, 58, 255), new Color32(40, 36, 32, 255)), 16);
            Star = ToSprite(PaintStar(Gold), 16);
            StarEmpty = ToSprite(PaintStar(new Color32(70, 64, 58, 255)), 16);
            Scarab = ToSprite(PaintScarabIcon(), 16);
            Lock = ToSprite(PaintLock(), 16);
        }

        // ------------------------------------------------------------------ public getters

        public Sprite Floor(int variant) => _floors[Mathf.Abs(variant) % _floors.Length];
        public Sprite Wall(int variant) => _walls[Mathf.Abs(variant) % _walls.Length];

        /// <summary>Sprite for a tile as the player perceives it.</summary>
        public Sprite ForTile(Tile t, int variant, bool doorOpen, bool channelActive, bool trapArmed)
        {
            switch (t.Type)
            {
                case TileType.Wall: return Wall(variant);
                case TileType.Floor: return Floor(variant);
                case TileType.Exit: return Cached("exit", PaintExit);
                case TileType.Door: return doorOpen ? Cached("door_open", () => PaintDoor(true)) : Cached("door_closed", () => PaintDoor(false));
                case TileType.Button: return channelActive ? Cached("button_on", () => PaintButton(true)) : Cached("button_off", () => PaintButton(false));
                case TileType.Trap:
                    if (t.Trap == TrapKind.Spikes) return trapArmed ? Cached("spikes", () => PaintSpikes(true)) : Cached("spikes_off", () => PaintSpikes(false));
                    return trapArmed ? Cached("dark", () => PaintDarkness(true)) : Cached("dark_off", () => PaintDarkness(false));
                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Locked && !channelActive) return Cached("tp_locked", () => PaintPortal(new Color32(110, 110, 120, 255), true));
                    if (t.Teleporter == TeleporterKind.Cursed) return Cached("tp_cursed", () => PaintPortal(new Color32(150, 230, 80, 255), false));
                    return Cached("tp", () => PaintPortal(Turquoise, false));
                case TileType.BreakableFloor: return Cached("breakable", PaintBreakable);
                case TileType.LadderUp: return Cached("ladder_up", () => PaintLadder(true));
                case TileType.LadderDown: return Cached("ladder_down", () => PaintLadder(false));
            }
            return Floor(variant);
        }

        public Sprite Mummy(SkinDef skin) => Cached("mummy_" + skin.Id, () => PaintMummy(skin));

        /// <summary>Colour of the small light a point of interest emits once discovered.</summary>
        public static Color? GlowColor(Tile t, bool channelActive)
        {
            switch (t.Type)
            {
                case TileType.Exit: return new Color(1f, 0.85f, 0.45f);
                case TileType.Button: return channelActive ? new Color(0.3f, 1f, 0.9f) : new Color(0.35f, 0.5f, 1f);
                case TileType.Door: return channelActive ? new Color(0.3f, 1f, 0.9f) : new Color(1f, 0.35f, 0.25f);
                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Cursed) return new Color(0.6f, 1f, 0.3f);
                    if (t.Teleporter == TeleporterKind.Locked && !channelActive) return null;
                    return new Color(0.3f, 0.95f, 1f);
                case TileType.Trap: return t.Trap == TrapKind.Darkness ? new Color(0.55f, 0.25f, 0.9f) : (Color?)null;
            }
            return null;
        }

        Sprite Cached(string key, System.Func<Px> paint)
        {
            if (!_cache.TryGetValue(key, out var s)) _cache[key] = s = ToSprite(paint());
            return s;
        }

        // ------------------------------------------------------------------ painters

        static Px PaintFloor(int seed)
        {
            var p = new Px(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float n = Px.Hash(x, y, seed) * 0.18f - 0.09f;
                    p.Set(x, y, Px.Shade(Sand, n));
                }
            // Slab grout, offset per variant.
            int off = seed % 2 == 0 ? 0 : 8;
            for (int i = 0; i < 32; i++)
            {
                p.Set(i, 0, SandDark); p.Set(i, 16, SandDark);
                p.Set((off) % 32, i, SandDark);
                p.Set((off + 16) % 32, i < 16 ? i : i, SandDark);
            }
            for (int i = 1; i < 32; i++) { p.Set(i, 1, SandLight); p.Set(i, 17, SandLight); }
            // A few cracks and pebbles.
            int cx = 4 + (int)(Px.Hash(1, 2, seed) * 22), cy = 4 + (int)(Px.Hash(3, 4, seed) * 22);
            for (int k = 0; k < 6; k++) p.Set(cx + k, cy + (k % 3 == 0 ? 1 : 0), SandDark);
            for (int k = 0; k < 4; k++) p.Set((int)(Px.Hash(k, 9, seed) * 31), (int)(Px.Hash(9, k, seed) * 31), SandDark);
            return p;
        }

        static Px PaintWall(int seed)
        {
            var p = new Px(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    p.Set(x, y, Px.Shade(Stone, Px.Hash(x, y, seed + 50) * 0.14f - 0.07f));
            // Bricks.
            for (int row = 0; row < 4; row++)
            {
                int y0 = row * 8;
                for (int x = 0; x < 32; x++) p.Set(x, y0, StoneDark);
                int shift = row % 2 == 0 ? 0 : 8;
                for (int y = y0; y < y0 + 8; y++) { p.Set(shift, y, StoneDark); p.Set((shift + 16) % 32, y, StoneDark); }
            }
            // Lit cap on top (fake 3/4 view).
            for (int y = 26; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    p.Set(x, y, Px.Shade(StoneLight, Px.Hash(x, y, seed) * 0.1f - 0.05f));
            for (int x = 0; x < 32; x++) p.Set(x, 25, StoneDark);
            // Carved hieroglyph.
            int g = seed % 4;
            Color32 carve = StoneDark;
            if (g == 0) { p.Circle(16, 13, 4, carve, false); p.Set(16, 13, carve); p.Line(12, 13, 8, 11, carve); } // eye
            else if (g == 1) { p.Circle(16, 17, 3, carve, false); p.Line(16, 14, 16, 5, carve); p.Line(12, 12, 20, 12, carve); } // ankh
            else if (g == 2) { for (int k = 0; k < 3; k++) for (int x = 8; x < 24; x++) p.Set(x, 8 + k * 4 + ((x / 2) % 2), carve); } // water
            else { p.Line(10, 6, 14, 18, carve); p.Line(14, 18, 20, 20, carve); p.Line(20, 20, 22, 14, carve); p.Line(14, 6, 18, 6, carve); } // bird-ish
            return p;
        }

        static Px PaintDoor(bool open)
        {
            var p = PaintFloor(1);
            if (open)
            {
                p.Rect(0, 0, 4, 31, StoneDark); p.Rect(27, 0, 31, 31, StoneDark);
                p.Rect(5, 14, 26, 17, Black);
                return p;
            }
            p.Rect(0, 0, 31, 31, StoneDark);
            p.Rect(3, 2, 28, 29, new Color32(110, 90, 62, 255));
            // Eye of Horus in gold.
            p.Circle(16, 17, 5, Gold, false);
            p.Circle(16, 17, 2, Lapis, true);
            p.Line(9, 20, 23, 20, Gold);
            p.Line(14, 12, 12, 7, Gold);
            p.Line(18, 12, 22, 9, Gold);
            return p;
        }

        static Px PaintButton(bool pressed)
        {
            var p = PaintFloor(2);
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
            // Ankh in the middle.
            p.Circle(16, 20, 2, ring, false);
            p.Line(16, 17, 16, 10, ring);
            p.Line(13, 16, 19, 16, ring);
            if (sealedBar) { p.Rect(4, 15, 27, 17, MetalDark); p.Rect(4, 16, 27, 16, Metal); }
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
            var wood = new Color32(130, 84, 40, 255);
            var woodDark = new Color32(84, 52, 24, 255);
            if (!up) p.Rect(4, 2, 27, 29, Black);
            p.Rect(8, 2, 9, 29, up ? wood : woodDark); p.Rect(22, 2, 23, 29, up ? wood : woodDark);
            for (int y = 5; y < 29; y += 5) p.Rect(10, y, 21, y + 1, wood);
            if (up) for (int y = 26; y < 30; y++) for (int x = 6; x < 26; x++) p.Set(x, y, Px.Lerp(p.Get(x, y), SandLight, 0.4f));
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
            // Sun disk.
            p.Circle(16, 24, 3, new Color32(255, 255, 255, 255), true);
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

        static Sprite ToSprite(Px p, int ppu = Ppu, Vector4 border = default)
        {
            var tex = new Texture2D(p.W, p.H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (p.W == 64) tex.filterMode = FilterMode.Bilinear; // soft glow
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
