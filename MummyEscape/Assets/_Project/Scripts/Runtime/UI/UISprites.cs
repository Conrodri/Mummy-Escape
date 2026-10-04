using System;
using UnityEngine;

namespace MummyEscape.UI
{
    /// <summary>
    /// Smooth (anti-aliased) interface shapes and line icons, drawn once at startup from signed distance functions.
    /// Shapes are white so any colour can tint them; rounded rects are 9-sliced and take any corner radius through
    /// <see cref="UnityEngine.UI.Image.pixelsPerUnitMultiplier"/> (see <see cref="UIKit.Rounded"/>).
    /// </summary>
    public static class UISprites
    {
        /// <summary>Corner radius, in texture pixels, of the rounded sprites.</summary>
        public const int Radius = 64;
        const int IconSize = 128;

        public static Sprite Round { get; private set; }
        /// <summary>Rounded rect whose white fades to grey downwards, with a light rim on top: tinted, a glossy button.</summary>
        public static Sprite RoundGloss { get; private set; }
        public static Sprite RoundOutline { get; private set; }
        public static Sprite Circle { get; private set; }
        public static Sprite Ring { get; private set; }
        public static Sprite RadialGlow { get; private set; }

        public static Sprite Back, Next, Pause, Play, Gear, Podium, Friends, Bag, Share, Retry, Home, Close, Clock, Steps, Hand, Map, Check, User, Globe, Plus, Note, Swords, Seal, Arrow, Flag;

        public static void Init()
        {
            if (Round != null) return;
            int size = Radius * 2 + 2;
            var border = new Vector4(Radius, Radius, Radius, Radius);
            Round = Make(size, size, (x, y) => Fill(RoundedBox(x, y, size, size, Radius)), border);
            RoundGloss = Make(size, size, (x, y) =>
            {
                float d = RoundedBox(x, y, size, size, Radius);
                float a = Fill(d).a;
                float t = y / (float)size; // 0 bottom, 1 top
                float shade = Mathf.Lerp(0.74f, 1f, t);
                // Light rim just inside the top edge.
                if (y > size - 10) shade += 0.12f;
                return new Color(shade, shade, shade, a);
            }, border);
            RoundOutline = Make(size, size, (x, y) => Fill(Mathf.Abs(RoundedBox(x, y, size, size, Radius - 2)) - 2.5f), border);
            Circle = Make(IconSize, IconSize, (x, y) => Fill(Len(x - 64, y - 64) - 63));
            Ring = Make(IconSize, IconSize, (x, y) => Fill(Mathf.Abs(Len(x - 64, y - 64) - 61) - 2.5f));
            RadialGlow = Make(IconSize, IconSize, (x, y) =>
            {
                float t = Mathf.Clamp01(1f - Len(x - 64, y - 64) / 64f);
                return new Color(1, 1, 1, t * t * (3f - 2f * t));
            });

            // Icons, drawn on a 128 grid with y going up.
            Back = Icon(p => Min(Seg(p, 78, 104, 42, 64, 14), Seg(p, 42, 64, 78, 24, 14)));
            Next = Icon(p => Min(Seg(p, 50, 104, 86, 64, 14), Seg(p, 86, 64, 50, 24, 14)));
            Pause = Icon(p => Min(Box(p, 46, 64, 11, 38, 6), Box(p, 82, 64, 11, 38, 6)));
            Play = Icon(p => Poly(p, 40, 106, 40, 22, 106, 64) - 4f);
            Gear = Icon(Gearwheel);
            Podium = Icon(p => Min(Box(p, 64, 56, 17, 36, 4), Min(Box(p, 27, 44, 16, 24, 4), Box(p, 101, 36, 16, 16, 4))));
            Friends = Icon(p =>
            {
                float a = Min(Disc(p, 50, 86, 15), Max(Disc(p, 50, 30, 36), 26 - p.y));
                float b = Min(Disc(p, 88, 90, 13), Max(Disc(p, 90, 38, 32), 30 - p.y));
                return Min(a, Max(b, -(a - 7)));
            });
            Bag = Icon(p => Min(Box(p, 64, 46, 40, 32, 10), Max(Annulus(p, 64, 80, 20, 10), 80 - p.y)));
            Share = Icon(p => Min(Min(Disc(p, 94, 98, 15), Min(Disc(p, 34, 64, 15), Disc(p, 94, 30, 15))),
                                  Min(Seg(p, 34, 64, 94, 98, 9), Seg(p, 34, 64, 94, 30, 9))));
            Retry = Icon(RetryArrow);
            Home = Icon(p =>
            {
                float roof = Poly(p, 64, 112, 14, 66, 114, 66) - 2f;
                float body = Box(p, 64, 42, 34, 28, 4);
                return Max(Min(roof, body), -Box(p, 64, 30, 10, 16, 3));
            });
            Close = Icon(p => Min(Seg(p, 32, 32, 96, 96, 14), Seg(p, 32, 96, 96, 32, 14)));
            // Pointing up; rotate the image for the other directions.
            Arrow = Icon(p => Min(Poly(p, 64, 116, 18, 66, 110, 66), Box(p, 64, 40, 14, 30, 4)));
            Flag = Icon(p => Min(Seg(p, 34, 14, 34, 114, 10), Poly(p, 38, 112, 108, 92, 38, 68)));
            Clock = Icon(p => Min(Annulus(p, 64, 64, 44, 12), Min(Seg(p, 64, 64, 64, 92, 10), Seg(p, 64, 64, 86, 64, 10))));
            Steps = Icon(p => Min(Min(Seg(p, 42, 72, 46, 98, 26), Disc(p, 48, 50, 10)),
                                  Min(Seg(p, 82, 40, 86, 66, 26), Disc(p, 80, 18, 10))));
            Hand = Icon(p => Min(Annulus(p, 64, 64, 44, 10), Disc(p, 64, 64, 22)));
            Map = Icon(p =>
            {
                float sheet = Box(p, 64, 64, 50, 42, 8);
                return Max(sheet, -Min(Seg(p, 47, 30, 47, 98, 6), Seg(p, 81, 30, 81, 98, 6)));
            });
            Check = Icon(p => Min(Seg(p, 26, 66, 52, 38, 15), Seg(p, 52, 38, 102, 92, 15)));
            User = Icon(p => Min(Disc(p, 64, 86, 22), Max(Disc(p, 64, 12, 46), 22 - p.y)));
            Globe = Icon(p => Min(Annulus(p, 64, 64, 46, 9), Min(Seg(p, 20, 64, 108, 64, 8),
                                  Mathf.Abs(Len((p.x - 64) * 2.2f, p.y - 64) - 46) - 5f)));
            Plus = Icon(p => Min(Seg(p, 64, 26, 64, 102, 15), Seg(p, 26, 64, 102, 64, 15)));
            Note = Icon(p => Min(Len((p.x - 48) * 0.85f, (p.y - 34) * 1.15f) - 19f, Min(Seg(p, 64, 36, 64, 106, 10), Seg(p, 64, 104, 96, 82, 10))));
            // Two crossed khopesh-like blades: the duels.
            Swords = Icon(p => Min(Min(Min(Seg(p, 40, 40, 106, 106, 11), Seg(p, 18, 18, 36, 36, 13)), Seg(p, 26, 52, 52, 26, 10)),
                                   Min(Min(Seg(p, 88, 40, 22, 106, 11), Seg(p, 110, 18, 92, 36, 13)), Seg(p, 76, 26, 102, 52, 10))));
            // Feather of Maat in a ring: the seals earned in duels.
            Seal = Icon(p => Min(Annulus(p, 64, 64, 52, 9), Min(Len((p.x - 64) * 2.2f, (p.y - 70) * 0.95f) - 30f, Seg(p, 64, 24, 64, 44, 7))));
        }

        // ------------------------------------------------------------------ shapes

        static float Gearwheel(Vector2 p)
        {
            var c = new Vector2(64, 64);
            var d = p - c;
            float ring = Mathf.Abs(d.magnitude - 30f) - 11f;
            float teeth = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                // Rotate the point into the tooth's frame (tooth along +x).
                var q = new Vector2(d.x * Mathf.Cos(a) + d.y * Mathf.Sin(a), -d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
                teeth = Mathf.Min(teeth, Box(q, 46, 0, 12, 10, 3));
            }
            return Max(Min(ring, teeth), -(d.magnitude - 17f));
        }

        static float RetryArrow(Vector2 p)
        {
            var c = new Vector2(64, 60);
            var d = p - c;
            const float r = 38f;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; // -180..180
            // Gap between 25° and 85°: the arc runs from 85° (top) round to 25° (upper right).
            bool inGap = ang > 25f && ang < 85f;
            float arc;
            if (!inGap) arc = Mathf.Abs(d.magnitude - r) - 6.5f;
            else
            {
                var e = c + Polar(r, 25f);
                arc = (p - e).magnitude - 6.5f;
            }
            // Arrowhead at the 85° end, pointing clockwise (rightwards).
            var tip0 = c + Polar(r, 85f);
            var radial = Polar(1f, 85f);
            var dir = new Vector2(radial.y, -radial.x);
            var tip = tip0 + dir * 20f;
            var b1 = tip0 + radial * 18f - dir * 4f;
            var b2 = tip0 - radial * 18f - dir * 4f;
            float head = Poly(p, tip.x, tip.y, b1.x, b1.y, b2.x, b2.y) - 1f;
            return Min(arc, head);
        }

        static Vector2 Polar(float r, float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * r;
        static float Len(float x, float y) => Mathf.Sqrt(x * x + y * y);
        static float Min(float a, float b) => Mathf.Min(a, b);
        static float Max(float a, float b) => Mathf.Max(a, b);
        static float Disc(Vector2 p, float cx, float cy, float r) => Len(p.x - cx, p.y - cy) - r;
        static float Annulus(Vector2 p, float cx, float cy, float r, float w) => Mathf.Abs(Len(p.x - cx, p.y - cy) - r) - w / 2f;

        static float Seg(Vector2 p, float ax, float ay, float bx, float by, float w)
        {
            var a = new Vector2(ax, ay);
            var ab = new Vector2(bx, by) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - w / 2f;
        }

        /// <summary>Rounded box centred on (cx, cy) with half extents (hx, hy).</summary>
        static float Box(Vector2 p, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(p.x - cx) - hx + r, qy = Mathf.Abs(p.y - cy) - hy + r;
            return Len(Mathf.Max(qx, 0), Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        /// <summary>Signed distance to a convex polygon given as x, y pairs (either winding).</summary>
        static float Poly(Vector2 p, params float[] xy)
        {
            int n = xy.Length / 2;
            float inside = float.MinValue, edge = float.MaxValue;
            float area = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += xy[i * 2] * xy[j * 2 + 1] - xy[j * 2] * xy[i * 2 + 1];
            }
            float sign = area > 0 ? 1f : -1f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = new Vector2(xy[i * 2], xy[i * 2 + 1]);
                var b = new Vector2(xy[j * 2], xy[j * 2 + 1]);
                var e = b - a;
                var normal = new Vector2(e.y, -e.x).normalized * sign; // outward
                inside = Mathf.Max(inside, Vector2.Dot(p - a, normal));
                float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
                edge = Mathf.Min(edge, (p - (a + e * t)).magnitude);
            }
            return inside > 0 ? edge : inside;
        }

        static float RoundedBox(int x, int y, int w, int h, float r) =>
            Box(new Vector2(x + 0.5f, y + 0.5f), w / 2f, h / 2f, w / 2f - 1f, h / 2f - 1f, r);

        static Color Fill(float d) => new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));

        // ------------------------------------------------------------------ textures

        static Sprite Icon(Func<Vector2, float> sdf) =>
            Make(IconSize, IconSize, (x, y) => Fill(sdf(new Vector2(x + 0.5f, y + 0.5f))));

        static Sprite Make(int w, int h, Func<int, int, Color> pixel, Vector4 border = default)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

    }
}
