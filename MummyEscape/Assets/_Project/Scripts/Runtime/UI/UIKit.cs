using System;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    public enum ButtonStyle
    {
        /// <summary>Glossy gold, dark text: the one action the screen is about.</summary>
        Primary,
        /// <summary>Dark plate with a thin gold rim.</summary>
        Secondary,
        /// <summary>Text only (links, discreet actions).</summary>
        Ghost,
        /// <summary>Glossy red: destructive actions.</summary>
        Danger,
    }

    /// <summary>
    /// Tiny code-first uGUI toolkit: dark warm surfaces, rounded corners, gold accents, Cinzel titles and Nunito text.
    /// Static texts passed to it are translated (<see cref="Loc.T"/>).
    /// </summary>
    public static class UIKit
    {
        public static readonly Color Gold = new Color32(232, 195, 90, 255);
        public static readonly Color Sand = new Color32(241, 227, 196, 255);
        public static readonly Color Dim = new Color32(156, 139, 112, 255);
        public static readonly Color Lapis = new Color32(38, 64, 140, 255);
        public static readonly Color Turquoise = new Color32(64, 224, 208, 255);
        public static readonly Color Danger = new Color32(214, 84, 64, 255);
        public static readonly Color Shade = new Color(0.03f, 0.02f, 0.01f, 0.78f);
        /// <summary>Text on gold.</summary>
        public static readonly Color Ink = new Color32(46, 28, 10, 255);
        /// <summary>Card background.</summary>
        public static readonly Color Surface = new Color32(31, 24, 18, 250);
        /// <summary>Raised element on a card (secondary button, list row, tile).</summary>
        public static readonly Color SurfaceHi = new Color32(52, 41, 31, 255);
        public static readonly Color Rim = new Color(0.91f, 0.76f, 0.35f, 0.28f);
        public static readonly Color Success = new Color32(52, 178, 150, 255);

        public const float ButtonHeight = 96;
        public const float SmallButtonHeight = 80;
        public const int TextSize = 34;

        public static Font Font { get; private set; }
        public static Font BoldFont { get; private set; }
        public static Font DisplayFont { get; private set; }
        public static ArtLibrary Art { get; private set; }

        public static void Init(ArtLibrary art)
        {
            Art = art;
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Font = Resources.Load<Font>("Fonts/Nunito-SemiBold") ?? fallback;
            BoldFont = Resources.Load<Font>("Fonts/Nunito-ExtraBold") ?? Font;
            DisplayFont = Resources.Load<Font>("Fonts/Cinzel-Bold") ?? BoldFont;
            UISprites.Init();
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

        /// <summary>Fixed-size rect anchored at (ax, ay) of its parent, positioned relative to that anchor.</summary>
        public static RectTransform Place(RectTransform rt, float ax, float ay, float w, float h, float x = 0, float y = 0)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
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

        /// <summary>Gives a 9-sliced rounded sprite the wanted corner radius (reference pixels).</summary>
        public static Image Rounded(Image img, float radius)
        {
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.preserveAspect = false;
            img.pixelsPerUnitMultiplier = UISprites.Radius / Mathf.Max(1f, radius);
            return img;
        }

        /// <summary>Rounded plate (any colour), optionally with a thin rim.</summary>
        public static Image Plate(Transform parent, Color color, float radius, Color? rim = null, bool gloss = false, string name = "Plate")
        {
            var img = Rounded(Image(parent, gloss ? UISprites.RoundGloss : UISprites.Round, color, false, name), radius);
            if (rim.HasValue) AddRim(img, rim.Value, radius);
            return img;
        }

        /// <summary>Thin outline drawn over a rounded plate (ignored by layouts).</summary>
        public static Image AddRim(Image plate, Color color, float radius)
        {
            var rim = Rounded(Image(plate.transform, UISprites.RoundOutline, color, false, "Rim"), radius);
            Stretch(rim.rectTransform);
            rim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return rim;
        }

        /// <summary>Soft drop shadow under an element.</summary>
        public static void DropShadow(Graphic g, float distance = 6f, float alpha = 0.4f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, alpha);
            s.effectDistance = new Vector2(0, -distance);
        }

        public static readonly Color BackdropColor = new Color32(17, 12, 9, 255);

        /// <summary>Opaque full-screen background with a warm glow at the top, bleeding under the notch / home bar.</summary>
        public static Image Backdrop(Transform screenRoot, Color? color = null)
        {
            var img = Image(screenRoot, Art.White, color ?? BackdropColor, true, "Backdrop");
            Stretch(img.rectTransform, -600, -600, -600, -600);
            img.transform.SetAsFirstSibling();
            var glow = Image(img.transform, UISprites.RadialGlow, new Color(0.95f, 0.6f, 0.25f, 0.13f), false, "Glow");
            glow.preserveAspect = false;
            Place(glow.rectTransform, 0.5f, 1f, 2200, 1700, 0, -150);
            return img;
        }


        /// <summary>Card: rounded dark panel with a gold rim, sizing itself to its vertical content.</summary>
        public static RectTransform Card(Transform parent, int padding = 36, float spacing = 18)
        {
            var img = Panel(parent, "Card");
            img.raycastTarget = false;
            Column(img.transform, spacing, padding);
            return img.rectTransform;
        }

        /// <summary>Small caps title of a card section.</summary>
        public static Text SectionTitle(Transform parent, string text)
        {
            var t = Label(parent, text, 28, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.text = t.text.ToUpperInvariant();
            Size(t, 48);
            return t;
        }

        /// <summary>Full-width tappable list row (rounded plate) with a horizontal layout for its content.</summary>
        public static Image ListItem(Transform parent, float height, Action onClick, out HorizontalLayoutGroup content, bool highlight = false)
        {
            var img = Plate(parent, highlight ? new Color32(30, 92, 84, 255) : SurfaceHi, 22, highlight ? new Color(0.25f, 0.88f, 0.8f, 0.5f) : (Color?)null, false, "Item"); // noloc
            Size(img, height);
            img.raycastTarget = onClick != null;
            if (onClick != null)
            {
                var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // set by Awake only when built active
                var colors = btn.colors;
                colors.pressedColor = new Color(0.8f, 0.78f, 0.72f);
                colors.highlightedColor = Color.white;
                colors.selectedColor = Color.white;
                btn.colors = colors;
                btn.onClick.AddListener(() =>
                {
                    App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                    onClick();
                });
                img.gameObject.AddComponent<PressScale>().Amount = 0.98f;
            }
            content = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            content.padding = new RectOffset(30, 30, 8, 8);
            content.spacing = 20;
            content.childAlignment = TextAnchor.MiddleLeft;
            content.childControlWidth = content.childControlHeight = true;
            content.childForceExpandWidth = false;
            content.childForceExpandHeight = true;
            return img;
        }

        /// <summary>Row of mutually exclusive tabs inside a dark pill: the selected one turns gold.</summary>
        public sealed class Segmented
        {
            readonly Button[] _buttons;
            readonly Text[] _labels;
            public int Selected { get; private set; } = -1;

            public Segmented(Transform parent, string[] labels, Action<int> onSelect, float height = 84)
            {
                var track = Plate(parent, new Color(0, 0, 0, 0.4f), height / 2f, null, false, "Tabs"); // noloc
                Size(track, height);
                var row = track.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(6, 6, 6, 6);
                row.spacing = 6;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = true;
                _buttons = new Button[labels.Length];
                _labels = new Text[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    _buttons[i] = Button(track.transform, labels[i], () => { Select(index); onSelect?.Invoke(index); }, 30, ButtonStyle.Ghost);
                    Rounded(_buttons[i].image, (height - 12) / 2f);
                    _labels[i] = _buttons[i].GetComponentInChildren<Text>();
                    // Narrow tabs: keep only a small side margin so the text can stay big.
                    _labels[i].rectTransform.offsetMin = new Vector2(6, _labels[i].rectTransform.offsetMin.y);
                    _labels[i].rectTransform.offsetMax = new Vector2(-6, _labels[i].rectTransform.offsetMax.y);
                    // Equal shares of the track whatever the label lengths (shrinking the text if needed).
                    Size(_buttons[i], -1, 0, 1);
                }
                // One line per tab, every tab at the size of the longest label ("Mummies" never splits in two).
                track.gameObject.AddComponent<OneLineLabels>().Init(_labels, 30, 16);
            }

            /// <summary>The track holding the tabs (to hide them all).</summary>
            public GameObject Root => _buttons[0].transform.parent.gameObject;

            public void SetLabel(int i, string text) => _labels[i].text = Loc.T(text);

            public void Select(int index)
            {
                Selected = index;
                for (int i = 0; i < _buttons.Length; i++)
                {
                    bool on = i == index;
                    _buttons[i].image.sprite = on ? UISprites.RoundGloss : UISprites.Round;
                    _buttons[i].image.color = on ? Gold : new Color(1, 1, 1, 0);
                    _labels[i].color = on ? Ink : Dim;
                    var shadow = _labels[i].GetComponent<Shadow>();
                    if (shadow != null) shadow.enabled = !on;
                }
            }
        }

        /// <summary>Rounded dark panel with a gold rim and a drop shadow.</summary>
        public static Image Panel(Transform parent, string name = "Panel")
        {
            var img = Plate(parent, Surface, 36, Rim, false, name);
            img.raycastTarget = true;
            DropShadow(img, 10, 0.45f);
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            bool italic = style == FontStyle.Italic || style == FontStyle.BoldAndItalic;
            t.font = bold ? BoldFont : Font;
            t.text = Loc.T(text); // static texts are translated here; dynamic ones use Loc.F at the call site
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.45f);
            shadow.effectDistance = new Vector2(0, -2);
            return t;
        }

        /// <summary>Title in the display face (Cinzel), with a gold gradient when coloured gold.</summary>
        public static Text Title(Transform parent, string text, int size, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var c = color ?? Gold;
            var t = Label(parent, text, size, Color.white, align);
            t.font = DisplayFont;
            var g = t.gameObject.AddComponent<UIGradient>();
            g.Top = Color.Lerp(c, Color.white, 0.35f);
            g.Bottom = c * new Color(0.82f, 0.72f, 0.6f, 1f);
            return t;
        }

        /// <summary>Recolours a <see cref="Title"/>.</summary>
        public static void TintTitle(Text title, Color c)
        {
            var g = title.GetComponent<UIGradient>();
            if (g == null) { title.color = c; return; }
            g.Top = Color.Lerp(c, Color.white, 0.35f);
            g.Bottom = c * new Color(0.82f, 0.72f, 0.6f, 1f);
            title.SetVerticesDirty();
        }

        public static Button Button(Transform parent, string label, Action onClick, int fontSize = TextSize, ButtonStyle style = ButtonStyle.Secondary)
        {
            Image img;
            Color text;
            switch (style)
            {
                case ButtonStyle.Primary: img = Plate(parent, Gold, 26, null, true, "Button"); text = Ink; break; // noloc
                case ButtonStyle.Danger: img = Plate(parent, Danger, 26, null, true, "Button"); text = Color.white; break; // noloc
                case ButtonStyle.Ghost: img = Plate(parent, new Color(1, 1, 1, 0), 26, null, false, "Button"); text = Gold; break; // noloc
                default: img = Plate(parent, SurfaceHi, 26, Rim, false, "Button"); text = Sand; break; // noloc
            }
            img.raycastTarget = true;
            if (style == ButtonStyle.Primary || style == ButtonStyle.Danger) DropShadow(img, 6, 0.45f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // set by Awake only when built active
            var colors = btn.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.8f, 0.75f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f);
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                onClick?.Invoke();
            });
            img.gameObject.AddComponent<PressScale>();
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label(img.transform, label, fontSize, text, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(t.rectTransform, 18, 6, 18, 6);
                if (style == ButtonStyle.Primary) t.GetComponent<Shadow>().effectColor = new Color(1, 1, 1, 0.25f);
            }
            return btn;
        }

        /// <summary>Round button holding a line icon.</summary>
        public static Button IconButton(Transform parent, Sprite icon, Action onClick, float size = 96, ButtonStyle style = ButtonStyle.Secondary)
        {
            // In a layout, a fixed slot keeps the button round even when the row stretches its children.
            bool laidOut = parent.GetComponent<HorizontalOrVerticalLayoutGroup>() != null;
            if (laidOut)
            {
                var slot = Rect("IconSlot", parent);
                Size(slot, size, size);
                parent = slot;
            }
            var btn = Button(parent, null, onClick, TextSize, style);
            var img = btn.image;
            img.sprite = style == ButtonStyle.Primary || style == ButtonStyle.Danger ? UISprites.RoundGloss : UISprites.Round;
            Rounded(img, size / 2f);
            var rim = img.transform.Find("Rim")?.GetComponent<Image>();
            if (rim != null) Rounded(rim, size / 2f);
            var glyph = Image(img.transform, icon, style == ButtonStyle.Primary ? Ink : Sand, false, "Icon");
            Place(glyph.rectTransform, 0.5f, 0.5f, size * 0.5f, size * 0.5f);
            if (laidOut) Place((RectTransform)btn.transform, 0.5f, 0.5f, size, size);
            else ((RectTransform)btn.transform).sizeDelta = new Vector2(size, size);
            return btn;
        }

        /// <summary>Icon button with its caption underneath (secondary actions laid out in a row).</summary>
        public static Button IconAction(Transform parent, Sprite icon, string caption, Action onClick, float size = 104)
        {
            var col = Rect("Action " + caption, parent);
            Column(col, 10, 0, TextAnchor.UpperCenter).childForceExpandWidth = false;
            var btn = IconButton(col, icon, onClick, size);
            var t = Label(col, caption, 26, Dim, TextAnchor.UpperCenter, FontStyle.Bold);
            FitText(t, 18);
            Size(t, 40, size + 60);
            Size(col, size + 50, size + 60);
            return btn;
        }

        /// <summary>Pill with an icon and a short value (wallet, rewards). Returns the value text.</summary>
        public static Text Chip(Transform parent, Sprite icon, string value, Color? tint = null, float height = 72)
        {
            var plate = Plate(parent, new Color(0, 0, 0, 0.42f), height / 2f, tint.HasValue ? new Color(tint.Value.r, tint.Value.g, tint.Value.b, 0.45f) : Rim, false, "Chip"); // noloc
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(icon != null ? 14 : 28, 28, 8, 8);
            h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (parent.GetComponent<LayoutGroup>() == null)
                plate.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (icon != null) Size(Image(plate.transform, icon, Color.white, false, "Icon"), height - 22, height - 22);
            var t = Label(plate.transform, "", Mathf.RoundToInt(height * 0.44f), tint ?? Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.text = value;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(t, height - 16);
            Size(plate, height);
            return t;
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
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
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
            Size(row, 112);
            var title = Label(row, label, TextSize, Sand, TextAnchor.MiddleLeft);
            TopBand(title.rectTransform, 50);
            var valueText = Label(row, format(value), 30, Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            TopBand(valueText.rectTransform, 50);

            var sliderRt = Rect("Slider", row);
            BottomBand(sliderRt, 56, 0);

            var bg = Plate(sliderRt, new Color(0f, 0f, 0f, 0.5f), 7, null, false, "Track"); // noloc
            bg.raycastTarget = true;
            Stretch(bg.rectTransform, 0, 21, 0, 21);

            var fillArea = Rect("Fill Area", sliderRt);
            Stretch(fillArea, 0, 21, 0, 21);
            var fill = Plate(fillArea, Gold, 7, null, true, "Fill"); // noloc
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", sliderRt);
            Stretch(handleArea, 26, 0, 26, 0);
            var handle = Image(handleArea, UISprites.Circle, Sand, true, "Handle");
            handle.preserveAspect = true;
            handle.rectTransform.sizeDelta = new Vector2(52, 0);
            DropShadow(handle, 4, 0.5f);

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
            Size(row, 88);
            var text = Label(row.transform, label, TextSize, Sand, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 0, 0, 150, 0);

            var track = Plate(row.transform, Color.white, 30, null, false, "Track"); // noloc
            Place(track.rectTransform, 1f, 0.5f, 108, 60, -4, 0);
            var knob = Image(track.transform, UISprites.Circle, Color.white, false, "Knob");
            knob.preserveAspect = true;
            var krt = knob.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0, 0.5f);
            krt.sizeDelta = new Vector2(48, 48);
            DropShadow(knob, 3, 0.4f);

            var t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = row;
            t.transition = Selectable.Transition.None;
            void Paint(bool on)
            {
                track.color = on ? Success : new Color32(70, 58, 47, 255);
                knob.color = on ? Color.white : new Color32(200, 188, 168, 255);
                krt.anchoredPosition = new Vector2(on ? 78 : 30, 0);
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

        public static InputField Input(Transform parent, string placeholder, int fontSize = 38)
        {
            var bg = Plate(parent, new Color(0, 0, 0, 0.4f), 22, Rim, false, "Input"); // noloc
            bg.raycastTarget = true;
            Size(bg, 96);
            var text = Label(bg.transform, "", fontSize, Sand, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 28, 8, 28, 8);
            text.supportRichText = false;
            var ph = Label(bg.transform, placeholder, fontSize, Dim, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 28, 8, 28, 8);
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
            var bg = Rounded(root.gameObject.AddComponent<Image>(), 28);
            bg.sprite = UISprites.Round;
            bg.color = new Color(0, 0, 0, 0.22f);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
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

        /// <summary>Row of ankhs, full for the life left and hollow for the life lost (as in the HUD).</summary>
        public static RectTransform Ankhs(Transform parent, int hp, int max, float size)
        {
            var row = Rect("Ankhs", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = size * 0.12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            for (int i = 0; i < max; i++) Size(Image(row, i < hp ? Art.Ankh : Art.AnkhEmpty, Color.white), size, size);
            Size(row, size);
            return row;
        }

        /// <summary>
        /// Fixed-column grid laid out at its design size inside a stretching slot, scaled down as a whole when the slot is
        /// narrower or shorter (shrinking the cells instead would crush their content). Size the slot like any layout child.
        /// </summary>
        public static GridLayoutGroup FittedGrid(Transform parent, int columns, Vector2 cell, Vector2 spacing, out RectTransform slot)
        {
            slot = Rect("GridSlot", parent);
            var rt = Rect("Grid", slot);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(columns * cell.x + (columns - 1) * spacing.x, 0);
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = spacing;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperCenter;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            FitInParent(rt, 0);
            return g;
        }

        /// <summary>Keeps a fixed-size modal panel inside its parent (the safe area) on narrow or short screens.</summary>
        public static RectTransform FitInParent(RectTransform rt, float margin = 28)
        {
            if (!rt.TryGetComponent<FitInParent>(out var fit)) fit = rt.gameObject.AddComponent<FitInParent>();
            fit.Margin = margin;
            return rt;
        }
    }

    /// <summary>
    /// Scales a panel down uniformly when it is wider or taller than its parent minus a margin: fixed reference widths
    /// (~950) overflow the canvas on tall phones once the safe area (notch, rounded corners) is taken out.
    /// </summary>
    public sealed class FitInParent : MonoBehaviour
    {
        public float Margin = 28;
        RectTransform _rt;

        void LateUpdate()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;
            Vector2 size = _rt.rect.size, room = parent.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            // Panels anchored to the top keep their offset from it: only the space below counts.
            float top = _rt.anchorMin.y >= 1f ? -_rt.anchoredPosition.y : 0f;
            float k = Mathf.Min(1f, (room.x - 2 * Margin) / size.x, (room.y - top - 2 * Margin) / size.y);
            k = Mathf.Max(0.5f, k);
            if (Mathf.Abs(_rt.localScale.x - k) > 0.001f) _rt.localScale = new Vector3(k, k, 1f);
        }
    }

    /// <summary>Shrinks a control slightly while it is pressed.</summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float Amount = 0.95f;
        Selectable _selectable;
        float _target = 1f;

        void Awake() => _selectable = GetComponent<Selectable>();
        void OnDisable() { _target = 1f; transform.localScale = Vector3.one; }

        public void OnPointerDown(PointerEventData e)
        {
            if (_selectable == null || _selectable.IsInteractable()) _target = Amount;
        }

        public void OnPointerUp(PointerEventData e) => _target = 1f;
        public void OnPointerExit(PointerEventData e) => _target = 1f;

        void Update()
        {
            float s = transform.localScale.x;
            if (Mathf.Abs(s - _target) < 0.001f) return;
            s = Mathf.MoveTowards(s, _target, Time.unscaledDeltaTime * 1.6f);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Vertical colour gradient over a graphic (multiplies its vertex colours).</summary>
    public sealed class UIGradient : BaseMeshEffect
    {
        public Color Top = Color.white;
        public Color Bottom = Color.white;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float h = Mathf.Max(0.001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var c = Color.Lerp(Bottom, Top, (v.position.y - min) / h);
                v.color = (Color32)((Color)v.color * c);
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>
    /// Keeps a set of labels on one line each, all at the largest size where the longest still fits its box (tabs).
    /// Recomputed only when the widths or the texts change.
    /// </summary>
    public sealed class OneLineLabels : MonoBehaviour
    {
        Text[] _labels;
        int _max, _min;
        float _lastWidth = -1f;
        string _lastTexts;

        public void Init(Text[] labels, int maxSize, int minSize)
        {
            _labels = labels;
            _max = maxSize;
            _min = minSize;
            foreach (var t in labels)
            {
                t.resizeTextForBestFit = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        void LateUpdate()
        {
            if (_labels == null || _labels.Length == 0) return;
            float width = float.MaxValue;
            string texts = "";
            foreach (var t in _labels)
            {
                width = Mathf.Min(width, t.rectTransform.rect.width);
                texts += t.text + "|"; // noloc
            }
            if (width <= 0f || (Mathf.Approximately(width, _lastWidth) && texts == _lastTexts)) return;
            _lastWidth = width;
            _lastTexts = texts;

            int size = _max;
            foreach (var t in _labels)
                while (size > _min && WidthAt(t, size) > width) size--;
            foreach (var t in _labels) t.fontSize = size;
        }

        static float WidthAt(Text t, int size)
        {
            var settings = t.GetGenerationSettings(new Vector2(float.MaxValue, float.MaxValue));
            settings.fontSize = size;
            settings.resizeTextForBestFit = false;
            settings.scaleFactor = 1f;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, settings);
        }
    }
}
