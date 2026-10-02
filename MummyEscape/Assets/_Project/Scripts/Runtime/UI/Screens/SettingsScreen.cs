using UnityEngine;

namespace MummyEscape.UI.Screens
{
    /// <summary>Sound, image and comfort options, on an opaque background so it reads clearly over the menu or the game.</summary>
    public sealed class SettingsScreen : UIScreen
    {
        public override bool IsModal => true;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Paramètres", () => Router.Close(this));
            var body = Body(200, 110, 50);
            UIKit.Column(body, 36);
            var s = App.Settings;

            var sound = UIKit.Card(body);
            UIKit.SectionTitle(sound, "Son");
            UIKit.Slider(sound, "Musique", s.MusicVolume, s.SetMusicVolume);
            UIKit.Slider(sound, "Effets sonores", s.SfxVolume, v => s.SetSfxVolume(v));

            var image = UIKit.Card(body);
            UIKit.SectionTitle(image, "Image");
            UIKit.Slider(image, "Luminosité", s.Brightness, s.SetBrightness, -1f, 1f,
                v => Mathf.Abs(v) < 0.02f ? "normale" : (v > 0 ? "+" : "") + Mathf.RoundToInt(v * 100f) + " %");
            UIKit.Toggle(image, "Effets lumineux avancés", s.AdvancedLighting, s.SetAdvancedLighting);
            UIKit.Toggle(image, "Tremblements d'écran", s.ScreenShake, s.SetScreenShake);

            var comfort = UIKit.Card(body);
            UIKit.SectionTitle(comfort, "Confort");
            UIKit.Toggle(comfort, "Vibrations", s.Haptics, s.SetHaptics);

            var version = UIKit.Label(Root, $"Mummy Escape v{Application.version} · générateur v{Core.DifficultyTable.GeneratorVersion}", 28, UIKit.Dim);
            UIKit.BottomBand(version.rectTransform, 50, 30);
        }
    }
}
