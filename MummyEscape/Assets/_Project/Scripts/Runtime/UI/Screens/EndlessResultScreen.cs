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
        Button _main, _ranking;
        Difficulty _mode;
        /// <summary>The Halloween tomb to play on "Main", 0 on an endless ladder.</summary>
        int _halloweenNext;
        /// <summary>A tower floor's end: "Main" climbs on, or opens the tower for a new run.</summary>
        bool _tower, _towerClimbs;

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
            _ranking = UIKit.Button(panel, "Classement", Ranking);
            UIKit.Size(_ranking, UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Menu principal", Menu, UIKit.TextSize, ButtonStyle.Ghost), 72);
        }

        /// <param name="tomb">Number of the tomb just played; <paramref name="progress"/> already counts the run.</param>
        public void Show(Difficulty mode, int tomb, LevelResult result, EndlessProgress progress)
        {
            _mode = mode;
            _halloweenNext = 0;
            _tower = false;
            _ranking.gameObject.SetActive(true);
            bool more = progress.Cleared < GameController.EndlessTombCount(mode);
            _title.text = result.Won ? Loc.F("Tombeau {0} atteint !", progress.Reached) : Loc.T("La momie est tombée…");
            _text.text = result.Won
                ? Loc.F("Tombeau {0} franchi en {1}.", tomb, LevelResult.FormatTime(result.TimeMs)) + "\n"
                  + Loc.F("+{0} scarabées · +{1} XP de pass", Monetization.BattlePass.EndlessScarabs, Monetization.BattlePass.EndlessXp) + "\n"
                  + Loc.F("Temps cumulé : {0}", LevelResult.FormatTime(progress.TimeMs))
                  + (more ? "" : "\n" + Loc.T("Tu as franchi tous les tombeaux : d'autres arrivent avec les mises à jour."))
                : Loc.F("Le tombeau {0} t'attend toujours : même carte, nouvel essai.", tomb);
            _energy.text = PlayGate.Status(App, Monetization.PlayMode.Endless);
            _main.gameObject.SetActive(more);
            UIKit.SetLabel(_main, result.Won ? "Tombeau suivant" : "Réessayer");
        }

        /// <summary>End of a Halloween tomb: on to the next one (the first again once the act is finished), or the same one.</summary>
        public void ShowHalloween(int tomb, LevelResult result, bool finished)
        {
            _halloweenNext = !result.Won ? tomb : App.Save.HalloweenDone ? tomb % Halloween.TombCount + 1 : App.Save.HalloweenNext;
            _tower = false;
            _ranking.gameObject.SetActive(false);
            _title.text = finished ? Loc.T("Acte Halloween terminé !") : result.Won ? Loc.F("Tombeau {0} franchi !", tomb) : Loc.T("La momie est tombée…");
            _text.text = finished ? Loc.F("Tu gagnes « {0} » et le thème d'accueil Halloween.", Loc.T(Visual.HalloweenSkins.Resolve(Halloween.RewardSkin).Name))
                       : result.Won ? Loc.F("Tombeau {0} franchi en {1}.", tomb, LevelResult.FormatTime(result.TimeMs))
                         + (App.Save.HalloweenDone ? "" : "\n" + Loc.F("{0} / {1} tombeaux", App.Save.Data.HalloweenCleared, Halloween.TombCount))
                       : Loc.F("Le tombeau {0} t'attend toujours : même carte, nouvel essai.", tomb);
            _energy.text = PlayGate.Status(App, Monetization.PlayMode.Solo);
            // The event may close while a tomb is played: that run counts, no new one starts.
            _main.gameObject.SetActive(Halloween.IsOpen(System.DateTime.UtcNow));
            UIKit.SetLabel(_main, result.Won ? "Tombeau suivant" : "Réessayer");
        }

        /// <summary>End of a tower floor: on to the next one, or the run is over (a death, or under 2 stars).</summary>
        public void ShowTower(int floor, LevelResult result, Services.TowerOutcome outcome, TowerRun run)
        {
            _tower = true;
            _towerClimbs = outcome.Continues;
            _halloweenNext = 0;
            _ranking.gameObject.SetActive(true);
            _title.text = outcome.Continues ? Loc.F("Étage {0} franchi !", floor)
                        : result.Won ? Loc.T("Moins de 2 étoiles : la course s'arrête")
                        : Loc.T("La momie est tombée…");
            string text = outcome.Continues
                ? Loc.F("Étage {0} franchi en {1}.", floor, LevelResult.FormatTime(result.TimeMs))
                  + "\n" + (run.CurseDue ? Loc.T("Choisis un malus pour continuer.") : Loc.F("L'étage {0} t'attend.", run.Floor))
                : Loc.F("Course terminée : {0} étages franchis.", run.Cleared);
            if (outcome.RewardSkin != null) text += "\n" + Loc.F("Tu gagnes « {0} » !", Loc.T(Visual.TowerSkins.Resolve(outcome.RewardSkin).Name));
            else if (outcome.Record) text += "\n" + Loc.T("Nouveau record !");
            _text.text = text;
            _energy.text = outcome.Continues ? "" : PlayGate.Status(App, Monetization.PlayMode.Endless);
            _main.gameObject.SetActive(true);
            UIKit.SetLabel(_main, !outcome.Continues ? "Nouvelle course" : run.CurseDue ? "Choisir un malus" : "Étage suivant");
        }

        void Main()
        {
            if (_tower)
            {
                Router.Close(this);
                if (_towerClimbs && App.Save.ActiveTower?.CurseDue == false) _ = App.Game.StartTower();
                else // a new run, or the handicap to pick: both on the tower screen
                {
                    App.Game.Abandon();
                    Router.Reset<MainMenuScreen>();
                    Router.Open<TowerScreen>();
                }
                return;
            }
            if (_halloweenNext > 0)
            {
                int tomb = _halloweenNext;
                PlayGate.HalloweenRun(App, () =>
                {
                    Router.Close(this);
                    _ = App.Game.StartHalloween(tomb);
                });
                return;
            }
            var mode = _mode;
            PlayGate.EndlessRun(App, () =>
            {
                Router.Close(this);
                _ = App.Game.StartEndless(mode);
            });
        }

        void Ranking()
        {
            // Before it opens: it loads the board it shows.
            if (_tower) Router.Get<LeaderboardScreen>().FocusTower();
            else Router.Get<LeaderboardScreen>().FocusEndless(_mode);
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
