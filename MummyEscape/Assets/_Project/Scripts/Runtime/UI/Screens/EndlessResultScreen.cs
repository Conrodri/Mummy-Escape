using MummyEscape.Core;
using MummyEscape.Game;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// End of an endless tomb: "Tombeau n atteint" and on to the next one, or fallen and the same tomb again (each costs a
    /// point of the endless energy). No stars: only getting out counts, the time adds up for the board.
    /// </summary>
    public sealed class EndlessResultScreen : UIScreen
    {
        public override bool IsModal => true;

        Text _title, _text, _energy;
        Button _main;
        Difficulty _mode;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Card(Root, 44, 22);
            UIKit.FitInParent(UIKit.Place(panel, 0.5f, 0.5f, 860, 0));
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel, "", 60);
            UIKit.FitText(_title, 34);
            UIKit.Size(_title, 100);
            _text = UIKit.Label(panel, "", 32, UIKit.Sand);
            UIKit.Size(_text, 150);
            _energy = UIKit.Label(panel, "", 26, UIKit.Dim);
            UIKit.Size(_energy, 40);
            _main = UIKit.Button(panel, "Tombeau suivant", Main, 38, ButtonStyle.Primary); // a label now: an empty one has no Text
            UIKit.Size(_main, UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(panel, "Classement", Ranking), UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Menu principal", Menu, UIKit.TextSize, ButtonStyle.Ghost), 72);
        }

        /// <param name="tomb">Number of the tomb just played; <paramref name="progress"/> already counts the run.</param>
        public void Show(Difficulty mode, int tomb, LevelResult result, EndlessProgress progress)
        {
            _mode = mode;
            bool more = progress.Cleared < GameController.EndlessTombCount(mode);
            _title.text = result.Won ? Loc.F("Tombeau {0} atteint !", progress.Reached) : Loc.T("La momie est tombée…");
            _text.text = result.Won
                ? Loc.F("Tombeau {0} franchi en {1}.", tomb, LevelResult.FormatTime(result.TimeMs)) + "\n"
                  + Loc.F("Temps cumulé : {0}", LevelResult.FormatTime(progress.TimeMs))
                  + (more ? "" : "\n" + Loc.T("Tu as franchi tous les tombeaux : d'autres arrivent avec les mises à jour."))
                : Loc.F("Le tombeau {0} t'attend toujours : même carte, nouvel essai.", tomb);
            _energy.text = PlayGate.Status(App, Monetization.PlayMode.Endless);
            _main.gameObject.SetActive(more);
            UIKit.SetLabel(_main, result.Won ? "Tombeau suivant" : "Réessayer");
        }

        void Main()
        {
            var mode = _mode;
            PlayGate.EndlessRun(App, () =>
            {
                Router.Close(this);
                _ = App.Game.StartEndless(mode);
            });
        }

        void Ranking()
        {
            Router.Get<LeaderboardScreen>().FocusEndless(_mode); // before it opens: it loads the board it shows
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            Router.Open<LeaderboardScreen>();
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
