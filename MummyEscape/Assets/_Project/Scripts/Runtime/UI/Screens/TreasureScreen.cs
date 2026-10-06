using MummyEscape.Monetization;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The real-money shop: golden scarabs sold by the store (10 € = 100), and what they buy: the season pass, the
    /// treasure exclusives (never sold for scarabs) and scarabs.
    /// </summary>
    public sealed class TreasureScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Shop;

        /// <summary>Golden scarabs stand out from the scarabs earned in the tombs.</summary>
        public static readonly Color GoldColor = new Color32(255, 214, 64, 255);
        const int Columns = 4;

        Text _gold, _coins, _note;
        RectTransform _list;
        ScrollRect _scroll;
        bool _busy;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Trésor");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);

            var top = UIKit.Row(body, 80, 14);
            top.childAlignment = TextAnchor.MiddleLeft;
            _gold = UIKit.Chip(top.transform, UIKit.Art.GoldScarab, "", GoldColor, 76);
            _coins = UIKit.Chip(top.transform, UIKit.Art.Scarab, "", UIKit.Sand, 76);
            UIKit.Size(UIKit.Rect("Spacer", top.transform), -1, -1, 1); // noloc

            _note = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 44);

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 18;
        }

        public override void OnShow()
        {
            _busy = false;
            _note.text = "";
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
        }

        void Refresh()
        {
            var save = App.Save;
            _gold.text = save.Gold.ToString();
            _coins.text = save.Data.Coins.ToString();
            UIKit.ClearChildren(_list);

            Section("Scarabées dorés", "La monnaie du Trésor, achetée avec de l'argent réel : 10 € = 100 scarabées dorés.");
            if (!Store.Available) Hint("Boutique de la plateforme indisponible pour le moment.");
            for (int i = 0; i < GoldShop.Packs.Count; i += 3)
            {
                var row = UIKit.Row(_list, 300, 14);
                for (int k = 0; k < 3; k++)
                {
                    if (i + k < GoldShop.Packs.Count) PackCard(row.transform, GoldShop.Packs[i + k]);
                    else UIKit.Size(UIKit.Rect("Spacer", row.transform), -1, 0, 1); // noloc
                }
            }

            Section("Pass de saison", null);
            PassCard();

            Section("Exclusivités du Trésor", "Introuvables ailleurs : ni en boutique, ni au casino.");
            for (int i = 0; i < GoldShop.Exclusives.Count; i += Columns)
            {
                var row = UIKit.Row(_list, 340, 14);
                for (int k = 0; k < Columns; k++)
                {
                    if (i + k < GoldShop.Exclusives.Count) ItemCard(row.transform, GoldShop.Exclusives[i + k]);
                    else UIKit.Size(UIKit.Rect("Spacer", row.transform), -1, 0, 1); // noloc
                }
            }

            Section("Scarabées", "Pour la boutique et la roue du casino.");
            foreach (var offer in GoldShop.ScarabOffers) OfferRow(offer);

            Hint("Achats facultatifs : tout le jeu se joue sans payer. Si tu es mineur, demande l'accord d'un parent avant d'acheter.");
        }

        void Section(string title, string text)
        {
            UIKit.SectionTitle(_list, title);
            if (text != null) Hint(text);
        }

        void Hint(string text)
        {
            var t = UIKit.Label(_list, text, 26, UIKit.Dim);
            UIKit.FitText(t, 18);
            UIKit.Size(t, 76);
        }

        void PackCard(Transform parent, GoldPack pack)
        {
            var card = UIKit.Plate(parent, UIKit.SurfaceHi, 28, UIKit.Rim, false, pack.ProductId);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 4, 14, TextAnchor.MiddleCenter);
            var icon = UIKit.Image(card.transform, UIKit.Art.GoldScarab, Color.white);
            icon.preserveAspect = true;
            UIKit.Size(icon, 90, 90);
            var amount = UIKit.Title(card.transform, (pack.Gold + pack.Bonus).ToString(), 52, GoldColor);
            UIKit.Size(amount, 64);
            var bonus = UIKit.Label(card.transform, pack.Bonus > 0 ? Loc.F("dont {0} offerts", pack.Bonus) : " ", 22, UIKit.Turquoise, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(bonus, 30);
            var btn = UIKit.Button(card.transform, Store.Price(pack), () => BuyPack(pack), 30, ButtonStyle.Primary);
            btn.interactable = Store.Available && !_busy;
            UIKit.Size(btn, 76);
        }

        void PassCard()
        {
            var save = App.Save;
            var season = BattlePass.Current;
            UIKit.ListItem(_list, 200, () => Router.Open<PassScreen>(), out var h);
            var preview = UIKit.Image(h.transform, null, Color.white);
            ItemPreview.Show(preview, SkinCatalog.Get(season.Legendary));
            preview.preserveAspect = true;
            UIKit.Size(preview, 170, 150, 0);
            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 4, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(col, Loc.T(season.Name), 34, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(name, 48);
            var sub = UIKit.Label(col, save.HasPass ? Loc.F("Pass premium actif · palier {0}/{1}", save.PassTier, BattlePass.Tiers)
                                                    : Loc.F("Skin légendaire, set de 10 pièces, parties illimitées · {0} scarabées dorés", GoldShop.PassPrice),
                                  24, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(sub, 16);
            UIKit.Size(sub, 70);
            UIKit.IconButton(h.transform, UISprites.Next, () => Router.Open<PassScreen>(), 84);
        }

        void ItemCard(Transform parent, GoldItem item)
        {
            var save = App.Save;
            var def = SkinCatalog.Get(item.SkinId);
            bool owned = save.Data.OwnedSkins.Contains(item.SkinId);
            bool worn = save.IsWorn(item.SkinId);
            var card = UIKit.Plate(parent, worn ? ShopScreen.WornFill : UIKit.SurfaceHi, 28, worn ? ShopScreen.WornRim : UIKit.Rim, false, item.SkinId);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 2, 12, TextAnchor.MiddleCenter);
            var tag = UIKit.Label(card.transform, def.Legendary ? Loc.T("Légendaire").ToUpperInvariant() : Loc.T("Trésor").ToUpperInvariant(), 20,
                                  def.Legendary ? new Color32(255, 96, 220, 255) : GoldColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(tag, 28, 0);
            var preview = UIKit.Image(card.transform, null, Color.white);
            preview.preserveAspect = true;
            ItemPreview.Show(preview, def);
            UIKit.Size(preview, 150, 0);
            var name = UIKit.Label(card.transform, def.Name, 22, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);
            string label = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.F("{0} dorés", item.Gold);
            var btn = UIKit.Button(card.transform, label, () => OnItem(item), 22, !worn ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = !worn && !_busy;
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 66, 0);
        }

        void OfferRow(ScarabOffer offer)
        {
            UIKit.ListItem(_list, 120, null, out var h);
            var icon = UIKit.Image(h.transform, UIKit.Art.Scarab, Color.white);
            icon.preserveAspect = true;
            UIKit.Size(icon, 80, 80, 0);
            var label = UIKit.Label(h.transform, Loc.F("{0} scarabées", offer.Scarabs), 34, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(label, -1, -1, 1);
            var btn = UIKit.Button(h.transform, Loc.F("{0} dorés", offer.Gold), () => OnOffer(offer), 30, ButtonStyle.Primary);
            btn.interactable = !_busy;
            UIKit.Size(btn, 84, 260, 0);
        }

        async void BuyPack(GoldPack pack)
        {
            if (_busy || !Store.Available) return;
            _busy = true;
            Refresh();
            PurchaseOutcome r;
            try { r = await Store.Provider.BuyAsync(pack.ProductId); }
            catch (System.Exception e) { r = new PurchaseOutcome { Error = e.Message }; }
            if (this == null) return;
            _busy = false;
            if (r.Ok && App.Save.CreditPurchase(r.TransactionId, pack.Gold + pack.Bonus))
            {
                App.Audio.Play(Sfx.Coin);
                _note.text = Loc.F("+{0} scarabées dorés. Merci !", pack.Gold + pack.Bonus);
            }
            else if (!r.Ok && !r.Cancelled) _note.text = Loc.T("Achat impossible : réessaie plus tard.");
            Refresh();
        }

        void OnItem(GoldItem item)
        {
            var save = App.Save;
            var def = SkinCatalog.Get(item.SkinId);
            if (save.Data.OwnedSkins.Contains(item.SkinId))
            {
                save.SelectSkin(item.SkinId);
                Refresh();
                return;
            }
            if (!EnoughGold(item.Gold)) return;
            Router.Open<OfferDialog>().Configure(Loc.T(def.Name), Loc.F("L'acheter pour {0} scarabées dorés ?", item.Gold),
                (Loc.F("Acheter · {0} dorés", item.Gold), ButtonStyle.Primary, () =>
                {
                    if (!save.BuyWithGold(item)) return;
                    save.SelectSkin(item.SkinId);
                    App.Audio.Play(Sfx.Win);
                    _note.text = Loc.F("{0} : à toi !", Loc.T(def.Name));
                    Refresh();
                }),
                ("Annuler", ButtonStyle.Ghost, null));
        }

        void OnOffer(ScarabOffer offer)
        {
            if (!EnoughGold(offer.Gold)) return;
            Router.Open<OfferDialog>().Configure("Scarabées", Loc.F("Échanger {0} scarabées dorés contre {1} scarabées ?", offer.Gold, offer.Scarabs),
                (Loc.F("Échanger · {0} dorés", offer.Gold), ButtonStyle.Primary, () =>
                {
                    if (!App.Save.BuyScarabs(offer)) return;
                    App.Audio.Play(Sfx.Coin);
                    _note.text = Loc.F("+{0} scarabées", offer.Scarabs);
                    Refresh();
                }),
                ("Annuler", ButtonStyle.Ghost, null));
        }

        /// <summary>True when the wallet holds enough; otherwise says how many are missing and points at the packs.</summary>
        bool EnoughGold(int price) => EnoughGold(App, Router, price, () => _scroll.verticalNormalizedPosition = 1f);

        internal static bool EnoughGold(App.GameApp app, UIRouter router, int price, System.Action showPacks)
        {
            int missing = price - app.Save.Gold;
            if (missing <= 0) return true;
            router.Open<OfferDialog>().Configure("Pas assez de scarabées dorés", Loc.F("Il t'en manque {0}.", missing),
                ("Voir les packs", ButtonStyle.Primary, showPacks),
                ("Plus tard", ButtonStyle.Ghost, null));
            return false;
        }
    }
}
