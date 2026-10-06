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

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            var title = UIKit.Label(Root, "MUMMY RUSH", 72, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.TopBand(title.rectTransform, 130, 40);
            _content = PrivacyUI.ScrollBody(this, Body(190, 40, 40), out _scroll);
        }

        /// <summary>Opens at a given step; from the settings, closing returns to them instead of the menu.</summary>
        public WelcomeScreen Begin(Step step, bool fromSettings)
        {
            _fromSettings = fromSettings;
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
            PrivacyUI.Wide(_content, "Jouer en ligne", () => Go(Step.Age), 38, 104, ButtonStyle.Primary);
            PrivacyUI.Wide(_content, "Jouer hors ligne", () => Finish(false, null, false));
            PrivacyUI.Paragraph(_content, "Tu peux changer d'avis à tout moment dans Paramètres › Confidentialité.", 28, UIKit.Dim);
        }

        void Age()
        {
            UIKit.Size(UIKit.Title(_content, "Ton année de naissance", 46), 90);
            PrivacyUI.Paragraph(_content, "Elle sert seulement à savoir si l'accord d'un parent est nécessaire. Elle n'est ni conservée, ni envoyée.", 30, UIKit.Dim);
            var year = YearField("AAAA");
            bool accepted = false;
            UIKit.Toggle(_content, "J'accepte les conditions d'utilisation", false, v => accepted = v);
            var links = UIKit.Row(_content, 90, 20);
            UIKit.Size(UIKit.Button(links.transform, "Lire les conditions", () => PrivacyUI.OpenLegal(Router, false), 30), -1, -1, 1);
            UIKit.Size(UIKit.Button(links.transform, "Lire la confidentialité", () => PrivacyUI.OpenLegal(Router, true), 30), -1, -1, 1);
            var error = UIKit.Label(_content, "", 32, UIKit.Danger);
            UIKit.Size(error, 60);
            var row = UIKit.Row(_content, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Retour", OnBack), -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Continuer", () =>
            {
                if (!ReadYear(year, out int y)) { error.text = Loc.T("Indique une année valide (4 chiffres)."); return; }
                if (!accepted) { error.text = Loc.T("Accepte les conditions pour jouer en ligne."); return; }
                _isMinor = PrivacyService.IsUnderConsentAge(y, CountryService.Detect());
                if (_isMinor) Go(Step.Parent);
                else Finish(true, false, false);
            }, UIKit.TextSize, ButtonStyle.Primary), -1, -1, 1.4f);
        }

        void Parent()
        {
            UIKit.Size(UIKit.Title(_content, "Demande à un parent", 46), 90);
            PrivacyUI.Paragraph(_content,
                "Pour jouer en ligne à ton âge, l'accord d'un parent ou d'un tuteur est nécessaire.\n\n" +
                "À l'attention du parent : le mode en ligne publie un pseudonyme (attribué au hasard, modifiable) et les scores de votre enfant, " +
                "et lui permet d'ajouter des amis par leur code. Aucune adresse e-mail, aucun nom réel, aucune publicité. " +
                "Vous pouvez retirer cet accord et supprimer ses données à tout moment dans Paramètres › Confidentialité.");
            PrivacyUI.Wide(_content, "Lire la politique de confidentialité", () => PrivacyUI.OpenLegal(Router, true), 30, 88);
            PrivacyUI.Paragraph(_content, "Parent ou tuteur : votre année de naissance", 32, UIKit.Gold);
            var year = YearField("AAAA");
            bool confirmed = false;
            UIKit.Toggle(_content, "Je suis son parent ou tuteur et j'autorise", false, v => confirmed = v);
            var error = UIKit.Label(_content, "", 32, UIKit.Danger);
            UIKit.Size(error, 60);
            var row = UIKit.Row(_content, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Hors ligne", () => Finish(false, true, false)), -1, -1, 1);
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
            if (App.Privacy.OnlineAllowed) _ = App.StartOnline();
            else App.StopOnline();
            if (_fromSettings && Router.Get<PrivacyScreen>().gameObject.activeSelf) Router.Get<PrivacyScreen>().OnShow();
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
                PrivacyUI.Wide(online, "Retirer l'autorisation parentale", () => { App.Privacy.SetParentalConsent(false); App.StopOnline(); Rebuild(); }, 30, 88);
            UIKit.Toggle(online, "Mes amis voient ma progression", p.ShareProgress, v => { App.Privacy.SetShareProgress(v); _ = App.PublishProgress(); });
            string country = App.Save.Data.Country;
            string countryLabel = string.IsNullOrEmpty(country) ? Loc.T("non affiché") : country == SaveService.AutoCountry ? Loc.T("celui de l'appareil") : CountryService.NameOf(country);
            PrivacyUI.Wide(online, Loc.F("Pays dans les classements : {0}", countryLabel), () => Router.Open<CountryPickerScreen>().OnPicked(Rebuild), 30, 88);
            PrivacyUI.Wide(online, "Mon compte", () => Router.Open<AccountScreen>(), 36);
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
            PrivacyUI.Paragraph(info, "Ni mesure d'audience, ni traceur, ni adresse e-mail. Publicité (Google AdMob) seulement quand tu choisis d'en regarder une.", 30);
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
            if (!p.AgeChecked) { Router.Open<WelcomeScreen>().Begin(WelcomeScreen.Step.Age, true); return; }
            if (p.IsMinor && !p.ParentalConsent) { Router.Open<WelcomeScreen>().Begin(WelcomeScreen.Step.Parent, true); return; }
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
                    if (error == null) Rebuild();
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
    /// Optional account: Google Play Games (Android) or a username + password (no e-mail). Create, sign in, change
    /// password, sign out, delete.
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

            if (!App.Privacy.OnlineAllowed && !online.IsDemo)
            {
                UIKit.SectionTitle(card, "Mode hors ligne");
                PrivacyUI.Paragraph(card, "Le compte fait partie du mode en ligne. Active-le dans Confidentialité pour créer un compte ou te connecter.");
                PrivacyUI.Wide(card, "Confidentialité", () => { Router.Close(this); Router.Open<PrivacyScreen>(); });
                return;
            }

            if (online.Account == AccountState.Account)
            {
                UIKit.SectionTitle(card, "Connecté");
                string who = online.HasPassword ? Loc.F("Identifiant : {0}", online.Username) : Loc.T("Compte Google Play Jeux");
                if (online.HasPassword && online.GoogleLinked) who += "\n" + Loc.T("Lié à Google Play Jeux");
                PrivacyUI.Paragraph(card, who + "\n" + Loc.F("Pseudonyme public : {0}", online.PlayerName) + "\n\n" +
                    Loc.T(online.GoogleLinked ? "Ta progression est sauvegardée en ligne : sur un autre téléphone Android, connecte-toi avec Google Play Jeux pour la retrouver."
                                              : "Ta progression est sauvegardée en ligne : connecte-toi avec ce compte sur un autre appareil pour la retrouver."));
                if (google && !online.GoogleLinked) PrivacyUI.Wide(card, "Lier Google Play Jeux", LinkGoogle);
                if (online.HasPassword) PrivacyUI.Wide(card, "Changer le mot de passe", ChangePassword);
                GoogleError(card);
                PrivacyUI.Wide(card, "Se déconnecter", async () => { await App.SignOut(); Rebuild(); });
                PrivacyUI.Wide(card, "Supprimer mon compte", () => { Router.Close(this); Router.Open<PrivacyScreen>(); }, 34);
                return;
            }

            if (online.Account == AccountState.Guest || online.IsDemo)
            {
                UIKit.SectionTitle(card, "Tu joues en invité");
                PrivacyUI.Paragraph(card, google
                    ? "Ta progression n'existe que sur ce téléphone. Crée un compte pour la sauvegarder et la retrouver ailleurs : en un geste avec Google Play Jeux, ou avec un identifiant et un mot de passe, sans adresse e-mail."
                    : "Ta progression n'existe que sur ce téléphone. Crée un compte pour la sauvegarder et la retrouver ailleurs : " +
                      "il suffit d'un identifiant et d'un mot de passe, sans adresse e-mail.");
                if (google) PrivacyUI.Wide(card, "Continuer avec Google Play Jeux", LinkGoogle, 36, 100, ButtonStyle.Primary);
                PrivacyUI.Wide(card, "Créer un compte", CreateAccount, 36, 100, google ? ButtonStyle.Secondary : ButtonStyle.Primary);
            }
            else
            {
                UIKit.SectionTitle(card, "Non connecté");
                PrivacyUI.Paragraph(card, Loc.T(online.Status), 30, UIKit.Dim);
                if (google) PrivacyUI.Wide(card, "Se connecter avec Google Play Jeux", SignInWithGoogle, 36, 100, ButtonStyle.Primary);
                PrivacyUI.Wide(card, "Continuer en invité", async () => { await App.StartOnline(); Rebuild(); });
            }
            PrivacyUI.Wide(card, "J'ai déjà un compte", SignIn);
            GoogleError(card);
            PrivacyUI.Paragraph(card, "Sans e-mail, un mot de passe oublié ne peut pas être récupéré : note-le bien.", 28, UIKit.Dim);
        }

        void GoogleError(Transform card)
        {
            if (!string.IsNullOrEmpty(_googleError)) PrivacyUI.Paragraph(card, "<color=#E0903A>" + Loc.T(_googleError) + "</color>", 28);
        }

        /// <summary>The guest becomes a Google account; if that Google account already has its progress, offers to sign in to it.</summary>
        async void LinkGoogle()
        {
            if (_busy) return;
            _busy = true;
            string error = await App.LinkGoogle();
            _busy = false;
            if (this == null) return;
            _googleError = null;
            if (error == IOnlineService.GoogleTaken)
            {
                Router.Open<ConfirmDialog>().Configure("Compte Google déjà utilisé",
                    "Ce compte Google Play Jeux a déjà sa propre progression. T'y connecter ? Celle de ce téléphone y sera ajoutée.",
                    "Me connecter", async () =>
                    {
                        string e = await App.SignInWithGoogle();
                        if (e == null) Rebuild();
                        return e;
                    });
                return;
            }
            _googleError = error;
            Rebuild();
        }

        async void SignInWithGoogle()
        {
            if (_busy) return;
            _busy = true;
            string error = await App.SignInWithGoogle();
            _busy = false;
            if (this == null) return;
            if (error == null && !App.Privacy.OnlineAllowed) App.Privacy.SetOnline(true);
            _googleError = error;
            Rebuild();
        }

        void CreateAccount() =>
            Router.Open<FormDialog>().Configure("Créer un compte",
                Loc.F("Identifiant : {0}-{1} caractères (lettres, chiffres, . - _ @).", AccountRules.UsernameMin, AccountRules.UsernameMax) + "\n" +
                Loc.F("Mot de passe : {0}-{1} caractères, majuscule, minuscule, chiffre et symbole.", AccountRules.PasswordMin, AccountRules.PasswordMax),
                new[] { ("Identifiant", false), ("Mot de passe", true), ("Confirme le mot de passe", true) }, "Créer",
                async v =>
                {
                    string error = AccountRules.CheckUsername(v[0]) ?? AccountRules.CheckPassword(v[1]);
                    if (error != null) return error;
                    if (v[1] != v[2]) return "Les deux mots de passe diffèrent.";
                    error = await App.CreateAccount(v[0], v[1]);
                    if (error == null) Rebuild();
                    return error;
                });

        void SignIn() =>
            Router.Open<FormDialog>().Configure("Se connecter",
                App.Online.Account == AccountState.Guest ? "La progression de ce téléphone sera ajoutée à celle du compte." : "",
                new[] { ("Identifiant", false), ("Mot de passe", true) }, "Connexion",
                async v =>
                {
                    if (string.IsNullOrEmpty(v[0]) || string.IsNullOrEmpty(v[1])) return "Remplis les deux champs.";
                    string error = await App.SignIn(v[0], v[1]);
                    if (error == null)
                    {
                        if (!App.Privacy.OnlineAllowed) App.Privacy.SetOnline(true);
                        Rebuild();
                    }
                    return error;
                });

        void ChangePassword() =>
            Router.Open<FormDialog>().Configure("Changer le mot de passe", "",
                new[] { ("Mot de passe actuel", true), ("Nouveau mot de passe", true), ("Confirme le nouveau", true) }, "Changer",
                async v =>
                {
                    string error = AccountRules.CheckPassword(v[1]);
                    if (error != null) return error;
                    if (v[1] != v[2]) return "Les deux mots de passe diffèrent.";
                    return await App.Online.ChangePasswordAsync(v[0], v[1]);
                });
    }

    // ====================================================================== dialogs

    /// <summary>Modal form with several fields (passwords hidden). The callback returns an error, or null to close.</summary>
    public sealed class FormDialog : UIScreen
    {
        public override bool IsModal => true;
        RectTransform _panel, _fields;
        Text _title, _hint, _error;
        Button _confirm;
        InputField[] _inputs = new InputField[0];
        Func<string[], Task<string>> _onConfirm;
        bool _busy;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.85f), true, "Shade");
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            _panel = UIKit.Card(Root, 44, 22);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1);
            _panel.pivot = new Vector2(0.5f, 1);
            _panel.anchoredPosition = new Vector2(0, -150);
            _panel.sizeDelta = new Vector2(980, 0);
            UIKit.FitInParent(_panel);
            _panel.GetComponent<Image>().raycastTarget = true;
            _panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(_panel, "", 46);
            UIKit.Size(_title, 80);
            _hint = PrivacyUI.Paragraph(_panel, "", 28, UIKit.Dim);
            _fields = UIKit.Rect("Fields", _panel);
            UIKit.Column(_fields, 18);
            _error = UIKit.Label(_panel, "", 30, UIKit.Danger);
            UIKit.Size(_error, 80);
            var row = UIKit.Row(_panel, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Annuler", () => Router.Close(this)), -1, -1, 1);
            _confirm = UIKit.Button(row.transform, "OK", Confirm, UIKit.TextSize, ButtonStyle.Primary);
            UIKit.Size(_confirm, -1, -1, 1.4f);
        }

        public FormDialog Configure(string title, string hint, (string placeholder, bool secret)[] fields, string confirmLabel, Func<string[], Task<string>> onConfirm)
        {
            _title.text = Loc.T(title);
            _hint.text = Loc.T(hint);
            _hint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            UIKit.ClearChildren(_fields);
            _inputs = new InputField[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                var f = UIKit.Input(_fields, fields[i].placeholder, 38);
                f.characterLimit = AccountRules.PasswordMax;
                f.contentType = fields[i].secret ? InputField.ContentType.Password : InputField.ContentType.Alphanumeric;
                if (!fields[i].secret) f.contentType = InputField.ContentType.Standard;
                f.keyboardType = TouchScreenKeyboardType.ASCIICapable;
                UIKit.Size(f, 96);
                _inputs[i] = f;
            }
            UIKit.SetLabel(_confirm, confirmLabel);
            _error.text = "";
            _onConfirm = onConfirm;
            if (_inputs.Length > 0) _inputs[0].ActivateInputField();
            return this;
        }

        async void Confirm()
        {
            if (_busy || _onConfirm == null) return;
            _busy = true;
            _confirm.interactable = false;
            _error.text = "";
            var values = new string[_inputs.Length];
            for (int i = 0; i < values.Length; i++) values[i] = _inputs[i].text.Trim();
            string error;
            try { error = await _onConfirm(values); }
            catch (Exception e) { error = e.Message; }
            _busy = false;
            if (this == null) return;
            _confirm.interactable = true;
            if (string.IsNullOrEmpty(error))
            {
                foreach (var f in _inputs) f.text = ""; // never keep passwords around
                Router.Close(this);
            }
            else _error.text = Loc.T(error);
        }
    }

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
            UIKit.Size(UIKit.Button(row.transform, "Annuler", () => Router.Close(this)), -1, -1, 1);
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
