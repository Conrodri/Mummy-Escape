using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Skins bought with scarabs (earned by collecting new stars). IAP can be plugged in later.</summary>
    public sealed class ShopScreen : UIScreen
    {
        Text _coins;
        RectTransform _grid;

        protected override void Build()
        {
            Header("Boutique");
            var body = Body(200, 60);
            UIKit.Column(body, 24);

            var wallet = UIKit.Rect("Wallet", body);
            UIKit.Size(wallet, 80);
            _coins = UIKit.Chip(wallet, UIKit.Art.Scarab, "", UIKit.Gold, 76);
            UIKit.Place((RectTransform)_coins.transform.parent, 0.5f, 0.5f, 0, 76);

            UIKit.Size(UIKit.Label(body, "Gagne des scarabées en décrochant de nouvelles étoiles.", 28, UIKit.Dim), 50);

            _grid = (RectTransform)UIKit.FittedGrid(body, 2, new Vector2(440, 520), new Vector2(40, 40), out var slot).transform;
            UIKit.Size(slot, -1, -1, -1, 1);
        }

        public override void OnShow() => Refresh();

        void Refresh()
        {
            var save = App.Save;
            _coins.text = save.Data.Coins.ToString();
            UIKit.ClearChildren(_grid);
            foreach (var skin in SkinCatalog.All)
            {
                var card = UIKit.Panel(_grid, skin.Id);
                UIKit.Column(card.transform, 10, 30, TextAnchor.MiddleCenter);
                var preview = UIKit.Image(card.transform, App.Art.MummyPortrait(skin), Color.white);
                UIKit.Size(preview, 220, 220);
                UIKit.Size(UIKit.Label(card.transform, skin.Name, 32, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold), 56);

                bool owned = save.Data.OwnedSkins.Contains(skin.Id);
                bool selected = save.Data.SelectedSkin == skin.Id;
                string label = selected ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.F("{0} scarabées", skin.Price);
                var s = skin;
                bool affordable = !owned && save.Data.Coins >= skin.Price;
                var btn = UIKit.Button(card.transform, label, () => OnSkin(s), 30, affordable || (owned && !selected) ? ButtonStyle.Primary : ButtonStyle.Secondary);
                btn.interactable = !selected && (owned || save.Data.Coins >= skin.Price);
                UIKit.Size(btn, UIKit.SmallButtonHeight);
            }
        }

        void OnSkin(SkinDef skin)
        {
            var save = App.Save;
            if (!save.Data.OwnedSkins.Contains(skin.Id))
            {
                if (!save.TryBuySkin(skin.Id, skin.Price)) return;
                App.Audio.Play(Sfx.Coin);
            }
            save.SelectSkin(skin.Id);
            Refresh();
        }
    }
}
