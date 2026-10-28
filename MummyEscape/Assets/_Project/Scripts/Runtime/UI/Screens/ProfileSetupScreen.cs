using MummyEscape.Monetization;
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

        /// <summary>Asked once, the first time the player opens a social or PvP screen (the solo game never needs it), once online mode is accepted and a session is up.</summary>
        public static bool Needed(App.GameApp app) =>
            !app.Save.Data.ProfileDone && app.Privacy.OnlineAllowed && (app.Online.IsAvailable || app.Online.IsDemo);

        /// <summary>Opens over <paramref name="router"/>'s screen when the profile is still to be chosen.</summary>
        public static void AskIfNeeded(UIRouter router, App.GameApp app)
        {
            if (Needed(app)) router.Open<ProfileSetupScreen>();
        }

        InputField _name;
        Text _country, _error;
        Image _mummy;
        Button _go, _cancel;
        bool _busy;
        /// <summary>Opened from the account screen: a change of name, which can be left (<see cref="ForRename"/>).</summary>
        bool _rename;
        System.Action _renamed;

        /// <summary>The first change is free, the next ones cost <see cref="Pvp.GoldShop.RenamePrice"/>.</summary>
        int RenamePrice => App.Save.Data.Renames == 0 ? 0 : Pvp.GoldShop.RenamePrice;

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
            UIKit.Size(_go, 116);
            _cancel = UIKit.Button(body, "Annuler", () => Router.Close(this), 34, ButtonStyle.Ghost);
            UIKit.Size(_cancel, 90);
        }

        /// <summary>A change of name from the account screen: it can be cancelled, and says what it costs.</summary>
        public void ForRename(System.Action renamed)
        {
            _rename = true;
            _renamed = renamed;
            _cancel.gameObject.SetActive(true);
            int price = RenamePrice;
            UIKit.SetLabel(_go, price == 0 ? "Changer (gratuit la première fois)" : Loc.F("Changer · {0} dorés", price));
        }

        public override void OnShow()
        {
            _busy = false;
            _rename = false;
            _cancel.gameObject.SetActive(false);
            UIKit.SetLabel(_go, "C'est parti !");
            _error.text = "";
            _name.text = StripTag(App.Online.PlayerName);
            MummyAnimator.Show(_mummy, App.Art, App.Save.Loadout);
            RefreshCountry();
        }

        // The first choice is required: Back does nothing there.
        public void OnBack()
        {
            if (_rename && !_busy) Router.Close(this);
        }

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
            bool changed = name != StripTag(App.Online.PlayerName);
            int price = _rename && changed ? RenamePrice : 0;
            if (price > 0 && !TreasureScreen.EnoughGold(App, Router, price, () => Router.Open<TreasureScreen>())) return;
            _busy = true;
            _go.interactable = false;
            // Keeping the suggested name keeps the friend code (a new name gets a new #1234).
            if (changed)
            {
                try { await App.Online.SetPlayerNameAsync(name); }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Profile] rename: " + e.Message);
                    if (this == null) return;
                    _busy = false;
                    _go.interactable = true;
                    _error.text = Loc.T("Ce nom n'a pas pu être enregistré : essaies-en un autre.");
                    return;
                }
                // shortcut: paid after the rename, so a failed rename never costs gold; a lost answer here makes it free.
                if (_rename) await GoldWallet.RunAsync(App, p => p.PayRenameAsync());
            }
            if (this == null) return;
            App.Save.Data.ProfileDone = true;
            App.Save.Save();
            _ = App.PublishProgress();
            _go.interactable = true;
            Router.Close(this);
            Router.Current?.OnShow();
            if (_rename) _renamed?.Invoke();
        }

        internal static string StripTag(string name)
        {
            int hash = name?.IndexOf('#') ?? -1;
            return hash > 0 ? name.Substring(0, hash) : name ?? "";
        }
    }
}
