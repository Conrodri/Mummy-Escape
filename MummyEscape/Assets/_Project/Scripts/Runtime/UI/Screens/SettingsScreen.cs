using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Sound, image and comfort options, on an opaque background so it reads clearly over the menu or the game.</summary>
    public sealed class SettingsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Home;

        public override bool IsModal => true;
        Button _language;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Paramètres", () => Router.Close(this));
            // Scrolls once the cards outgrow short screens.
            var body = UIKit.Scroll(Body(200, 110, 50), out var scroll);
            UIKit.Stretch((RectTransform)scroll.transform);
            scroll.GetComponent<Image>().color = Color.clear;
            body.GetComponent<VerticalLayoutGroup>().spacing = 36;
            var s = App.Settings;

            var sound = UIKit.Card(body);
            UIKit.SectionTitle(sound, "Son");
            UIKit.Slider(sound, "Musique", s.MusicVolume, s.SetMusicVolume);
            UIKit.Slider(sound, "Effets sonores", s.SfxVolume, v => s.SetSfxVolume(v));

            var image = UIKit.Card(body);
            UIKit.SectionTitle(image, "Image");
            UIKit.Slider(image, "Luminosité", s.Brightness, s.SetBrightness, -1f, 1f,
                v => Mathf.Abs(v) < 0.02f ? Loc.T("normale") : (v > 0 ? "+" : "") + Mathf.RoundToInt(v * 100f) + " %");
            UIKit.Toggle(image, "Effets lumineux avancés", s.AdvancedLighting, s.SetAdvancedLighting);
            UIKit.Toggle(image, "Tremblements d'écran", s.ScreenShake, s.SetScreenShake);

            var comfort = UIKit.Card(body);
            UIKit.SectionTitle(comfort, "Confort et compte");
            UIKit.Toggle(comfort, "Vibrations", s.Haptics, s.SetHaptics);
            UIKit.Toggle(comfort, "Aperçu du tombeau au départ", s.ShowPreview, s.SetShowPreview);
            _language = UIKit.Button(comfort, "Langue", () => Router.Open<LanguagePickerScreen>());
            UIKit.Size(_language, UIKit.ButtonHeight);
            RefreshLanguage();

            var row = UIKit.Row(comfort, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Mon compte", () => Router.Open<AccountScreen>()), -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Confidentialité", () => Router.Open<PrivacyScreen>()), -1, -1, 1);
#if !UNITY_IOS
            // Apple's guidelines discourage quit buttons; on iOS the home gesture closes the app.
            UIKit.Size(UIKit.Button(comfort, "Quitter le jeu", AskQuit, UIKit.TextSize, ButtonStyle.Danger), UIKit.ButtonHeight);
#endif

            var version = UIKit.Label(Root, $"Mummy Rush v{Application.version} · " + Loc.F("générateur v{0}", Core.DifficultyTable.GeneratorVersion), 28, UIKit.Dim); // noloc
            UIKit.BottomBand(version.rectTransform, 50, 30);
        }

        void AskQuit() =>
            Router.Open<ConfirmDialog>().Configure("Quitter Mummy Rush ?", "Ta progression est enregistrée.", "Quitter", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return System.Threading.Tasks.Task.FromResult<string>(null);
            });

        /// <summary>"Langue : Français", or "Langue : automatique (English)" when following the device.</summary>
        public void RefreshLanguage()
        {
            if (_language == null) return;
            UIKit.SetLabel(_language, string.IsNullOrEmpty(App.Settings.Language)
                ? Loc.F("Langue : automatique ({0})", Loc.Info.NativeName)
                : Loc.F("Langue : {0}", Loc.Info.NativeName));
        }
    }
}
