using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The studio's intro at launch, over everything: on black, the white outline of three peaks traces itself; a little
    /// cartoon climber pulls himself up from behind the small one on the left, leaps over the summit onto the right one,
    /// front-flips back onto the summit and plants a flag reading "PEAK"; then "PEAK DEVELOPMENT" appears letter by letter and
    /// it all fades out on the game. About 7 s; a tap skips to the fade. Around it: a starry sky, a glowing pen tracing the
    /// ridge, a slow camera push-in that shakes on each landing, squash and stretch, motion trails, dust and a burst at the flag.
    /// </summary>
    public sealed class StudioIntro : MonoBehaviour, IPointerDownHandler
    {
        const float LineWidth = 11f, HeroLine = 5f, LimbWidth = 6f;
        const float End = 7.3f, FadeOut = 0.55f;
        // Landings (time, place, strength): dust, squash, camera shake and a footstep.
        static readonly (float at, int spot, float strength)[] Landings = { (2.55f, 0, 0.5f), (3.8f, 1, 0.8f), (4.85f, 2, 1f) };
        const float Plant = 5.05f;
        const float Tracking = 34f, SubTracking = 12f;
        // The climber's size: his drawing is about 120 tall.
        const float HeroScale = 1.5f;

        // The mountain, around the centre of the art: a small peak on the left, the summit in the middle, a middle-sized one on the right.
        static readonly Vector2 BaseLeft = new Vector2(-460, -220), Low = new Vector2(-300, -20), Valley1 = new Vector2(-210, -90);
        static readonly Vector2 Summit = new Vector2(0, 260), Valley2 = new Vector2(160, 0), Mid = new Vector2(275, 115);
        static readonly Vector2 BaseRight = new Vector2(460, -220);
        static readonly Vector2[] Ridge = { BaseLeft, Low, Valley1, Summit, Valley2, Mid, BaseRight };
        // Where the climber lands, coming back from the right: on the summit's right slope, an arm's length from the flag.
        static readonly float SlopeDown = (Summit.y - Valley2.y) / (Valley2.x - Summit.x);
        static readonly Vector2 Perch = new Vector2(Summit.x + 32f * HeroScale, Summit.y - 32f * HeroScale * SlopeDown);

        LineArt _art, _hero, _sky, _fx;
        RectTransform _stage, _underline;
        RectTransform[] _title, _sub;
        float[] _titleWidths, _subWidths;
        Services.AudioService _sfx;
        float _previous = -1f;
        LineArt.Stroke _mountain, _snow, _pole, _flag, _flagOutline;
        RectTransform _peakText;
        CanvasGroup _group, _name;
        Services.AudioService _audio;
        float _time;
        bool _skipped;

        /// <summary>Plays the intro once, on its own canvas above the game's; the music waits for its end.</summary>
        public static void Play(Services.AudioService audio)
        {
            audio?.HoldMusic(true);
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
            intro._audio = intro._sfx = audio;
            intro.Build(go.transform);
        }

        // Skipped, finished or torn down: the game's music fades in.
        void OnDestroy()
        {
            if (_audio != null) _audio.HoldMusic(false);
        }

        static LineArt NewArt(Transform root, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(root, false);
            var art = go.AddComponent<LineArt>();
            art.raycastTarget = false;
            var rect = art.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000, 800);
            rect.anchoredPosition = new Vector2(0, 120);
            return art;
        }

        void Build(Transform screen)
        {
            // Everything on one stage: the camera moves it (push-in, shakes) and fits it to narrow screens.
            var stageGo = new GameObject("Stage", typeof(RectTransform)); // noloc
            stageGo.transform.SetParent(screen, false);
            _stage = (RectTransform)stageGo.transform;
            Stretch(_stage);
            var root = _stage;
            _sky = NewArt(root, "Sky"); // noloc

            // The climber first, behind the mountain while he comes up its far side; brought to the front once on top.
            _hero = NewArt(root, "Hero"); // noloc
            _art = NewArt(root, "Art"); // noloc
            var rect = _art.rectTransform;

            // The mountain's inside, black, down past its foot: it hides the climber behind it.
            for (int i = 1; i < Ridge.Length; i++)
            {
                var a = Ridge[i - 1];
                var b = Ridge[i];
                var inside = _art.Add(0f, a, b, new Vector2(b.x, BaseLeft.y - 200f), new Vector2(a.x, BaseLeft.y - 200f));
                inside.Filled = true;
                inside.Color = Color.black;
            }
            // A soft glow under the ridge line.
            _art.Add(LineWidth * 4f, Ridge).Color = new Color(1f, 1f, 1f, 0.05f);
            _art.Add(LineWidth * 2.2f, Ridge).Color = new Color(1f, 1f, 1f, 0.09f);
            _mountain = _art.Add(LineWidth, Ridge);
            // A snow line across the summit, between its two slopes.
            _snow = _art.Add(LineWidth * 0.75f, new Vector2(-54, 170), new Vector2(-27, 143), new Vector2(0, 170), new Vector2(28, 141), new Vector2(55, 170));
            _pole = _art.Add(6f, Summit, Summit);
            _flag = _art.Add(0f, Summit, Summit, Summit, Summit);
            _flag.Filled = true;
            _flagOutline = _art.Add(3f, Summit, Summit, Summit, Summit);
            _flagOutline.Closed = true;

            // "PEAK" on the flag: black on the white cloth.
            var peak = Label(rect, "PEAK", 30, Color.black); // noloc
            _peakText = peak.rectTransform;
            _peakText.sizeDelta = new Vector2(92, 52);
            _fx = NewArt(root, "Fx"); // noloc

            // The studio's name under the mountain.
            var nameGo = new GameObject("Name", typeof(RectTransform)); // noloc
            nameGo.transform.SetParent(root, false);
            _name = nameGo.AddComponent<CanvasGroup>();
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.sizeDelta = new Vector2(1000, 200);
            nameRect.anchoredPosition = new Vector2(0, -250);
            _title = Letters(nameRect, "PEAK", 96, Color.white, out _titleWidths); // noloc
            _sub = Letters(nameRect, "DEVELOPMENT", 36, new Color(1f, 1f, 1f, 0.78f), out _subWidths); // noloc
            var line = new GameObject("Underline", typeof(RectTransform), typeof(Image)); // noloc
            line.transform.SetParent(nameRect, false);
            line.GetComponent<Image>().raycastTarget = false;
            line.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);
            _underline = (RectTransform)line.transform;
            _underline.anchorMin = _underline.anchorMax = new Vector2(0.5f, 0.5f);
            _underline.anchoredPosition = new Vector2(0, -12);

            Apply(0f);
        }

        /// <summary>A word as one text per letter, each placed by <see cref="Word"/>; their widths, to space them.</summary>
        static RectTransform[] Letters(RectTransform parent, string word, int size, Color color, out float[] widths)
        {
            var letters = new RectTransform[word.Length];
            widths = new float[word.Length];
            for (int i = 0; i < word.Length; i++)
            {
                var t = Label(parent, word[i].ToString(), size, color);
                widths[i] = t.preferredWidth;
                letters[i] = t.rectTransform;
                letters[i].sizeDelta = new Vector2(widths[i] + 24f, size * 1.6f);
            }
            return letters;
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
            float scale = width > 0f ? Mathf.Min(1f, (width - 60f) / 960f) : 1f;
            float t = _skipped ? Mathf.Max(_time, End) : _time;
            float zoom = Mathf.Lerp(0.93f, 1f, Smooth(Phase(t, 0f, 5.4f))) + 0.03f * Bump(t, Landings[2].at, 0.35f) + 0.02f * Bump(t, Plant, 0.3f);
            _stage.localScale = new Vector3(scale * zoom, scale * zoom, 1f);
            _stage.anchoredPosition = Shake(t);
            Sounds(t);
            Apply(_time);
            // The music rises with the fade to the game.
            if (_time >= End && _audio != null) { _audio.HoldMusic(false); _audio = null; }
            if (_time >= End + FadeOut) Destroy(_group.gameObject);
        }

        static float Phase(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>A jump from a to b, rising <paramref name="height"/> above the straight line at mid-flight.</summary>
        static Vector2 Arc(Vector2 a, Vector2 b, float height, float u) => Vector2.Lerp(a, b, u) + new Vector2(0, 4f * height * u * (1f - u));

        /// <summary>1 at <paramref name="at"/>, dying out (quadratically) over <paramref name="length"/>; 0 before.</summary>
        static float Bump(float t, float at, float length)
        {
            if (t < at) return 0f;
            float k = 1f - Phase(t, at, at + length);
            return k * k;
        }

        /// <summary>The camera's shake: a jolt on each landing and when the pole goes in.</summary>
        static Vector2 Shake(float t)
        {
            float amount = 0f;
            foreach (var l in Landings) amount += 14f * l.strength * Bump(t, l.at, 0.3f);
            amount += 12f * Bump(t, Plant, 0.28f);
            return amount <= 0f ? Vector2.zero : new Vector2(Mathf.Sin(t * 93f), Mathf.Cos(t * 71f + 1.3f)) * amount;
        }

        void Sounds(float t)
        {
            float before = _previous;
            _previous = t;
            if (_sfx == null || _skipped || before < 0f) return;
            bool Crossed(float at) => before < at && t >= at;
            if (Crossed(2.2f)) _sfx.Play(Services.Sfx.Climb);
            foreach (var l in Landings) if (Crossed(l.at)) _sfx.Play(Services.Sfx.Step, 0.12f);
            if (Crossed(Plant)) _sfx.Play(Services.Sfx.Bump, 0f);
            if (Crossed(5.55f)) _sfx.Play(Services.Sfx.Coin, 0f);
        }

        void Apply(float t)
        {
            // Skipped: everything in place, the fade starts at once.
            if (_skipped) t = Mathf.Max(t, End);

            _mountain.Reveal = EaseOut(Phase(t, 0.2f, 1.4f));
            _snow.Reveal = Phase(t, 1.2f, 1.6f);

            // The flag: the climber drives the pole into the summit, then the cloth unfurls (to the left, away from him) and flutters.
            float drive = EaseOut(Phase(t, 5.05f, 5.3f));
            float sink = 50f * (1f - drive);
            var top = Summit + new Vector2(0, 160f + sink);
            Set(_pole, Summit + new Vector2(0, sink), top);
            _pole.Reveal = t >= 5.05f ? 1f : 0f;
            float cloth = Phase(t, 5.3f, 5.7f);
            float width = 92f * (cloth < 1f ? EaseOut(cloth) * 1.06f : 1f);
            float flutter = Mathf.Sin(t * 7f) * 4f * cloth;
            var c0 = top;
            var c1 = top + new Vector2(-width, flutter);
            var c2 = top + new Vector2(-width, -52f + flutter);
            var c3 = top + new Vector2(0, -52f);
            Set(_flag, c0, c1, c2, c3);
            Set(_flagOutline, c0, c1, c2, c3);
            _flagOutline.Reveal = cloth > 0f ? 1f : 0f;
            _peakText.anchoredPosition = top + new Vector2(-width * 0.5f, -26f + flutter * 0.5f);
            _peakText.localScale = new Vector3(Mathf.Clamp01(width / 92f), 1f, 1f);
            _peakText.gameObject.SetActive(cloth > 0.3f);

            Climber(t, sink);

            Word(_title, _titleWidths, t, 5.5f, 0.09f, Tracking, 34f);
            Word(_sub, _subWidths, t, 6.0f, 0.035f, SubTracking + 18f * (1f - EaseOut(Phase(t, 6.0f, 6.9f))), -48f);
            float line = EaseOut(Phase(t, 5.85f, 6.35f));
            _underline.sizeDelta = new Vector2(420f * line, 3f);
            Sky(t);
            Effects(t);
            _group.alpha = 1f - Phase(t, End, End + FadeOut);
            _art.Redraw();
            _hero.Redraw();
            _sky.Redraw();
            _fx.Redraw();
        }

        // ---- The studio's name ----

        /// <summary>
        /// Spaces the letters of a word around the centre and drops each one in after the previous: from above, faded,
        /// slightly too big, settling with a small bounce.
        /// </summary>
        static void Word(RectTransform[] letters, float[] widths, float t, float start, float stagger, float tracking, float y)
        {
            float total = tracking * (letters.Length - 1);
            foreach (float w in widths) total += w;
            float x = -total * 0.5f;
            for (int i = 0; i < letters.Length; i++)
            {
                float u = Phase(t, start + i * stagger, start + i * stagger + 0.35f);
                float settle = u < 1f ? 1f + Mathf.Sin(u * Mathf.PI) * 0.22f * (1f - u) : 1f;
                letters[i].anchoredPosition = new Vector2(x + widths[i] * 0.5f, y + 46f * (1f - EaseOut(u)));
                letters[i].localScale = new Vector3(settle, settle, 1f);
                var text = letters[i].GetComponent<Text>();
                var c = text.color;
                c.a = (letters.Length > 4 ? 0.78f : 1f) * Smooth(u);
                text.color = c;
                x += widths[i] + tracking;
            }
        }

        // ---- Sky and effects (redrawn every frame from the time alone) ----

        static float Hash(int i, float salt) => Mathf.Repeat(Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.547f, 1f);

        /// <summary>Stars that come out with the mountain and twinkle (the mountain's black inside hides the low ones), and one shooting star.</summary>
        void Sky(float t)
        {
            _sky.Strokes.Clear();
            float appear = Smooth(Phase(t, 0f, 1.4f));
            for (int i = 0; i < 46; i++)
            {
                var c = new Vector2((Hash(i, 1f) * 2f - 1f) * 560f, Mathf.Lerp(-380f, 820f, Hash(i, 2f)));
                float r = 1.6f + Hash(i, 3f) * 2.4f;
                float twinkle = 0.25f + 0.55f * (0.5f + 0.5f * Mathf.Sin(t * (1.5f + Hash(i, 4f) * 3f) + Hash(i, 5f) * 6.3f));
                Diamond(_sky, c, r, new Color(1f, 1f, 1f, twinkle * appear));
            }
            float u = Phase(t, 1.6f, 2.15f);
            if (u > 0f && u < 1f)
            {
                var head = Vector2.Lerp(new Vector2(480f, 760f), new Vector2(60f, 560f), EaseOut(u));
                var tail = head + new Vector2(150f, 72f) * (1f - u * 0.5f);
                _sky.Add(3f, tail, head).Color = new Color(1f, 1f, 1f, 0.6f * Mathf.Sin(u * Mathf.PI));
            }
        }

        static void Diamond(LineArt art, Vector2 c, float r, Color color)
        {
            var d = art.Add(0f, c + new Vector2(0, r), c + new Vector2(r, 0), c + new Vector2(0, -r), c + new Vector2(-r, 0));
            d.Filled = true;
            d.Color = color;
        }

        static Vector2 SpotOf(int spot) => spot == 0 ? Low : spot == 1 ? Mid : Perch;

        /// <summary>The pen's spark on the ridge being traced, dust at each landing, and a burst when the pole goes in.</summary>
        void Effects(float t)
        {
            _fx.Strokes.Clear();
            float reveal = _mountain.Reveal;
            if (reveal > 0f && reveal < 1f)
            {
                var tip = Along(Ridge, reveal);
                Ring(_fx, tip, 13f, 0f, new Color(1f, 1f, 1f, 0.25f), true);
                Ring(_fx, tip, 7f, 0f, Color.white, true);
                float spin = t * 6f;
                for (int k = 0; k < 4; k++)
                {
                    var dir = new Vector2(Mathf.Cos(spin + k * Mathf.PI / 2f), Mathf.Sin(spin + k * Mathf.PI / 2f));
                    _fx.Add(2.5f, tip + dir * 9f, tip + dir * 22f).Color = new Color(1f, 1f, 1f, 0.7f);
                }
            }
            foreach (var l in Landings)
            {
                float u = Phase(t, l.at, l.at + 0.5f);
                if (u <= 0f || u >= 1f) continue;
                var ground = SpotOf(l.spot);
                float e = EaseOut(u);
                for (int k = 0; k < 6; k++)
                {
                    float side = k < 3 ? -1f : 1f;
                    float reach = (26f + 22f * (k % 3)) * (0.6f + 0.4f * l.strength);
                    var c = ground + new Vector2(side * (14f + reach * e), 6f + (10f + 8f * (k % 3)) * e);
                    Ring(_fx, c, (3f + 7f * u) * (0.7f + 0.3f * l.strength), 3f, new Color(0.85f, 0.85f, 0.85f, 0.75f * (1f - u)), false);
                }
            }
            float burst = Phase(t, Plant, Plant + 0.4f);
            if (burst > 0f && burst < 1f)
            {
                var c = Summit + new Vector2(0, 20f);
                float e = EaseOut(burst);
                for (int k = 0; k < 10; k++)
                {
                    float a = Mathf.PI * (0.05f + 0.9f * k / 9f); // the upper half: the ground hides the rest
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    _fx.Add(4f, c + dir * (30f + 70f * e), c + dir * (44f + 120f * e)).Color = new Color(1f, 1f, 1f, 1f - burst);
                }
            }
        }

        static void Ring(LineArt art, Vector2 c, float r, float width, Color color, bool filled)
        {
            var s = art.Add(width, Ellipse(c, r, r, 14));
            s.Color = color;
            s.Filled = filled;
            s.Closed = !filled;
        }

        /// <summary>The point at <paramref name="share"/> of a polyline's length.</summary>
        static Vector2 Along(Vector2[] points, float share)
        {
            float total = 0f;
            for (int i = 1; i < points.Length; i++) total += Vector2.Distance(points[i - 1], points[i]);
            float left = total * share;
            for (int i = 1; i < points.Length; i++)
            {
                float length = Vector2.Distance(points[i - 1], points[i]);
                if (length >= left) return Vector2.Lerp(points[i - 1], points[i], length > 0f ? left / length : 0f);
                left -= length;
            }
            return points[points.Length - 1];
        }

        /// <summary>A trail behind the climber's body along a jump's arc: thin and faint at its tail.</summary>
        void Trail(float t, float from, float to, Vector2 a, Vector2 b, float height)
        {
            if (t < from || t > to + 0.15f) return;
            float head = Phase(t, from, to);
            float tail = Mathf.Max(0f, head - 0.3f * (1f - Phase(t, to, to + 0.15f)));
            const int Steps = 12;
            var previous = Arc(a, b, height, tail) + Pivot * HeroScale;
            for (int i = 1; i <= Steps; i++)
            {
                float k = i / (float)Steps;
                var point = Arc(a, b, height, Mathf.Lerp(tail, head, k)) + Pivot * HeroScale;
                _hero.Add(3f + 14f * k, previous, point).Color = new Color(1f, 1f, 1f, 0.22f * k);
                previous = point;
            }
        }

        /// <summary>Squashed on landing, stretched on take-off: (width, height) factors around the feet.</summary>
        static Vector2 Squash(float t)
        {
            float s = 0f;
            foreach (var l in Landings) s += 0.24f * l.strength * Bump(t, l.at, 0.22f);
            s += 0.12f * Bump(t, Plant, 0.2f);
            float stretch = 0.16f * (Bump(t, 3.0f, 0.18f) + Bump(t, 4.1f, 0.18f) + Bump(t, 2.2f, 0.15f));
            return new Vector2(1f + 0.8f * s - 0.5f * stretch, 1f - s + stretch);
        }

        // ---- The climber ----

        /// <summary>The climber's limbs, in his own space: feet on the ground at (0, 0), facing right, about 120 tall.</summary>
        struct Pose
        {
            public Vector2 KneeL, FootL, KneeR, FootR, HandL, HandR;
            /// <summary>How far the body sinks (crouching).</summary>
            public float Drop;

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                KneeL = Vector2.Lerp(a.KneeL, b.KneeL, t), FootL = Vector2.Lerp(a.FootL, b.FootL, t),
                KneeR = Vector2.Lerp(a.KneeR, b.KneeR, t), FootR = Vector2.Lerp(a.FootR, b.FootR, t),
                HandL = Vector2.Lerp(a.HandL, b.HandL, t), HandR = Vector2.Lerp(a.HandR, b.HandR, t),
                Drop = Mathf.Lerp(a.Drop, b.Drop, t),
            };
        }

        static Pose P(float kLx, float kLy, float fLx, float fLy, float kRx, float kRy, float fRx, float fRy,
            float hLx, float hLy, float hRx, float hRy, float drop = 0f) => new Pose
        {
            KneeL = new Vector2(kLx, kLy), FootL = new Vector2(fLx, fLy), KneeR = new Vector2(kRx, kRy), FootR = new Vector2(fRx, fRy),
            HandL = new Vector2(hLx, hLy), HandR = new Vector2(hRx, hRy), Drop = drop,
        };

        static readonly Pose Stand = P(-7, 16, -10, 0, 7, 16, 10, 0, -26, 38, 26, 38);
        // Knees bent, arms swung back: landing, or about to jump.
        static readonly Pose Crouch = P(-1, 13, -10, 0, 13, 13, 10, 0, -32, 40, -22, 36, 13);
        static readonly Pose Air = P(-2, 22, -15, 10, 12, 24, 4, 8, -20, 92, 26, 90);
        static readonly Pose Tuck = P(8, 46, -4, 30, 15, 44, 3, 28, 9, 48, 18, 46);
        // Hanging on the far side of the low peak, hands over the top, one then the other.
        static readonly Pose ClimbA = P(-6, 14, -6, -2, 6, 16, 8, 2, -16, 108, 22, 76);
        static readonly Pose ClimbB = P(-6, 16, -8, 2, 6, 14, 6, -2, -22, 76, 16, 108);

        // Pivot of the flip: the middle of the body.
        static readonly Vector2 Pivot = new Vector2(0, 55);

        void Climber(float t, float sink)
        {
            _hero.Strokes.Clear();
            if (t < 1.3f) return;
            Trail(t, 3.0f, 3.8f, Low, Mid, 300f);
            Trail(t, 4.1f, 4.85f, Mid, Perch, 150f);

            Vector2 feet;
            float angle = 0f, slope = 0f, facing = 1f;
            Pose pose;
            bool front = true;
            if (t < 2.2f)
            {
                // Climbing the far side: three pulls, hands over the top first, then the head.
                float pulls = Phase(t, 1.3f, 2.2f) * 3f;
                float lifted = Mathf.Floor(pulls) + Smooth(pulls - Mathf.Floor(pulls));
                feet = new Vector2(Low.x, Mathf.Lerp(Low.y - 150f * HeroScale, Low.y - 50f * HeroScale, lifted / 3f));
                pose = Pose.Lerp(ClimbA, ClimbB, 0.5f + 0.5f * Mathf.Sin(t * 10f));
                front = false;
            }
            else if (t < 2.55f)
            {
                // A hop up onto the peak.
                float u = Phase(t, 2.2f, 2.55f);
                feet = Vector2.Lerp(new Vector2(Low.x, Low.y - 50f * HeroScale), Low, u) + new Vector2(0, Mathf.Sin(u * Mathf.PI) * 45f * HeroScale);
                pose = Air;
                front = u > 0.4f;
            }
            else if (t < 3.0f)
            {
                // Lands, straightens up, crouches for the jump.
                feet = Low;
                pose = Pose.Lerp(Stand, Crouch, Mathf.Clamp01(1f - Phase(t, 2.55f, 2.72f) + Phase(t, 2.82f, 3.0f)));
            }
            else if (t < 3.8f)
            {
                // A huge leap right over the summit, onto the peak on the right.
                float u = Phase(t, 3.0f, 3.8f);
                feet = Arc(Low, Mid, 300f, u);
                pose = Pose.Lerp(Crouch, Air, Mathf.Min(1f, u * 5f));
            }
            else if (t < 4.1f)
            {
                // Lands on the right peak, turns round to face the summit, gathers himself for the flip.
                feet = Mid;
                facing = t < 3.95f ? 1f : -1f;
                pose = Pose.Lerp(Stand, Crouch, 1f - 0.6f * Mathf.Sin(Mathf.PI * Phase(t, 3.8f, 4.1f)));
            }
            else if (t < 4.85f)
            {
                // The front flip back onto the summit: tucked in mid-air, one full turn forward.
                float u = Phase(t, 4.1f, 4.85f);
                feet = Arc(Mid, Perch, 150f, u);
                facing = -1f;
                angle = 360f * Smooth(u);
                pose = Pose.Lerp(Air, Tuck, Mathf.Min(1f, Mathf.Sin(u * Mathf.PI) * 1.8f));
            }
            else
            {
                // Lands on the summit, drives the flag in, then waves.
                feet = Perch;
                facing = -1f;
                slope = SlopeDown;
                pose = Pose.Lerp(Stand, Crouch, 1f - Phase(t, 4.85f, 5.05f));
                var plant = Stand;
                plant.HandR = new Vector2((Perch.x - Summit.x) / HeroScale - 1f, 84f + sink / HeroScale);
                float wave = Phase(t, 5.55f, 5.75f);
                plant.HandL = Vector2.Lerp(Stand.HandL, new Vector2(-24f + Mathf.Sin(t * 11f) * 7f, 98f), wave);
                pose = Pose.Lerp(pose, plant, Phase(t, 4.95f, 5.1f));
            }

            // Behind the mountain while climbing its far side, in front of it afterwards.
            int artIndex = _art.transform.GetSiblingIndex();
            int heroIndex = _hero.transform.GetSiblingIndex();
            if (front != heroIndex > artIndex) _hero.transform.SetSiblingIndex(artIndex);

            DrawClimber(feet, angle, pose, slope, facing, angle == 0f ? Squash(t) : Vector2.one);
        }

        /// <param name="facing">1 facing right, -1 facing left (the drawing mirrored).</param>
        /// <param name="slope">How much the ground rises ahead of him, per unit forward.</param>
        /// <param name="squash">Width and height factors around the feet (squash and stretch).</param>
        void DrawClimber(Vector2 feet, float angle, Pose p, float slope, float facing, Vector2 squash)
        {
            var rot = Quaternion.Euler(0, 0, angle);
            var centre = feet + Pivot * HeroScale;
            Vector2 W(Vector2 local)
            {
                local = new Vector2(local.x * squash.x, local.y * squash.y);
                var l = (local - Pivot) * HeroScale;
                l.x *= facing;
                return centre + (Vector2)(rot * l);
            }
            // Standing on a slope: each foot (and half each knee) follows the ground.
            Vector2 G(Vector2 local, float share) => local + new Vector2(0, local.x * slope * share);

            float d = p.Drop;
            var back = new Color(0.72f, 0.72f, 0.72f);
            var hipL = new Vector2(-7, 32 - d);
            var hipR = new Vector2(7, 32 - d);
            var shoulderL = new Vector2(-13, 58 - d);
            var shoulderR = new Vector2(13, 58 - d);
            var headCentre = new Vector2(0, 90 - d);

            // Far side first, in grey: arm, leg, the little backpack.
            Limb(back, W(shoulderL), W(p.HandL));
            Limb(back, W(hipL), W(G(p.KneeL, 0.5f)), W(G(p.FootL, 1f)));
            Shape(Color.white, back, Ellipse(G(p.FootL, 1f) + new Vector2(3, 2), 8, 5, 10), W);
            Shape(Color.white, back, Ellipse(p.HandL, 5.5f, 5.5f, 10), W);
            Shape(Color.black, Color.white, new[] { new Vector2(-15, 36 - d), new Vector2(-28, 38 - d), new Vector2(-30, 58 - d), new Vector2(-15, 62 - d) }, W);

            // The near leg, the body, the head.
            Limb(Color.white, W(hipR), W(G(p.KneeR, 0.5f)), W(G(p.FootR, 1f)));
            Shape(Color.white, Color.white, Ellipse(G(p.FootR, 1f) + new Vector2(3, 2), 8, 5, 10), W);
            Shape(Color.black, Color.white, Rounded(new Vector2(0, 47 - d), 17, 19, 16), W);
            Shape(Color.black, Color.white, Ellipse(headCentre, 25, 24, 22), W);
            // Big eyes looking ahead, a smile, a tuft of hair.
            for (int k = 0; k < 2; k++)
            {
                var eye = headCentre + new Vector2(5 + k * 11, 3);
                Shape(Color.white, Color.white, Ellipse(eye, 4.5f, 6.5f, 12), W);
                Shape(Color.black, Color.black, Ellipse(eye + new Vector2(1.5f, 0), 2.2f, 3f, 8), W);
            }
            var smile = _hero.Add(3f, W(headCentre + new Vector2(4, -10)), W(headCentre + new Vector2(10, -13)), W(headCentre + new Vector2(17, -9)));
            smile.Color = Color.white;
            _hero.Add(4f, W(headCentre + new Vector2(-6, 23)), W(headCentre + new Vector2(-2, 33)), W(headCentre + new Vector2(7, 34)));

            // The near arm over the body.
            Limb(Color.white, W(shoulderR), W(p.HandR));
            Shape(Color.white, Color.white, Ellipse(p.HandR, 5.5f, 5.5f, 10), W);
        }

        void Limb(Color color, params Vector2[] points)
        {
            var s = _hero.Add(LimbWidth, points);
            s.Color = color;
        }

        /// <summary>A filled convex shape with an outline (the fill hides what is behind it).</summary>
        void Shape(Color fill, Color line, Vector2[] local, System.Func<Vector2, Vector2> toWorld)
        {
            var points = new Vector2[local.Length];
            for (int i = 0; i < local.Length; i++) points[i] = toWorld(local[i]);
            var inside = _hero.Add(0f, points);
            inside.Filled = true;
            inside.Color = fill;
            if (line == fill) return;
            var edge = _hero.Add(HeroLine, points);
            edge.Closed = true;
            edge.Color = line;
        }

        static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n)
        {
            var points = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                float a = k / (float)n * Mathf.PI * 2f;
                points[k] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }
            return points;
        }

        /// <summary>A rounded box (a superellipse), for the body.</summary>
        static Vector2[] Rounded(Vector2 c, float rx, float ry, int n)
        {
            var points = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                float a = k / (float)n * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                points[k] = c + new Vector2(Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), 0.5f) * rx, Mathf.Sign(sin) * Mathf.Pow(Mathf.Abs(sin), 0.5f) * ry);
            }
            return points;
        }

        static void Set(LineArt.Stroke s, params Vector2[] points)
        {
            s.Points.Clear();
            s.Points.AddRange(points);
        }
    }
}
