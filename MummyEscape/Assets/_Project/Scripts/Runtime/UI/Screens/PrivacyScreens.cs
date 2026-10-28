using System;
using System.Threading.Tasks;
using MummyEscape.Online;
using MummyEscape.Services;
using MummyEscape.UI.Legal;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Shared building blocks of the account / privacy screens.</summary>
    static class PrivacyUI
    {
        public static Text Paragraph(Transform parent, string text, int size = 32, Color? color = null)
        {
            var t = UIKit.Label(parent, text, size, color ?? UIKit.Sand, TextAnchor.UpperLeft);
            t.lineSpacing = 1.1f;
            return t;
        }

        /// <summary>Text with "# " section titles (legal documents).</summary>
        public static void Document(Transform parent, string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("# "))
                {
                    var title = UIKit.Label(parent, line.Substring(2), 40, UIKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold);
                    UIKit.Size(title, 90);
                }
                else Paragraph(parent, line, 30);
            }
        }

        public static Button Wide(Transform parent, string label, Action onClick, int size = UIKit.TextSize, float height = UIKit.ButtonHeight, ButtonStyle style = ButtonStyle.Secondary)
        {
            var b = UIKit.Button(parent, label, onClick, size, style);
            UIKit.Size(b, height);
            return b;
        }

        /// <summary>The player's id in a read-only field with a copy button (for a support ticket); nothing offline.</summary>
        public static void PlayerIdLine(App.GameApp app, Transform card)
        {
            string id = app.Online.PlayerId;
            if (string.IsNullOrEmpty(id) || !app.Online.IsAvailable) return;
            var field = UIKit.Input(card, "", 24);
            field.text = id;
            field.readOnly = true;
            UIKit.Size(field, 70);
            Button copy = null;
            copy = Wide(card, "Copier l'identifiant", () =>
            {
                GUIUtility.systemCopyBuffer = id;
                UIKit.SetLabel(copy, "Copié !");
            }, 28, 80);
            Paragraph(card, "Ton identifiant de joueur, à donner au support si besoin.", 24, UIKit.Dim);
        }

        public static RectTransform ScrollBody(UIScreen screen, RectTransform body, out ScrollRect scroll)
        {
            var content = UIKit.Scroll(body, out scroll);
            UIKit.Stretch((RectTransform)scroll.transform);
            content.GetComponent<VerticalLayoutGroup>().spacing = 28;
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(20, 20, 20, 40);
            return content;
        }

        public static void OpenLegal(UIRouter router, bool privacy) =>
            router.Open<LegalScreen>().Show(privacy ? "Confidentialité" : "Conditions", privacy ? LegalTexts.CurrentPrivacy : LegalTexts.CurrentTerms);
    }

    // ====================================================================== legal documents

    /// <summary>Privacy policy / terms of use, readable in the game at any time.</summary>
    public sealed class LegalScreen : UIScreen
    {
        public override bool IsModal => true;
        Text _title;
        RectTransform _content;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            _title = Header("", () => Router.Close(this));
            _content = PrivacyUI.ScrollBody(this, Body(190, 40, 40), out _scroll);
        }

        public void Show(string title, string[] lines)
        {
            _title.text = Loc.T(title);
            UIKit.ClearChildren(_content);
            PrivacyUI.Document(_content, lines);
            _scroll.verticalNormalizedPosition = 1f;
        }
    }

    // ====================================================================== first launch

    /// <summary>
    /// First launch (and after a policy change): explains what happens offline and online, lets the player choose,
    /// asks the birth year only to play online (neutral age gate, nothing stored but "minor or not"), and asks a
    /// parent's approval under the age of digital consent. Cannot be skipped with the back button.
    /// </summary>
    public sealed class WelcomeScreen : UIScreen, IBackHandler
    {
        public enum Step { Intro, Age, Parent }

        public override bool IsModal => true;
        Step _step;
        bool _fromSettings;
        RectTransform _content;
        ScrollRect _scroll;
        bool _isMinor;
        bool _analytics;
        Action _then;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            var title = UIKit.Label(Root, "MUMMY RUSH", 72, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.TopBand(title.rectTransform, 130, 40);
            _content = PrivacyUI.ScrollBody(this, Body(190, 40, 40), out _scroll);
        }

        /// <summary>Opens at a given step (the age or the parent, asked by the chat or the settings); <paramref name="then"/> runs once answered.</summary>
        public WelcomeScreen Begin(Step step, bool fromSettings, Action then = null)
        {
            _fromSettings = fromSettings;
            _then = then;
            Go(step);
            return this;
        }

        // Opened without Begin (startup): first-launch flow.
        public override void OnShow()
        {
            _fromSettings = false;
            Go(Step.Intro);
        }

        public void OnBack()
        {
            if (_step == Step.Parent) Go(Step.Age);
            else if (_step == Step.Age && !_fromSettings) Go(Step.Intro);
            else if (_fromSettings) Router.Close(this);
            // Intro on first launch: a choice is required.
        }

        void Go(Step step)
        {
            _step = step;
            UIKit.ClearChildren(_content);
            switch (step)
            {
                case Step.Intro: Intro(); break;
                case Step.Age: Age(); break;
                case Step.Parent: Parent(); break;
            }
            _scroll.verticalNormalizedPosition = 1f;
        }

        void Intro()
        {
            UIKit.Size(UIKit.Title(_content, "Avant de jouer", 46), 90);
            PrivacyUI.Paragraph(_content,
                "Mummy Rush se joue sans connexion : dans ce mode, rien ne quitte ton téléphone.\n\n" +
                "Le mode en ligne ajoute les classements, les amis et un compte facultatif pour retrouver ta progression sur un autre appareil. " +
                "Il envoie alors à notre prestataire (Unity) un identifiant aléatoire, ton pseudonyme et tes scores.\n\n" +
                "Ni pistage, ni adresse e-mail. Les publicités (Google) ne s'affichent que si tu choisis d'en regarder une pour rejouer.");
            var links = UIKit.Row(_content, 100, 20);
            UIKit.Size(UIKit.Button(links.transform, "Confidentialité", () => PrivacyUI.OpenLegal(Router, true), 32), -1, -1, 1);
            UIKit.Size(UIKit.Button(links.transform, "Conditions", () => PrivacyUI.OpenLegal(Router, false), 32), -1, -1, 1);
            UIKit.Size(UIKit.Rect("Gap", _content), 20);
            if (Services.Analytics.Provider != null)
            {
                _analytics = false; // never pre-ticked
                UIKit.Toggle(_content, "En ligne, partager des statistiques d'usage anonymes", false, v => _analytics = v);
                PrivacyUI.Paragraph(_content, "Quels tombeaux sont joués, gagnés ou abandonnés (prestataire Unity, jamais d'identifiant publicitaire). Modifiable dans Confidentialité.", 26, UIKit.Dim);
            }
            PrivacyUI.Paragraph(_content, "En jouant, tu acceptes les conditions d'utilisation et la politique de confidentialité.", 28, UIKit.Sand);
            PrivacyUI.Wide(_content, "Jouer", () => Finish(true, null, false), 38, 104, ButtonStyle.Primary);
            PrivacyUI.Wide(_content, "Jouer hors ligne", () => Finish(false, null, false));
            PrivacyUI.Paragraph(_content, "Tu peux changer d'avis à tout moment dans Paramètres › Confidentialité.", 28, UIKit.Dim);
        }

        void Age()
        {
            UIKit.Size(UIKit.Title(_content, "Ton année de naissance", 46), 90);
            PrivacyUI.Paragraph(_content, "Elle sert seulement à savoir si l'accord d'un parent est nécessaire pour le tchat. Elle n'est ni conservée, ni envoyée.", 30, UIKit.Dim);
            var year = YearField("AAAA");
            var error = UIKit.Label(_content, "", 32, UIKit.Danger);
            UIKit.Size(error, 60);
            var row = UIKit.Row(_content, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Retour", OnBack, UIKit.TextSize, ButtonStyle.Leave), -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Continuer", () =>
            {
                if (!ReadYear(year, out int y)) { error.text = Loc.T("Indique une année valide (4 chiffres)."); return; }
                _isMinor = PrivacyService.IsUnderConsentAge(y, CountryService.Detect());
                if (_isMinor) Go(Step.Parent);
                else Finish(true, false, false);
            }, UIKit.TextSize, ButtonStyle.Primary), -1, -1, 1.4f);
        }

        void Parent()
        {
            UIKit.Size(UIKit.Title(_content, "Demande à un parent", 46), 90);
            PrivacyUI.Paragraph(_content,
                "Pour utiliser le tchat à ton âge, l'accord d'un parent ou d'un tuteur est nécessaire.\n\n" +
                "À l'attention du parent : le tchat permet à votre enfant d'échanger des messages avec ses amis (ajoutés par leur code) et sa guilde ; " +
                "le tchat global lui reste fermé. Aucune adresse e-mail, aucun nom réel. " +
                "Vous pouvez retirer cet accord à tout moment dans Paramètres › Confidentialité.");
            PrivacyUI.Wide(_content, "Lire la politique de confidentialité", () => PrivacyUI.OpenLegal(Router, true), 30, 88);
            PrivacyUI.Paragraph(_content, "Parent ou tuteur : votre année de naissance", 32, UIKit.Gold);
            var year = YearField("AAAA");
            bool confirmed = false;
            UIKit.Toggle(_content, "Je suis son parent ou tuteur et j'autorise", false, v => confirmed = v);
            var error = UIKit.Label(_content, "", 32, UIKit.Danger);
            UIKit.Size(error, 60);
            var row = UIKit.Row(_content, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Sans tchat", () => Finish(true, true, false)), -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Autoriser", () =>
            {
                if (!ReadYear(year, out int y) || DateTime.UtcNow.Year - y < 19) { error.text = Loc.T("L'accord doit venir d'un adulte."); return; }
                if (!confirmed) { error.text = Loc.T("Cochez la case pour confirmer votre accord."); return; }
                Finish(true, true, true);
            }, UIKit.TextSize, ButtonStyle.Primary), -1, -1, 1.4f);
        }

        InputField YearField(string placeholder)
        {
            var field = UIKit.Input(_content, placeholder, 42);
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 4;
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)field.placeholder).alignment = TextAnchor.MiddleCenter;
            UIKit.Size(field, 104);
            return field;
        }

        static bool ReadYear(InputField f, out int year) =>
            int.TryParse(f.text, out year) && year >= 1900 && year <= DateTime.UtcNow.Year;

        void Finish(bool online, bool? isMinor, bool parental)
        {
            App.Privacy.Answer(online, isMinor, parental);
            Router.Close(this);
            if (!_fromSettings)
            {
                // First launch: the online choice is made here. From the chat or the settings, the online mode is already running.
                App.Privacy.SetAnalytics(online && _analytics);
                if (App.Privacy.OnlineAllowed) _ = App.StartOnline(true);
                else App.StopOnline();
            }
            if (_fromSettings && Router.Get<PrivacyScreen>().gameObject.activeSelf) Router.Get<PrivacyScreen>().OnShow();
            _then?.Invoke();
            _then = null;
        }
    }

    // ====================================================================== privacy settings

    /// <summary>Every privacy choice and right in one place: online mode, sharing, country, export, deletion, documents.</summary>
    public sealed class PrivacyScreen : UIScreen
    {
        public override bool IsModal => true;
        RectTransform _content;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Confidentialité", () => Router.Close(this));
            _content = PrivacyUI.ScrollBody(this, Body(190, 40, 40), out _scroll);
        }

        public override void OnShow() => Rebuild();

        void Rebuild()
        {
            if (this == null) return;
            UIKit.ClearChildren(_content);
            var p = App.Privacy.Data;

            var online = UIKit.Card(_content);
            UIKit.SectionTitle(online, "Mode en ligne");
            UIKit.Toggle(online, "Classements, amis, compte", App.Privacy.OnlineAllowed, SetOnline);
            PrivacyUI.Paragraph(online, App.Online.IsAvailable ? Loc.F("État : en ligne ({0})", AccountLabel()) : Loc.F("État : {0}", Loc.T(App.Online.Status)), 28, UIKit.Dim);
            if (p.IsMinor && p.ParentalConsent)
                PrivacyUI.Wide(online, "Retirer l'autorisation parentale (tchat)", () => { App.Privacy.SetParentalConsent(false); Rebuild(); }, 30, 88);
            else if (p.IsMinor)
                PrivacyUI.Wide(online, "Autoriser le tchat (parent ou tuteur)", () => Router.Open<WelcomeScreen>().Begin(WelcomeScreen.Step.Parent, true), 30, 88);
            string country = App.Save.Data.Country;
            string countryLabel = string.IsNullOrEmpty(country) ? Loc.T("non affiché") : country == SaveService.AutoCountry ? Loc.T("celui de l'appareil") : CountryService.NameOf(country);
            PrivacyUI.Wide(online, Loc.F("Pays dans les classements : {0}", countryLabel), () => Router.Open<CountryPickerScreen>().OnPicked(Rebuild), 30, 88);
            PrivacyUI.Wide(online, "Mon compte", () => Router.Open<AccountScreen>(), 36);
            if (Services.Analytics.Provider != null)
            {
                UIKit.Toggle(online, "Statistiques d'usage anonymes", p.AnalyticsEnabled, on => { App.Privacy.SetAnalytics(on); Services.Analytics.Apply(App.Privacy.AnalyticsAllowed && App.Online.IsAvailable); });
                PrivacyUI.Paragraph(online, "Quels tombeaux sont joués, gagnés ou abandonnés, pour améliorer le jeu. Jamais d'identifiant publicitaire.", 26, UIKit.Dim);
            }
            if (Monetization.Ads.Provider != null && Monetization.Ads.Provider.HasPrivacyOptions)
                PrivacyUI.Wide(online, "Choix publicitaires", () => Monetization.Ads.Provider.ShowPrivacyOptions(Rebuild), 36);

            var data = UIKit.Card(_content);
            UIKit.SectionTitle(data, "Mes données");
            PrivacyUI.Wide(data, "Exporter mes données", Export, 36);
            if (p.AgeChecked || App.Online.IsAvailable)
                PrivacyUI.Wide(data, "Supprimer mes données en ligne", DeleteOnline, 36);
            PrivacyUI.Wide(data, "Effacer les données de ce téléphone", WipeLocal, 36);

            var info = UIKit.Card(_content);
            UIKit.SectionTitle(info, "Informations");
            PrivacyUI.Paragraph(info, "Ni traceur, ni adresse e-mail. Statistiques d'usage seulement si tu les actives ci-dessus. Publicité (Google AdMob) seulement quand tu choisis d'en regarder une.", 30);
            PrivacyUI.Wide(info, "Politique de confidentialité", () => PrivacyUI.OpenLegal(Router, true), 34);
            PrivacyUI.Wide(info, "Conditions d'utilisation", () => PrivacyUI.OpenLegal(Router, false), 34);
            if (!LegalTexts.Contact.StartsWith("["))
                PrivacyUI.Wide(info, "Contacter l'éditeur", () => Application.OpenURL("mailto:" + LegalTexts.Contact + "?subject=Mummy%20Escape%20-%20donn%C3%A9es%20personnelles"), 34);
            PrivacyUI.Paragraph(info, "Une réclamation ? Tu peux saisir la CNIL (cnil.fr) ou l'autorité de ton pays.", 28, UIKit.Dim);
            if (!string.IsNullOrEmpty(p.AnsweredAtUtc))
                PrivacyUI.Paragraph(info, Loc.F("Ton dernier choix : {0} (politique v{1}).", p.AnsweredAtUtc.Substring(0, Math.Min(10, p.AnsweredAtUtc.Length)), p.AcceptedPolicyVersion), 26, UIKit.Dim);
        }

        string AccountLabel() =>
            App.Online.Account == AccountState.Account ? Loc.F("compte {0}", App.Online.Username) : Loc.T("invité");

        void SetOnline(bool on)
        {
            var p = App.Privacy.Data;
            if (!on)
            {
                App.Privacy.SetOnline(false);
                App.StopOnline();
                Rebuild();
                return;
            }
            App.Privacy.SetOnline(true);
            _ = StartThenRebuild();
        }

        async Task StartThenRebuild()
        {
            await App.StartOnline();
            Rebuild();
        }

        async void Export()
        {
            string json = await App.ExportPersonalData();
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "mes-donnees-mummy-rush.json"), json); }
            catch (Exception e) { Debug.LogWarning("[Privacy] export file: " + e.Message); }
            App.Share.ShareText(json);
        }

        void DeleteOnline() =>
            Router.Open<ConfirmDialog>().Configure("Supprimer mes données en ligne ?",
                "Ton profil, ton compte, ton pseudonyme, tes scores, tes amis et ta sauvegarde en ligne seront définitivement effacés. " +
                "Le mode en ligne sera désactivé. Ta progression sur ce téléphone est conservée.",
                "Supprimer", async () =>
                {
                    string error = await App.DeleteOnlineData();
                    if (error == null) { await Services.Analytics.DeleteAsync(); Rebuild(); }
                    return error;
                });

        void WipeLocal() =>
            Router.Open<ConfirmDialog>().Configure("Effacer ce téléphone ?",
                "Ta progression, tes scarabées, tes apparences et tes choix enregistrés sur ce téléphone seront effacés. " +
                "Tes données en ligne ne sont pas touchées (supprime-les d'abord si tu le souhaites).",
                "Effacer", () =>
                {
                    App.WipeLocalData();
                    Router.Reset<MainMenuScreen>();
                    Router.Open<WelcomeScreen>().Begin(WelcomeScreen.Step.Intro, false);
                    return Task.FromResult<string>(null);
                });
    }

    // ====================================================================== account

    /// <summary>
    /// Optional account, made and signed in with Google Play Games only (Android). Create, sign in, sign out, delete.
    /// </summary>
    public sealed class AccountScreen : UIScreen
    {
        public override bool IsModal => true;
        RectTransform _content;
        ScrollRect _scroll;
        /// <summary>The last Google attempt that failed, shown under the buttons.</summary>
        string _googleError;
        bool _busy;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Compte", () => Router.Close(this));
            _content = PrivacyUI.ScrollBody(this, Body(190, 40, 40), out _scroll);
        }

        public override void OnShow()
        {
            _googleError = null;
            Rebuild();
        }

        void Rebuild()
        {
            if (this == null) return;
            UIKit.ClearChildren(_content);
            var online = App.Online;
            var card = UIKit.Card(_content);
            bool google = GoogleSignIn.Supported;

            if (online.Account == AccountState.Account)
            {
                UIKit.SectionTitle(card, "Connecté");
                PrivacyUI.Paragraph(card, Loc.T("Compte Google Play Jeux") + "\n" + Loc.F("Pseudonyme public : {0}", online.PlayerName) + "\n\n" +
                    Loc.T("Ta progression est sauvegardée en ligne : sur un autre téléphone Android, connecte-toi avec Google Play Jeux pour la retrouver."));
                PrivacyUI.Wide(card, App.Save.Data.Renames == 0 ? "Changer de nom (gratuit)" : Loc.F("Changer de nom · {0} dorés", Pvp.GoldShop.RenamePrice),
                    () => Router.Open<ProfileSetupScreen>().ForRename(Rebuild));
                PrivacyUI.Wide(card, "Se déconnecter", async () => { await App.SignOut(); Rebuild(); });
                PrivacyUI.Wide(card, "Supprimer mon compte", () => { Router.Close(this); Router.Open<PrivacyScreen>(); }, 34);
                PlayerIdLine(card);
                return;
            }

            // Signed out: offline play only (the editor's guest and demo land here too, with their status).
            UIKit.SectionTitle(card, "Non connecté");
            PrivacyUI.Paragraph(card, google
                ? "Connecte-toi avec Google Play Jeux pour jouer en ligne (duels, amis, classements) et sauvegarder ta progression. Sans compte, tu joues hors ligne."
                : "Les comptes se créent avec Google Play Jeux, sur Android.");
            PrivacyUI.Paragraph(card, Loc.T(online.Status), 28, UIKit.Dim);
            if (google) PrivacyUI.Wide(card, "Se connecter avec Google Play Jeux", SignInWithGoogle, 36, 100, ButtonStyle.Primary);
            if (!string.IsNullOrEmpty(_googleError)) PrivacyUI.Paragraph(card, "<color=#E0903A>" + Loc.T(_googleError) + "</color>", 28);
            PlayerIdLine(card);
        }

        /// <summary>The player id, for the support (refunds, unblocking) and the team list (Pvp.Developers). Selectable to copy.</summary>
        void PlayerIdLine(Transform card) => PrivacyUI.PlayerIdLine(App, card);

        /// <summary>Links a guest kept from an earlier version, or signs in to the Google account's player (this phone's progress joins it).</summary>
        async void SignInWithGoogle()
        {
            if (_busy) return;
            _busy = true;
            string error = await App.ConnectGoogle();
            _busy = false;
            if (this == null) return;
            _googleError = error;
            Rebuild();
        }
    }

    // ====================================================================== dialogs

    /// <summary>Yes / no question for destructive actions.</summary>
    public sealed class ConfirmDialog : UIScreen
    {
        public override bool IsModal => true;
        Text _title, _text, _error;
        Button _confirm;
        Func<Task<string>> _onConfirm;
        bool _busy;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.85f), true, "Shade");
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            var panel = UIKit.Card(Root, 44, 22);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(960, 0);
            UIKit.FitInParent(panel);
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _title = UIKit.Label(panel, "", 48, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_title, 110);
            _text = PrivacyUI.Paragraph(panel, "", 32);
            _error = UIKit.Label(panel, "", 30, UIKit.Danger);
            UIKit.Size(_error, 60);
            var row = UIKit.Row(panel, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Annuler", () => Router.Close(this), UIKit.TextSize, ButtonStyle.Leave), -1, -1, 1);
            _confirm = UIKit.Button(row.transform, "OK", Confirm, UIKit.TextSize, ButtonStyle.Primary);
            _confirm.GetComponent<Image>().color = new Color(1f, 0.6f, 0.55f);
            UIKit.Size(_confirm, -1, -1, 1);
        }

        public ConfirmDialog Configure(string title, string text, string confirmLabel, Func<Task<string>> onConfirm)
        {
            _title.text = Loc.T(title);
            _text.text = Loc.T(text);
            _error.text = "";
            UIKit.SetLabel(_confirm, confirmLabel);
            _onConfirm = onConfirm;
            return this;
        }

        async void Confirm()
        {
            if (_busy || _onConfirm == null) return;
            _busy = true;
            _confirm.interactable = false;
            string error;
            try { error = await _onConfirm(); }
            catch (Exception e) { error = e.Message; }
            _busy = false;
            if (this == null) return;
            _confirm.interactable = true;
            if (string.IsNullOrEmpty(error)) Router.Close(this);
            else _error.text = Loc.T(error);
        }
    }
}
