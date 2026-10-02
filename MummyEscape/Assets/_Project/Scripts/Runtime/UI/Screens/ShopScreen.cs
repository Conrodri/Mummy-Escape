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

            _coins = UIKit.Label(body, "", 46, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_coins, 80);
            UIKit.Size(UIKit.Label(body, "Gagne des scarabées en décrochant de nouvelles étoiles.", 32, UIKit.Dim), 60);

            _grid = UIKit.Rect("Grid", body);
            UIKit.Size(_grid, -1, -1, -1, 1);
            var grid = _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(440, 520);
            grid.spacing = new Vector2(40, 40);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        public override void OnShow() => Refresh();

        void Refresh()
        {
            var save = App.Save;
            _coins.text = $"{save.Data.Coins} scarabées";
            UIKit.ClearChildren(_grid);
            foreach (var skin in SkinCatalog.All)
            {
                var card = UIKit.Panel(_grid, skin.Id);
                UIKit.Column(card.transform, 10, 30, TextAnchor.MiddleCenter);
                var preview = UIKit.Image(card.transform, App.Art.MummyPortrait(skin), Color.white);
                UIKit.Size(preview, 220, 220);
                UIKit.Size(UIKit.Label(card.transform, skin.Name, 36, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold), 60);

                bool owned = save.Data.OwnedSkins.Contains(skin.Id);
                bool selected = save.Data.SelectedSkin == skin.Id;
                string label = selected ? "Équipé" : owned ? "Équiper" : $"{skin.Price} scarabées";
                var s = skin;
                var btn = UIKit.Button(card.transform, label, () => OnSkin(s), 34);
                btn.interactable = !selected && (owned || save.Data.Coins >= skin.Price);
                UIKit.Size(btn, 100);
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
