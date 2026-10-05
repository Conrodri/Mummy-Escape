using System.Collections.Generic;
using MummyEscape.Pvp;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>The 2v2 ranking (duos by 2v2 Elo) or the guild ranking (guilds by points, with their war record).</summary>
    public sealed class TeamLeaderboardScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Ranking;

        const int Top = 100;
        static readonly Color Gold1 = new Color32(255, 215, 90, 255);
        static readonly Color Silver = new Color32(200, 210, 222, 255);
        static readonly Color Bronze = new Color32(214, 140, 72, 255);

        UIKit.Segmented _category;
        RectTransform _list;
        ScrollRect _scroll;
        Text _info;
        bool _guilds;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Classement");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 18);
            _category = LeaderboardScreen.Categories(body, Router, -1);
            _info = UIKit.Label(body, "", 30, UIKit.Dim);
            UIKit.FitText(_info, 20);
            UIKit.Size(_info, 46);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 6;
        }

        /// <summary>Opens the guild ranking (<paramref name="guilds"/>) or the 2v2 one.</summary>
        public void Show(bool guilds)
        {
            _guilds = guilds;
            OnShow();
        }

        public override void OnShow()
        {
            _category.Select(_guilds ? 3 : 2);
            Reload();
        }

        async void Reload()
        {
            UIKit.ClearChildren(_list);
            _scroll.verticalNormalizedPosition = 1f;
            _info.text = Loc.T("Chargement…");
            var pvp = App.Pvp;
            if (pvp == null)
            {
                _info.text = Loc.T("Classement disponible en ligne uniquement.");
                return;
            }
            int request = ++_request;
            bool guilds = _guilds;
            var rows = new List<(string name, string record, string score)>();
            if (guilds)
            {
                var board = await pvp.GetGuildBoardAsync(Top);
                foreach (var g in board.Guilds)
                    rows.Add(($"[{g.Tag}] {g.Name}", Loc.F("{0}/{1} membres", g.MemberCount, TeamConfig.GuildMaxMembers) + " · " // noloc
                              + Loc.F("guerres {0} V · {1} D", g.WarWins, g.WarLosses), g.Points.ToString()));
            }
            else
            {
                var board = await pvp.GetDuoBoardAsync(Top);
                foreach (var d in board.Rows)
                    rows.Add((d.Name, "<color=#40E0D0>" + Loc.F("{0} V", d.Wins) + "</color> · " + Loc.F("{0} N", d.Draws) + " · <color=#D65440>" + Loc.F("{0} D", d.Losses) + "</color>", d.Elo.ToString())); // noloc
            }
            if (request != _request || this == null || guilds != _guilds) return;
            _info.text = guilds ? Loc.T("Guildes · par points de guilde") : Loc.T("2v2 · par Elo de duo");
            if (pvp.IsDemo) _info.text += "  ·  <color=#E8C35A>" + Loc.T("démo hors ligne") + "</color>";
            if (rows.Count == 0) UIKit.Size(UIKit.Label(_list, guilds ? "Aucune guilde pour le moment." : "Aucun duo classé pour le moment.", 28, UIKit.Dim), 80);
            for (int i = 0; i < rows.Count; i++) Row(i + 1, rows[i].name, rows[i].record, rows[i].score);
        }

        void Row(int rank, string name, string record, string score)
        {
            UIKit.ListItem(_list, 92, null, out var h);
            h.GetComponent<Image>().color = rank <= 3 ? new Color(1f, 0.85f, 0.4f, 0.12f) : new Color(1f, 0.92f, 0.75f, rank % 2 == 0 ? 0.04f : 0.07f);
            var r = UIKit.Label(h.transform, rank.ToString(), rank <= 3 ? 46 : 38, rank == 1 ? Gold1 : rank == 2 ? Silver : rank == 3 ? Bronze : UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(r, -1, 90, 0);
            var who = UIKit.Rect("Who", h.transform); // noloc
            UIKit.Size(who, -1, -1, 1);
            UIKit.Column(who, 0, 0, TextAnchor.MiddleLeft);
            var n = UIKit.Label(who, name, 32, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(n, 20);
            UIKit.Size(n, 42);
            var rec = UIKit.Label(who, record, 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(rec, 14);
            UIKit.Size(rec, 28);
            var s = UIKit.Label(h.transform, score, 34, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.Size(s, -1, 130, 0);
        }
    }
}
