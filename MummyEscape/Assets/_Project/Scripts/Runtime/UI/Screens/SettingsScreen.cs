using UnityEngine;

namespace MummyEscape.UI.Screens
{
    public sealed class SettingsScreen : UIScreen
    {
        public override bool IsModal => true;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0.04f, 0.03f, 0.02f, 0.96f), true);
            UIKit.Stretch(shade.rectTransform);
            Header("Paramètres", () => Router.Close(this));
            var body = Body(220, 80, 70);
            UIKit.Column(body, 26);
            var s = App.Settings;

            UIKit.Size(UIKit.Label(body, "Son", 46, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 70);
            UIKit.Slider(body, "Musique", s.MusicVolume, s.SetMusicVolume);
            UIKit.Slider(body, "Effets sonores", s.SfxVolume, v => s.SetSfxVolume(v));

            UIKit.Size(UIKit.Label(body, "Image", 46, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 70);
            UIKit.Slider(body, "Luminosité", s.Brightness, s.SetBrightness, -1f, 1f);
            UIKit.Toggle(body, "Effets lumineux avancés (bloom, grain, poussière)", s.AdvancedLighting, s.SetAdvancedLighting);
            UIKit.Toggle(body, "Tremblements d'écran", s.ScreenShake, s.SetScreenShake);

            UIKit.Size(UIKit.Label(body, "Confort", 46, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 70);
            UIKit.Toggle(body, "Vibrations", s.Haptics, s.SetHaptics);

            var version = UIKit.Label(Root, $"Mummy Escape v{Application.version} · générateur v{Core.DifficultyTable.GeneratorVersion}", 26, UIKit.Dim);
            UIKit.BottomBand(version.rectTransform, 50, 20);
        }
    }
}
