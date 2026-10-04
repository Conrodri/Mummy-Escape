using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Cosmetics bought with scarabs (earned by collecting new stars): one tab per slot (mummy, colour, torch, hat,
    /// shoes) plus the act collections, sold whole for less. Every card previews the item on the current outfit.
    /// IAP can be plugged in later.
    /// </summary>
    public sealed class ShopScreen : UIScreen
    {
        const int Columns = 4;
        const float CardHeight = 340;

        static readonly string[] Tabs = { "Thèmes", "Momies", "Couleurs", "Torches", "Chapeaux", "Pieds" };
        static readonly CosmeticSlot[] TabSlots = { CosmeticSlot.Mummy, CosmeticSlot.Mummy, CosmeticSlot.Color, CosmeticSlot.Torch, CosmeticSlot.Hat, CosmeticSlot.Shoes };

        static readonly Color WornFill = new Color32(30, 92, 84, 255);
        static readonly Color WornRim = new Color(0.25f, 0.88f, 0.8f, 0.6f);

        Text _coins, _stars;
        Image _outfit;
        UIKit.Segmented _tabs;
        RectTransform _list;
        ScrollRect _scroll;
        int _tab = 1;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Boutique");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);

            // The current outfit, the wallet and how to fill it.
            var top = UIKit.Panel(body, "Outfit"); // noloc
            top.raycastTarget = false;
            UIKit.Size(top, 230);
            var row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 30, 15, 15);
            row.spacing = 24;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            _outfit = UIKit.Image(top.transform, null, Color.white);
            _outfit.preserveAspect = true;
            UIKit.Size(_outfit, 200, 170);
            var side = UIKit.Rect("Side", top.transform);
            UIKit.Size(side, 200, 0, 1);
            UIKit.Column(side, 10, 0, TextAnchor.MiddleLeft);
            // Stars unlock the act collections, scarabs pay for them.
            var wallet = UIKit.Row(side, 72, 14);
            wallet.childAlignment = TextAnchor.MiddleLeft;
            _stars = UIKit.Chip(wallet.transform, UIKit.Art.Star, "", null, 72);
            _coins = UIKit.Chip(wallet.transform, UIKit.Art.Scarab, "", UIKit.Gold, 72);
            var hint = UIKit.Label(side, "Les étoiles débloquent les collections des actes, les scarabées les paient.", 26, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(hint, 18);
            UIKit.Size(hint, 80);

            _tabs = new UIKit.Segmented(body, Tabs, i => { _tab = i; Refresh(); _scroll.verticalNormalizedPosition = 1f; }, 80);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        public override void OnShow()
        {
            _tabs.Select(_tab);
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
        }

        void Refresh()
        {
            var save = App.Save;
            _coins.text = save.Data.Coins.ToString();
            _stars.text = save.TotalStars.ToString();
            _outfit.sprite = App.Art.MummyPortrait(save.Loadout);
            UIKit.ClearChildren(_list);
            if (_tab == 0) { FillSets(); return; }

            // One continuous grid, classics first then act after act (each card wears its act's tag):
            // per-act sections would leave a single card per row.
            var items = new List<SkinDef>(SkinCatalog.InSlot(TabSlots[_tab]));
            items.Sort((a, b) => a.Theme != b.Theme ? a.Theme.CompareTo(b.Theme) : a.Price.CompareTo(b.Price));
            // Duel rewards the player owns come last, ready to wear (they are never sold here).
            items.AddRange(PvpSkins.Owned(save.Data.OwnedSkins, TabSlots[_tab]));
            for (int i = 0; i < items.Count; i += Columns)
            {
                var cards = UIKit.Row(_list, CardHeight, 14);
                for (int k = 0; k < Columns; k++)
                {
                    // Every column gets the same share of the width, whatever the names in it.
                    if (i + k < items.Count) ItemCard(cards.transform, items[i + k]);
                    else UIKit.Size(UIKit.Rect("Spacer", cards.transform), -1, 0, 1);
                }
            }
        }

        static string ThemeName(int act) => Loc.F("Acte {0} — {1}", act, Loc.T(DifficultyTable.GetAct(act).Name));

        static Color ThemeColor(int act) => Color.Lerp(TombTheme.ForAct(act).Accent, UIKit.Sand, 0.25f);

        void ItemCard(Transform parent, SkinDef item)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(item.Id);
            bool worn = save.IsWorn(item.Id);
            bool locked = !owned && save.TotalStars < item.MinStars;
            bool affordable = !locked && save.Data.Coins >= item.Price;

            var card = UIKit.Plate(parent, worn ? WornFill : UIKit.SurfaceHi, 28, worn ? WornRim : UIKit.Rim, false, item.Id);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 2, 12, TextAnchor.MiddleCenter);
            var tag = UIKit.Label(card.transform, item.Pvp ? Loc.T("Duel").ToUpperInvariant() : item.Theme == 0 ? Loc.T("Classique") : Loc.F("Acte {0}", item.Theme).ToUpperInvariant(), 20,
                                  item.Pvp ? UIKit.Turquoise : item.Theme == 0 ? UIKit.Dim : ThemeColor(item.Theme), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(tag, 14);
            UIKit.Size(tag, 28, 0);
            var preview = UIKit.Image(card.transform, App.Art.MummyPortrait(save.Loadout.With(item)), Color.white);
            UIKit.Size(preview, 150, 0);
            if (locked) Lock(preview, 64);
            var name = UIKit.Label(card.transform, item.Name, 22, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);

            string label = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper")
                         : locked ? Loc.F("{0} étoiles requises", item.MinStars) : Loc.F("{0} scarabées", item.Price);
            var btn = UIKit.Button(card.transform, label, () => OnItem(item), 22,
                                   !worn && (owned || affordable) ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = !worn && (owned || affordable);
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 66, 0);
        }

        /// <summary>Greys out a preview and puts a padlock over it (not enough stars yet).</summary>
        static void Lock(Image preview, float size)
        {
            preview.color = new Color(0.35f, 0.32f, 0.3f, 1f);
            var padlock = UIKit.Image(preview.transform, UIKit.Art.Lock, Color.white, false, "Lock"); // noloc
            padlock.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(padlock.rectTransform, 0.5f, 0.5f, size, size);
        }

        void OnItem(SkinDef item)
        {
            var save = App.Save;
            if (!save.Data.OwnedSkins.Contains(item.Id))
            {
                if (!save.TryBuySkin(item.Id, item.Price)) return;
                App.Audio.Play(Sfx.Coin);
            }
            save.SelectSkin(item.Id);
            Refresh();
        }

        /// <summary>One wide card per act: the whole collection worn, bought at once for less, or put on in one tap.</summary>
        void FillSets()
        {
            var save = App.Save;
            var intro = UIKit.Label(_list, "Chaque acte a sa collection : momie, torche, chapeau et chaussures, débloquée par tes étoiles (25 par acte). L'ensemble complet coûte 20 % de moins.", 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var set = SkinCatalog.ThemeSet(act);
                var look = save.Loadout;
                int missingPrice = 0;
                bool worn = true;
                foreach (var s in set)
                {
                    look = look.With(s);
                    if (!save.Data.OwnedSkins.Contains(s.Id)) missingPrice += s.Price;
                    if (!save.IsWorn(s.Id)) worn = false;
                }
                int price = SkinCatalog.SetPrice(missingPrice);
                int minStars = act * SkinCatalog.StarsPerTheme;
                bool locked = missingPrice > 0 && save.TotalStars < minStars;
                bool affordable = !locked && save.Data.Coins >= price;

                var card = UIKit.Plate(_list, worn ? WornFill : UIKit.SurfaceHi, 28, worn ? WornRim : UIKit.Rim, false, "Set" + act); // noloc
                UIKit.Size(card, 280);
                var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(20, 26, 18, 18);
                row.spacing = 20;
                row.childAlignment = TextAnchor.MiddleLeft;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = false;

                var preview = UIKit.Image(card.transform, App.Art.MummyPortrait(look), Color.white);
                UIKit.Size(preview, 230, 190);
                if (locked) Lock(preview, 80);
                var info = UIKit.Rect("Info", card.transform);
                UIKit.Size(info, -1, 0, 1);
                UIKit.Column(info, 6, 0, TextAnchor.MiddleLeft);

                var title = UIKit.Label(info, ThemeName(act), 30, ThemeColor(act), TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.FitText(title, 18);
                UIKit.Size(title, 48);
                var names = new List<string>();
                foreach (var s in set) names.Add(Loc.T(s.Name));
                var pieces = UIKit.Label(info, string.Join(" · ", names), 22, UIKit.Dim, TextAnchor.MiddleLeft);
                UIKit.FitText(pieces, 16);
                UIKit.Size(pieces, 76);

                int a = act;
                string label = worn ? Loc.T("Équipé") : missingPrice == 0 ? Loc.T("Tout équiper")
                             : locked ? Loc.F("Débloqué à {0} étoiles", minStars) : Loc.F("Ensemble : {0} scarabées", price);
                bool usable = !worn && (missingPrice == 0 || affordable);
                var btn = UIKit.Button(info, label, () => OnSet(a, price), 26, usable ? ButtonStyle.Primary : ButtonStyle.Secondary);
                btn.interactable = usable;
                UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
                UIKit.Size(btn, 76);
            }
        }

        void OnSet(int act, int price)
        {
            var save = App.Save;
            var set = SkinCatalog.ThemeSet(act);
            bool missing = false;
            foreach (var s in set) if (!save.Data.OwnedSkins.Contains(s.Id)) missing = true;
            if (missing)
            {
                if (!save.TryBuyAll(set, price)) return;
                App.Audio.Play(Sfx.Coin);
            }
            foreach (var s in set) save.SelectSkin(s.Id);
            Refresh();
        }
    }
}
