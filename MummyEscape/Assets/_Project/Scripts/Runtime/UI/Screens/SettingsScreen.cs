using MummyEscape.Monetization;
using MummyEscape.Services;
using MummyEscape.UI.Legal;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The options, one card per tab: sound, game, image, notifications, account, help. Opaque background so it reads
    /// clearly over the menu or the game. The privacy choices and rights have their own screen (Confidentialité).
    /// </summary>
    public sealed class SettingsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Home;

        public override bool IsModal => true;
        Button _language;
        readonly List<RectTransform> _cards = new List<RectTransform>();
        readonly List<string> _titles = new List<string>();
        UIKit.Segmented _tabs;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Paramètres", () => Router.Close(this));
            // The tabs on top, the chosen card under them (scrolls on short screens).
            var column = Body(200, 110, 50);
            UIKit.Column(column, 24);
            var body = UIKit.Scroll(column, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _scroll.GetComponent<Image>().color = Color.clear;
            var s = App.Settings;

            var sound = Card(body, "Son");
            UIKit.Slider(sound, "Ambiance", s.MusicVolume, s.SetMusicVolume);
            UIKit.Slider(sound, "Effets sonores", s.SfxVolume, v => s.SetSfxVolume(v));
            UIKit.Toggle(sound, "Vibrations", s.Haptics, s.SetHaptics);

            var game = Card(body, "Jeu");
            var moveLabel = UIKit.Label(game, "Déplacements", UIKit.TextSize, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(moveLabel, 56);
            var controls = new UIKit.Segmented(game, new[] { "Glisser", "Croix", "Joystick" }, i => s.SetControls((Services.MoveControl)i));
            controls.Select((int)s.Controls);
            UIKit.Toggle(game, "Croix et joystick à droite", s.PadRight, s.SetPadRight);
            UIKit.Toggle(game, "Aperçu du tombeau au départ", s.ShowPreview, s.SetShowPreview);

            var image = Card(body, "Image");
            UIKit.Toggle(image, "Effets lumineux avancés", s.AdvancedLighting, s.SetAdvancedLighting);
            UIKit.Toggle(image, "Tremblements d'écran", s.ScreenShake, s.SetScreenShake);
            UIKit.Toggle(image, "Économie de batterie (30 images/s)", s.BatterySaver, s.SetBatterySaver);

            if (Notifications.Provider != null)
            {
                var notif = Card(body, "Notifications");
                UIKit.Toggle(notif, "Énergie pleine", s.NotifyEnergy, on => { s.SetNotifyEnergy(on); if (on) Notifications.Ask(); });
                UIKit.Toggle(notif, "Événements (Halloween…)", s.NotifyEvents, on => { s.SetNotifyEvents(on); if (on) Notifications.Ask(); });
                UIKit.Toggle(notif, "Quêtes du jour", s.NotifyQuests, on => { s.SetNotifyQuests(on); if (on) Notifications.Ask(); });
            }

            var account = Card(body, "Compte");
            UIKit.Size(UIKit.Button(account, "Mon compte", () => Router.Open<AccountScreen>()), UIKit.ButtonHeight);
            UIKit.Size(UIKit.Button(account, "Confidentialité et données", () => Router.Open<PrivacyScreen>(), UIKit.TextSize, ButtonStyle.Secondary), UIKit.ButtonHeight);
            _language = UIKit.Button(account, "Langue", () => Router.Open<LanguagePickerScreen>(), UIKit.TextSize, ButtonStyle.Secondary);
            UIKit.Size(_language, UIKit.ButtonHeight);
            RefreshLanguage();

            var help = Card(body, "Aide");
            var row = UIKit.Row(help, UIKit.ButtonHeight, 20);
            var rate = UIKit.Button(row.transform, "Noter Mummy Rush", () => Application.OpenURL("market://details?id=" + Application.identifier)); // noloc
            UIKit.FitText(rate.GetComponentInChildren<Text>(), 26); // two words too many for half a narrow row
            UIKit.Size(rate, -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Discord", () => Application.OpenURL(FriendsScreen.DiscordInvite), UIKit.TextSize, ButtonStyle.Secondary), -1, -1, 1); // noloc
            var row2 = UIKit.Row(help, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row2.transform, "Nous écrire", () => Application.OpenURL("mailto:" + LegalTexts.Contact + "?subject=Mummy%20Rush%20-%20support"), UIKit.TextSize, ButtonStyle.Secondary), -1, -1, 1); // noloc
            UIKit.Size(UIKit.Button(row2.transform, "Crédits", () => Router.Open<LegalScreen>().Show("Crédits", Credits.Lines), UIKit.TextSize, ButtonStyle.Secondary), -1, -1, 1);
            PrivacyUI.PlayerIdLine(App, help);

            // Built last, once the cards (the notifications one is optional) are known, then put over them.
            _tabs = new UIKit.Segmented(column, _titles.ToArray(), ShowCard);
            _tabs.Root.transform.SetAsFirstSibling();
            ShowCard(0);

            var version = UIKit.Label(Root, $"Mummy Rush v{Application.version} · " + Loc.F("générateur v{0}", Core.DifficultyTable.GeneratorVersion), 28, UIKit.Dim); // noloc
            UIKit.BottomBand(version.rectTransform, 50, 30);
        }

        RectTransform Card(Transform body, string title)
        {
            var card = UIKit.Card(body);
            UIKit.SectionTitle(card, title);
            _cards.Add(card);
            _titles.Add(title);
            return card;
        }

        void ShowCard(int index)
        {
            _tabs.Select(index);
            for (int i = 0; i < _cards.Count; i++) _cards[i].gameObject.SetActive(i == index);
            _scroll.verticalNormalizedPosition = 1f;
        }

        public void RefreshLanguage()
        {
            if (_language == null) return;
            UIKit.SetLabel(_language, string.IsNullOrEmpty(App.Settings.Language)
                ? Loc.F("Langue : automatique ({0})", Loc.Info.NativeName)
                : Loc.F("Langue : {0}", Loc.Info.NativeName));
        }
    }

    /// <summary>Who made the game and what it is built on.</summary>
    static class Credits
    {
        public static string[] Lines => new[]
        {
            "# Mummy Rush", // noloc
            Loc.T("Conçu, dessiné et programmé par Peak Development (Constant Rodriguez), Paris."),
            "# " + Loc.T("Outils et contenus"),
            Loc.T("Moteur : Unity (Unity Technologies). Services en ligne : Unity Gaming Services.") + "\n" +
            Loc.T("Pixel-art : procédural, avec des références générées via PixelLab.") + "\n" +
            Loc.T("Musiques : composées avec ACE-Step, arrangées pour le jeu.") + "\n" +
            Loc.T("Polices : Nunito (Vernon Adams, Cyreal, Jacques Le Bailly) et Cinzel (Natanael Gama), sous licence SIL Open Font License 1.1."),
            "# " + Loc.T("Merci"),
            Loc.T("Aux premiers testeurs du Discord, pour chaque tombeau signalé trop sombre ou trop facile."),
        };
    }
}
