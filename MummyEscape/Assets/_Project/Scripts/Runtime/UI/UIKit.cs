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
            img.preserveAspect = sprite != null && sprite.border == Vector4.zero;
            return img;
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
            t.text = text;
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

        public static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static LayoutElement Size(Component c, float height = -1, float width = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
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
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            Size(rt, height);
            return h;
        }

        public static Slider Slider(Transform parent, string label, float value, Action<float> onChange, float min = 0f, float max = 1f)
        {
            var row = Rect("Slider " + label, parent);
            Size(row, 130);
            var title = Label(row, label, 38, Sand, TextAnchor.UpperLeft);
            TopBand(title.rectTransform, 50);

            var sliderRt = Rect("Slider", row);
            sliderRt.anchorMin = new Vector2(0, 0);
            sliderRt.anchorMax = new Vector2(1, 0);
            sliderRt.pivot = new Vector2(0.5f, 0);
            sliderRt.sizeDelta = new Vector2(-40, 50);
            sliderRt.anchoredPosition = new Vector2(0, 10);

            var bg = Image(sliderRt, Art.White, new Color(0.15f, 0.12f, 0.1f), true, "Background");
            Stretch(bg.rectTransform, 0, 18, 0, 18);

            var fillArea = Rect("Fill Area", sliderRt);
            Stretch(fillArea, 0, 18, 0, 18);
            var fill = Image(fillArea, Art.White, Gold, false, "Fill");
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", sliderRt);
            Stretch(handleArea, 20, 0, 20, 0);
            var handle = Image(handleArea, Art.ButtonSprite, Color.white, true, "Handle");
            handle.preserveAspect = false;
            handle.rectTransform.sizeDelta = new Vector2(48, 0);

            var s = sliderRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            s.onValueChanged.AddListener(v => onChange?.Invoke(v));
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var row = Rect("Toggle " + label, parent);
            Size(row, 90);
            var text = Label(row, label, 38, Sand, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 0, 0, 140, 0);

            var box = Image(row, Art.Panel, Color.white, true, "Box");
            box.preserveAspect = false;
            var boxRt = box.rectTransform;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(1, 0.5f);
            boxRt.pivot = new Vector2(1, 0.5f);
            boxRt.sizeDelta = new Vector2(120, 70);
            boxRt.anchoredPosition = new Vector2(-10, 0);

            var check = Image(box.transform, Art.White, Turquoise, false, "Check");
            Stretch(check.rectTransform, 16, 16, 16, 16);

            var t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = box;
            t.graphic = check;
            t.isOn = value;
            t.onValueChanged.AddListener(v =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
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
