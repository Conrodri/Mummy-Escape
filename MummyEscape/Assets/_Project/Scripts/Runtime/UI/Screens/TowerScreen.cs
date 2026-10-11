using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The challenge tower (<see cref="Tower"/>): the run in progress (go on, or leave it), or a new run with the handicaps
    /// chosen; the record and the floors that give an exclusive skin. A run costs one point of the endless energy.
    /// </summary>
    public sealed class TowerScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        RectTransform _content;
        TowerCurse _curses;

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
            Paragraph(rules, Loc.T("Chaque course tire de nouveaux tombeaux et coûte un point d'énergie du mode sans fin."), 26, UIKit.Dim);
            Paragraph(rules, best.BestPoints > 0
                ? Loc.F("Record : {0} pts en {1} étages · plus haut étage franchi : {2}", best.BestPoints, best.BestFloor, best.HighestCleared)
                : Loc.T("Pas encore de record."), 28, UIKit.Gold);

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
                Paragraph(play, Loc.F("Étage {0} · {1} pts", run.Floor, run.Points) + (run.Curses != TowerCurse.None ? "\n" + CurseNames(run.Curses) : ""), 30, UIKit.Sand);
                UIKit.Size(UIKit.Button(play, Loc.F("Étage {0}", run.Floor), Continue, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
                UIKit.Size(UIKit.Button(play, "Abandonner la course", () => { save.EndTower(); Fill(); }, UIKit.TextSize, ButtonStyle.Ghost), 72);
            }
            else
            {
                UIKit.SectionTitle(play, "Malus");
                Paragraph(play, Loc.T("Chaque malus rapporte 1 point de plus par étage (2 sans malus)."), 26, UIKit.Dim);
                Curse(play, "Plus de pièges", TowerCurse.Traps);
                Curse(play, "Tombeaux plus grands", TowerCurse.Bigger);
                Curse(play, "Un étage de plus par tombeau", TowerCurse.Storeys);
                UIKit.Size(UIKit.Button(play, Loc.F("Commencer · {0} pts par étage", Tower.PointsPerFloor(_curses)), StartRun, 36, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            }
            UIKit.Size(UIKit.Button(_content, "Classement", Ranking), UIKit.ButtonHeight);
        }

        static void Paragraph(Transform parent, string text, int size, Color color)
        {
            var label = UIKit.Label(parent, "", size, color);
            label.text = text;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void Curse(Transform parent, string label, TowerCurse curse) =>
            UIKit.Toggle(parent, label, (_curses & curse) != 0, on =>
            {
                _curses = on ? _curses | curse : _curses & ~curse;
                Fill();
            });

        static string CurseNames(TowerCurse curses)
        {
            var names = new System.Collections.Generic.List<string>();
            if ((curses & TowerCurse.Traps) != 0) names.Add(Loc.T("Plus de pièges"));
            if ((curses & TowerCurse.Bigger) != 0) names.Add(Loc.T("Tombeaux plus grands"));
            if ((curses & TowerCurse.Storeys) != 0) names.Add(Loc.T("Un étage de plus par tombeau"));
            return string.Join(" · ", names);
        }

        void StartRun()
        {
            var curses = _curses;
            PlayGate.EndlessRun(App, () =>
            {
                App.Save.StartTower(curses);
                Continue();
            });
        }

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
