using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// Line drawing in the UI: strokes (polylines of a given width, drawn up to <see cref="Stroke.Reveal"/> of their
    /// length, so they can be traced as they appear) and filled convex shapes. Coordinates are in the rect's local space.
    /// </summary>
    public sealed class LineArt : MaskableGraphic
    {
        public sealed class Stroke
        {
            public List<Vector2> Points = new List<Vector2>();
            public float Width = 6f;
            /// <summary>Share of the length drawn, from the first point (0-1).</summary>
            public float Reveal = 1f;
            public bool Closed;
            /// <summary>A filled convex shape instead of a line.</summary>
            public bool Filled;
            public Color Color = Color.white;
        }

        public readonly List<Stroke> Strokes = new List<Stroke>();

        public Stroke Add(float width, params Vector2[] points)
        {
            var s = new Stroke { Width = width };
            s.Points.AddRange(points);
            Strokes.Add(s);
            return s;
        }

        /// <summary>Call after changing the strokes.</summary>
        public void Redraw() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var s in Strokes)
            {
                if (s.Points.Count < 2) continue;
                if (s.Filled) Fill(vh, s);
                else Line(vh, s);
            }
        }

        static void Fill(VertexHelper vh, Stroke s)
        {
            int first = vh.currentVertCount;
            foreach (var p in s.Points) vh.AddVert(p, s.Color, Vector2.zero);
            for (int i = 1; i + 1 < s.Points.Count; i++) vh.AddTriangle(first, first + i, first + i + 1);
        }

        static void Line(VertexHelper vh, Stroke s)
        {
            int count = s.Points.Count + (s.Closed ? 1 : 0);
            float total = 0f;
            for (int i = 1; i < count; i++) total += Vector2.Distance(s.Points[(i - 1) % s.Points.Count], s.Points[i % s.Points.Count]);
            float left = total * Mathf.Clamp01(s.Reveal);
            for (int i = 1; i < count && left > 0f; i++)
            {
                var a = s.Points[(i - 1) % s.Points.Count];
                var b = s.Points[i % s.Points.Count];
                float length = Vector2.Distance(a, b);
                if (length <= 0f) continue;
                if (length > left) { b = a + (b - a) * (left / length); length = left; }
                left -= length;
                Segment(vh, a, b, s.Width, s.Color);
            }
        }

        /// <summary>A quad from a to b, its ends pushed out by half the width so the joints close up.</summary>
        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            var dir = (b - a).normalized;
            var n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            var cap = dir * (width * 0.5f);
            a -= cap;
            b += cap;
            int i = vh.currentVertCount;
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
