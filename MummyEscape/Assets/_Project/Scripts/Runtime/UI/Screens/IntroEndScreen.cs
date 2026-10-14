using MummyEscape.App;
using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>End of an act intro: out of the corridor, on to the act's first tomb; fallen in it, once more.</summary>
    public sealed class IntroEndScreen : UIScreen
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
            UIKit.Size(UIKit.Button(panel, "Menu principal", Menu, UIKit.TextSize, ButtonStyle.Leave), 72);
        }

        public void Show(bool won)
        {
            _won = won;
            _title.text = Loc.T(won ? "Intro terminée !" : "La momie est tombée…");
            _text.text = Loc.T(won
                ? "Tu connais les pièges de cet acte.\nÀ toi de t'évader pour de vrai !"
                : "Pas de panique : l'intro ne coûte rien.\nRecommence et lis bien chaque panneau.");
            UIKit.SetLabel(_main, won ? Loc.F("Jouer le tombeau {0}-1", App.Game.IntroAct) : "Réessayer");
        }

        void Main()
        {
            Router.Close(this);
            if (!_won) { App.Game.Restart(); return; }
            PlayAct(App, Router, App.Game.IntroAct);
        }

        /// <summary>Leaves the intro (done by now) for the act's first Easy tomb: the end screen's button and Pause › Passer.</summary>
        public static void PlayAct(GameApp app, UIRouter router, int act)
        {
            var first = new LevelId(Difficulty.Easy, act, 1);
            app.Game.Abandon();
            router.Reset<MainMenuScreen>();
            PlayGate.Solo(app, first, () =>
            {
                router.Open<HudScreen>();
                _ = app.Game.StartLevel(first);
            });
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
