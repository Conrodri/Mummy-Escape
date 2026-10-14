using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The challenge tower (<see cref="Tower"/>): the run in progress (the handicap to pick when one is due, go on, or leave
    /// it), or a new run; the record and the floors that give an exclusive skin. A run costs one point of the endless energy.
    /// </summary>
    public sealed class TowerScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        RectTransform _content;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Tour de défi");
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

            var rules = UIKit.Card(_content);
            Paragraph(rules, Loc.T("Enchaîne des tombeaux tirés au hasard, un peu plus durs à chaque étage."), 30, UIKit.Sand);
            Paragraph(rules, Loc.F("Pour monter d'un étage, sors du tombeau avec au moins 2 étoiles : moins de {0} % du chemin idéal en coups, et moins de {0} % du temps de la momie experte.", LevelResult.OneStarPercent), 28, UIKit.Sand);
            Paragraph(rules, Loc.T("La course est perdue dès qu'un piège a raison de toi, ou dès que tu sors avec une seule étoile. Ton score : le nombre d'étages franchis."), 28, UIKit.Sand);
            Paragraph(rules, Loc.F("Tous les {0} étages, choisis un malus : il reste jusqu'à la fin de la course.", Tower.CurseEvery), 28, UIKit.Sand);
            Paragraph(rules, Loc.T("Chaque course tire de nouveaux tombeaux et coûte un point d'énergie du mode sans fin."), 26, UIKit.Dim);
            Paragraph(rules, best.BestFloors > 0 ? Loc.F("Record : {0} étages", best.BestFloors) : Loc.T("Pas encore de record."), 28, UIKit.Gold);

            var rewards = UIKit.Card(_content);
            UIKit.SectionTitle(rewards, "Récompenses exclusives");
            for (int i = 0; i < Tower.RewardFloors.Length; i++)
            {
                var skin = TowerSkins.Resolve(Tower.RewardSkins[i]);
                bool owned = save.Data.OwnedSkins.Contains(skin.Id);
                Paragraph(rewards, Loc.F("Étage {0} — {1}", Tower.RewardFloors[i], Loc.T(skin.Name)) + (owned ? "  ✓" : ""), 28, owned ? UIKit.Turquoise : UIKit.Sand);
            }

            var run = save.ActiveTower;
            var play = UIKit.Card(_content);
            if (!save.IsTowerUnlocked)
                Paragraph(play, Loc.F("Finis {0} pour ouvrir la tour.", LevelNames.Of(Progression.Last(Difficulty.Easy))), 30, UIKit.Gold);
            else if (run != null)
            {
                UIKit.SectionTitle(play, "Course en cours");
                Paragraph(play, Loc.F("Étage {0}", run.Floor) + (run.Curses.Count > 0 ? "\n" + CurseNames(run.Curses) : ""), 30, UIKit.Sand);
                if (run.CurseDue)
                {
                    // Mandatory: the next floor waits for the pick.
                    Paragraph(play, Loc.T("Choisis un malus pour continuer :"), 28, UIKit.Gold);
                    var row = UIKit.Row(play, 330, 14);
                    foreach (var curse in Tower.Curses) CurseCard(row.transform, curse, run.Curses);
                }
                else UIKit.Size(UIKit.Button(play, Loc.F("Étage {0}", run.Floor), Continue, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
                UIKit.Size(UIKit.Button(play, "Abandonner la course", () => { save.EndTower(); Fill(); }, UIKit.TextSize, ButtonStyle.Leave), 72);
            }
            else UIKit.Size(UIKit.Button(play, "Commencer", StartRun, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(_content, "Classement", Ranking), UIKit.ButtonHeight);
        }

        static void Paragraph(Transform parent, string text, int size, Color color)
        {
            var label = UIKit.Label(parent, "", size, color);
            label.text = text;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>A square to tap: the handicap's icon, its name, how many times it is taken, and what it does in a line.</summary>
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
            var name = UIKit.Label(card.transform, Loc.T(NameOf(curse)), 26, can ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 16);
            UIKit.Size(name, 64);
            var count = UIKit.Label(card.transform, $"+1  ({taken.Of(curse)}/{Tower.Cap(curse)})", 24, can ? UIKit.Gold : UIKit.Dim); // noloc
            UIKit.Size(count, 32);
            var desc = UIKit.Label(card.transform, Loc.T(DescOf(curse)), 20, UIKit.Dim);
            UIKit.FitText(desc, 13);
            UIKit.Size(desc, 70);
        }

        /// <summary>Armed spikes for the traps, the tomb map for the size, the tower for the storeys.</summary>
        Sprite IconOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? App.Art.ForTile(new Tile { Type = TileType.Trap, Trap = TrapKind.Spikes }, new TileLook { Armed = true })
            : curse == TowerCurse.Bigger ? UISprites.Map : UISprites.Tower;

        static string NameOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? "Plus de pièges" : curse == TowerCurse.Bigger ? "Tombeaux plus grands" : "Un étage de plus par tombeau";

        static string DescOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? "Un piège de plus à chaque étage, pointes puis ombre."
            : curse == TowerCurse.Bigger ? "Chaque tombeau gagne une case en largeur et en hauteur."
            : "Chaque tombeau a un étage de plus à traverser.";

        /// <summary>"Plus de pièges ×2 · Tombeaux plus grands": the handicaps taken so far.</summary>
        static string CurseNames(TowerCurses curses)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var c in Tower.Curses)
            {
                int n = curses.Of(c);
                if (n > 0) names.Add(Loc.T(NameOf(c)) + (n > 1 ? " ×" + n : "")); // noloc
            }
            return Loc.F("Malus : {0}", string.Join(" · ", names));
        }

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

        void Ranking()
        {
            Router.Get<LeaderboardScreen>().FocusTower(); // before it opens: it loads the board it shows
            Router.Open<LeaderboardScreen>();
        }
    }
}
