using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The titles: each one with what earns it and how far the player is, the earned ones ready to wear. The chosen
    /// title shows under the player's name in the profile and to the rivals of the duels (VS screen, race, replays).
    /// </summary>
    public sealed class TitlesScreen : UIScreen
    {
        RectTransform _list;
        ScrollRect _scroll;
        Text _current;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Titres");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 18);
            _current = UIKit.Label(body, "", 32, UIKit.Sand);
            UIKit.FitText(_current, 20);
            UIKit.Size(_current, 60);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;
        }

        public override void OnShow()
        {
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
            LoadProfile();
        }

        /// <summary>The duel titles need the server's profile (wins, best league): fetched once if missing.</summary>
        async void LoadProfile()
        {
            if (App.Pvp == null || App.PvpProfile != null) return;
            int request = ++_request;
            await App.RefreshPvpProfile();
            if (request == _request && this != null && isActiveAndEnabled) Refresh();
        }

        void Refresh()
        {
            string equipped = TitleBook.Equipped(App);
            _current.text = equipped == null ? Loc.T("Aucun titre affiché") : Loc.T("Ton titre :") + " " + TitleBook.Line(equipped);
            UIKit.ClearChildren(_list);

            var none = Row(Loc.T("Aucun titre"), Loc.T("Ton nom seul, sans titre"), UIKit.Dim, true, equipped == null, "");
            none.name = "None"; // noloc
            Section("Solo", TitleKind.Solo, equipped);
            Section("Duel", TitleKind.Wins, equipped, TitleKind.League);
            Section("Vitesse", TitleKind.Speed, equipped);
        }

        void Section(string title, TitleKind kind, string equipped, TitleKind? also = null)
        {
            var header = UIKit.SectionTitle(_list, title);
            UIKit.Size(header, 64);
            foreach (var t in Titles.All)
            {
                if (t.Kind != kind && t.Kind != also) continue;
                var (value, goal) = TitleBook.Progress(App, t);
                bool earned = value >= goal;
                string how = Condition(t) + (earned || t.Kind == TitleKind.League ? "" : $"  ·  {value}/{goal}"); // noloc
                Row(Loc.T(t.Name), how, TitleBook.ColorOf(t), earned, equipped == t.Id, t.Id);
            }
        }

        static string Condition(TitleDef t)
        {
            switch (t.Kind)
            {
                case TitleKind.Solo: return Loc.F("{0} étoiles dans l'acte {1}", t.Goal, t.Act);
                case TitleKind.Wins: return Loc.F("{0} victoires en duel", t.Goal);
                case TitleKind.League: return Loc.F("Atteindre la ligue {0}", Loc.T(Visual.PvpSkins.LeagueName(t.League)));
                default: return Loc.F("{0} tombeaux différents en moins de 5 s", t.Goal);
            }
        }

        GameObject Row(string name, string how, Color color, bool earned, bool worn, string id)
        {
            UIKit.ListItem(_list, 120, null, out var h, worn);
            h.padding = new RectOffset(28, 20, 10, 10);
            var texts = UIKit.Rect("Texts", h.transform);
            UIKit.Size(texts, -1, -1, 1);
            var title = UIKit.Label(texts, name, 34, earned ? color : UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(title, 22);
            UIKit.TopBand(title.rectTransform, 56, 6);
            var sub = UIKit.Label(texts, how, 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(sub, 16);
            UIKit.BottomBand(sub.rectTransform, 44, 6);

            string label = worn ? Loc.T("Affiché") : earned ? Loc.T("Afficher") : Loc.T("Verrouillé");
            var btn = UIKit.Button(h.transform, label, () => { App.Save.SelectTitle(id); Refresh(); }, 26,
                                   earned && !worn ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = earned && !worn;
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 76, 210);
            return h.gameObject;
        }
    }
}
