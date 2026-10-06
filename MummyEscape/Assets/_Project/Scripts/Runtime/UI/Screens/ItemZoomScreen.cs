using System;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// A cosmetic seen up close (tap on a card in the shop, the casino or the treasure): big animated preview on a
    /// turning halo, alone or tried on the player's mummy, its rarity, where it comes from and the card's action.
    /// </summary>
    public sealed class ItemZoomScreen : UIScreen
    {
        public override bool IsModal => true;

        Text _tag, _name, _detail;
        Image _preview, _halo, _rays;
        RectTransform _panel, _pop;
        UIKit.Segmented _view;
        Button _action;
        SkinDef _item;
        Action _onAction;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.88f), true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Close(this));

            // The pop-in scales this holder: the panel itself is scaled by FitInParent every frame.
            _pop = UIKit.Stretch(UIKit.Rect("Pop", Root)); // noloc
            var panel = UIKit.Plate(_pop, Color.white, 44, UIKit.Rim, false, "Panel"); // noloc
            UIFx.Gradient(panel, new Color32(58, 45, 34, 255), new Color32(24, 18, 13, 255));
            panel.raycastTarget = true;
            UIKit.DropShadow(panel, 14, 0.6f);
            _panel = panel.rectTransform;
            UIKit.Place(_panel, 0.5f, 0.5f, 900, 0);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.FitInParent(_panel);
            UIKit.Column(panel.transform, 14, 36, TextAnchor.UpperCenter);

            _tag = UIKit.Label(panel.transform, "", 26, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_tag, 40);
            _name = UIKit.Title(panel.transform, "", 58, UIKit.Sand);
            UIKit.FitText(_name, 30);
            UIKit.Size(_name, 84);

            // The stage: rays and a halo turning behind the item, which floats.
            var stage = UIKit.Rect("Stage", panel.transform); // noloc
            UIKit.Size(stage, 640);
            _rays = UIKit.Image(stage, Rays, new Color(1f, 0.85f, 0.5f, 0.16f), false, "Rays"); // noloc
            UIKit.Place(_rays.rectTransform, 0.5f, 0.5f, 820, 820);
            UIFx.Spin(_rays, 12f);
            _halo = UIKit.Image(stage, UISprites.RadialGlow, new Color(1f, 0.75f, 0.4f, 0.6f), false, "Halo"); // noloc
            _halo.preserveAspect = false;
            UIKit.Place(_halo.rectTransform, 0.5f, 0.5f, 620, 620);
            UIFx.Pulse(_halo, 0.06f, 2.8f);
            var floater = UIKit.Rect("Float", stage); // noloc
            UIKit.Place(floater, 0.5f, 0.5f, 520, 520);
            UIFx.Float(floater, 14f, 2.6f);
            _preview = UIKit.Image(floater, null, Color.white, false, "Preview"); // noloc
            _preview.preserveAspect = true;
            UIKit.Stretch(_preview.rectTransform);

            _view = new UIKit.Segmented(panel.transform, new[] { "L'objet", "Sur ta momie" }, ShowView, 80);

            _detail = UIKit.Label(panel.transform, "", 28, UIKit.Dim);
            UIKit.FitText(_detail, 18);
            UIKit.Size(_detail, 110);

            _action = UIKit.Button(panel.transform, "Équiper", () =>
            {
                var act = _onAction;
                Router.Close(this);
                act?.Invoke();
            }, 38, ButtonStyle.Primary);
            UIKit.Size(_action, 110);
            UIKit.Size(UIKit.Button(panel.transform, "Fermer", () => Router.Close(this), 30, ButtonStyle.Ghost), 80);
        }

        /// <summary>
        /// Shows <paramref name="item"/>. <paramref name="action"/> is the card's button label (buy, equip…); a null
        /// <paramref name="onAction"/> greys it out (worn, locked, too expensive).
        /// </summary>
        public ItemZoomScreen Show(SkinDef item, string tag, Color tagColor, string detail, string action, Action onAction)
        {
            _item = item;
            _onAction = onAction;
            _tag.text = (tag ?? "").ToUpperInvariant();
            _tag.color = tagColor;
            _name.text = Loc.T(item.Name);
            _detail.text = detail ?? "";
            UIKit.SetLabel(_action, action);
            _action.interactable = onAction != null;
            _action.image.color = onAction != null ? UIKit.Gold : UIKit.SurfaceHi;
            _action.GetComponentInChildren<Text>().color = onAction != null ? UIKit.Ink : UIKit.Sand;
            var halo = item.Legendary ? (Color)LegendarySkins.Accent(item.Fx) : Color.Lerp(tagColor, new Color(1f, 0.75f, 0.4f), 0.5f);
            _halo.color = new Color(halo.r, halo.g, halo.b, 0.6f);
            _rays.color = new Color(halo.r, halo.g, halo.b, item.Legendary ? 0.3f : 0.16f);
            _view.Select(0);
            ShowView(0);
            UIFx.PopIn(_pop, 0f, 0.8f);
            return this;
        }

        void ShowView(int i)
        {
            if (_item == null) return;
            if (i == 0) ItemPreview.Show(_preview, _item);
            else MummyAnimator.Show(_preview, App.Art, App.Save.Loadout.With(_item));
        }

        // ------------------------------------------------------------------ rays sprite

        static Sprite _raysSprite;

        /// <summary>Soft light rays fanning out from the centre (drawn once).</summary>
        static Sprite Rays
        {
            get
            {
                if (_raysSprite != null) return _raysSprite;
                const int n = 256;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                        float a = Mathf.Atan2(dy, dx);
                        float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 6f)), 6f); // 12 rays
                        float fade = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                        px[y * n + x] = new Color(1, 1, 1, ray * fade);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                _raysSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _raysSprite;
            }
        }
    }
}
