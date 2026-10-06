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
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Resume); // tap outside = resume

            var panel = UIKit.Card(Root, 44, 18);
            UIKit.FitInParent(UIKit.Place(panel, 0.5f, 0.5f, 760, 0));
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Size(UIKit.Title(panel, "Pause", 68), 100);
            _duelNote = UIKit.Label(panel, "Duel : le chrono continue de tourner !", 30, UIKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_duelNote, 20);
            UIKit.Size(_duelNote, 50);
            UIKit.Size(UIKit.Button(panel, "Reprendre", Resume, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            _restart = UIKit.Button(panel, "Recommencer", () => PlayGate.Play(App, Monetization.PlayMode.Solo, () => { Router.Close(this); App.Game.Restart(); }));
            UIKit.Size(_restart, UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Paramètres", () => Router.Open<SettingsScreen>()), UIKit.ButtonHeight);
            _quit = UIKit.Button(panel, "Quitter le niveau", Quit, UIKit.TextSize, ButtonStyle.Ghost);
            UIKit.Size(_quit, 72);
        }

        public override void OnShow()
        {
            bool duel = App.Game.InDuel, relay = App.Game.InRelay;
            _duelNote.gameObject.SetActive(duel || relay);
            _duelNote.text = Loc.T(relay ? "2v2 : le chrono continue de tourner !" : "Duel : le chrono continue de tourner !");
            _restart.gameObject.SetActive(!duel && !relay);
            UIKit.SetLabel(_quit, relay ? "Quitter le match (défaite du duo)" : duel ? "Abandonner le duel (défaite)" : "Quitter le niveau");
            App.Game.SetPaused(true);
        }

        public override void OnHide() => App.Game.SetPaused(false);

        void Resume() => Router.Close(this);

        void Quit()
        {
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
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
