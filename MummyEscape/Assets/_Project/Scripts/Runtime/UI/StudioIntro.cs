using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The studio's intro at launch, over everything: on black, the white outline of a mountain traces itself, a little
    /// climber walks up to the summit and plants a flag reading "PEAK", then "PEAK DEVELOPMENT" appears and it all fades
    /// out on the game. About 6 s; a tap skips to the fade.
    /// </summary>
    public sealed class StudioIntro : MonoBehaviour, IPointerDownHandler
    {
        const float LineWidth = 6f, FigureWidth = 4.5f;
        const float End = 5.4f, FadeOut = 0.5f;

        // The mountain, around the centre of the art: a small peak, a saddle, the summit, a shoulder.
        static readonly Vector2 BaseLeft = new Vector2(-430, -220), SmallPeak = new Vector2(-200, 20), Saddle = new Vector2(-110, -60);
        static readonly Vector2 Summit = new Vector2(70, 240), Shoulder = new Vector2(200, 80), BaseRight = new Vector2(430, -220);
        static readonly Vector2[] Path = { BaseLeft, SmallPeak, Saddle, Summit };

        LineArt _art;
        LineArt.Stroke _mountain, _snow, _pole, _flag, _flagOutline;
        LineArt.Stroke[] _figure;
        RectTransform _peakText;
        CanvasGroup _group, _name;
        float _time;
        bool _skipped;

        /// <summary>Plays the intro once, on its own canvas above the game's.</summary>
        public static void Play()
        {
            var go = new GameObject("StudioIntro", typeof(RectTransform)); // noloc
            go.layer = 5;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var group = go.AddComponent<CanvasGroup>();

            var back = new GameObject("Black", typeof(RectTransform), typeof(Image)); // noloc
            back.transform.SetParent(go.transform, false);
            var image = back.GetComponent<Image>();
            image.color = Color.black;
            Stretch(image.rectTransform);
            var intro = back.AddComponent<StudioIntro>();
            intro._group = group;
            intro.Build(go.transform);
        }

        void Build(Transform root)
        {
            var artGo = new GameObject("Art", typeof(RectTransform)); // noloc
            artGo.transform.SetParent(root, false);
            _art = artGo.AddComponent<LineArt>();
            _art.raycastTarget = false;
            var rect = _art.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000, 800);
            rect.anchoredPosition = new Vector2(0, 120);

            _mountain = _art.Add(LineWidth, BaseLeft, SmallPeak, Saddle, Summit, Shoulder, BaseRight);
            // A snow line across the summit, between its two slopes.
            _snow = _art.Add(LineWidth * 0.8f, new Vector2(16, 150), new Vector2(45, 125), new Vector2(75, 150), new Vector2(105, 122), new Vector2(143, 150));
            _pole = _art.Add(FigureWidth, Summit, Summit);
            _flag = _art.Add(0f, Summit, Summit, Summit, Summit);
            _flag.Filled = true;
            _flagOutline = _art.Add(3f, Summit, Summit, Summit, Summit);
            _flagOutline.Closed = true;
            _figure = new LineArt.Stroke[6];
            for (int i = 0; i < _figure.Length; i++) _figure[i] = _art.Add(FigureWidth, Vector2.zero, Vector2.zero);
            _figure[5].Closed = true; // the head

            // "PEAK" on the flag: black on the white cloth.
            var peak = Label(rect, "PEAK", 30, Color.black); // noloc
            _peakText = peak.rectTransform;
            _peakText.sizeDelta = new Vector2(92, 52);

            // The studio's name under the mountain.
            var nameGo = new GameObject("Name", typeof(RectTransform)); // noloc
            nameGo.transform.SetParent(root, false);
            _name = nameGo.AddComponent<CanvasGroup>();
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.sizeDelta = new Vector2(1000, 200);
            nameRect.anchoredPosition = new Vector2(0, -250);
            var title = Label(nameRect, "P E A K", 84, Color.white); // noloc
            title.rectTransform.anchoredPosition = new Vector2(0, 30);
            var sub = Label(nameRect, "D E V E L O P M E N T", 36, new Color(1f, 1f, 1f, 0.75f)); // noloc
            sub.rectTransform.anchoredPosition = new Vector2(0, -50);

            Apply(0f);
        }

        static Text Label(RectTransform parent, string text, int size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform)); // noloc
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.Load<Font>("Fonts/Nunito-ExtraBold") ?? UIKit.BoldFont; // noloc
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(1000, size * 1.6f);
            return t;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_skipped || _time >= End) return;
            _skipped = true;
            _time = End;
        }

        void Update()
        {
            // The first frames come after the loading: no jump over the start of the animation.
            _time += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            // Narrow screens: the whole drawing shrinks to fit the width.
            float width = ((RectTransform)_group.transform).rect.width;
            float scale = width > 0f ? Mathf.Min(1f, (width - 60f) / 940f) : 1f;
            _art.rectTransform.localScale = _name.transform.localScale = new Vector3(scale, scale, 1f);
            Apply(_time);
            if (_time >= End + FadeOut) Destroy(_group.gameObject);
        }

        static float Phase(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        void Apply(float t)
        {
            // Skipped: everything in place, the fade starts at once.
            if (_skipped) t = Mathf.Max(t, End);

            _mountain.Reveal = EaseOut(Phase(t, 0.2f, 1.4f));
            _snow.Reveal = Phase(t, 1.2f, 1.6f);

            // The climber walks up the left ridge, over the small peak and the saddle, to just below the summit.
            float total = PathLength();
            float climb = Phase(t, 1.3f, 3.3f);
            float distance = Mathf.Lerp(0f, total - 20f, climb * climb * (3f - 2f * climb) * 0.3f + climb * 0.7f);
            var feet = PointAt(distance, out var dir);
            bool arrived = t >= 3.3f;
            bool visible = t >= 1.3f;
            float lean = arrived ? 0f : -Mathf.Atan2(dir.y, Mathf.Abs(dir.x)) * 0.35f;
            Figure(feet + new Vector2(0, LineWidth * 0.5f), distance / 7f, lean, arrived, visible);

            // The flag: the pole rises, then the cloth unfurls and flutters.
            float pole = EaseOut(Phase(t, 3.3f, 3.6f));
            var top = Summit + new Vector2(0, 120f * pole);
            Set(_pole, Summit, top);
            float cloth = Phase(t, 3.6f, 4.0f);
            float width = 92f * (cloth < 1f ? EaseOut(cloth) * 1.06f : 1f);
            float flutter = Mathf.Sin(t * 7f) * 4f * cloth;
            var c0 = top;
            var c1 = top + new Vector2(width, flutter);
            var c2 = top + new Vector2(width, -52f + flutter);
            var c3 = top + new Vector2(0, -52f);
            Set(_flag, c0, c1, c2, c3);
            Set(_flagOutline, c0, c1, c2, c3);
            _flagOutline.Reveal = cloth > 0f ? 1f : 0f;
            _peakText.anchoredPosition = top + new Vector2(width * 0.5f, -26f + flutter * 0.5f);
            _peakText.localScale = new Vector3(Mathf.Clamp01(width / 92f), 1f, 1f);
            _peakText.gameObject.SetActive(cloth > 0.3f);

            _name.alpha = EaseOut(Phase(t, 3.9f, 4.6f));
            _group.alpha = 1f - Phase(t, End, End + FadeOut);
            _art.Redraw();
        }

        static float PathLength()
        {
            float total = 0f;
            for (int i = 1; i < Path.Length; i++) total += Vector2.Distance(Path[i - 1], Path[i]);
            return total;
        }

        static Vector2 PointAt(float distance, out Vector2 dir)
        {
            for (int i = 1; i < Path.Length; i++)
            {
                float length = Vector2.Distance(Path[i - 1], Path[i]);
                dir = (Path[i] - Path[i - 1]).normalized;
                if (distance <= length || i == Path.Length - 1) return Path[i - 1] + dir * Mathf.Min(distance, length);
                distance -= length;
            }
            dir = Vector2.right;
            return Path[0];
        }

        /// <summary>The stick climber, feet at <paramref name="feet"/>: walking (legs and arms swing) or cheering at the top.</summary>
        void Figure(Vector2 feet, float phase, float lean, bool cheering, bool visible)
        {
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            Vector2 footL = cheering ? new Vector2(-6, 0) : new Vector2(s * 8f, Mathf.Max(0f, c) * 5f);
            Vector2 footR = cheering ? new Vector2(6, 0) : new Vector2(-s * 8f, Mathf.Max(0f, -c) * 5f);
            var hip = new Vector2(0, 22);
            var neck = new Vector2(0, 42);
            Vector2 handL = cheering ? neck + new Vector2(-14, -12) : neck + new Vector2(-s * 9f, -16);
            Vector2 handR = cheering ? neck + new Vector2(12, 18) : neck + new Vector2(s * 9f, -16);
            var head = new Vector2(0, 51);

            var rot = Quaternion.Euler(0, 0, lean * Mathf.Rad2Deg);
            Vector2 W(Vector2 p) => feet + (Vector2)(rot * p);
            Set(_figure[0], W(footL), W(hip));
            Set(_figure[1], W(footR), W(hip));
            Set(_figure[2], W(hip), W(neck));
            Set(_figure[3], W(handL), W(neck + new Vector2(0, -3)));
            Set(_figure[4], W(handR), W(neck + new Vector2(0, -3)));
            var ring = _figure[5].Points;
            ring.Clear();
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * Mathf.PI * 2f;
                ring.Add(W(head + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f));
            }
            foreach (var stroke in _figure) stroke.Reveal = visible ? 1f : 0f;
        }

        static void Set(LineArt.Stroke s, params Vector2[] points)
        {
            s.Points.Clear();
            s.Points.AddRange(points);
        }
    }
}
