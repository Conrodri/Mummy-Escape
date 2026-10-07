using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Per-level top 100 (everyone gets the same tomb, so scores compare directly): rank 1 at the top, filters
    /// Monde / Pays / Amis, and the player's own position pinned at the bottom.
    /// </summary>
    public sealed class LeaderboardScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Ranking;

        const int Top = 100;
        static readonly LeaderboardScope[] Scopes = { LeaderboardScope.Global, LeaderboardScope.Country, LeaderboardScope.Friends };
        static readonly Color Gold1 = new Color32(255, 214, 92, 255);
        static readonly Color Silver = new Color32(206, 212, 222, 255);
        static readonly Color Bronze = new Color32(214, 140, 72, 255);

        readonly List<LevelId> _levels = new List<LevelId>(DifficultyTable.AllLevels());
        readonly List<RowView> _rows = new List<RowView>();
        int _index;
        int _scope;
        Text _levelLabel, _actLabel, _info;
        UIKit.Segmented _tabs, _category;
        ScrollRect _scroll;
        RowView _me;
        Text _meEmpty;
        int _requestId;

        sealed class RowView
        {
            public GameObject Root;
            public Image Background;
            public Text Rank, Name, Country, Score;
        }

        public void Focus(LevelId id)
        {
            int i = _levels.IndexOf(id);
            if (i >= 0) _index = i;
        }

        public void Focus(LevelId id, LeaderboardScope scope)
        {
            Focus(id);
            _scope = System.Array.IndexOf(Scopes, scope);
        }

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Classement");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 18);
            _category = Categories(body, Router, 0);

            // Level picker.
            var picker = UIKit.Row(body, 120, 16);
            UIKit.IconButton(picker.transform, UISprites.Back, () => Move(-1), 84);
            var titles = UIKit.Rect("Titles", picker.transform);
            UIKit.Size(titles, -1, -1, 1);
            _levelLabel = UIKit.Title(titles, "", 48);
            UIKit.TopBand(_levelLabel.rectTransform, 70, 4);
            _actLabel = UIKit.Label(titles, "", 30, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.BottomBand(_actLabel.rectTransform, 40, 4);
            UIKit.IconButton(picker.transform, UISprites.Next, () => Move(1), 84);

            _tabs = new UIKit.Segmented(body, new[] { "Monde", "Pays", "Amis" }, SetScope);
            _info = UIKit.Label(body, "", 30, UIKit.Dim);
            UIKit.Size(_info, 46);

            var list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            list.GetComponent<VerticalLayoutGroup>().spacing = 6;
            for (int i = 0; i < Top; i++) _rows.Add(CreateRow(list, 92));

            // Player's own position, always visible.
            var meSlot = UIKit.Rect("Me", body);
            UIKit.Size(meSlot, 112);
            _me = CreateRow(meSlot, 112);
            UIKit.Stretch((RectTransform)_me.Root.transform);
            _me.Background.color = new Color(0.13f, 0.4f, 0.4f, 0.9f);
            _meEmpty = UIKit.Label(meSlot, "", 34, UIKit.Sand);
            UIKit.Stretch(_meEmpty.rectTransform, 24, 0, 24, 0);
        }

        RowView CreateRow(Transform parent, float height)
        {
            UIKit.ListItem(parent, height, null, out var h);
            var v = new RowView { Root = h.gameObject, Background = h.GetComponent<Image>() };
            v.Rank = UIKit.Label(h.transform, "", 36, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(v.Rank, -1, 90, 0);
            v.Name = UIKit.Label(h.transform, "", 34, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(v.Name, 24);
            UIKit.Size(v.Name, -1, -1, 1);
            v.Country = UIKit.Label(h.transform, "", 26, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(v.Country, -1, 70, 0);
            v.Score = UIKit.Label(h.transform, "", 30, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            v.Score.lineSpacing = 0.85f;
            UIKit.Size(v.Score, -1, 230, 0);
            return v;
        }

        /// <summary>Gap to the optimal route, with the play time (the tie breaker) underneath.</summary>
        static string ScoreText(LeaderboardRow row) =>
            row.TimeMs > 0
                ? $"{LevelResult.FormatOverPar(row.OverPar)}\n<size=22><color=#9C8B70>{LevelResult.FormatTime(row.TimeMs)}</color>{(row.Suspicious ? " <b><color=#E0903A>?</color></b>" : "")}</size>" // noloc
                : LevelResult.FormatOverPar(row.OverPar);

        /// <summary>
        /// The two rankings, Solo (per level) and Duel (monthly Elo), one tab away from each other. Like the bottom bar,
        /// each opens over the main menu: Back always leads home.
        /// </summary>
        internal static UIKit.Segmented Categories(Transform body, UIRouter router, int selected)
        {
            return new UIKit.Segmented(body, new[] { "Solo", "Duel", "2v2", "Guildes" }, i =>
            {
                if (i == selected) return;
                if (router.Current is TeamLeaderboardScreen team && i >= 2)
                {
                    team.Show(i == 3); // 2v2 ⇄ guilds on the same screen
                    return;
                }
                router.Reset<MainMenuScreen>();
                if (i == 0) router.Open<LeaderboardScreen>();
                else if (i == 1) router.Open<PvpLeaderboardScreen>();
                else router.Open<TeamLeaderboardScreen>().Show(i == 3);
            }, 96);
        }

        public override void OnShow()
        {
            _category.Select(0);
            _tabs.Select(_scope);
            // The country tab says which country: "France" rather than "Pays".
            string country = App.Online.Country;
            _tabs.SetLabel(1, string.IsNullOrEmpty(country) ? "Pays" : CountryService.NameOf(country));
            Reload();
        }

        void Move(int delta)
        {
            _index = (_index + delta + _levels.Count) % _levels.Count;
            Reload();
        }

        void SetScope(int scope)
        {
            _scope = scope;
            Reload();
        }

        async void Reload()
        {
            var id = _levels[_index];
            var scope = Scopes[_scope];
            _levelLabel.text = Loc.F("Niveau {0}", id);
            _actLabel.text = Loc.F("Acte {0}", id.Act) + " · " + Loc.T(DifficultyTable.GetAct(id.Act).Name);
            _info.text = Loc.T("Chargement…");
            _scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _rows.Count; i++) Fill(_rows[i], i + 1, null, scope != LeaderboardScope.Friends);
            ShowMe(null, false);

            string country = App.Online.Country;
            if (scope == LeaderboardScope.Country && string.IsNullOrEmpty(country))
            {
                _info.text = Loc.T("Choisis ton pays dans Amis › Profil.");
                return;
            }

            int request = ++_requestId;
            var page = await App.Online.GetLeaderboardAsync(id, scope, Top);
            if (request != _requestId || this == null) return; // superseded by a newer request

            string title = scope == LeaderboardScope.Global ? Loc.T("Top 100 mondial")
                         : scope == LeaderboardScope.Country ? Loc.F("Top 100 · {0}", CountryService.NameOf(country))
                         : Loc.T("Toi et tes amis");
            if (App.Online.IsDemo) title += "  ·  <color=#E8C35A>" + Loc.T("démo hors ligne") + "</color>";
            else if (!App.Online.IsAvailable) title += "  ·  " + Loc.T("hors ligne");
            _info.text = title;

            for (int i = 0; i < _rows.Count; i++)
                Fill(_rows[i], i + 1, i < page.Rows.Count ? page.Rows[i] : null, scope != LeaderboardScope.Friends);
            ShowMe(page.Me, true);
        }

        void Fill(RowView v, int rank, LeaderboardRow row, bool showEmpty)
        {
            v.Root.SetActive(row != null || showEmpty);
            v.Rank.text = rank.ToString();
            v.Rank.color = rank == 1 ? Gold1 : rank == 2 ? Silver : rank == 3 ? Bronze : UIKit.Dim;
            v.Rank.fontSize = rank <= 3 ? 46 : 38;
            if (row == null)
            {
                v.Name.text = "—";
                v.Name.color = new Color(1, 1, 1, 0.2f);
                v.Country.text = v.Score.text = "";
                v.Background.color = new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.03f : 0.05f);
                return;
            }
            v.Name.text = row.IsMe ? Loc.F("{0}  (toi)", row.PlayerName) : row.PlayerName;
            v.Name.color = row.IsMe ? UIKit.Turquoise : UIKit.Sand;
            v.Country.text = row.Country;
            v.Score.text = ScoreText(row);
            v.Background.color = row.IsMe ? new Color(0.13f, 0.4f, 0.4f, 0.65f)
                               : rank <= 3 ? new Color(1f, 0.85f, 0.4f, 0.12f)
                               : new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.04f : 0.07f);
        }

        void ShowMe(LeaderboardRow me, bool loaded)
        {
            _me.Root.SetActive(me != null);
            _meEmpty.gameObject.SetActive(me == null);
            if (me == null)
            {
                var rec = App.Save.GetRecord(_levels[_index]);
                _meEmpty.text = !loaded ? ""
                    : rec != null && rec.HasBest ? Loc.F("Ton record : {0} (pas encore classé)", LevelResult.FormatScore(rec.BestOverPar, rec.BestTimeMs))
                    : Loc.T("Termine ce niveau pour entrer au classement");
                return;
            }
            _me.Rank.text = me.Rank > 0 ? me.Rank.ToString() : "100+";
            _me.Rank.color = UIKit.Gold;
            _me.Rank.fontSize = 40;
            _me.Name.text = Loc.F("{0}  (toi)", me.PlayerName);
            _me.Name.color = UIKit.Sand;
            _me.Country.text = me.Country;
            _me.Score.text = ScoreText(me);
        }
    }
}
