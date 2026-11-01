using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The rankings: the four ladders the server keeps (duel, 2v2, tower, endless), each by Monde / country / Amis, and the
    /// guilds. Rank 1 at the top, the player's own row pinned at the bottom. Opens on the last board viewed.
    /// </summary>
    public sealed class LeaderboardScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Ranking;

        /// <summary>The tabs: the first four are the <see cref="Ladder"/>s, in their order.</summary>
        public enum Board { Duel, Duo, Tower, Endless, Guilds }

        static readonly Color Gold1 = new Color32(255, 214, 92, 255);
        static readonly Color Silver = new Color32(206, 212, 222, 255);
        static readonly Color Bronze = new Color32(214, 140, 72, 255);

        readonly List<RowView> _rows = new List<RowView>();
        Board _board;
        int _scope, _seasonsAgo;
        Text _info, _rules, _meEmpty;
        UIKit.Segmented _boards, _scopes, _seasons;
        Button _pickCountry;
        ScrollRect _scroll;
        GameObject _meSlot;
        RowView _me;
        int _requestId;

        sealed class RowView
        {
            public GameObject Root;
            public Image Background;
            public Text Rank, Country, Name, Sub, Value;
        }

        /// <summary>Opens the rankings on this board.</summary>
        public static void Open(UIRouter router, Board board)
        {
            router.Get<LeaderboardScreen>()._board = board; // before it opens: it loads the board it shows
            router.Open<LeaderboardScreen>();
        }

        bool Guilds => _board == Board.Guilds;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Classement");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 14);
            _boards = new UIKit.Segmented(body, new[] { "Duel", "2v2", "Tour", "Sans fin", "Guildes" }, i => { _board = (Board)i; Reload(); }, 96, new[] { 5 });
            _scopes = new UIKit.Segmented(body, new[] { "Monde", "Pays", "Amis" }, i => { _scope = i; Reload(); });
            _seasons = new UIKit.Segmented(body, new[] { "Ce mois-ci", "Mois dernier" }, i => { _seasonsAgo = i; Reload(); }, 72);

            _info = UIKit.Label(body, "", 30, UIKit.Dim);
            UIKit.FitText(_info, 20);
            UIKit.Size(_info, 42);
            _rules = UIKit.Label(body, "", 24, UIKit.Dim);
            UIKit.FitText(_rules, 16);
            UIKit.Size(_rules, 34);
            _pickCountry = UIKit.Button(body, "Choisir mon pays", () => Router.Open<CountryPickerScreen>().OnPicked(OnShow), 30, ButtonStyle.Primary);
            UIKit.Size(_pickCountry, 88);

            var list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            list.GetComponent<VerticalLayoutGroup>().spacing = 6;
            for (int i = 0; i < Ladders.Size; i++) _rows.Add(CreateRow(list, 92));

            // The player's own row, always visible.
            var meSlot = UIKit.Rect("Me", body);
            _meSlot = meSlot.gameObject;
            UIKit.Size(meSlot, 112);
            _me = CreateRow(meSlot, 112);
            UIKit.Stretch((RectTransform)_me.Root.transform);
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
            v.Country = UIKit.Label(h.transform, "", 26, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(v.Country, -1, 64, 0);
            // The name, and the record (or the time) underneath.
            var who = UIKit.Rect("Who", h.transform); // noloc
            UIKit.Size(who, -1, -1, 1);
            UIKit.Column(who, 0, 0, TextAnchor.MiddleLeft);
            v.Name = UIKit.Label(who, "", 32, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(v.Name, 20);
            UIKit.Size(v.Name, 42);
            v.Sub = UIKit.Label(who, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(v.Sub, 14);
            UIKit.Size(v.Sub, 28);
            v.Value = UIKit.Label(h.transform, "", 32, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.FitText(v.Value, 20);
            UIKit.Size(v.Value, -1, 190, 0);
            return v;
        }

        public override void OnShow()
        {
            _boards.Select((int)_board);
            // The country tab says which country: "France" rather than "Pays".
            string country = App.Save.Country;
            _scopes.SetLabel(1, string.IsNullOrEmpty(country) ? "Pays" : CountryService.NameOf(country));
            Reload();
        }

        async void Reload()
        {
            var board = _board;
            var ladder = (Ladder)board;
            var scope = (LadderScope)_scope;
            int seasonsAgo = board == Board.Duel ? _seasonsAgo : 0;
            _scopes.Root.SetActive(!Guilds);
            _seasons.Root.SetActive(board == Board.Duel);
            _scopes.Select(_scope);
            _seasons.Select(_seasonsAgo);
            _meSlot.SetActive(!Guilds);
            _pickCountry.gameObject.SetActive(false);
            _info.text = Loc.T("Chargement…");
            _rules.text = Rules(board, null);
            _scroll.verticalNormalizedPosition = 1f;
            bool placeholders = Guilds || scope != LadderScope.Friends;
            for (int i = 0; i < _rows.Count; i++) Clear(_rows[i], i + 1, placeholders);
            ShowMe(null, false);

            var pvp = App.Pvp;
            if (pvp == null)
            {
                _info.text = Loc.T("Classement disponible en ligne uniquement.");
                HideRows();
                return;
            }
            if (board == Board.Duo) App.Quests.Visit(Quests.VisitDuoBoard);
            int request = ++_requestId;

            if (Guilds)
            {
                var guilds = await pvp.GetGuildBoardAsync(Ladders.Size);
                if (request != _requestId || this == null) return; // superseded by a newer request
                _info.text = Demo(pvp, Loc.T("Guildes · par points de guilde"));
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (i >= guilds.Guilds.Count) continue;
                    var g = guilds.Guilds[i];
                    Fill(_rows[i], i + 1, $"[{g.Tag}] {g.Name}", "", // noloc
                         Loc.F("{0}/{1} membres", g.MemberCount, TeamConfig.GuildMaxMembers) + " · " + Loc.F("guerres {0} V · {1} D", g.WarWins, g.WarLosses), // noloc
                         g.Points.ToString(), false);
                }
                return;
            }

            if (scope == LadderScope.Country && string.IsNullOrEmpty(App.Save.Country))
            {
                NoCountry();
                return;
            }
            // The server writes the tower and endless ladders from the runs it replays: the queued ones first.
            if (board == Board.Tower || board == Board.Endless) await App.SendSoloRuns(true);
            if (request != _requestId || this == null) return;
            var page = await pvp.GetLadderAsync(ladder, scope, seasonsAgo);
            // A country chosen while the server was out of reach: told again, then asked once more.
            if (page?.Error == "NO_COUNTRY" && !string.IsNullOrEmpty(App.Save.Country)) // noloc
            {
                await App.SendCountry();
                page = await pvp.GetLadderAsync(ladder, scope, seasonsAgo);
            }
            if (request != _requestId || this == null) return;

            if (page == null || page.Error == PvpServiceFactory.NetworkError)
            {
                _info.text = Loc.T("Connexion impossible. Vérifie ton accès à Internet.");
                HideRows();
                return;
            }
            if (page.Error == "NO_COUNTRY") // noloc
            {
                NoCountry();
                return;
            }
            _info.text = Demo(pvp, scope == LadderScope.World ? Loc.T("Top 100 mondial")
                                 : scope == LadderScope.Country ? Loc.F("Top 100 · {0}", CountryService.NameOf(App.Save.Country))
                                 : Loc.T("Toi et tes amis"));
            _rules.text = Rules(board, page.Season);
            for (int i = 0; i < _rows.Count && i < page.Rows.Count; i++) Fill(_rows[i], page.Rows[i], ladder);
            ShowMe(page.Me, true);
        }

        void NoCountry()
        {
            _info.text = Loc.T("Choisis ton pays pour voir son classement.");
            _pickCountry.gameObject.SetActive(true);
            HideRows();
        }

        void HideRows()
        {
            foreach (var row in _rows) row.Root.SetActive(false);
        }

        static string Demo(IPvpService pvp, string title) => pvp.IsDemo ? title + "  ·  <color=#E8C35A>" + Loc.T("démo hors ligne") + "</color>" : title; // noloc

        /// <summary>What the board ranks by.</summary>
        string Rules(Board board, string season)
        {
            switch (board)
            {
                case Board.Duel:
                    if (string.IsNullOrEmpty(season)) return ""; // loading
                    string month = PvpSkins.MonthName(season);
                    return _seasonsAgo == 0 ? Loc.F("Elo du mois · {0} (remise à zéro le 1er du mois)", month) : Loc.F("Classement final · {0}", month);
                case Board.Duo: return Loc.T("2v2 · par Elo de duo");
                case Board.Tower: return Loc.T("Le plus d'étages en une course, puis le temps");
                case Board.Endless:
                    var weights = new List<string>();
                    foreach (var m in DifficultyExt.All) weights.Add(LevelNames.Mode(m) + " ×" + Ladders.EndlessWeights[(int)m]); // noloc
                    return Loc.F("Points par tombeau : {0}, puis le temps", string.Join(" · ", weights)); // noloc
                default: return "";
            }
        }

        static void Clear(RowView v, int rank, bool placeholder)
        {
            v.Root.SetActive(placeholder);
            SetRank(v, rank);
            v.Name.text = "—";
            v.Name.color = new Color(1, 1, 1, 0.2f);
            v.Country.text = v.Sub.text = v.Value.text = "";
            v.Background.color = new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.03f : 0.05f);
        }

        void Fill(RowView v, LadderRow row, Ladder ladder) =>
            Fill(v, row.Rank, NameOf(row), row.Country, SubOf(row, ladder), ValueOf(row, ladder), row.IsMe);

        void Fill(RowView v, int rank, string name, string country, string sub, string value, bool me)
        {
            v.Root.SetActive(true);
            SetRank(v, rank);
            v.Name.text = me ? Loc.F("{0}  (toi)", name) : name;
            v.Name.color = me ? UIKit.Turquoise : UIKit.Sand;
            v.Country.text = country;
            v.Sub.text = sub;
            v.Value.text = value;
            v.Background.color = me ? new Color(0.13f, 0.4f, 0.4f, 0.65f)
                               : rank <= 3 ? new Color(1f, 0.85f, 0.4f, 0.12f)
                               : new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.04f : 0.07f);
        }

        static void SetRank(RowView v, int rank)
        {
            v.Rank.text = rank > 0 ? rank.ToString() : "—";
            v.Rank.color = rank == 1 ? Gold1 : rank == 2 ? Silver : rank == 3 ? Bronze : UIKit.Dim;
            v.Rank.fontSize = rank > 0 && rank <= 3 ? 46 : 38;
        }

        static string NameOf(LadderRow row) => string.IsNullOrEmpty(row.MateName) ? row.PlayerName : row.PlayerName + " & " + row.MateName; // noloc

        /// <summary>Duel and 2v2: the record (the duel's with its league); tower and endless: the time that breaks ties.</summary>
        static string SubOf(LadderRow row, Ladder ladder)
        {
            if (ladder == Ladder.Tower || ladder == Ladder.Endless) return row.TimeMs > 0 ? LevelResult.FormatTime(row.TimeMs) : "";
            string record = "<color=#40E0D0>" + Loc.F("{0} V", row.Wins) + "</color> · " + Loc.F("{0} N", row.Draws) + " · <color=#D65440>" + Loc.F("{0} D", row.Losses) + "</color>"; // noloc
            if (ladder != Ladder.Duel) return record;
            var league = Leagues.FromElo(row.Value);
            return "<color=#" + ColorUtility.ToHtmlStringRGB(PvpSkins.LeagueColor(league)) + ">" + Loc.T(PvpSkins.LeagueName(league)) + "</color> · " + record; // noloc
        }

        static string ValueOf(LadderRow row, Ladder ladder) =>
            ladder == Ladder.Tower ? Loc.F("{0} étages", row.Value)
            : ladder == Ladder.Endless ? Loc.F("{0} pts", row.Value)
            : row.Value.ToString();

        void ShowMe(LadderRow me, bool loaded)
        {
            _me.Root.SetActive(me != null);
            _meEmpty.gameObject.SetActive(me == null);
            if (me == null)
            {
                _meEmpty.text = !loaded ? ""
                    : _board == Board.Duel ? (_seasonsAgo == 0 ? Loc.T("Joue un duel pour entrer au classement du mois") : Loc.T("Pas classé ce mois-là"))
                    : _board == Board.Duo ? Loc.T("Joue un match 2v2 pour entrer au classement")
                    : _board == Board.Tower ? Loc.T("Franchis un étage de la tour pour entrer au classement")
                    : Loc.T("Franchis un tombeau sans fin pour entrer au classement");
                return;
            }
            Fill(_me, me, (Ladder)_board);
            _me.Rank.color = UIKit.Gold;
            _me.Name.text = Loc.F("{0}  (toi)", NameOf(me));
            _me.Name.color = UIKit.Sand;
            _me.Background.color = new Color(0.13f, 0.4f, 0.4f, 0.9f);
        }
    }
}
