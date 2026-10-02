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
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Resume); // tap outside = resume

            var panel = UIKit.Card(Root, 44, 18);
            UIKit.Place(panel, 0.5f, 0.5f, 760, 0);
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Size(UIKit.Title(panel, "Pause", 68), 100);
            UIKit.Size(UIKit.Button(panel, "Reprendre", Resume, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(panel, "Recommencer", () => { Router.Close(this); App.Game.Restart(); }), UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Paramètres", () => Router.Open<SettingsScreen>()), UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(panel, "Quitter le niveau", () => { App.Game.Abandon(); Router.Reset<MainMenuScreen>(); }, UIKit.TextSize, ButtonStyle.Ghost), 72);
        }

        public override void OnShow() => App.Game.SetPaused(true);
        public override void OnHide() => App.Game.SetPaused(false);

        void Resume() => Router.Close(this);
    }
}
