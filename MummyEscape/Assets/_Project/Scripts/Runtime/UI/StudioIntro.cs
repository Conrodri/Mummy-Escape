using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The studio's intro at launch, over everything: on black, the glowing white outline of three peaks comes out of the
    /// dark while the camera slowly pushes in; "PEAK" drops in letter by letter, a line draws itself under it, then
    /// "DEVELOPMENT" settles into place; it all fades out on the game. About 3 s; a tap skips to the fade.
    /// </summary>
    public sealed class StudioIntro : MonoBehaviour, IPointerDownHandler
    {
        const float LineWidth = 11f;
        const float End = 3.2f, FadeOut = 0.55f;
        const float Tracking = 34f, SubTracking = 12f;

        // The mountain, around the centre of the art: a small peak on the left, the summit in the middle, a middle-sized one on the right.
        static readonly Vector2 BaseLeft = new Vector2(-460, -220), Low = new Vector2(-300, -20), Valley1 = new Vector2(-210, -90);
        static readonly Vector2 Summit = new Vector2(0, 260), Valley2 = new Vector2(160, 0), Mid = new Vector2(275, 115);
        static readonly Vector2 BaseRight = new Vector2(460, -220);
        static readonly Vector2[] Ridge = { BaseLeft, Low, Valley1, Summit, Valley2, Mid, BaseRight };

        LineArt _ridge;
        LineArt.Stroke _glowWide, _glowTight, _mountain, _snow;
        RectTransform _stage, _underline;
        RectTransform[] _title, _sub;
        float[] _titleWidths, _subWidths;
        CanvasGroup _group;
        Services.AudioService _audio, _sfx;
        float _time, _previous = -1f;
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

        void Build(Transform screen)
        {
            // Everything on one stage: the camera pushes it in and fits it to narrow screens.
            var stageGo = new GameObject("Stage", typeof(RectTransform)); // noloc
            stageGo.transform.SetParent(screen, false);
            _stage = (RectTransform)stageGo.transform;
            Stretch(_stage);

            // The ridge in light: a wide soft glow, a tighter one, then the line (blended by their maximum: no seams).
            var go = new GameObject("Ridge", typeof(RectTransform), typeof(CanvasRenderer)); // noloc
            go.transform.SetParent(_stage, false);
            _ridge = go.AddComponent<LineArt>();
            _ridge.raycastTarget = false;
            _ridge.Blend = true;
            var rect = _ridge.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000, 800);
            rect.anchoredPosition = new Vector2(0, 120);
            _glowWide = _ridge.Add(LineWidth * 9f, Ridge);
            _glowWide.Soft = true;
            _glowTight = _ridge.Add(LineWidth * 3f, Ridge);
            _glowTight.Soft = true;
            _mountain = _ridge.Add(LineWidth, Ridge);
            // A snow line across the summit, between its two slopes.
            _snow = _ridge.Add(LineWidth * 0.75f, new Vector2(-54, 170), new Vector2(-27, 143), new Vector2(0, 170), new Vector2(28, 141), new Vector2(55, 170));

            // The studio's name under the mountain.
            var nameGo = new GameObject("Name", typeof(RectTransform)); // noloc
            nameGo.transform.SetParent(_stage, false);
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.sizeDelta = new Vector2(1000, 200);
            nameRect.anchoredPosition = new Vector2(0, -250);
            _title = Letters(nameRect, "PEAK", 96, out _titleWidths); // noloc
            _sub = Letters(nameRect, "DEVELOPMENT", 36, out _subWidths); // noloc
            var line = new GameObject("Underline", typeof(RectTransform), typeof(Image)); // noloc
            line.transform.SetParent(nameRect, false);
            var lineImage = line.GetComponent<Image>();
            lineImage.raycastTarget = false;
            lineImage.color = new Color(1f, 1f, 1f, 0.55f);
            _underline = (RectTransform)line.transform;
            _underline.anchorMin = _underline.anchorMax = new Vector2(0.5f, 0.5f);
            _underline.anchoredPosition = new Vector2(0, -12);

            Apply(0f);
        }

        /// <summary>A word as one text per letter, each placed by <see cref="Word"/>; their widths, to space them.</summary>
        static RectTransform[] Letters(RectTransform parent, string word, int size, out float[] widths)
        {
            var letters = new RectTransform[word.Length];
            widths = new float[word.Length];
            for (int i = 0; i < word.Length; i++)
            {
                var t = Label(parent, word[i].ToString(), size);
                widths[i] = t.preferredWidth;
                letters[i] = t.rectTransform;
                letters[i].sizeDelta = new Vector2(widths[i] + 24f, size * 1.6f);
            }
            return letters;
        }

        static Text Label(RectTransform parent, string text, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform)); // noloc
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.Load<Font>("Fonts/Nunito-ExtraBold") ?? UIKit.BoldFont; // noloc
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
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
            // Narrow screens: the whole drawing shrinks to fit the width; a slow push-in on top.
            float width = ((RectTransform)_group.transform).rect.width;
            float scale = width > 0f ? Mathf.Min(1f, (width - 60f) / 960f) : 1f;
            float zoom = Mathf.Lerp(0.94f, 1f, EaseOut(Phase(_time, 0f, End)));
            _stage.localScale = new Vector3(scale * zoom, scale * zoom, 1f);
            Apply(_time);
            // The music rises with the fade to the game.
            if (_time >= End && _audio != null) { _audio.HoldMusic(false); _audio = null; }
            if (_time >= End + FadeOut) Destroy(_group.gameObject);
        }

        static float Phase(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        static float Smooth(float x) => x * x * (3f - 2f * x);

        void Apply(float t)
        {
            // Skipped: everything in place, the fade starts at once.
            if (_skipped) t = Mathf.Max(t, End);

            // The mountain comes out of the dark, its glow breathing slowly.
            float appear = Smooth(Phase(t, 0f, 0.8f));
            _mountain.Color = _snow.Color = new Color(1f, 1f, 1f, appear);
            float breath = 0.85f + 0.15f * Mathf.Sin(t * 2.2f);
            _glowWide.Color = new Color(1f, 1f, 1f, 0.16f * appear * breath);
            _glowTight.Color = new Color(1f, 1f, 1f, 0.32f * appear * breath);
            _ridge.Redraw();

            Word(_title, _titleWidths, t, 0.7f, 0.09f, Tracking, 34f, 1f);
            _underline.sizeDelta = new Vector2(420f * EaseOut(Phase(t, 1.05f, 1.55f)), 3f);
            Word(_sub, _subWidths, t, 1.2f, 0.035f, SubTracking + 18f * (1f - EaseOut(Phase(t, 1.2f, 2.1f))), -48f, 0.78f);

            if (_sfx != null && !_skipped && _previous < 0.7f && t >= 0.7f) _sfx.Play(Services.Sfx.Coin, 0f);
            _previous = t;
            _group.alpha = 1f - Phase(t, End, End + FadeOut);
        }

        /// <summary>
        /// Spaces the letters of a word around the centre and drops each one in after the previous: from above, faded,
        /// slightly too big, settling with a small bounce.
        /// </summary>
        static void Word(RectTransform[] letters, float[] widths, float t, float start, float stagger, float tracking, float y, float opacity)
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
                c.a = opacity * Smooth(u);
                text.color = c;
                x += widths[i] + tracking;
            }
        }
    }
}
