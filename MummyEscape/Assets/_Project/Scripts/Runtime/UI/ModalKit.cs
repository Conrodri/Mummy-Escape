using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The one look of every window over the game (results, pause, quit and offer dialogs): the dimmed screen, the
    /// riveted gold panel, a title in the display font over a gold rule, stat tiles, reward chips and a row of icon
    /// actions under the main button.
    /// </summary>
    public static class ModalKit
    {
        public const int Pad = 44;

        /// <summary>Dims the screen and opens the panel, sized to its content; a tap outside runs <paramref name="outside"/>.</summary>
        public static Transform Frame(RectTransform root, float width, Action outside = null, float spacing = 20)
        {
            var shade = UIKit.Image(root, UIKit.Art.White, UIKit.Shade, true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            if (outside != null) shade.gameObject.AddComponent<Button>().onClick.AddListener(() => outside());
            var panel = UIKit.Panel(root);
            UIKit.FitInParent(UIKit.Place(panel.rectTransform, 0.5f, 0.5f, width, 0));
            var col = UIKit.Column(panel.transform, spacing, 0);
            col.padding = new RectOffset(Pad, Pad, 40, Pad);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return panel.transform;
        }

        /// <summary>The title, a subtitle under it, then the rule.</summary>
        public static Text Header(Transform panel, string title, int size, out Text subtitle)
        {
            var t = Header(panel, title, size, false);
            subtitle = UIKit.Label(panel, "", 28, UIKit.Dim);
            UIKit.FitText(subtitle, 18);
            UIKit.Size(subtitle, 38);
            Rule(panel);
            return t;
        }

        /// <summary>The title over the rule.</summary>
        public static Text Header(Transform panel, string title, int size = 72) => Header(panel, title, size, true);

        static Text Header(Transform panel, string title, int size, bool rule)
        {
            var t = UIKit.Title(panel, title, size);
            UIKit.FitText(t, size / 2);
            UIKit.Size(t, size + 26);
            if (rule) Rule(panel);
            return t;
        }

        /// <summary>Gold title for a good outcome, red for a fall.</summary>
        public static void Tint(Text title, bool good) => UIKit.TintTitle(title, good ? UIKit.Gold : UIKit.Danger);

        /// <summary>A gold rule with a diamond in the middle, under every title.</summary>
        public static void Rule(Transform panel)
        {
            var rule = UIKit.Rect("Rule", panel); // noloc
            UIKit.Size(rule, 20);
            var gold = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.55f);
            foreach (var (from, to) in new[] { (0.1f, 0.46f), (0.54f, 0.9f) })
            {
                var line = UIKit.Image(rule, UIKit.Art.White, gold, false, "Line"); // noloc
                line.rectTransform.anchorMin = new Vector2(from, 0.5f);
                line.rectTransform.anchorMax = new Vector2(to, 0.5f);
                line.rectTransform.sizeDelta = new Vector2(0, 3);
            }
            var diamond = UIKit.Image(rule, UIKit.Art.White, UIKit.Gold, false, "Diamond"); // noloc
            UIKit.Place(diamond.rectTransform, 0.5f, 0.5f, 13, 13);
            diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        }

        /// <summary>Stat tiles two by two, <paramref name="rows"/> rows, inside a panel <paramref name="width"/> wide.</summary>
        public static RectTransform Tiles(Transform panel, int rows, float width)
        {
            var grid = UIKit.Rect("Stats", panel); // noloc
            float cell = (width - 2 * Pad - 18) / 2;
            UIKit.Size(grid, rows * 150 + (rows - 1) * 18);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(cell, 150);
            g.spacing = new Vector2(18, 18);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            return grid;
        }

        /// <summary>Stat tile: icon on the left, big value over its caption.</summary>
        public static Text Tile(Transform parent, Sprite icon, string caption, out Text captionText)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, null, false, "Tile " + caption); // noloc
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(26, 20, 16, 16);
            h.spacing = 20;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var image = UIKit.Image(plate.transform, icon, UIKit.Gold, false, "Icon"); // noloc
            UIKit.Size(image, 56, 56);
            image.gameObject.SetActive(icon != null);
            var values = UIKit.Rect("Values", plate.transform); // noloc
            UIKit.Size(values, 118, -1, 1);
            UIKit.Column(values, 0, 0, TextAnchor.MiddleLeft);
            var value = UIKit.Label(values, "", 50, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            value.name = "Value"; // noloc
            UIKit.FitText(value, 26);
            UIKit.Size(value, 66);
            captionText = UIKit.Label(values, caption, 24, UIKit.Dim, TextAnchor.UpperLeft);
            UIKit.FitText(captionText, 18);
            UIKit.Size(captionText, 40);
            return value;
        }

        /// <summary>Sets a tile built by <see cref="Tile"/>: its icon, value and caption (null icon: none).</summary>
        public static void SetTile(Text value, Text caption, Sprite icon, string text, string captionText)
        {
            var image = value.transform.parent.parent.Find("Icon").GetComponent<Image>(); // noloc
            image.sprite = icon;
            image.gameObject.SetActive(icon != null);
            value.text = text;
            caption.text = captionText;
        }

        const float ChipHeight = 60, ChipGap = 12, ChipSpacing = 16;

        /// <summary>Where the reward chips go (see <see cref="LayOutChips"/>).</summary>
        public static RectTransform Chips(Transform panel)
        {
            var rt = UIKit.Rect("Rewards", panel); // noloc
            UIKit.Column(rt, ChipGap, 0);
            return rt;
        }

        /// <summary>
        /// Fills the chips line after line: a chip goes on the current line while it fits, else starts a new one; a chip
        /// wider than the panel gets the whole line and its text shrinks.
        /// </summary>
        public static void LayOutChips(RectTransform area, List<(Sprite icon, string text, Color tint)> chips, float panelWidth)
        {
            float width = panelWidth - 2 * Pad;
            UIKit.ClearChildren(area);
            area.gameObject.SetActive(chips.Count > 0);
            HorizontalLayoutGroup line = null;
            float used = 0;
            int lines = 0;
            foreach (var (icon, text, tint) in chips)
            {
                if (line == null) { line = UIKit.Row(area, ChipHeight, ChipSpacing); lines++; }
                var label = UIKit.Chip(line.transform, icon, text, tint, ChipHeight);
                var chip = (RectTransform)label.transform.parent;
                LayoutRebuilder.ForceRebuildLayoutImmediate(chip);
                float w = LayoutUtility.GetPreferredWidth(chip);
                if (w > width)
                {
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UIKit.FitText(label, 16);
                    UIKit.Size(label, ChipHeight - 16, 0, 1);
                    UIKit.Size(chip, ChipHeight, width);
                    w = width;
                }
                if (used > 0 && used + ChipSpacing + w > width)
                {
                    line = UIKit.Row(area, ChipHeight, ChipSpacing);
                    lines++;
                    chip.SetParent(line.transform, false);
                    used = w;
                }
                else used += (used > 0 ? ChipSpacing : 0) + w;
            }
            UIKit.Size(area, lines == 0 ? 0 : lines * ChipHeight + (lines - 1) * ChipGap);
        }

        /// <summary>The row of icon actions under the main button (Rejouer, Classement, Menu...).</summary>
        public static Transform Actions(Transform panel) => UIKit.Row(panel, 160, 40).transform;
    }
}
