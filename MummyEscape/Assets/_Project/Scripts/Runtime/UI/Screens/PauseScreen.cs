using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class PauseScreen : UIScreen
    {
        public override bool IsModal => true;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform);

            var panel = UIKit.Panel(Root);
            var rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(800, 900);
            UIKit.Column(panel.transform, 28, 60);

            UIKit.Size(UIKit.Label(panel.transform, "Pause", 72, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), 120);
            UIKit.Size(UIKit.Button(panel.transform, "Reprendre", Resume), 130);
            UIKit.Size(UIKit.Button(panel.transform, "Recommencer", () => { Router.Close(this); App.Game.Restart(); }), 130);
            UIKit.Size(UIKit.Button(panel.transform, "Paramètres", () => Router.Open<SettingsScreen>()), 130);
            UIKit.Size(UIKit.Button(panel.transform, "Quitter le niveau", () => { App.Game.Abandon(); Router.Reset<MainMenuScreen>(); }), 130);
        }

        public override void OnShow() => App.Game.SetPaused(true);
        public override void OnHide() => App.Game.SetPaused(false);

        void Resume() => Router.Close(this);
    }
}
