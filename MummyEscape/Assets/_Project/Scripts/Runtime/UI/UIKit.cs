using System;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>Tiny code-first uGUI toolkit with the game's Egyptian styling.</summary>
    public static class UIKit
    {
        public static readonly Color Gold = new Color32(232, 195, 90, 255);
        public static readonly Color Sand = new Color32(241, 227, 196, 255);
        public static readonly Color Dim = new Color32(156, 139, 112, 255);
        public static readonly Color Lapis = new Color32(38, 64, 140, 255);
        public static readonly Color Turquoise = new Color32(64, 224, 208, 255);
        public static readonly Color Danger = new Color32(214, 84, 64, 255);
        public static readonly Color Shade = new Color(0f, 0f, 0f, 0.72f);

        public static Font Font { get; private set; }
        public static ArtLibrary Art { get; private set; }

        public static void Init(ArtLibrary art)
        {
            Art = art;
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors to a horizontal band at the top (height in reference pixels).</summary>
        public static RectTransform TopBand(RectTransform rt, float height, float y = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, -y);
            return rt;
        }

        public static RectTransform BottomBand(RectTransform rt, float height, float y = 0)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, y);
            return rt;
        }

        public static Image Image(Transform parent, Sprite sprite, Color color, bool raycast = false, string name = "Image")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            // Icons keep their aspect; the plain white sprite is used for colour fills and must stretch.
            img.preserveAspect = sprite != null && sprite.border == Vector4.zero && sprite != Art.White;
            return img;
        }

        public static readonly Color BackdropColor = new Color32(20, 14, 9, 255);

        /// <summary>Opaque full-screen background, bleeding under the notch / home bar (behind the safe area).</summary>
        public static Image Backdrop(Transform screenRoot, Color? color = null)
        {
            var img = Image(screenRoot, Art.White, color ?? BackdropColor, true, "Backdrop");
            Stretch(img.rectTransform, -600, -600, -600, -600);
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>Framed panel that sizes itself to its vertical content.</summary>
        public static RectTransform Card(Transform parent, int padding = 36, float spacing = 18)
        {
            var img = Panel(parent, "Card");
            img.raycastTarget = false;
            Column(img.transform, spacing, padding);
            return img.rectTransform;
        }

        public static Text SectionTitle(Transform parent, string text)
        {
            var t = Label(parent, text, 44, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            Size(t, 64);
            return t;
        }

        /// <summary>Full-width tappable list item (dark plate) with a horizontal layout for its content.</summary>
        public static Image ListItem(Transform parent, float height, Action onClick, out HorizontalLayoutGroup content, bool highlight = false)
        {
            var img = Image(parent, Art.White, highlight ? new Color(0.16f, 0.42f, 0.42f, 0.75f) : new Color(1f, 0.92f, 0.75f, 0.07f), true, "Item");
            Size(img, height);
            img.raycastTarget = onClick != null;
            if (onClick != null)
            {
                var btn = img.gameObject.AddComponent<Button>();
                var colors = btn.colors;
                colors.pressedColor = new Color(0.7f, 0.65f, 0.55f);
                colors.highlightedColor = Color.white;
                btn.colors = colors;
                btn.onClick.AddListener(() =>
                {
                    App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                    onClick();
                });
            }
            content = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            content.padding = new RectOffset(28, 28, 8, 8);
            content.spacing = 20;
            content.childAlignment = TextAnchor.MiddleLeft;
            content.childControlWidth = content.childControlHeight = true;
            content.childForceExpandWidth = false;
            content.childForceExpandHeight = true;
            return img;
        }

        /// <summary>Row of mutually exclusive tabs.</summary>
        public sealed class Segmented
        {
            readonly Button[] _buttons;
            readonly Text[] _labels;
            public int Selected { get; private set; } = -1;

            public Segmented(Transform parent, string[] labels, Action<int> onSelect, float height = 110)
            {
                var row = Row(parent, height, 12);
                _buttons = new Button[labels.Length];
                _labels = new Text[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    _buttons[i] = Button(row.transform, labels[i], () => { Select(index); onSelect?.Invoke(index); }, 40);
                    _labels[i] = _buttons[i].GetComponentInChildren<Text>();
                    Size(_buttons[i], -1, -1, 1);
                }
            }

            public void SetLabel(int i, string text) => _labels[i].text = Loc.T(text);

            public void Select(int index)
            {
                Selected = index;
                for (int i = 0; i < _buttons.Length; i++)
                {
                    bool on = i == index;
                    _buttons[i].image.color = on ? Color.white : new Color(0.38f, 0.34f, 0.3f, 0.9f);
                    _labels[i].color = on ? Gold : Dim;
                }
            }
        }

        public static Image Panel(Transform parent, string name = "Panel")
        {
            var img = Image(parent, Art.Panel, Color.white, true, name);
            img.preserveAspect = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = Loc.T(text); // static texts are translated here; dynamic ones use Loc.F at the call site
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, int fontSize = 44, Sprite sprite = null)
        {
            var img = Image(parent, sprite ?? Art.ButtonSprite, Color.white, true, "Button");
            img.preserveAspect = false;
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 0.95f, 0.8f);
            colors.pressedColor = new Color(0.75f, 0.7f, 0.6f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                onClick?.Invoke();
            });
            if (!string.IsNullOrEmpty(label))
            {
                var text = Label(img.transform, label, fontSize, Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(text.rectTransform, 16, 8, 16, 8);
            }
            return btn;
        }

        /// <summary>Shrinks the text (down to minSize) so it stays on its line instead of overflowing.</summary>
        public static Text FitText(Text t, int minSize)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = minSize;
            t.resizeTextMaxSize = t.fontSize;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = Loc.T(text);
        }

        public static LayoutElement Size(Component c, float height = -1, float width = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            // A fixed size means "don't stretch" unless a flexible size is given explicitly
            // (otherwise a layout group child such as a Row reports itself as flexible and eats the free space).
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; if (flexHeight < 0) le.flexibleHeight = 0; }
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; if (flexWidth < 0) le.flexibleWidth = 0; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return le;
        }

        public static VerticalLayoutGroup Column(Transform t, float spacing = 20, int padding = 0, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = t.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing = 16)
        {
            var rt = Rect("Row", parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            // Children share the width according to their LayoutElement (flexWidth 1 = fill, fixed width = fixed).
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            Size(rt, height);
            return h;
        }

        /// <summary>Labelled slider with its value shown on the right. format turns the value into text (default: percent).</summary>
        public static Slider Slider(Transform parent, string label, float value, Action<float> onChange, float min = 0f, float max = 1f, Func<float, string> format = null)
        {
            format = format ?? (v => Mathf.RoundToInt((v - min) / (max - min) * 100f) + " %");
            var row = Rect("Slider " + label, parent);
            Size(row, 140);
            var title = Label(row, label, 40, Sand, TextAnchor.MiddleLeft);
            TopBand(title.rectTransform, 56);
            var valueText = Label(row, format(value), 36, Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            TopBand(valueText.rectTransform, 56);

            var sliderRt = Rect("Slider", row);
            BottomBand(sliderRt, 70, 0);
            sliderRt.offsetMin = new Vector2(10, sliderRt.offsetMin.y);
            sliderRt.offsetMax = new Vector2(-10, sliderRt.offsetMax.y);

            var bg = Image(sliderRt, Art.White, new Color(0f, 0f, 0f, 0.55f), true, "Track");
            Stretch(bg.rectTransform, 0, 25, 0, 25);

            var fillArea = Rect("Fill Area", sliderRt);
            Stretch(fillArea, 0, 25, 0, 25);
            var fill = Image(fillArea, Art.White, Gold, false, "Fill");
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", sliderRt);
            Stretch(handleArea, 30, 0, 30, 0);
            var handle = Image(handleArea, Art.ButtonSprite, Color.white, true, "Handle");
            handle.preserveAspect = false;
            handle.rectTransform.sizeDelta = new Vector2(60, 0);

            var s = sliderRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            s.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                onChange?.Invoke(v);
            });
            return s;
        }

        /// <summary>Labelled on/off switch (whole row is tappable).</summary>
        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var row = Image(parent, Art.White, new Color(0, 0, 0, 0), true, "Toggle " + label);
            Size(row, 100);
            var text = Label(row.transform, label, 40, Sand, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 0, 0, 190, 0);

            var track = Image(row.transform, Art.White, Color.white, false, "Track");
            var trt = track.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(1, 0.5f);
            trt.pivot = new Vector2(1, 0.5f);
            trt.sizeDelta = new Vector2(150, 64);
            trt.anchoredPosition = new Vector2(-10, 0);
            var knob = Image(track.transform, Art.ButtonSprite, Color.white, false, "Knob");
            knob.preserveAspect = false;
            var krt = knob.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0, 0.5f);
            krt.sizeDelta = new Vector2(70, 76);
            var state = Label(track.transform, "", 26, Sand, TextAnchor.MiddleCenter, FontStyle.Bold);

            var t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = row;
            t.transition = Selectable.Transition.None;
            void Paint(bool on)
            {
                track.color = on ? new Color(0.13f, 0.62f, 0.56f) : new Color(0.22f, 0.18f, 0.15f);
                krt.anchoredPosition = new Vector2(on ? 112 : 38, 0);
                state.text = on ? Loc.T("ON") : Loc.T("OFF");
                var srt = state.rectTransform;
                Stretch(srt, on ? 8 : 76, 0, on ? 76 : 8, 0);
            }
            t.isOn = value;
            Paint(value);
            t.onValueChanged.AddListener(v =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                Paint(v);
                onChange?.Invoke(v);
            });
            return t;
        }

        public static InputField Input(Transform parent, string placeholder, int fontSize = 40)
        {
            var bg = Image(parent, Art.Panel, Color.white, true, "Input");
            bg.preserveAspect = false;
            Size(bg, 100);
            var text = Label(bg.transform, "", fontSize, Sand, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 24, 8, 24, 8);
            text.supportRichText = false;
            var ph = Label(bg.transform, placeholder, fontSize, Dim, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 24, 8, 24, 8);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = 20;
            return field;
        }

        /// <summary>Vertical scroll list; add rows to the returned content.</summary>
        public static RectTransform Scroll(Transform parent, out ScrollRect scroll)
        {
            var root = Rect("Scroll", parent);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.25f);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            var viewport = Rect("Viewport", root);
            Stretch(viewport, 8, 8, 8, 8);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            Column(content, 12, 8);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Row of 3 stars (filled up to count).</summary>
        public static RectTransform Stars(Transform parent, int count, float size)
        {
            var row = Rect("Stars", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = size * 0.15f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            for (int i = 0; i < 3; i++)
            {
                var s = Image(row, i < count ? Art.Star : Art.StarEmpty, Color.white);
                Size(s, size, size);
            }
            Size(row, size);
            return row;
        }
    }
}
