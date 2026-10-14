using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Cosmetics bought with scarabs (earned by collecting new stars). The season pass banner and the wallet (golden scarabs:
    /// money, a tap opens the Treasure) sit on top; then four sections: the Treasure (its exclusives, first so it is seen),
    /// Skins (one category per slot: mummy, colour, torch, hat, shoes), the act
    /// Collections, sold whole for less, and the Seals, where Maât's seals (won in duels) buy the league skins.
    /// Every card shows the item alone; a tap opens it up close (<see cref="ItemZoomScreen"/>), where it is bought or put on.
    /// The casino has its own tab in the bottom bar (<see cref="CasinoScreen"/>).
    /// </summary>
    public sealed class ShopScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Shop;

        const int Columns = 3;
        const float CardHeight = 380;

        static readonly string[] Sections = { "Trésor", "Promos", "Skins", "Sceaux", "Collections" }; // two lines: 2 + 3
        const int TreasureSection = 0, SaleSection = 1, SkinsSection = 2, SealsSection = 3, SetsSection = 4;
        static readonly (string name, CosmeticSlot slot)[] Categories =
        {
            ("Momies", CosmeticSlot.Mummy), ("Couleurs", CosmeticSlot.Color), ("Torches", CosmeticSlot.Torch),
            ("Chapeaux", CosmeticSlot.Hat), ("Pieds", CosmeticSlot.Shoes),
        };

        internal static readonly Color WornFill = new Color32(30, 92, 84, 255);
        internal static readonly Color WornRim = new Color(0.25f, 0.88f, 0.8f, 0.6f);
        static Color LegendaryColor => CasinoScreen.LegendaryColor;
        internal static readonly Color HalloweenColor = new Color32(255, 140, 40, 255);
        internal static readonly Color TowerColor = new Color32(170, 140, 255, 255);
        internal static readonly Color CoopColor = new Color32(90, 230, 210, 255);

        // The collections by what pays for them; the sets that are won, not sold, last.
        static readonly string[] SetCategories = { "Scarabées", "Scarabées dorés", "Sceaux", "Récompenses" };
        internal static readonly string[] QualityNames = { "Commun", "Rare", "Épique", "Légendaire" };
        internal static readonly Color[] QualityColors =
        {
            new Color32(190, 182, 168, 255), new Color32(90, 170, 255, 255), new Color32(200, 120, 255, 255), CasinoScreen.LegendaryColor,
        };

        Text _coins, _stars, _seals, _gold, _note;
        PassBanner _pass;
        UIKit.Segmented _sections, _setTabs;
        RectTransform _categories;
        int _setCategory;
        bool _byQuality = true; // by default: the weakest first, the best at the bottom
        readonly List<(Image plate, Image icon, Text label)> _chips = new List<(Image, Image, Text)>();
        readonly List<Text> _chipCounts = new List<Text>();
        (Image plate, Image icon, Text label) _sortChip;
        RectTransform _list;
        ScrollRect _scroll;
        int _section = SaleSection;
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
            UIKit.SameWidth(_stars, _coins, _seals, _gold);
            var goldPlate = _gold.transform.parent;
            var plus = UIKit.Image(goldPlate, UISprites.Plus, TreasureScreen.GoldColor, false, "Plus"); // noloc
            UIKit.Size(plus, 30, 30);
            goldPlate.GetComponent<Image>().raycastTarget = true;
            goldPlate.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Open<TreasureScreen>());
            goldPlate.gameObject.AddComponent<PressScale>();

            _pass = new PassBanner(body, 170, () => Router.Open<PassScreen>());

            _sections = new UIKit.Segmented(body, Sections, i =>
            {
                _section = i;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            }, 88, new[] { 2, 3 });

            // The slot categories of the Skins section: large icon chips.
            _categories = UIKit.Row(body, 112, 10).GetComponent<RectTransform>();
            for (int i = 0; i < Categories.Length; i++) CategoryChip(i);
            _sortChip = SortChip(_categories, () =>
            {
                _byQuality = !_byQuality;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            });
            UIKit.Size(_sortChip.plate, -1, 0, 1);

            _setTabs = new UIKit.Segmented(body, SetCategories, i =>
            {
                _setCategory = i;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            }, 72);

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        /// <summary>The Treasure tab: the way to the golden scarab packs, then the exclusives as cards that open up close.</summary>
        void FillTreasure()
        {
            TreasureStrip(_list);
            Intro("Les exclusivités du Trésor, introuvables ailleurs. Elles s'achètent en scarabées dorés.");
            var items = TreasureScreen.Exclusives(App);
            Grid(items.Count, (parent, i) => GoldCard(parent, items[i]));
        }

        void GoldCard(Transform parent, Monetization.GoldItem gold)
        {
            var save = App.Save;
            var item = SkinCatalog.Get(gold.SkinId);
            bool owned = save.Data.OwnedSkins.Contains(item.Id);
            bool worn = save.IsWorn(item.Id);
            var (tag, color) = item.Legendary ? (Loc.T("Légendaire"), LegendaryColor) : (Loc.T("Trésor"), TreasureScreen.GoldColor);
            int cost = Monetization.ShopSale.GoldPrice(gold, DateTime.UtcNow);
            Sprite icon; string price; Color priceColor;
            if (worn) { icon = UISprites.Check; price = Loc.T("Équipé"); priceColor = UIKit.Turquoise; }
            else if (owned) { icon = null; price = Loc.T("Possédé"); priceColor = UIKit.Sand; }
            else { icon = UIKit.Art.GoldScarab; price = cost.ToString(); priceColor = save.Gold >= cost ? TreasureScreen.GoldColor : UIKit.Danger; }
            var card = Card(parent, item, tag, color, worn, false, icon, price, priceColor,
                () => TreasureScreen.Zoom(App, Router, gold, () => Router.Open<TreasureScreen>(), _ => Refresh()));
            if (GoldBuyable(gold)) BuyDot(card);
            if (!owned && cost < gold.Gold) SaleBadge(card);
            UIFx.PopIn(card, Mathf.Min(_cardIndex++, 12) * 0.035f);
        }

        /// <summary>The way to the Treasure screen: golden scarab packs, the pass, scarab exchanges.</summary>
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
            text.text = Loc.T("Scarabées dorés") + "  <size=22><color=#E8D6A8>" + Loc.T("packs, Pass de saison, échanges") + "</color></size>"; // noloc
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
            _chipCounts.Add(CountBadge(plate.transform));
        }

        /// <summary>A chip that toggles the sort by quality (lit with <see cref="Light"/> when on); also on the mummy screen.</summary>
        internal static (Image plate, Image icon, Text label) SortChip(Transform parent, Action onTap)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, UIKit.Rim, false, "Sort"); // noloc
            plate.raycastTarget = true;
            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.onClick.AddListener(() => { MummyEscape.App.GameApp.I.Audio.Play(Sfx.Click, 0f); onTap(); });
            plate.gameObject.AddComponent<PressScale>();
            var icon = UIKit.Image(plate.transform, UIKit.Art.Star, UIKit.Dim, false, "Icon"); // noloc
            UIKit.Place(icon.rectTransform, 0.5f, 1f, 50, 50, 0, -12);
            var label = UIKit.Label(plate.transform, "Qualité", 24, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(label, 14);
            UIKit.BottomBand(label.rectTransform, 38, 8);
            return (plate, icon, label);
        }

        /// <summary>A gold disc on a corner with a count: how many items can be bought right now.</summary>
        static Text CountBadge(Transform parent)
        {
            var dot = UIKit.Image(parent, UISprites.Circle, UIKit.Gold, false, "Count"); // noloc
            dot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(dot.rectTransform, 1f, 1f, 38, 38, -4, -4);
            var text = UIKit.Label(dot.transform, "", 22, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(text.rectTransform);
            return text;
        }

        internal static void Light((Image plate, Image icon, Text label) chip, bool on)
        {
            chip.plate.color = on ? new Color32(84, 64, 30, 255) : UIKit.SurfaceHi;
            var rim = chip.plate.transform.Find("Rim")?.GetComponent<Image>();
            if (rim != null) rim.color = on ? UIKit.Gold : UIKit.Rim;
            chip.icon.color = chip.label.color = on ? UIKit.Gold : UIKit.Dim;
        }

        // ------------------------------------------------------------------ what can be bought now

        bool Buyable(SkinDef s)
        {
            var save = App.Save;
            return s.Price > 0 && !save.Data.OwnedSkins.Contains(s.Id) && save.TotalStars >= s.MinStars && !CoopLocked(s) && save.Data.Coins >= ScarabPrice(s);
        }

        /// <summary>The player's 2v2 stars as the server last sent them (0 before the profile is loaded).</summary>
        int CoopStars => CoopLogic.TotalStars(App.Session.Profile?.Data?.CoopStars);

        /// <summary>A 2v2 piece the player lacks the 2v2 stars for.</summary>
        bool CoopLocked(SkinDef s) => CoopStars < CoopSkins.MinStars(s);

        int BuyableIn(CosmeticSlot slot)
        {
            int n = 0;
            foreach (var s in SkinCatalog.InSlot(slot)) if (Buyable(s)) n++;
            foreach (var s in CoopSkins.InSlot(slot)) if (Buyable(s)) n++;
            foreach (var s in HalloweenSkins.InShop(slot, App.Save.Data.OwnedSkins, DateTime.UtcNow)) if (Buyable(s)) n++;
            return n;
        }

        bool GoldBuyable(Monetization.GoldItem g) => !App.Save.Data.OwnedSkins.Contains(g.SkinId) && App.Save.Gold >= Monetization.ShopSale.GoldPrice(g, DateTime.UtcNow);

        static bool SealBuyable(SealItem item, PlayerPvpData d, ICollection<string> owned) =>
            d != null && !owned.Contains(item.Id) && !d.UnlockedRewards.Contains(item.Id) && d.HighestLeague >= item.MinLeague && d.Seals >= item.Price;

        static string Counted(string name, int n) => n > 0 ? Loc.T(name) + " · " + n : Loc.T(name); // noloc

        /// <summary>The tabs and the slot chips say how many items the wallet can pay for right now.</summary>
        void RefreshCounts(PlayerPvpData d)
        {
            int skins = 0;
            for (int i = 0; i < Categories.Length; i++)
            {
                int n = BuyableIn(Categories[i].slot);
                skins += n;
                _chipCounts[i].text = n.ToString();
                _chipCounts[i].transform.parent.gameObject.SetActive(n > 0);
            }
            int treasure = 0;
            foreach (var g in TreasureScreen.Exclusives(App)) if (GoldBuyable(g)) treasure++;
            int seals = 0;
            foreach (var s in SealShop.Items) if (SealBuyable(s, d, App.Save.Data.OwnedSkins)) seals++;
            int sale = 0;
            foreach (var s in ScarabSale()) if (Buyable(s)) sale++;
            foreach (var g in Monetization.ShopSale.GoldOnSale(DateTime.UtcNow)) if (GoldBuyable(g)) sale++;
            _sections.SetLabel(SaleSection, Counted(Sections[SaleSection], sale));
            _sections.SetLabel(TreasureSection, Counted(Sections[TreasureSection], treasure));
            _sections.SetLabel(SkinsSection, Counted(Sections[SkinsSection], skins));
            _sections.SetLabel(SealsSection, Counted(Sections[SealsSection], seals));
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
            if (App.Pvp == null || App.Session.Profile != null) return;
            int request = ++_request;
            await App.Session.RefreshProfile();
            if (request == _request && this != null && isActiveAndEnabled) Refresh();
        }

        void Refresh()
        {
            var save = App.Save;
            _coins.text = save.Data.Coins.ToString();
            _gold.text = save.Gold.ToString();
            _stars.text = save.TotalStars.ToString();
            var pvp = App.Session.Profile?.Data;
            _seals.transform.parent.gameObject.SetActive(App.Pvp != null);
            _seals.text = (pvp?.Seals ?? 0).ToString();
            // The pass banner stays on top of every tab.
            _pass.Refresh(App);
            RefreshCounts(pvp);

            _categories.gameObject.SetActive(_section == SkinsSection);
            _setTabs.Root.SetActive(_section == SetsSection);
            _setTabs.Select(_setCategory);
            for (int i = 0; i < _chips.Count; i++) Light(_chips[i], i == _category);
            Light(_sortChip, _byQuality);

            UIKit.ClearChildren(_list);
            _note = null;
            _cardIndex = 0;
            if (_section == SaleSection) { FillSale(); return; }
            if (_section == TreasureSection) { FillTreasure(); return; }
            if (_section == SetsSection) { FillSets(pvp); return; }
            if (_section == SealsSection) { FillSeals(pvp); return; }

            var slot = Categories[_category].slot;
            // One continuous grid, classics first then act after act (each card wears its act's tag).
            var items = new List<SkinDef>(SkinCatalog.InSlot(slot));
            items.Sort((a, b) => a.Theme != b.Theme ? a.Theme.CompareTo(b.Theme) : a.Price.CompareTo(b.Price));
            items.AddRange(CoopSkins.InSlot(slot)); // sold all year, after the act pieces
            // Duel rewards, casino legendaries, treasure exclusives and pass rewards the player owns come last, ready to wear.
            items.AddRange(PvpSkins.Owned(save.Data.OwnedSkins, slot));
            if (slot == CosmeticSlot.Color) items.AddRange(LegendarySkins.OwnedScarabLegendaries(save.Data.OwnedSkins));
            foreach (var s in Monetization.PremiumSkins.All)
                if (s.Slot == slot && save.Data.OwnedSkins.Contains(s.Id)) items.Add(s);
            items.AddRange(HalloweenSkins.InShop(slot, save.Data.OwnedSkins, DateTime.UtcNow));
            foreach (var s in TowerSkins.All) if (s.Slot == slot && save.Data.OwnedSkins.Contains(s.Id)) items.Add(s);
            if (_byQuality) items = items.OrderBy(Quality).ToList(); // stable: the usual order within a quality
            Grid(items.Count, (parent, i) => ItemCard(parent, items[i]));
            // The way to the sets, a discreet link under the grid.
            var toSets = UIKit.Button(_list, "Ensembles complets à -20 % : voir les collections ›", () =>
            {
                _section = SetsSection;
                _sections.Select(_section);
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            }, 24, ButtonStyle.Ghost);
            toSets.GetComponentInChildren<Text>().color = UIKit.Gold;
            UIKit.FitText(toSets.GetComponentInChildren<Text>(), 16);
            UIKit.Size(toSets, 64);
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

        internal static string ThemeName(int act) => Loc.F("Acte {0} — {1}", act, Loc.T(DifficultyTable.GetAct(act).Name));

        internal static Color ThemeColor(int act) => Color.Lerp(TombTheme.ForAct(act).Accent, UIKit.Sand, 0.25f);

        internal static (string tag, Color color) TagOf(SkinDef item)
        {
            if (item.Legendary) return (Loc.T("Légendaire"), LegendaryColor);
            if (item.Badge == HalloweenSkins.Badge) return (Loc.T(item.Badge), HalloweenColor);
            if (item.Badge == TowerSkins.Badge) return (Loc.T(item.Badge), TowerColor);
            if (item.Badge == CoopSkins.Badge) return (Loc.T(item.Badge), CoopColor);
            if (item.Badge != null) return (Loc.T(item.Badge), TreasureScreen.GoldColor);
            if (item.Pvp) return (Loc.T("Duel"), UIKit.Turquoise);
            if (item.Theme == 0) return (Loc.T("Classique"), new Color32(200, 186, 160, 255));
            return (Loc.F("Acte {0}", item.Theme), ThemeColor(item.Theme));
        }

        /// <summary>0 Commun (classics), 1 Rare (acts, duels), 2 Épique (Treasure, pass, events, tower, seals), 3 Légendaire (animated).</summary>
        internal static int Quality(SkinDef item)
        {
            if (item.Legendary) return 3;
            if (item.Badge != null || SealShop.Get(item.Id) != null) return 2;
            return item.Pvp || item.Theme > 0 ? 1 : 0;
        }

        static string HalloweenDates => Loc.T("Événement Halloween, du 24 octobre au 28 novembre.");

        internal static string Origin(SkinDef item)
        {
            switch (item.Id)
            {
                // Event pieces: what they are and how they were won, the event's dates last.
                case Halloween.RewardSkin: return Loc.T("Une citrouille sculptée, lueur de bougie comprise. Offerte à qui franchit les 10 tombeaux de l'acte Halloween.") + "\n" + HalloweenDates;
                case Halloween.ShopSkin: return Loc.T("Des bandelettes teintes à minuit, aux yeux couleur de citrouille. En vente aux scarabées pendant l'événement.") + "\n" + HalloweenDates;
                case Halloween.GoldSkin: return Loc.T("Le chapeau pointu des sorcières du Nil. Au Trésor, en scarabées d'or, pendant l'événement.") + "\n" + HalloweenDates;
                case Halloween.WheelLegendary: return Loc.T("Les feux de Samhain courent sur les bandelettes. Légendaire de la roue aux scarabées, seulement pendant l'événement.") + "\n" + HalloweenDates;
            }
            if (item.Legendary) return Loc.T("Exclusivité du casino ou du pass : bandages animés.");
            if (item.Badge == HalloweenSkins.Badge) return HalloweenDates;
            if (item.Badge == TowerSkins.Badge) return Loc.F("Récompense de l'étage {0} de la tour de défi.", TowerSkins.FloorOf(item.Id));
            if (item.Badge == CoopSkins.Badge) return Loc.F("Pièce de la campagne 2v2, en vente aux scarabées dès {0} étoiles 2v2.", CoopSkins.MinStars(item));
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

            // The quality in its colour before the origin (alone when they say the same, like Légendaire).
            int quality = Quality(item);
            string qualityName = Loc.T(QualityNames[quality]);
            var tagText = UIKit.Label(card.transform, "", 20, tagColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            tagText.text = (qualityName == tag ? "" : "<color=#" + ColorUtility.ToHtmlStringRGB(QualityColors[quality]) + ">" + qualityName.ToUpperInvariant() + "</color> · ") // noloc
                         + tag.ToUpperInvariant();
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
            int coopMin = CoopSkins.MinStars(item);
            bool coopLocked = !owned && CoopStars < coopMin;
            bool locked = !owned && save.TotalStars < item.MinStars || coopLocked;
            bool affordable = !locked && save.Data.Coins >= ScarabPrice(item);
            var (tag, color) = TagOf(item);

            Sprite icon; string price; Color priceColor;
            if (worn) { icon = UISprites.Check; price = Loc.T("Équipé"); priceColor = UIKit.Turquoise; }
            else if (owned) { icon = null; price = Loc.T("Possédé"); priceColor = UIKit.Sand; }
            else if (coopLocked) { icon = UIKit.Art.Star; price = Loc.F("{0} 2v2", coopMin); priceColor = UIKit.Dim; }
            else if (locked) { icon = UIKit.Art.Star; price = item.MinStars.ToString(); priceColor = UIKit.Dim; }
            else { icon = UIKit.Art.Scarab; price = ScarabPrice(item).ToString(); priceColor = affordable ? UIKit.Gold : UIKit.Danger; }

            var card = Card(parent, item, tag, color, worn, locked, icon, price, priceColor, () =>
            {
                string action = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper")
                              : coopLocked ? Loc.F("{0} étoiles 2v2 requises", coopMin)
                              : locked ? Loc.F("{0} étoiles requises", item.MinStars)
                              : affordable ? Loc.F("Acheter · {0} scarabées", ScarabPrice(item)) : Loc.F("Il te faut {0} scarabées", ScarabPrice(item));
                string detail = Origin(item) + (coopLocked ? "\n" + Loc.F("Débloqué à {0} étoiles 2v2 (tu en as {1}).", coopMin, CoopStars)
                                              : locked ? "\n" + Loc.F("Débloqué à {0} étoiles (tu en as {1}).", item.MinStars, save.TotalStars) : "");
                Router.Open<ItemZoomScreen>().Show(item, tag, color, detail, action,
                    !worn && (owned || affordable) ? () => OnItem(item) : (Action)null);
            });
            if (!owned && affordable) BuyDot(card);
            if (!owned && ScarabPrice(item) < item.Price) SaleBadge(card);
            UIFx.PopIn(card, Mathf.Min(_cardIndex++, 12) * 0.035f);
        }

        /// <summary>A red "-25 %" tab on the top left of the item.</summary>
        static void SaleBadge(Image card)
        {
            var badge = UIKit.Plate(card.transform, UIKit.Danger, 14, null, false, "Sale"); // noloc
            badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(badge.rectTransform, 0f, 1f, 96, 40, 62, -66);
            var text = UIKit.Label(badge.transform, "", 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.text = "-" + Monetization.ShopSale.PercentOff + " %"; // noloc
            UIKit.Stretch(text.rectTransform);
        }

        // ------------------------------------------------------------------ sale

        /// <summary>Today's scarab pieces on sale (the classics then the act pieces), as the server prices them.</summary>
        static List<SkinDef> ScarabSale()
        {
            var list = new List<SkinDef>();
            foreach (var i in Monetization.ScarabShop.Sale(DateTime.UtcNow)) list.Add(SkinCatalog.Get(i.Id));
            return list;
        }

        internal static int ScarabPrice(SkinDef item) =>
            Monetization.ScarabShop.Get(item.Id) is Monetization.ScarabItem i ? Monetization.ScarabShop.PriceNow(i, DateTime.UtcNow) : item.Price;

        /// <summary>The Promos tab: this week's 7 pieces at -25 %, the rarest first, and when the next ones come.</summary>
        void FillSale()
        {
            App.Quests.Visit(Quests.VisitSale);
            Intro("Chaque semaine, 3 pièces communes, 2 rares, 1 épique et 1 légendaire à -25 %. Une pièce ne revient qu'une fois toutes les autres passées.");
            var left = Monetization.ShopSale.Ends(DateTime.UtcNow) - DateTime.UtcNow;
            string when = left.Days > 0 ? Loc.F("Nouvelles promos dans {0} j {1} h", left.Days, left.Hours) : Loc.F("Nouvelles promos dans {0} h {1:00} min", left.Hours, left.Minutes);
            var countdown = UIKit.Label(_list, when, 28, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(countdown, 44);

            var cards = new List<Action<Transform>>();
            var gold = Monetization.ShopSale.GoldOnSale(DateTime.UtcNow);
            for (int i = gold.Count - 1; i >= 0; i--) { var g = gold[i]; cards.Add(p => GoldCard(p, g)); } // legendary, then epic
            var scarab = ScarabSale();
            for (int i = scarab.Count - 1; i >= 0; i--) { var s = scarab[i]; cards.Add(p => ItemCard(p, s)); } // rares, then commons
            Grid(cards.Count, (parent, i) => cards[i](parent));
        }

        /// <summary>A pulsing gold disc on the card's corner: this one can be bought right now.</summary>
        static void BuyDot(Image card)
        {
            var dot = UIKit.Image(card.transform, UISprites.Circle, UIKit.Gold, false, "Buyable"); // noloc
            dot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(dot.rectTransform, 1f, 0f, 34, 34, -30, 30); // on the free end of the price pill, clear of the tag line
            UIKit.Stretch(UIKit.Image(dot.transform, UISprites.Plus, UIKit.Ink).rectTransform, 7, 7, 7, 7);
            UIFx.Pulse(dot, 0.12f, 1.6f);
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
            if (App.Save.Data.OwnedSkins.Contains(item.Id)) { App.Save.SelectSkin(item.Id); Refresh(); return; }
            int price = ScarabPrice(item);
            Confirm(item.Name, Loc.F("L'acheter pour {0} scarabées ?", price), Loc.F("Acheter · {0} scarabées", price), async () =>
            {
                // The server takes the scarabs and gives the piece (the wallet it sends back lists it).
                if (!await BuyScarabs(item.Name, p => p.BuyScarabPieceAsync(item.Id), () => App.Save.Data.OwnedSkins.Contains(item.Id))) return;
                App.Save.SelectSkin(item.Id);
                Refresh();
            });
        }

        /// <summary>Every purchase asks first: a stray tap never spends.</summary>
        void Confirm(string title, string question, string buy, Action action) =>
            Router.Open<OfferDialog>().Configure(Loc.T(title), question, (buy, ButtonStyle.Primary, action), ("Annuler", ButtonStyle.Ghost, null));

        /// <summary>A scarab purchase asked to the server: true once <paramref name="done"/>, otherwise its error in a dialog.</summary>
        async System.Threading.Tasks.Task<bool> BuyScarabs(string name, Func<Online.IPvpService, System.Threading.Tasks.Task<WalletResponse>> call, Func<bool> done)
        {
            string error = await Monetization.ScarabWallet.RunAsync(App, call);
            if (this == null) return false;
            if (error != null && !done())
            {
                Router.Open<OfferDialog>().Configure(Loc.T(name), Monetization.ScarabWallet.ErrorText(error), ("OK", ButtonStyle.Primary, null)); // noloc
                Refresh();
                return false;
            }
            App.Audio.Play(Sfx.Coin);
            return true;
        }

        // ------------------------------------------------------------------ collections

        /// <summary>
        /// One wide card per set, by category: the act collections (scarabs, 20 % off), the Treasure's parures (golden scarabs,
        /// 20 % off), the league pieces (seals, full price), then the sets that are won, not sold (Halloween, the tower, the team's).
        /// </summary>
        void FillSets(PlayerPvpData d)
        {
            var save = App.Save;
            int index = 0;
            if (_setCategory == 1)
            {
                Intro("Les parures du Trésor, en scarabées dorés : les pièces qui te manquent à -20 %.");
                foreach (var gold in Monetization.GoldShop.Sets)
                {
                    var set = SkinSets.Pieces(gold.SkinIds);
                    int price = Monetization.GoldShop.SetPrice(gold, save.Data.OwnedSkins);
                    bool worn = Worn(set);
                    string label = worn ? Loc.T("Équipé") : price == 0 ? Loc.T("Tout équiper") : Loc.T("Acheter la parure");
                    var g = gold;
                    SetCard(Loc.T(gold.Name), set, TreasureScreen.GoldColor, new Color32(70, 52, 20, 255), new Color32(30, 22, 8, 255),
                            false, label, !worn, () => OnGoldSet(g, set, price), index++,
                            UIKit.Art.GoldScarab, worn ? 0 : price, save.Gold >= price ? TreasureScreen.GoldColor : UIKit.Danger);
                }
                return;
            }
            if (_setCategory == 2) { SealPack(d); return; }
            if (_setCategory == 3)
            {
                Intro("Ces ensembles ne se vendent pas : ils se gagnent. Complets, ils s'équipent d'un geste.");
                if (Halloween.IsOpen(DateTime.UtcNow) || Owns(HalloweenSkins.All))
                    Showcase(Loc.T("Nuit d'Halloween"), SkinSets.Halloween, HalloweenColor, index++);
                Showcase(Loc.T("Tour de défi"), TowerSkins.All, TowerColor, index++);
                if (Owns(SkinSets.Team)) Showcase(Loc.T("Équipe de Mummy Rush"), SkinSets.Team, new Color32(90, 255, 190, 255), index++);
                return;
            }

            Intro("Chaque acte a sa collection : momie, torche, chapeau et chaussures, débloquée par tes étoiles (50 par acte). L'ensemble complet coûte 20 % de moins.");
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var set = SkinCatalog.ThemeSet(act);
                int missingPrice = 0;
                foreach (var s in set) if (!save.Data.OwnedSkins.Contains(s.Id)) missingPrice += s.Price;
                int price = SkinCatalog.SetPrice(missingPrice);
                int minStars = act * SkinCatalog.StarsPerTheme;
                bool locked = missingPrice > 0 && save.TotalStars < minStars;
                bool worn = Worn(set);
                string label = worn ? Loc.T("Équipé") : missingPrice == 0 ? Loc.T("Tout équiper")
                             : locked ? Loc.F("Débloqué à {0} étoiles", minStars) : Loc.T("Acheter l'ensemble");
                bool selling = !worn && missingPrice > 0 && !locked;
                var theme = TombTheme.ForAct(act);
                int a = act;
                SetCard(ThemeName(act), set, ThemeColor(act), Color.Lerp(theme.Wall, Color.black, 0.2f), Color.Lerp(theme.FloorDark, Color.black, 0.55f),
                        locked, label, !worn && (missingPrice == 0 || !locked && save.Data.Coins >= price), () => OnSet(a, price), index++,
                        UIKit.Art.Scarab, selling ? price : 0, save.Data.Coins >= price ? UIKit.Gold : UIKit.Danger);
            }
        }

        /// <summary>
        /// Maât's league pieces on one card, at their full price: one tap buys every missing piece whose league is reached.
        /// Not worn together (three of them are colours): each piece is put on from the Seals tab.
        /// </summary>
        void SealPack(PlayerPvpData d)
        {
            Intro("Les pièces des ligues de duel, réunies. Le bouton achète d'un coup celles qui te manquent et dont tu as atteint la ligue, au prix normal.");
            _note = UIKit.Label(_list, "", 26, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 44);
            if (App.Pvp == null) _note.text = Loc.T("Les duels se jouent en ligne : active le mode en ligne (Paramètres › Confidentialité).");
            else if (d == null) _note.text = Loc.T("Chargement…");

            var set = new List<SkinDef>();
            var buy = new List<string>();
            int price = 0, missing = 0;
            League? next = null;
            foreach (var item in SealShop.Items)
            {
                var def = PvpSkins.ShopItem(item.Id);
                if (def == null) continue;
                set.Add(def);
                if (App.Save.Data.OwnedSkins.Contains(item.Id) || d != null && d.UnlockedRewards.Contains(item.Id)) continue;
                missing++;
                if (d != null && d.HighestLeague >= item.MinLeague) { buy.Add(item.Id); price += item.Price; }
                else if (next == null) next = item.MinLeague;
            }
            bool affordable = buy.Count > 0 && d.Seals >= price;
            string label = missing == 0 ? Loc.T("Collection complète")
                         : buy.Count > 0 ? Loc.P(buy.Count, "Acheter {0} pièce", "Acheter {0} pièces")
                         : next != null ? Loc.F("Ligue {0} requise", Loc.T(PvpSkins.LeagueName(next.Value))) : Loc.T("Chargement…");
            SetCard(Loc.T("Pièces des ligues"), set, UIKit.Turquoise, new Color32(24, 70, 66, 255), new Color32(10, 30, 28, 255),
                    false, label, !_busy && affordable, () => OnSealPack(buy, price), 0,
                    UISprites.Seal, buy.Count > 0 ? price : 0, affordable ? UIKit.Turquoise : UIKit.Danger);
        }

        void OnSealPack(List<string> ids, int price)
        {
            Router.Open<OfferDialog>().Configure(Loc.T("Pièces des ligues"), Loc.P(ids.Count, "Acheter {0} pièce", "Acheter {0} pièces") + Loc.F(" pour {0} sceaux ?", price),
                (Loc.F("Acheter · {0} sceaux", price), ButtonStyle.Primary, () => BuySeals(ids)),
                ("Annuler", ButtonStyle.Ghost, null));
        }

        /// <summary>One server purchase per piece, as in the Seals tab; stops at the first refusal and says why.</summary>
        async void BuySeals(List<string> ids)
        {
            if (_busy || App.Pvp == null) return;
            _busy = true;
            Refresh();
            string error = null;
            foreach (var id in ids)
            {
                var r = await App.Pvp.BuyWithSealsAsync(id);
                if (this == null) return;
                if (r == null || !r.Ok) { error = PvpScreen.ErrorText(r?.Error); break; }
                App.Session.UpdateWallet(r.Seals, r.UnlockedRewards);
                if (App.Session.Profile?.Data != null) App.Session.Profile.Data.UnlockedRewards = r.UnlockedRewards;
            }
            _busy = false;
            if (error == null) App.Audio.Play(Sfx.Coin);
            Refresh();
            if (error != null && _note != null) _note.text = error;
        }

        internal static bool Worn(IEnumerable<SkinDef> set)
        {
            foreach (var s in set) if (!MummyEscape.App.GameApp.I.Save.IsWorn(s.Id)) return false;
            return true;
        }

        bool Owns(IEnumerable<SkinDef> set)
        {
            foreach (var s in set) if (App.Save.Data.OwnedSkins.Contains(s.Id)) return true;
            return false;
        }

        /// <summary>A set that is won, not sold: put on in one tap once complete, otherwise how many pieces are there.</summary>
        void Showcase(string name, IReadOnlyList<SkinDef> set, Color accent, int index)
        {
            int owned = 0;
            foreach (var s in set) if (App.Save.Data.OwnedSkins.Contains(s.Id)) owned++;
            bool worn = Worn(set);
            string label = worn ? Loc.T("Équipé") : owned == set.Count ? Loc.T("Tout équiper") : Loc.F("{0} / {1} pièces", owned, set.Count);
            SetCard(name, set, accent, Color.Lerp(accent, Color.black, 0.75f), Color.Lerp(accent, Color.black, 0.9f),
                    false, label, !worn && owned == set.Count, () => Wear(set), index);
        }

        void SetCard(string name, IReadOnlyList<SkinDef> set, Color accent, Color top, Color bottom, bool locked, string label, bool usable, Action onTap, int index, Sprite priceIcon = null, int price = 0, Color? priceTint = null) =>
            SetCard(_list, name, set, accent, top, bottom, locked, label, usable, onTap, piece =>
            {
                var (tag, color) = TagOf(piece);
                Router.Open<ItemZoomScreen>().Show(piece, tag, color, Origin(piece), Loc.T("Fermer"), () => { });
            }, index, priceIcon, price, priceTint);

        /// <summary>A set's card: the set alone on a mummy, its pieces (each opens up close), one button and, when it sells, the price as a chip with its currency.</summary>
        internal static void SetCard(Transform list, string name, IReadOnlyList<SkinDef> set, Color accent, Color top, Color bottom, bool locked, string label, bool usable, Action onTap, Action<SkinDef> zoom, int index, Sprite priceIcon = null, int price = 0, Color? priceTint = null)
        {
            var save = MummyEscape.App.GameApp.I.Save;
            var look = SkinCatalog.Classic; // the set alone, not over the current outfit
            foreach (var s in set) look = look.With(s);
            bool worn = Worn(set);

            var card = UIKit.Plate(list, Color.white, 30, worn ? WornRim : new Color(accent.r, accent.g, accent.b, 0.5f), false, "Set" + index); // noloc
            UIFx.Gradient(card, top, bottom);
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
            MummyAnimator.Show(preview, MummyEscape.App.GameApp.I.Art, look);
            if (locked) Lock(preview, 80);

            var info = UIKit.Rect("Info", card.transform); // noloc
            UIKit.Size(info, -1, 0, 1);
            UIKit.Column(info, 6, 0, TextAnchor.MiddleLeft);
            var title = UIKit.Label(info, name, 30, accent, TextAnchor.MiddleLeft, FontStyle.Bold);
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
                chip.gameObject.AddComponent<Button>().onClick.AddListener(() => zoom(piece));
            }

            // The button, then the price as number + icon (never "500 scarabées" in words).
            var action = UIKit.Row(info, 78, 10);
            var btn = UIKit.Button(action.transform, label, onTap, 26, usable ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = usable;
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, -1, -1, 1);
            if (price > 0) UIKit.Chip(action.transform, priceIcon, price.ToString(), priceTint, 70);
            UIFx.PopIn(card, Mathf.Min(index, 10) * 0.06f, 0.94f);
        }

        void Wear(IEnumerable<SkinDef> set)
        {
            foreach (var s in set) App.Save.SelectSkin(s.Id);
            Refresh();
        }

        void OnSet(int act, int price)
        {
            var owned = App.Save.Data.OwnedSkins;
            var set = SkinCatalog.ThemeSet(act);
            bool Complete() => set.TrueForAll(s => owned.Contains(s.Id));
            if (Complete()) { Wear(set); return; }
            Confirm(ThemeName(act), Loc.F("Acheter l'ensemble pour {0} scarabées ?", price), Loc.F("Acheter · {0} scarabées", price), async () =>
            {
                if (await BuyScarabs(ThemeName(act), p => p.BuyScarabSetAsync(act), Complete)) Wear(set);
            });
        }

        /// <summary>A Treasure parure: put on when complete, otherwise its missing pieces bought from the server's wallet.</summary>
        void OnGoldSet(Monetization.GoldSet gold, List<SkinDef> set, int price)
        {
            if (price == 0) { Wear(set); return; }
            if (!TreasureScreen.EnoughGold(App, Router, price, () => Router.Open<TreasureScreen>())) return;
            Router.Open<OfferDialog>().Configure(Loc.T(gold.Name), Loc.F("Acheter les pièces qui te manquent pour {0} scarabées dorés ?", price),
                (Loc.F("Acheter · {0} dorés", price), ButtonStyle.Primary, async () =>
                {
                    string error = await Monetization.GoldWallet.RunAsync(App, p => p.BuyGoldItemAsync(gold.Id));
                    if (this == null) return;
                    if (error != null && Monetization.GoldShop.SetPrice(gold, App.Save.Data.OwnedSkins) > 0)
                    {
                        Router.Open<OfferDialog>().Configure(Loc.T(gold.Name), Monetization.GoldWallet.ErrorText(error), ("OK", ButtonStyle.Primary, null)); // noloc
                        return;
                    }
                    App.Audio.Play(Sfx.Win);
                    Wear(set);
                }),
                ("Annuler", ButtonStyle.Ghost, null));
        }

        void Intro(string text)
        {
            var intro = UIKit.Label(_list, text, 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
        }

        // ------------------------------------------------------------------ seals

        /// <summary>
        /// Maât's seal shop: skins bought with seals (every duel, daily chest), each from a league up.
        /// The purchase goes through the server, which holds the seals.
        /// </summary>
        void FillSeals(PlayerPvpData d)
        {
            Intro("Les sceaux de Maât se gagnent en duel (20 par victoire, 10 par défaite puis 1 de moins à chacune dans la journée, coffre quotidien). Chaque article demande d'avoir atteint sa ligue.");
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
            if (!owned && affordable) BuyDot(card);
            UIFx.PopIn(card, Mathf.Min(_cardIndex++, 12) * 0.035f);
        }

        void OnSealItem(string id, bool owned)
        {
            if (owned)
            {
                App.Save.GrantSkins(new[] { id });
                App.Save.SelectSkin(id);
                Refresh();
                return;
            }
            int price = SealShop.Get(id).Price;
            Confirm(PvpSkins.ShopItem(id).Name, Loc.F("L'acheter pour {0} sceaux ?", price), Loc.F("Acheter · {0} sceaux", price), () => BuySealItem(id));
        }

        async void BuySealItem(string id)
        {
            if (_busy || App.Pvp == null) return;
            _busy = true;
            Refresh();
            var r = await App.Pvp.BuyWithSealsAsync(id);
            if (this == null) return;
            _busy = false;
            string note;
            if (r != null && r.Ok)
            {
                App.Session.UpdateWallet(r.Seals, r.UnlockedRewards);
                if (App.Session.Profile?.Data != null) App.Session.Profile.Data.UnlockedRewards = r.UnlockedRewards;
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
