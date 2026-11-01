using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The challenge tower (<see cref="Tower"/>): the record and the floors that give an exclusive skin (tiles to zoom),
    /// then the run in progress (the floor, the handicaps taken, three cards to pick one when due, go on or leave it), or
    /// a new run. The rules sit behind the header's "?". A run costs one point of the endless energy.
    /// </summary>
    public sealed class TowerScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        RectTransform _content;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            UIKit.HelpIcon(Header("Tour de défi"), "Tour de défi", () => string.Join("\n",
                Loc.T("Chaque étage : un tombeau un peu plus dur."),
                Loc.F("2 étoiles pour monter : au plus {0} % des coups idéaux et du temps expert.", LevelResult.OneStarPercent),
                Loc.T("Un piège fatal ou 1 étoile : la course s'arrête."),
                Loc.F("Tous les {0} étages, un malus au choix, jusqu'à la fin.", Tower.CurseEvery),
                Loc.T("1 point d'énergie sans fin par course.")));
            var body = Body(180, 40, 40);
            UIKit.Column(body, 16);
            EnergyBadge.Create(body, Monetization.PlayMode.Endless); // a run costs a point of the endless energy
            _content = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _content.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        public override void OnShow() => Fill();

        void Fill()
        {
            UIKit.ClearChildren(_content);
            var save = App.Save;
            var best = save.Data.TowerBest;

            if (best.BestFloors > 0)
                UIKit.Chip(UIKit.Row(_content, 64, 14).transform, UISprites.Crown, Loc.F("Record : {0} étages", best.BestFloors), UIKit.Gold, 64);

            UIKit.SectionTitle(_content, "Récompenses exclusives");
            var rewards = UIKit.Row(_content, 230, 14);
            for (int i = 0; i < Tower.RewardFloors.Length; i++) RewardTile(rewards.transform, Tower.RewardFloors[i], TowerSkins.Resolve(Tower.RewardSkins[i]), i);

            var run = save.ActiveTower;
            var play = UIKit.Card(_content);
            if (!save.IsTowerUnlocked)
            {
                var padlock = UIKit.Image(play, UIKit.Art.Lock, Color.white, false, "Lock"); // noloc
                padlock.preserveAspect = true;
                UIKit.Size(padlock, 90);
                Paragraph(play, Loc.F("Finis {0} pour ouvrir la tour.", LevelNames.Of(Progression.Last(Difficulty.Easy))), 30, UIKit.Gold);
            }
            else if (run != null)
            {
                // The floor, big, under the tower; the handicaps taken as icon chips.
                var floor = UIKit.Row(play, 96, 18);
                UIKit.Size(UIKit.Image(floor.transform, UISprites.Tower, UIKit.Gold, false, "Icon"), 80, 80); // noloc
                var number = UIKit.Title(floor.transform, Loc.F("Étage {0}", run.Floor).ToUpperInvariant(), 64, UIKit.Gold);
                UIKit.FitText(number, 32);
                if (run.Curses.Count > 0)
                {
                    var taken = UIKit.Row(play, 60, 12);
                    foreach (var c in Tower.Curses)
                        if (run.Curses.Of(c) > 0) UIKit.Chip(taken.transform, IconOf(c), "×" + run.Curses.Of(c), UIKit.Sand, 60); // noloc
                }
                if (run.CurseDue)
                {
                    // Mandatory: the next floor waits for the pick.
                    var pick = UIKit.Label(play, "Choisir un malus", 34, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIKit.Size(pick, 50);
                    var row = UIKit.Row(play, 330, 14);
                    foreach (var curse in Tower.Curses) CurseCard(row.transform, curse, run.Curses);
                }
                else UIKit.Size(UIKit.Button(play, "Continuer", Continue, 40, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
                UIKit.Size(UIKit.Button(play, "Abandonner la course", () => { save.EndTower(); Fill(); }, 28, ButtonStyle.Leave), 72);
            }
            else UIKit.Size(UIKit.Button(play, "Jouer", StartRun, 40, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(_content, "Classement", Ranking), UIKit.ButtonHeight);
        }

        static void Paragraph(Transform parent, string text, int size, Color color)
        {
            var label = UIKit.Label(parent, "", size, color);
            label.text = text;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>A reward floor: the skin's picture, its floor, a tick once owned; a tap zooms on the skin.</summary>
        void RewardTile(Transform row, int floor, SkinDef skin, int index)
        {
            bool owned = App.Save.Data.OwnedSkins.Contains(skin.Id);
            var tile = UIKit.Plate(row, UIKit.SurfaceHi, 22, owned ? UIKit.Success : UIKit.Rim, false, "Reward"); // noloc
            UIKit.Size(tile, -1, 0, 1);
            UIKit.Column(tile.transform, 4, 12);
            tile.raycastTarget = true;
            var btn = tile.gameObject.AddComponent<Button>();
            btn.targetGraphic = tile;
            btn.onClick.AddListener(() => { App.Audio.Play(Services.Sfx.Click, 0f); Zoom(skin, floor, owned); });
            tile.gameObject.AddComponent<PressScale>();
            UIKit.Size(ItemPreview.Create(tile.transform, skin), 150);
            var caption = UIKit.Label(tile.transform, Loc.F("Étage {0}", floor), 28, owned ? UIKit.Turquoise : UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(caption, 18);
            UIKit.Size(caption, 40);
            if (owned)
            {
                var check = UIKit.Image(tile.transform, UISprites.Check, UIKit.Success, false, "Owned"); // noloc
                check.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UIKit.Place(check.rectTransform, 1f, 1f, 40, 40, -24, -24);
            }
            UIFx.PopIn(tile, index * 0.05f);
        }

        void Zoom(SkinDef skin, int floor, bool owned)
        {
            bool worn = App.Save.IsWorn(skin.Id);
            Router.Open<ItemZoomScreen>().Show(skin, Loc.T("Tour de défi"), UIKit.Gold,
                Loc.F("Récompense de l'étage {0} de la tour de défi.", floor),
                worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.T("À gagner"),
                owned && !worn ? () => App.Save.SelectSkin(skin.Id) : (System.Action)null);
        }

        /// <summary>A big card to tap: the handicap's icon, its name, how many times it is taken, and its effect in a few words.</summary>
        void CurseCard(Transform row, TowerCurse curse, TowerCurses taken)
        {
            bool can = taken.CanTake(curse);
            var card = UIKit.Plate(row, can ? UIKit.SurfaceHi : new Color(0.16f, 0.13f, 0.1f), 22, can ? UIKit.Gold : UIKit.Rim);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 8, 18);
            card.raycastTarget = can;
            if (can)
            {
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                btn.onClick.AddListener(() => { App.Audio.Play(Services.Sfx.Click, 0f); Take(curse); });
                card.gameObject.AddComponent<PressScale>().Amount = 0.96f;
            }
            var icon = UIKit.Image(card.transform, IconOf(curse), can ? Color.white : UIKit.Dim, false, "Icon"); // noloc
            icon.preserveAspect = true;
            UIKit.Size(icon, 96, 96);
            var name = UIKit.Label(card.transform, Loc.T(NameOf(curse)), 28, can ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, 64);
            var count = UIKit.Label(card.transform, $"{taken.Of(curse)}/{Tower.Cap(curse)}", 26, can ? UIKit.Gold : UIKit.Dim); // noloc
            UIKit.Size(count, 34);
            var desc = UIKit.Label(card.transform, Loc.T(DescOf(curse)), 24, UIKit.Dim);
            UIKit.FitText(desc, 16);
            UIKit.Size(desc, 64);
        }

        /// <summary>Armed spikes for the traps, the tomb map for the size, the tower for the storeys.</summary>
        Sprite IconOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? App.Art.ForTile(new Tile { Type = TileType.Trap, Trap = TrapKind.Spikes }, new TileLook { Armed = true })
            : curse == TowerCurse.Bigger ? UISprites.Map : UISprites.Tower;

        static string NameOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? "Plus de pièges" : curse == TowerCurse.Bigger ? "Tombeaux plus grands" : "Plus d'étages";

        static string DescOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? "+1 piège par étage"
            : curse == TowerCurse.Bigger ? "+1 case de large et de haut"
            : "+1 étage par tombeau";

        void Take(TowerCurse curse)
        {
            App.Save.TakeTowerCurse(curse);
            Continue();
        }

        void StartRun() =>
            PlayGate.EndlessRun(App, () =>
            {
                App.Save.StartTower();
                Continue();
            });

        void Continue()
        {
            Router.Open<HudScreen>();
            _ = App.Game.StartTower();
        }

        void Ranking() => App.RequireOnline(() =>
        {
            Router.Get<LeaderboardScreen>().FocusTower(); // before it opens: it loads the board it shows
            Router.Open<LeaderboardScreen>();
        });
    }
}
