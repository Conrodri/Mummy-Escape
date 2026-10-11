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
            Paragraph(rules, Loc.T("Enchaîne les tombeaux, de plus en plus durs. La course s'arrête à la première chute, ou si tu sors avec moins de 2 étoiles."), 30, UIKit.Sand);
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
            if (run != null)
            {
                UIKit.SectionTitle(play, "Course en cours");
                Paragraph(play, Loc.F("Étage {0}", run.Floor) + (run.Curses.Count > 0 ? "\n" + CurseNames(run.Curses) : ""), 30, UIKit.Sand);
                if (run.CurseDue)
                {
                    // Mandatory: the next floor waits for the pick.
                    Paragraph(play, Loc.T("Choisis un malus pour continuer :"), 28, UIKit.Gold);
                    foreach (var curse in Tower.Curses)
                    {
                        var c = curse;
                        var b = UIKit.Button(play, Loc.F("{0} ({1}/{2})", Loc.T(NameOf(c)), run.Curses.Of(c), Tower.Cap(c)), () => Take(c), 32, ButtonStyle.Primary);
                        b.interactable = run.Curses.CanTake(c);
                        UIKit.Size(b, UIKit.ButtonHeight);
                    }
                }
                else UIKit.Size(UIKit.Button(play, Loc.F("Étage {0}", run.Floor), Continue, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
                UIKit.Size(UIKit.Button(play, "Abandonner la course", () => { save.EndTower(); Fill(); }, UIKit.TextSize, ButtonStyle.Ghost), 72);
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

        static string NameOf(TowerCurse curse) =>
            curse == TowerCurse.Traps ? "Plus de pièges" : curse == TowerCurse.Bigger ? "Tombeaux plus grands" : "Un étage de plus par tombeau";

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
