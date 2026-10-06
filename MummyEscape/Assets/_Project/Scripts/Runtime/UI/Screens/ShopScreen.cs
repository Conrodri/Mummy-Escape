using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Cosmetics bought with scarabs (earned by collecting new stars). The season pass banner and the Treasure (real
    /// money) sit on top; then three sections: Skins (one category per slot: mummy, colour, torch, hat, shoes), the act
    /// Collections, sold whole for less, and the Seals, where Maât's seals (won in duels) buy the league skins.
    /// Every card shows the item alone; a tap opens it up close (<see cref="ItemZoomScreen"/>), where it is bought or put on.
    /// The casino has its own tab in the bottom bar (<see cref="CasinoScreen"/>).
    /// </summary>
    public sealed class ShopScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Shop;

        const int Columns = 3;
        const float CardHeight = 380;

        static readonly string[] Sections = { "Skins", "Collections", "Sceaux" };
        const int SkinsSection = 0, SetsSection = 1, SealsSection = 2;
        static readonly (string name, CosmeticSlot slot)[] Categories =
        {
            ("Momies", CosmeticSlot.Mummy), ("Couleurs", CosmeticSlot.Color), ("Torches", CosmeticSlot.Torch),
            ("Chapeaux", CosmeticSlot.Hat), ("Pieds", CosmeticSlot.Shoes),
        };

        internal static readonly Color WornFill = new Color32(30, 92, 84, 255);
        internal static readonly Color WornRim = new Color(0.25f, 0.88f, 0.8f, 0.6f);
        static Color LegendaryColor => CasinoScreen.LegendaryColor;

        Text _coins, _stars, _seals, _gold, _note;
        PassBanner _pass;
        UIKit.Segmented _sections;
        RectTransform _categories;
        readonly List<(Image plate, Image icon, Text label)> _chips = new List<(Image, Image, Text)>();
        RectTransform _list;
        ScrollRect _scroll;
        int _section;
        int _category;
        bool _busy;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Boutique");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);

            // Wallet: stars unlock the act items, scarabs pay for them, seals buy the duel skins; golden scarabs open the Treasure.
            var wallet = UIKit.Row(body, 68, 12);
            wallet.childAlignment = TextAnchor.MiddleCenter;
            _stars = UIKit.Chip(wallet.transform, UIKit.Art.Star, "", null, 64);
            _coins = UIKit.Chip(wallet.transform, UIKit.Art.Scarab, "", UIKit.Gold, 64);
            _seals = UIKit.Chip(wallet.transform, UISprites.Seal, "", UIKit.Turquoise, 64);
            _gold = UIKit.Chip(wallet.transform, UIKit.Art.GoldScarab, "", TreasureScreen.GoldColor, 64);
            var goldPlate = _gold.transform.parent;
            var plus = UIKit.Image(goldPlate, UISprites.Plus, TreasureScreen.GoldColor, false, "Plus"); // noloc
            UIKit.Size(plus, 30, 30);
            goldPlate.GetComponent<Image>().raycastTarget = true;
            goldPlate.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Open<TreasureScreen>());
            goldPlate.gameObject.AddComponent<PressScale>();

            _pass = new PassBanner(body, 170, () => Router.Open<PassScreen>());
            TreasureStrip(body);

            _sections = new UIKit.Segmented(body, Sections, i =>
            {
                _section = i;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            }, 88);

            // The slot categories of the Skins section: large icon chips.
            _categories = UIKit.Row(body, 112, 10).GetComponent<RectTransform>();
            for (int i = 0; i < Categories.Length; i++) CategoryChip(i);

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        /// <summary>The way to the Treasure: golden scarabs and the exclusive skins.</summary>
        void TreasureStrip(Transform parent)
        {
            var strip = UIKit.Plate(parent, Color.white, 26, new Color(1f, 0.85f, 0.4f, 0.6f), false, "Treasure"); // noloc
            UIFx.Gradient(strip, new Color32(120, 86, 26, 255), new Color32(60, 40, 12, 255));
            UIKit.Size(strip, 92);
            strip.raycastTarget = true;
            var btn = strip.gameObject.AddComponent<Button>();
            btn.targetGraphic = strip;
            btn.onClick.AddListener(() => { App.Audio.Play(Sfx.Click, 0f); Router.Open<TreasureScreen>(); });
            strip.gameObject.AddComponent<PressScale>().Amount = 0.98f;
            var inner = UIKit.Stretch(UIKit.Rect("Inner", strip.transform)); // noloc
            UIFx.Shine(inner, 5.5f, 0.1f);
            var row = inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(20, 26, 8, 8);
            row.spacing = 16;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var icon = UIKit.Image(inner, UIKit.Art.GoldScarab, Color.white);
            UIKit.Size(icon, 64, 64);
            UIFx.Pulse(icon, 0.06f, 1.8f);
            var text = UIKit.Label(inner, "", 28, new Color32(255, 236, 180, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            text.text = Loc.T("Trésor") + "  <size=22><color=#E8D6A8>" + Loc.T("skins exclusifs et scarabées dorés") + "</color></size>"; // noloc
            UIKit.FitText(text, 16);
            UIKit.Size(text, -1, -1, 1);
            UIKit.Size(UIKit.Image(inner, UISprites.Next, new Color(1f, 0.9f, 0.6f, 0.8f)), 36, 36);
        }

        void CategoryChip(int index)
        {
            var plate = UIKit.Plate(_categories, UIKit.SurfaceHi, 24, UIKit.Rim, false, "Category"); // noloc
            UIKit.Size(plate, -1, 0, 1);
            plate.raycastTarget = true;
            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.onClick.AddListener(() =>
            {
                App.Audio.Play(Sfx.Click, 0f);
                _category = index;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            });
            plate.gameObject.AddComponent<PressScale>();
            var icon = UIKit.Image(plate.transform, CategoryIcon(Categories[index].slot), UIKit.Dim, false, "Icon"); // noloc
            UIKit.Place(icon.rectTransform, 0.5f, 1f, 50, 50, 0, -12);
            var label = UIKit.Label(plate.transform, Categories[index].name, 24, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(label, 14);
            UIKit.BottomBand(label.rectTransform, 38, 8);
            _chips.Add((plate, icon, label));
        }

        static Sprite CategoryIcon(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Color: return UISprites.Palette;
                case CosmeticSlot.Torch: return UISprites.Torch;
                case CosmeticSlot.Hat: return UISprites.Crown;
                case CosmeticSlot.Shoes: return UISprites.Boot;
                default: return UISprites.Mummy;
            }
        }

        public override void OnShow()
        {
            _busy = false;
            _sections.Select(_section);
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
            LoadSeals();
        }

        public override void OnHide() => App.PublishLookIfChanged();

        /// <summary>The seals and duel rewards live on the server: fetched once if the duel home was never opened.</summary>
        async void LoadSeals()
        {
            if (App.Pvp == null || App.PvpProfile != null) return;
            int request = ++_request;
            await App.RefreshPvpProfile();
            if (request == _request && this != null && isActiveAndEnabled) Refresh();
        }

        void Refresh()
        {
            var save = App.Save;
            _coins.text = save.Data.Coins.ToString();
            _gold.text = save.Gold.ToString();
            _stars.text = save.TotalStars.ToString();
            var pvp = App.PvpProfile?.Data;
            _seals.transform.parent.gameObject.SetActive(App.Pvp != null);
            _seals.text = (pvp?.Seals ?? 0).ToString();
            _pass.Refresh(App);

            _categories.gameObject.SetActive(_section == SkinsSection);
            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = i == _category;
                var (plate, icon, label) = _chips[i];
                plate.color = on ? new Color32(84, 64, 30, 255) : UIKit.SurfaceHi;
                var rim = plate.transform.Find("Rim")?.GetComponent<Image>();
                if (rim != null) rim.color = on ? UIKit.Gold : UIKit.Rim;
                icon.color = label.color = on ? UIKit.Gold : UIKit.Dim;
            }

            UIKit.ClearChildren(_list);
            _note = null;
            _cardIndex = 0;
            if (_section == SetsSection) { FillSets(); return; }
            if (_section == SealsSection) { FillSeals(pvp); return; }

            var slot = Categories[_category].slot;
            // One continuous grid, classics first then act after act (each card wears its act's tag).
            var items = new List<SkinDef>(SkinCatalog.InSlot(slot));
            items.Sort((a, b) => a.Theme != b.Theme ? a.Theme.CompareTo(b.Theme) : a.Price.CompareTo(b.Price));
            // Duel rewards, casino legendaries, treasure exclusives and pass rewards the player owns come last, ready to wear.
            items.AddRange(PvpSkins.Owned(save.Data.OwnedSkins, slot));
            if (slot == CosmeticSlot.Color) items.AddRange(LegendarySkins.OwnedScarabLegendaries(save.Data.OwnedSkins));
            foreach (var s in Monetization.PremiumSkins.All)
                if (s.Slot == slot && save.Data.OwnedSkins.Contains(s.Id)) items.Add(s);
            Grid(items.Count, (parent, i) => ItemCard(parent, items[i]));
        }

        /// <summary>Rows of <see cref="Columns"/> cards, each popping in shortly after the previous one.</summary>
        void Grid(int count, Action<Transform, int> card)
        {
            for (int i = 0; i < count; i += Columns)
            {
                var cards = UIKit.Row(_list, CardHeight, 14);
                for (int k = 0; k < Columns; k++)
                {
                    // Every column gets the same share of the width, whatever the names in it.
                    if (i + k < count) card(cards.transform, i + k);
                    else UIKit.Size(UIKit.Rect("Spacer", cards.transform), -1, 0, 1);
                }
            }
        }

        static string ThemeName(int act) => Loc.F("Acte {0} — {1}", act, Loc.T(DifficultyTable.GetAct(act).Name));

        static Color ThemeColor(int act) => Color.Lerp(TombTheme.ForAct(act).Accent, UIKit.Sand, 0.25f);

        static (string tag, Color color) TagOf(SkinDef item)
        {
            if (item.Legendary) return (Loc.T("Légendaire"), LegendaryColor);
            if (item.Badge != null) return (Loc.T(item.Badge), TreasureScreen.GoldColor);
            if (item.Pvp) return (Loc.T("Duel"), UIKit.Turquoise);
            if (item.Theme == 0) return (Loc.T("Classique"), new Color32(200, 186, 160, 255));
            return (Loc.F("Acte {0}", item.Theme), ThemeColor(item.Theme));
        }

        static string Origin(SkinDef item)
        {
            if (item.Legendary) return Loc.T("Exclusivité du casino ou du pass : bandages animés.");
            if (item.Badge != null) return Loc.T("Exclusivité du Trésor et du Pass de saison.");
            if (item.Pvp) return Loc.T("Récompense des duels.");
            if (item.Theme == 0) return Loc.T("Pièce classique, toujours en vente.");
            return Loc.F("Collection de l'{0}.", ThemeName(item.Theme));
        }

        // ------------------------------------------------------------------ cards

        /// <summary>
        /// A shop card: tag, the item on a glow, its name, and a footer with its price or state. The whole card opens the zoom.
        /// </summary>
        Image Card(Transform parent, SkinDef item, string tag, Color tagColor, bool worn, bool locked, Sprite priceIcon, string price, Color priceColor, Action onTap)
        {
            var card = UIKit.Plate(parent, Color.white, 28, worn ? WornRim : new Color(tagColor.r, tagColor.g, tagColor.b, 0.4f), false, item.Id);
            UIFx.Gradient(card, worn ? (Color)new Color32(40, 112, 102, 255) : new Color32(64, 51, 39, 255),
                                worn ? (Color)new Color32(22, 66, 60, 255) : new Color32(36, 28, 21, 255));
            UIKit.Size(card, -1, 0, 1);
            card.raycastTarget = true;
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            btn.onClick.AddListener(() => { App.Audio.Play(Sfx.Click, 0f); onTap(); });
            card.gameObject.AddComponent<PressScale>();
            UIKit.Column(card.transform, 4, 14, TextAnchor.MiddleCenter);

            var tagText = UIKit.Label(card.transform, tag.ToUpperInvariant(), 20, tagColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(tagText, 14);
            UIKit.Size(tagText, 28, 0);

            var stage = UIKit.Rect("Stage", card.transform); // noloc
            UIKit.Size(stage, 180, 0, -1, 1);
            UIFx.Halo(stage, new Color(tagColor.r, tagColor.g, tagColor.b, item.Legendary ? 0.55f : 0.3f), 260);
            var preview = ItemPreview.Create(stage, item);
            UIKit.Stretch(preview.rectTransform, 10, 6, 10, 6);
            if (locked) Lock(preview, 64);

            var name = UIKit.Label(card.transform, item.Name, 24, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);

            // Footer pill: price with its currency, or the state.
            var pill = UIKit.Plate(card.transform, worn ? new Color(0, 0, 0, 0.25f) : new Color(0, 0, 0, 0.45f), 24, null, false, "Price"); // noloc
            UIKit.Size(pill, 52, 0);
            var h = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 6, 6);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (priceIcon != null) UIKit.Size(UIKit.Image(pill.transform, priceIcon, worn ? UIKit.Turquoise : Color.white), 36, 36);
            var priceText = UIKit.Label(pill.transform, "", 24, priceColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            priceText.text = price;
            priceText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(priceText, 40);
            return card;
        }

        int _cardIndex;

        void ItemCard(Transform parent, SkinDef item)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(item.Id);
            bool worn = save.IsWorn(item.Id);
            bool locked = !owned && save.TotalStars < item.MinStars;
            bool affordable = !locked && save.Data.Coins >= item.Price;
            var (tag, color) = TagOf(item);

            Sprite icon; string price; Color priceColor;
            if (worn) { icon = UISprites.Check; price = Loc.T("Équipé"); priceColor = UIKit.Turquoise; }
            else if (owned) { icon = null; price = Loc.T("Possédé"); priceColor = UIKit.Sand; }
            else if (locked) { icon = UIKit.Art.Star; price = item.MinStars.ToString(); priceColor = UIKit.Dim; }
            else { icon = UIKit.Art.Scarab; price = item.Price.ToString(); priceColor = affordable ? UIKit.Gold : UIKit.Danger; }

            var card = Card(parent, item, tag, color, worn, locked, icon, price, priceColor, () =>
            {
                string action = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper")
                              : locked ? Loc.F("{0} étoiles requises", item.MinStars)
                              : affordable ? Loc.F("Acheter · {0} scarabées", item.Price) : Loc.F("Il te faut {0} scarabées", item.Price);
                string detail = Origin(item) + (locked ? "\n" + Loc.F("Débloqué à {0} étoiles (tu en as {1}).", item.MinStars, save.TotalStars) : "");
                Router.Open<ItemZoomScreen>().Show(item, tag, color, detail, action,
                    !worn && (owned || affordable) ? () => OnItem(item) : (Action)null);
            });
            UIFx.PopIn(card, Mathf.Min(_cardIndex++, 12) * 0.035f);
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

        // ------------------------------------------------------------------ collections

        /// <summary>One wide card per act: the collection on a mummy, its pieces, bought at once for less or put on in one tap.</summary>
        void FillSets()
        {
            var save = App.Save;
            Intro("Chaque acte a sa collection : momie, torche, chapeau et chaussures, débloquée par tes étoiles (25 par acte). L'ensemble complet coûte 20 % de moins.");
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var set = SkinCatalog.ThemeSet(act);
                var look = SkinCatalog.Classic; // the collection alone, not over the current outfit
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
                var accent = ThemeColor(act);
                var theme = TombTheme.ForAct(act);

                var card = UIKit.Plate(_list, Color.white, 30, worn ? WornRim : new Color(accent.r, accent.g, accent.b, 0.5f), false, "Set" + act); // noloc
                UIFx.Gradient(card, Color.Lerp(theme.Wall, Color.black, 0.2f), Color.Lerp(theme.FloorDark, Color.black, 0.55f));
                UIKit.Size(card, 300);
                var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(16, 26, 18, 18);
                row.spacing = 18;
                row.childAlignment = TextAnchor.MiddleLeft;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = false;

                var stage = UIKit.Rect("Stage", card.transform); // noloc
                UIKit.Size(stage, 260, 230);
                UIFx.Halo(stage, new Color(accent.r, accent.g, accent.b, 0.45f), 320);
                var preview = UIKit.Image(stage, null, Color.white);
                preview.preserveAspect = true;
                UIKit.Stretch(preview.rectTransform);
                MummyAnimator.Show(preview, App.Art, look);
                if (locked) Lock(preview, 80);

                var info = UIKit.Rect("Info", card.transform); // noloc
                UIKit.Size(info, -1, 0, 1);
                UIKit.Column(info, 6, 0, TextAnchor.MiddleLeft);
                var title = UIKit.Label(info, ThemeName(act), 30, accent, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.FitText(title, 18);
                UIKit.Size(title, 46);

                // The pieces, each on its own.
                var pieces = UIKit.Row(info, 76, 8);
                pieces.childAlignment = TextAnchor.MiddleLeft;
                foreach (var s in set)
                {
                    bool has = save.Data.OwnedSkins.Contains(s.Id);
                    var chip = UIKit.Plate(pieces.transform, new Color(0, 0, 0, has ? 0.25f : 0.45f), 16, has ? (Color?)WornRim : null, false, s.Id);
                    UIKit.Size(chip, 76, 76);
                    var icon = ItemPreview.Create(chip.transform, s);
                    UIKit.Stretch(icon.rectTransform, 6, 6, 6, 6);
                    var piece = s;
                    chip.raycastTarget = true;
                    chip.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                    {
                        var (tag, color) = TagOf(piece);
                        Router.Open<ItemZoomScreen>().Show(piece, tag, color, Origin(piece), Loc.T("Fermer"), () => { });
                    });
                }

                int a = act;
                string label = worn ? Loc.T("Équipé") : missingPrice == 0 ? Loc.T("Tout équiper")
                             : locked ? Loc.F("Débloqué à {0} étoiles", minStars) : Loc.F("Ensemble : {0} scarabées", price);
                bool usable = !worn && (missingPrice == 0 || affordable);
                var btn = UIKit.Button(info, label, () => OnSet(a, price), 26, usable ? ButtonStyle.Primary : ButtonStyle.Secondary);
                btn.interactable = usable;
                UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
                UIKit.Size(btn, 78);
                UIFx.PopIn(card, (act - 1) * 0.06f, 0.94f);
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

        void Intro(string text)
        {
            var intro = UIKit.Label(_list, text, 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
        }

        // ------------------------------------------------------------------ seals

        /// <summary>
        /// Maât's seal shop: skins bought with seals (first win of the day, daily chest), each from a league up.
        /// The purchase goes through the server, which holds the seals.
        /// </summary>
        void FillSeals(PlayerPvpData d)
        {
            Intro("Les sceaux de Maât se gagnent en duel (1re victoire du jour, coffre quotidien). Chaque article demande d'avoir atteint sa ligue.");
            _note = UIKit.Label(_list, "", 26, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 44);
            if (App.Pvp == null) _note.text = Loc.T("Les duels se jouent en ligne : active le mode en ligne (Paramètres › Confidentialité).");
            else if (d == null) _note.text = Loc.T("Chargement…");

            var items = new List<(SealItem item, SkinDef def)>();
            foreach (var item in SealShop.Items)
            {
                var def = PvpSkins.ShopItem(item.Id);
                if (def != null) items.Add((item, def));
            }
            Grid(items.Count, (parent, i) => SealCard(parent, items[i].item, items[i].def, d));
        }

        void SealCard(Transform parent, SealItem item, SkinDef def, PlayerPvpData d)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(item.Id) || (d != null && d.UnlockedRewards.Contains(item.Id));
            bool worn = save.IsWorn(item.Id);
            bool leagueOk = d != null && d.HighestLeague >= item.MinLeague;
            bool affordable = leagueOk && d.Seals >= item.Price;
            string tag = Loc.F("Ligue {0}", Loc.T(PvpSkins.LeagueName(item.MinLeague)));
            var color = owned || leagueOk ? PvpSkins.LeagueColor(item.MinLeague) : UIKit.Danger;

            Sprite icon; string price; Color priceColor;
            if (worn) { icon = UISprites.Check; price = Loc.T("Équipé"); priceColor = UIKit.Turquoise; }
            else if (owned) { icon = null; price = Loc.T("Possédé"); priceColor = UIKit.Sand; }
            else { icon = UISprites.Seal; price = item.Price.ToString(); priceColor = affordable ? UIKit.Turquoise : UIKit.Danger; }

            string id = item.Id;
            var card = Card(parent, def, tag, color, worn, !owned && !leagueOk, icon, price, priceColor, () =>
            {
                string action = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper")
                              : !leagueOk ? Loc.F("Ligue {0} requise", Loc.T(PvpSkins.LeagueName(item.MinLeague)))
                              : affordable ? Loc.F("Acheter · {0} sceaux", item.Price) : Loc.F("Il te faut {0} sceaux", item.Price);
                Router.Open<ItemZoomScreen>().Show(def, tag, color, Loc.T("Boutique des sceaux de Maât : les skins des ligues de duel."), action,
                    !_busy && !worn && (owned || affordable) ? () => OnSealItem(id, owned) : (Action)null);
            });
            UIFx.PopIn(card, Mathf.Min(_cardIndex++, 12) * 0.035f);
        }

        async void OnSealItem(string id, bool owned)
        {
            if (owned)
            {
                App.Save.GrantSkins(new[] { id });
                App.Save.SelectSkin(id);
                Refresh();
                return;
            }
            if (_busy || App.Pvp == null) return;
            _busy = true;
            Refresh();
            var r = await App.Pvp.BuyWithSealsAsync(id);
            if (this == null) return;
            _busy = false;
            string note;
            if (r != null && r.Ok)
            {
                App.UpdatePvpWallet(r.Seals, r.UnlockedRewards);
                if (App.PvpProfile?.Data != null) App.PvpProfile.Data.UnlockedRewards = r.UnlockedRewards;
                App.Save.SelectSkin(id);
                App.Audio.Play(Sfx.Coin);
                note = Loc.F("{0} : à toi !", Loc.T(PvpSkins.ShopItem(id).Name));
            }
            else note = PvpScreen.ErrorText(r?.Error);
            Refresh();
            if (_note != null) _note.text = note;
        }
    }
}
