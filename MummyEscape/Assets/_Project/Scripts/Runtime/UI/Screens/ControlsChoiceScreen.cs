using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Start of the first intro: the player picks how to move (swipe, D-pad, joystick), live behind the card.</summary>
    public sealed class ControlsChoiceScreen : UIScreen
    {
        public override bool IsModal => true;

        UIKit.Segmented _controls;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Card(Root, 44, 22);
            UIKit.FitInParent(UIKit.Place(panel, 0.5f, 0.5f, 860, 0));
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Size(UIKit.Title(panel, "Comment veux-tu te déplacer ?", 48), 100);
            var s = App.Settings;
            _controls = new UIKit.Segmented(panel, new[] { "Glisser", "Croix", "Joystick" }, i => s.SetControls((Services.MoveControl)i));
            UIKit.Toggle(panel, "Croix et joystick à droite", s.PadRight, s.SetPadRight);
            UIKit.Size(UIKit.Label(panel, "Tu pourras changer dans les Réglages.", 30, UIKit.Sand), 60);
            UIKit.Size(UIKit.Button(panel, "C'est parti !", () => Router.Close(this), 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
        }

        public override void OnShow()
        {
            _controls.Select((int)App.Settings.Controls);
            App.Game.SetPaused(true);
        }

        public override void OnHide() => App.Game.SetPaused(false);
    }
}
