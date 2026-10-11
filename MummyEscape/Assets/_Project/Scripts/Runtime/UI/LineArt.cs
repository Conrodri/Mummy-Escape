using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// Line drawing in the UI: strokes (polylines of a given width, drawn up to <see cref="Stroke.Reveal"/> of their
    /// length, so they can be traced as they appear) and filled convex shapes. Coordinates are in the rect's local space.
    /// Each segment is drawn by the LineArt shader as a capsule with anti-aliased edges, so joints are round and seamless;
    /// a single point is a dot. <see cref="Blend"/> on, colours mix by their maximum: glows and faint strokes overlap
    /// without doubling (meant for light on a dark background).
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
            /// <summary>A glow: full at the line, fading out to nothing at half the width.</summary>
            public bool Soft;
            public Color Color = Color.white;
        }

        public readonly List<Stroke> Strokes = new List<Stroke>();

        static Material _normal, _max;

        /// <summary>Colours mixed by their maximum instead of painted over (glows, light effects).</summary>
        public bool Blend
        {
            set => material = Make(value);
        }

        static Material Make(bool max)
        {
            ref var m = ref max ? ref _max : ref _normal;
            if (m != null) return m;
            m = new Material(Resources.Load<Shader>("Shaders/LineArt")) { hideFlags = HideFlags.HideAndDontSave }; // noloc
            m.SetFloat("_Op", (float)(max ? BlendOp.Max : BlendOp.Add)); // noloc
            m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); // noloc
            return m;
        }

        protected override void Awake()
        {
            base.Awake();
            if (material == defaultGraphicMaterial) material = Make(false);
        }

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
                if (s.Points.Count == 0 || s.Filled && s.Points.Count < 3) continue;
                if (s.Filled) Fill(vh, s);
                else if (s.Points.Count == 1) Segment(vh, s.Points[0], s.Points[0], s);
                else Line(vh, s);
            }
        }

        static void Fill(VertexHelper vh, Stroke s)
        {
            int first = vh.currentVertCount;
            foreach (var p in s.Points) vh.AddVert(p, s.Color, Vector4.zero);
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
                Segment(vh, a, b, s);
            }
        }

        /// <summary>
        /// A quad around the capsule from a to b (2 units of margin for the soft edge); the shader gets each corner's place
        /// along and across the segment, its length and its half width (negative for a glow).
        /// </summary>
        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, Stroke s)
        {
            float length = Vector2.Distance(a, b);
            var dir = length > 0f ? (b - a) / length : Vector2.right;
            var normal = new Vector2(-dir.y, dir.x);
            float half = s.Width * 0.5f;
            float r = half + 2f;
            float w = s.Soft ? -half : half;
            int i = vh.currentVertCount;
            Color32 color = s.Color;
            vh.AddVert(a - dir * r + normal * r, color, new Vector4(-r, r, length, w));
            vh.AddVert(b + dir * r + normal * r, color, new Vector4(length + r, r, length, w));
            vh.AddVert(b + dir * r - normal * r, color, new Vector4(length + r, -r, length, w));
            vh.AddVert(a - dir * r - normal * r, color, new Vector4(-r, -r, length, w));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
