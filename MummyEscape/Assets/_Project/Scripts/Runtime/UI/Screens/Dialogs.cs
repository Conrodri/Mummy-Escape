using System;
using System.Threading.Tasks;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Small modal with one text field, placed in the upper part of the screen so the phone keyboard never hides it.
    /// The confirm callback returns an error message to display, or null to close.
    /// </summary>
    public sealed class PromptDialog : UIScreen
    {
        public override bool IsModal => true;

        Text _title, _hint, _error;
        InputField _input;
        Button _confirm;
        Func<string, Task<string>> _onConfirm;
        bool _busy;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.8f), true, "Shade");
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Close(this)); // tap outside = cancel

            var panel = UIKit.Card(Root, 44, 26);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1);
            panel.pivot = new Vector2(0.5f, 1);
            panel.anchoredPosition = new Vector2(0, -260);
            panel.sizeDelta = new Vector2(960, 0);
            UIKit.FitInParent(panel);
            panel.GetComponent<Image>().raycastTarget = true; // swallow taps inside the panel
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel, "", 46);
            UIKit.Size(_title, 80);
            _hint = UIKit.Label(panel, "", 34, UIKit.Sand);
            UIKit.Size(_hint, 100);
            _input = UIKit.Input(panel, "", 40);
            UIKit.Size(_input, 100);
            _input.onSubmit.AddListener(_ => Confirm());
            _error = UIKit.Label(panel, "", 32, UIKit.Danger);
            UIKit.Size(_error, 50);

            var row = UIKit.Row(panel, UIKit.ButtonHeight, 20);
            UIKit.Size(UIKit.Button(row.transform, "Annuler", () => Router.Close(this)), -1, -1, 1);
            _confirm = UIKit.Button(row.transform, "OK", Confirm, UIKit.TextSize, ButtonStyle.Primary);
            UIKit.Size(_confirm, -1, -1, 1.4f);
        }

        public PromptDialog Configure(string title, string hint, string placeholder, string text, string confirmLabel, Func<string, Task<string>> onConfirm)
        {
            _title.text = Loc.T(title);
            _hint.text = Loc.T(hint);
            ((Text)_input.placeholder).text = Loc.T(placeholder);
            _input.text = text ?? "";
            UIKit.SetLabel(_confirm, confirmLabel);
            _error.text = "";
            _onConfirm = onConfirm;
            _input.ActivateInputField();
            return this;
        }

        async void Confirm()
        {
            if (_busy || _onConfirm == null) return;
            _busy = true;
            _confirm.interactable = false;
            _error.text = "";
            string error;
            try { error = await _onConfirm(_input.text.Trim()); }
            catch (Exception e) { error = e.Message; }
            _busy = false;
            if (this == null) return;
            _confirm.interactable = true;
            if (string.IsNullOrEmpty(error)) Router.Close(this);
            else _error.text = error;
        }
    }

    /// <summary>Full-screen country list for the per-country ranking.</summary>
    public sealed class CountryPickerScreen : UIScreen
    {
        public override bool IsModal => true;
        RectTransform _list;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Ton pays", () => Router.Close(this));
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            UIKit.Size(UIKit.Label(body, "Affiché à côté de tes scores et utilisé pour le classement « Pays ».\nFacultatif : tu peux le retirer à tout moment.", 30, UIKit.Dim), 90);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 6;
        }

        public override void OnShow()
        {
            UIKit.ClearChildren(_list);
            string chosen = App.Save.Data.Country;
            string device = CountryService.Detect();
            Item("", Loc.T("Ne pas afficher"), Loc.T("par défaut"), string.IsNullOrEmpty(chosen));
            Item(SaveService.AutoCountry, Loc.T("Pays de l'appareil"), string.IsNullOrEmpty(device) ? Loc.T("inconnu") : CountryService.NameOf(device), chosen == SaveService.AutoCountry);
            foreach (var c in CountryService.Sorted()) Item(c.Code, c.Name, c.Code, chosen == c.Code);
            _scroll.verticalNormalizedPosition = 1f;
        }

        void Item(string code, string name, string detail, bool selected)
        {
            UIKit.ListItem(_list, 96, () => Pick(code), out var h, selected);
            UIKit.Size(UIKit.Label(h.transform, name, 34, UIKit.Sand, TextAnchor.MiddleLeft, selected ? FontStyle.Bold : FontStyle.Normal), -1, -1, 1);
            UIKit.Size(UIKit.Label(h.transform, detail, 30, UIKit.Dim, TextAnchor.MiddleRight), -1, 320, 0);
        }

        void Pick(string code)
        {
            App.SetCountry(code);
            Router.Close(this);
            if (Router.Current is FriendsScreen friends) friends.OnShow();
        }
    }

    /// <summary>Game language: automatic (device language) or one of <see cref="Loc.Languages"/>, each shown in its own language.</summary>
    public sealed class LanguagePickerScreen : UIScreen
    {
        public override bool IsModal => true;
        RectTransform _list;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Langue", () => Router.Close(this));
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 6;
        }

        public override void OnShow()
        {
            UIKit.ClearChildren(_list);
            string chosen = App.Settings.Language;
            Item("", Loc.T("Automatique (langue du téléphone)"), Loc.FromSystem().NativeName, string.IsNullOrEmpty(chosen));
            foreach (var l in Loc.Languages) Item(l.Code, l.NativeName, l.Code.ToUpperInvariant(), chosen == l.Code);
        }

        void Item(string code, string name, string detail, bool selected)
        {
            UIKit.ListItem(_list, 100, () => Pick(code), out var h, selected);
            var label = UIKit.Label(h.transform, "", 36, UIKit.Sand, TextAnchor.MiddleLeft, selected ? FontStyle.Bold : FontStyle.Normal);
            label.text = name; // native names are never translated
            UIKit.Size(label, -1, -1, 1);
            var d = UIKit.Label(h.transform, "", 30, UIKit.Dim, TextAnchor.MiddleRight);
            d.text = detail;
            UIKit.Size(d, -1, 320, 0);
        }

        void Pick(string code)
        {
            Router.Close(this);
            var before = Loc.Current;
            App.Settings.SetLanguage(code); // a different language rebuilds every screen
            if (Loc.Current == before) Router.Get<SettingsScreen>().RefreshLanguage();
        }
    }
}
