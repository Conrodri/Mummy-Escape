using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class PauseScreen : UIScreen
    {
        public override bool IsModal => true;

        Button _restart, _quit;
        Text _duelNote;

        protected override void Build()
        {
            var panel = ModalKit.Frame(Root, 760, Resume, 18); // tap outside = resume
            ModalKit.Header(panel, "Pause", 68);
            _duelNote = UIKit.Label(panel, "Duel : le chrono continue de tourner !", 30, UIKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_duelNote, 20);
            UIKit.Size(_duelNote, 50);
            UIKit.Size(UIKit.Button(panel, "Reprendre", Resume, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            _restart = UIKit.Button(panel, "Recommencer", Restart);
            UIKit.Size(_restart, UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Paramètres", () => Router.Open<SettingsScreen>()), UIKit.ButtonHeight);
            // A button like the others, its text in red: leaving loses the run.
            _quit = UIKit.Button(panel, "Quitter le niveau", Quit);
            _quit.GetComponentInChildren<Text>().color = UIKit.Danger;
            UIKit.FitText(_quit.GetComponentInChildren<Text>(), 20);
            UIKit.Size(_quit, UIKit.ButtonHeight);
        }

        public override void OnShow()
        {
            bool duel = App.Game.InDuel, relay = App.Game.InRelay, coop = App.Game.InCoop, tower = App.Game.InTower;
            _duelNote.gameObject.SetActive(duel || relay || coop);
            _duelNote.text = Loc.T(relay || coop ? "2v2 : le chrono continue de tourner !" : "Duel : le chrono continue de tourner !");
            _restart.gameObject.SetActive(!duel && !relay && !coop && !tower); // a tower floor is played once: its preview is already seen
            UIKit.SetLabel(_quit, tower ? "Abandonner la course (fin de la tour)" : coop ? "Quitter (la partie s'arrête pour vous deux)" : relay ? "Quitter le match (défaite du duo)" : duel ? "Abandonner le duel (défaite)" : "Quitter le niveau");
            App.Game.SetPaused(true);
        }

        public override void OnHide() => App.Game.SetPaused(false);

        void Resume() => Router.Close(this);

        void Restart()
        {
            if (App.Game.InTutorial) { Router.Close(this); App.Game.Restart(); return; } // the tutorial costs no energy
            if (App.Game.InEndless) { PlayGate.EndlessRun(App, () => { Router.Close(this); App.Game.Restart(); }); return; }
            PlayGate.Solo(App, App.Game.CurrentLevel, () => { Router.Close(this); App.Game.Restart(); });
        }

        void Quit()
        {
            if (App.Game.InCoop)
            {
                CoopScreen.BackToCampaign(App);
                return;
            }
            if (App.Game.InRelay)
            {
                // The teammate is told; the result screen opens over the HUD.
                Router.Close(this);
                App.Game.ForfeitRelay();
                return;
            }
            if (App.Game.InDuel)
            {
                // The forfeit is sent like any run; the result screen opens over the HUD.
                Router.Close(this);
                App.Game.ForfeitDuel();
                return;
            }
            if (App.Game.InTower) App.Save.EndTower(); // leaving a floor ends the run where it stands
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
