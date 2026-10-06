using System.Collections.Generic;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The player's mummy: what it wears, one tab per slot with every item owned (bought, won in duels, from the guild
    /// or the casino), and the titles, each with what earns it. The outfit and the chosen title show to the rivals of
    /// the duels (VS screen, race, replays); buying is done in the Boutique.
    /// </summary>
    public sealed class MummyScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Mummy;

        const int Columns = 4;
        const float CardHeight = 300;
        static readonly string[] Tabs = { "Momies", "Couleurs", "Torches", "Chapeaux", "Pieds", "Titres" };
        static readonly CosmeticSlot[] TabSlots = { CosmeticSlot.Mummy, CosmeticSlot.Color, CosmeticSlot.Torch, CosmeticSlot.Hat, CosmeticSlot.Shoes };
        public const int TitlesTab = 5;

        Image _portrait;
        Text _name, _title;
        UIKit.Segmented _tabs;
        RectTransform _list;
        ScrollRect _scroll;
        int _tab;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Ma momie");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);

            // The mummy as the others see it: outfit, name and title.
            var top = UIKit.Panel(body, "Outfit"); // noloc
            top.raycastTarget = false;
            UIKit.Size(top, 260);
            var row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 30, 15, 15);
            row.spacing = 24;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            _portrait = UIKit.Image(top.transform, null, Color.white);
            _portrait.preserveAspect = true;
            UIKit.Size(_portrait, 220, 200);
            var side = UIKit.Rect("Side", top.transform);
            UIKit.Size(side, 200, 0, 1);
            UIKit.Column(side, 6, 0, TextAnchor.MiddleLeft);
            _name = UIKit.Label(side, "", 44, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_name, 28);
            UIKit.Size(_name, 60);
            _title = UIKit.Label(side, "", 30, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_title, 20);
            UIKit.Size(_title, 44);
            var shop = UIKit.Button(side, "Boutique", () => { Router.Reset<MainMenuScreen>(); Router.Open<ShopScreen>(); }, 28);
            UIKit.Size(shop, 76);

            _tabs = new UIKit.Segmented(body, Tabs, i => { _tab = i; Refresh(); _scroll.verticalNormalizedPosition = 1f; }, 80);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 14;
        }

        /// <summary>Opens on a tab (the titles from the friends' profile card).</summary>
        public void ShowTab(int tab)
        {
            _tab = tab;
            OnShow();
        }

        // Friends see the outfit and title chosen here.
        public override void OnHide() => App.PublishLookIfChanged();

        public override void OnShow()
        {
            _tabs.Select(_tab);
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
            LoadProfile();
        }

        /// <summary>The duel titles need the server's profile (wins, best league): fetched once if missing.</summary>
        async void LoadProfile()
        {
            if (App.Pvp == null || App.PvpProfile != null) return;
            int request = ++_request;
            await App.RefreshPvpProfile();
            if (request == _request && this != null && isActiveAndEnabled) Refresh();
        }

        void Refresh()
        {
            MummyAnimator.Show(_portrait, App.Art, App.Save.Loadout);
            _name.text = App.Online.PlayerName;
            string equipped = TitleBook.Equipped(App);
            _title.text = equipped == null ? Loc.T("Aucun titre affiché") : TitleBook.Line(equipped);
            UIKit.ClearChildren(_list);
            if (_tab == TitlesTab) FillTitles(equipped);
            else FillSlot(TabSlots[_tab]);
        }

        // ------------------------------------------------------------------ outfit

        /// <summary>Every owned item of the slot: the catalogue's first, then the duel, guild and casino rewards.</summary>
        List<SkinDef> Owned(CosmeticSlot slot)
        {
            var owned = App.Save.Data.OwnedSkins;
            var items = new List<SkinDef>();
            foreach (var s in SkinCatalog.InSlot(slot)) if (owned.Contains(s.Id)) items.Add(s);
            foreach (var id in owned)
            {
                var def = SkinCatalog.Get(id);
                if (def.Id == id && def.Slot == slot && !items.Contains(def)) items.Add(def);
            }
            return items;
        }

        void FillSlot(CosmeticSlot slot)
        {
            var items = Owned(slot);
            for (int i = 0; i < items.Count; i += Columns)
            {
                var cards = UIKit.Row(_list, CardHeight, 14);
                for (int k = 0; k < Columns; k++)
                {
                    if (i + k < items.Count) ItemCard(cards.transform, items[i + k]);
                    else UIKit.Size(UIKit.Rect("Spacer", cards.transform), -1, 0, 1);
                }
            }
            var more = UIKit.Label(_list, "D'autres tenues t'attendent dans la Boutique, et les duels, la guilde et le casino en offrent.", 26, UIKit.Dim);
            UIKit.FitText(more, 18);
            UIKit.Size(more, 84);
        }

        void ItemCard(Transform parent, SkinDef item)
        {
            var save = App.Save;
            bool worn = save.IsWorn(item.Id);
            var card = UIKit.Plate(parent, worn ? ShopScreen.WornFill : UIKit.SurfaceHi, 28, worn ? ShopScreen.WornRim : UIKit.Rim, false, item.Id);
            card.raycastTarget = true; // the whole card is the button
            UIKit.Size(card, -1, 0, 1);
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            btn.onClick.AddListener(() => Wear(item));
            card.gameObject.AddComponent<PressScale>();
            UIKit.Column(card.transform, 2, 12, TextAnchor.MiddleCenter);

            var preview = UIKit.Image(card.transform, null, Color.white);
            preview.raycastTarget = false;
            preview.preserveAspect = true;
            ItemPreview.Show(preview, item);
            UIKit.Size(preview, 170, 0);
            var name = UIKit.Label(card.transform, item.Name, 22, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);
            var state = UIKit.Label(card.transform, worn ? "Équipé" : "Équiper", 22, worn ? UIKit.Turquoise : UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(state, 15);
            UIKit.Size(state, 36, 0);
        }

        void Wear(SkinDef item)
        {
            if (App.Save.IsWorn(item.Id)) return;
            App.Save.SelectSkin(item.Id);
            App.Audio.Play(Sfx.Click);
            float scroll = _scroll.verticalNormalizedPosition;
            Refresh();
            _scroll.verticalNormalizedPosition = scroll;
        }

        // ------------------------------------------------------------------ titles

        void FillTitles(string equipped)
        {
            var none = TitleRow(Loc.T("Aucun titre"), Loc.T("Ton nom seul, sans titre"), UIKit.Dim, true, equipped == null, "");
            none.name = "None"; // noloc
            if (TitleBook.IsDeveloper(App)) Section("Légendaire", TitleKind.Developer, equipped);
            Section("Solo", TitleKind.Solo, equipped);
            Section("Duel", TitleKind.Wins, equipped, TitleKind.League);
            Section("Vitesse", TitleKind.Speed, equipped);
        }

        void Section(string title, TitleKind kind, string equipped, TitleKind? also = null)
        {
            var header = UIKit.SectionTitle(_list, title);
            UIKit.Size(header, 64);
            foreach (var t in Titles.All)
            {
                if (t.Kind != kind && t.Kind != also) continue;
                var (value, goal) = TitleBook.Progress(App, t);
                bool earned = value >= goal;
                string how = Condition(t) + (earned || t.Kind == TitleKind.League || t.Legendary ? "" : $"  ·  {value}/{goal}"); // noloc
                string name = t.Legendary && earned ? TitleBook.Shimmer(Loc.T(t.Name), Time.unscaledTime) : Loc.T(t.Name);
                TitleRow(name, how, TitleBook.ColorOf(t), earned, equipped == t.Id, t.Id);
            }
        }

        static string Condition(TitleDef t)
        {
            switch (t.Kind)
            {
                case TitleKind.Solo: return Loc.F("{0} étoiles dans l'acte {1}", t.Goal, t.Act);
                case TitleKind.Wins: return Loc.F("{0} victoires en duel", t.Goal);
                case TitleKind.League: return Loc.F("Atteindre la ligue {0}", Loc.T(PvpSkins.LeagueName(t.League)));
                case TitleKind.Developer: return Loc.T("Réservé à l'équipe de Mummy Rush");
                default: return Loc.F("{0} tombeaux différents en moins de 5 s", t.Goal);
            }
        }

        GameObject TitleRow(string name, string how, Color color, bool earned, bool worn, string id)
        {
            UIKit.ListItem(_list, 120, null, out var h, worn);
            h.padding = new RectOffset(28, 20, 10, 10);
            var texts = UIKit.Rect("Texts", h.transform);
            UIKit.Size(texts, -1, -1, 1);
            var title = UIKit.Label(texts, name, 34, earned ? color : UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(title, 22);
            UIKit.TopBand(title.rectTransform, 56, 6);
            var sub = UIKit.Label(texts, how, 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(sub, 16);
            UIKit.BottomBand(sub.rectTransform, 44, 6);

            string label = worn ? Loc.T("Affiché") : earned ? Loc.T("Afficher") : Loc.T("Verrouillé");
            var btn = UIKit.Button(h.transform, label, () =>
            {
                App.Save.SelectTitle(id);
                float scroll = _scroll.verticalNormalizedPosition;
                Refresh();
                _scroll.verticalNormalizedPosition = scroll;
            }, 26, earned && !worn ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = earned && !worn;
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 76, 210);
            return h.gameObject;
        }
    }
}
