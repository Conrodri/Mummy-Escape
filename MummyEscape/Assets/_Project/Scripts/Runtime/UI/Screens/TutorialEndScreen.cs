using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>End of the tutorial: out of the corridor, on to the first tomb; fallen in it, once more.</summary>
    public sealed class TutorialEndScreen : UIScreen
    {
        public override bool IsModal => true;

        Text _title, _text;
        Button _main;
        bool _won;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Card(Root, 44, 22);
            UIKit.FitInParent(UIKit.Place(panel, 0.5f, 0.5f, 860, 0));
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel, "", 60);
            UIKit.Size(_title, 100);
            _text = UIKit.Label(panel, "", 32, UIKit.Sand);
            UIKit.Size(_text, 150);
            _main = UIKit.Button(panel, "Réessayer", Main, 38, ButtonStyle.Primary); // a label now: an empty one has no Text
            UIKit.Size(_main, UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(panel, "Menu principal", Menu, UIKit.TextSize, ButtonStyle.Ghost), 72);
        }

        public void Show(bool won)
        {
            _won = won;
            _title.text = Loc.T(won ? "Tutoriel terminé !" : "La momie est tombée…");
            _text.text = Loc.T(won
                ? "Tu connais maintenant tous les pièges des tombeaux.\nÀ toi de t'évader pour de vrai !"
                : "Pas de panique : le tutoriel ne coûte rien.\nRecommence et lis bien chaque panneau.");
            UIKit.SetLabel(_main, won ? "Jouer le tombeau 1-1" : "Réessayer");
        }

        void Main()
        {
            Router.Close(this);
            if (!_won) { App.Game.Restart(); return; }
            var first = Progression.First(Difficulty.Easy);
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            PlayGate.Solo(App, first, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartLevel(first);
            });
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
