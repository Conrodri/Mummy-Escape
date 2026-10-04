using System.Collections.Generic;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Monthly duel ranking by Elo: this month (live) or last month (final standings, which set the rewards).</summary>
    public sealed class PvpLeaderboardScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Duel;

        const int Top = 100;
        static readonly Color Gold1 = new Color32(255, 215, 90, 255);
        static readonly Color Silver = new Color32(200, 210, 222, 255);
        static readonly Color Bronze = new Color32(214, 140, 72, 255);

        readonly List<RowView> _rows = new List<RowView>();
        UIKit.Segmented _tabs;
        Text _info, _meEmpty;
        ScrollRect _scroll;
        RowView _me;
        int _seasonsAgo;
        int _request;

        sealed class RowView
        {
            public GameObject Root;
            public Image Background;
            public Text Rank, Name, Record, League, Elo;
        }

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Classement des duels");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 18);

            _tabs = new UIKit.Segmented(body, new[] { "Ce mois-ci", "Mois dernier" }, i => { _seasonsAgo = i; Reload(); });
            _info = UIKit.Label(body, "", 30, UIKit.Dim);
            UIKit.FitText(_info, 20);
            UIKit.Size(_info, 46);

            var list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            list.GetComponent<VerticalLayoutGroup>().spacing = 6;
            for (int i = 0; i < Top; i++) _rows.Add(CreateRow(list, 92));

            var meSlot = UIKit.Rect("Me", body);
            UIKit.Size(meSlot, 112);
            _me = CreateRow(meSlot, 112);
            UIKit.Stretch((RectTransform)_me.Root.transform);
            _me.Background.color = new Color(0.13f, 0.4f, 0.4f, 0.9f);
            _meEmpty = UIKit.Label(meSlot, "", 30, UIKit.Sand);
            UIKit.FitText(_meEmpty, 20);
            UIKit.Stretch(_meEmpty.rectTransform, 24, 0, 24, 0);
        }

        RowView CreateRow(Transform parent, float height)
        {
            UIKit.ListItem(parent, height, null, out var h);
            var v = new RowView { Root = h.gameObject, Background = h.GetComponent<Image>() };
            v.Rank = UIKit.Label(h.transform, "", 36, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(v.Rank, -1, 90, 0);
            // Name, and the month's record underneath.
            var who = UIKit.Rect("Who", h.transform); // noloc
            UIKit.Size(who, -1, -1, 1);
            UIKit.Column(who, 0, 0, TextAnchor.MiddleLeft);
            v.Name = UIKit.Label(who, "", 32, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(v.Name, 22);
            UIKit.Size(v.Name, 42);
            v.Record = UIKit.Label(who, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(v.Record, 16);
            UIKit.Size(v.Record, 28);
            v.League = UIKit.Label(h.transform, "", 24, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(v.League, 16);
            UIKit.Size(v.League, -1, 140, 0);
            v.Elo = UIKit.Label(h.transform, "", 34, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.Size(v.Elo, -1, 120, 0);
            return v;
        }

        public override void OnShow()
        {
            _tabs.Select(_seasonsAgo);
            Reload();
        }

        async void Reload()
        {
            _info.text = Loc.T("Chargement…");
            _scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _rows.Count; i++) Fill(_rows[i], i + 1, null);
            ShowMe(null, false);
            var pvp = App.Pvp;
            if (pvp == null)
            {
                _info.text = Loc.T("Classement disponible en ligne uniquement.");
                return;
            }
            int request = ++_request;
            var page = await pvp.GetBoardAsync(_seasonsAgo, Top);
            if (request != _request || this == null) return;

            string month = PvpSkins.MonthName(page.Season);
            _info.text = string.IsNullOrEmpty(page.Season) ? Loc.T("Aucun classement archivé pour le moment.")
                       : _seasonsAgo == 0 ? Loc.F("Top 100 · {0} (remise à zéro le 1er du mois)", month)
                       : Loc.F("Classement final · {0}", month);
            if (pvp.IsDemo) _info.text += "  ·  <color=#E8C35A>" + Loc.T("démo hors ligne") + "</color>";
            for (int i = 0; i < _rows.Count; i++) Fill(_rows[i], i + 1, i < page.Rows.Count ? page.Rows[i] : null);
            ShowMe(page.Me, true);
        }

        void Fill(RowView v, int rank, PvpBoardRow row)
        {
            v.Root.SetActive(true);
            v.Rank.text = rank.ToString();
            v.Rank.color = rank == 1 ? Gold1 : rank == 2 ? Silver : rank == 3 ? Bronze : UIKit.Dim;
            v.Rank.fontSize = rank <= 3 ? 46 : 38;
            if (row == null)
            {
                v.Name.text = "—";
                v.Name.color = new Color(1, 1, 1, 0.2f);
                v.League.text = v.Elo.text = v.Record.text = "";
                v.Background.color = new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.03f : 0.05f);
                return;
            }
            v.Name.text = row.IsMe ? Loc.F("{0}  (toi)", row.PlayerName) : row.PlayerName;
            v.Name.color = row.IsMe ? UIKit.Turquoise : UIKit.Sand;
            SetLeague(v, row);
            v.Elo.text = row.Elo.ToString();
            v.Record.text = RecordText(row);
            v.Background.color = row.IsMe ? new Color(0.13f, 0.4f, 0.4f, 0.65f)
                               : rank <= 3 ? new Color(1f, 0.85f, 0.4f, 0.12f)
                               : new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.04f : 0.07f);
        }

        /// <summary>Wins, draws and losses of the month, coloured: "12 V · 1 N · 5 D".</summary>
        static string RecordText(PvpBoardRow row) =>
            "<color=#40E0D0>" + Loc.F("{0} V", row.Wins) + "</color> · " + Loc.F("{0} N", row.Draws) + " · <color=#D65440>" + Loc.F("{0} D", row.Losses) + "</color>"; // noloc

        static void SetLeague(RowView v, PvpBoardRow row)
        {
            // The rank column already tells the Top 100: this one shows the league.
            var league = Leagues.FromElo(row.Elo);
            v.League.text = Loc.T(PvpSkins.LeagueName(league));
            v.League.color = PvpSkins.LeagueColor(league);
        }

        void ShowMe(PvpBoardRow me, bool loaded)
        {
            _me.Root.SetActive(me != null);
            _meEmpty.gameObject.SetActive(me == null);
            if (me == null)
            {
                _meEmpty.text = !loaded ? "" : _seasonsAgo == 0 ? Loc.T("Joue un duel pour entrer au classement du mois") : Loc.T("Pas classé ce mois-là");
                return;
            }
            _me.Rank.text = me.Rank > 0 ? me.Rank.ToString() : "100+";
            _me.Rank.color = UIKit.Gold;
            _me.Rank.fontSize = 40;
            _me.Name.text = Loc.F("{0}  (toi)", me.PlayerName);
            _me.Name.color = UIKit.Sand;
            SetLeague(_me, me);
            _me.Elo.text = me.Elo.ToString();
            _me.Record.text = RecordText(me);
        }
    }
}
