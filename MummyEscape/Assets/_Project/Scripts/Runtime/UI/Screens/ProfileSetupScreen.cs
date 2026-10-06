using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// First online connection: the player picks their mummy name (seen in the rankings and by friends) and, if they
    /// want, their country. Asked once (<see cref="SaveData.ProfileDone"/>); the friends screen no longer edits them.
    /// The country stays changeable from Settings › Privacy (a privacy choice).
    /// </summary>
    public sealed class ProfileSetupScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        public const int NameMin = 3, NameMax = 16;

        /// <summary>Asked once, after the player accepted the online mode and a session is up.</summary>
        public static bool Needed(App.GameApp app) =>
            !app.Save.Data.ProfileDone && app.Privacy.OnlineAllowed && (app.Online.IsAvailable || app.Online.IsDemo);

        InputField _name;
        Text _country, _error;
        Image _mummy;
        Button _go;
        bool _busy;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            var title = UIKit.Title(Root, "Ta momie", 64);
            UIKit.TopBand(title.rectTransform, 120, 50);
            var body = Body(200, 60, 60);
            UIKit.Column(body, 22);

            var stage = UIKit.Rect("Stage", body); // noloc
            UIKit.Size(stage, 300, -1, -1, 1);
            UIFx.Halo(stage, new Color(1f, 0.7f, 0.35f, 0.5f), 560, 0f);
            var floater = UIKit.Place(UIKit.Rect("Float", stage), 0.5f, 0.5f, 520, 520); // noloc
            UIFx.Float(floater, 10f, 2.8f);
            _mummy = UIKit.Image(floater, null, Color.white);
            _mummy.preserveAspect = true;
            UIKit.Stretch(_mummy.rectTransform);

            var card = UIKit.Card(body, 36, 18);
            UIKit.SectionTitle(card, "Ton nom de momie");
            var hint = UIKit.Label(card, "Visible dans les classements et par tes amis. N'utilise pas ton vrai nom.", 28, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.Size(hint, 80);
            _name = UIKit.Input(card, "Nom", 40);
            _name.characterLimit = NameMax;
            UIKit.Size(_name, 104);

            UIKit.SectionTitle(card, "Ton pays (facultatif)");
            var row = UIKit.Row(card, UIKit.ButtonHeight, 16);
            _country = UIKit.Label(row.transform, "", 32, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_country, 20);
            UIKit.Size(_country, -1, -1, 1);
            var pick = UIKit.Button(row.transform, "Choisir", () => Router.Open<CountryPickerScreen>().OnPicked(RefreshCountry), 30);
            UIKit.Size(pick, -1, 260, 0);
            var note = UIKit.Label(card, "Affiché à côté de tes scores, pour le classement « Pays ». Modifiable dans Paramètres › Confidentialité.", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.Size(note, 70);

            _error = UIKit.Label(body, "", 30, UIKit.Danger);
            UIKit.Size(_error, 50);
            _go = UIKit.Button(body, "C'est parti !", Confirm, 42, ButtonStyle.Primary);
            UIKit.Rounded(_go.image, 56);
            UIKit.Size(_go, 116);
        }

        public override void OnShow()
        {
            _busy = false;
            _error.text = "";
            _name.text = StripTag(App.Online.PlayerName);
            MummyAnimator.Show(_mummy, App.Art, App.Save.Loadout);
            RefreshCountry();
        }

        // A choice is required: Back does nothing.
        public void OnBack() { }

        void RefreshCountry()
        {
            string code = App.Save.Country;
            _country.text = string.IsNullOrEmpty(code) ? Loc.T("Non affiché") : CountryService.NameOf(code);
        }

        async void Confirm()
        {
            if (_busy) return;
            string name = (_name.text ?? "").Trim();
            if (name.Length < NameMin) { _error.text = Loc.F("{0} caractères minimum.", NameMin); return; }
            _busy = true;
            _go.interactable = false;
            // Keeping the suggested name keeps the friend code (a new name gets a new #1234).
            if (name != StripTag(App.Online.PlayerName)) await App.Online.SetPlayerNameAsync(name);
            if (this == null) return;
            App.Save.Data.ProfileDone = true;
            App.Save.Save();
            _ = App.PublishProgress();
            _go.interactable = true;
            Router.Close(this);
            if (Router.Current is MainMenuScreen menu) menu.OnShow();
        }

        internal static string StripTag(string name)
        {
            int hash = name?.IndexOf('#') ?? -1;
            return hash > 0 ? name.Substring(0, hash) : name ?? "";
        }
    }
}
