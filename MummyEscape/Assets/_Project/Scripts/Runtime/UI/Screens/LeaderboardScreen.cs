using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Online;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Per-level leaderboard (everyone gets the same tomb, so scores are directly comparable).</summary>
    public sealed class LeaderboardScreen : UIScreen
    {
        readonly List<LevelId> _levels = new List<LevelId>(DifficultyTable.AllLevels());
        int _index;
        LeaderboardScope _scope = LeaderboardScope.Global;
        Text _levelLabel;
        Text _status;
        Button _globalTab, _friendsTab;
        RectTransform _list;
        int _requestId;

        public void Focus(LevelId id)
        {
            int i = _levels.IndexOf(id);
            if (i >= 0) _index = i;
        }

        protected override void Build()
        {
            Header("Classement");
            var body = Body(200, 60);
            UIKit.Column(body, 24);

            var pager = UIKit.Row(body, 120);
            UIKit.Size(UIKit.Button(pager.transform, "◄", () => Move(-1), 56), -1, 130, 0);
            _levelLabel = UIKit.Label(pager.transform, "", 54, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_levelLabel, -1, -1, 1);
            UIKit.Size(UIKit.Button(pager.transform, "►", () => Move(1), 56), -1, 130, 0);

            var tabs = UIKit.Row(body, 110);
            _globalTab = UIKit.Button(tabs.transform, "Mondial", () => SetScope(LeaderboardScope.Global), 40);
            _friendsTab = UIKit.Button(tabs.transform, "Amis", () => SetScope(LeaderboardScope.Friends), 40);

            _status = UIKit.Label(body, "", 34, UIKit.Dim);
            UIKit.Size(_status, 60);

            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
        }

        public override void OnShow() => Reload();

        void Move(int delta)
        {
            _index = (_index + delta + _levels.Count) % _levels.Count;
            Reload();
        }

        void SetScope(LeaderboardScope scope)
        {
            _scope = scope;
            Reload();
        }

        async void Reload()
        {
            var id = _levels[_index];
            var rec = App.Save.GetRecord(id);
            _levelLabel.text = $"Niveau {id}";
            _globalTab.interactable = _scope != LeaderboardScope.Global;
            _friendsTab.interactable = _scope != LeaderboardScope.Friends;
            _status.text = "Chargement…";
            UIKit.ClearChildren(_list);

            int request = ++_requestId;
            var rows = await App.Online.GetLeaderboardAsync(id, _scope, 50);
            if (request != _requestId || this == null) return; // a newer request superseded this one

            string mine = rec != null && rec.BestMoves > 0 ? $"Ton record : {rec.BestMoves} coups" : "Pas encore terminé";
            _status.text = App.Online.IsAvailable ? mine : $"{mine}\n<size=26>{App.Online.Status}</size>";
            if (rows.Count == 0) AddRow("—", "Aucun score pour l'instant", "", false);
            foreach (var r in rows)
                AddRow($"#{r.Rank}", r.PlayerName, $"{r.Moves} coups · -{r.HpLost} PV", r.IsMe);
        }

        void AddRow(string rank, string name, string score, bool highlight)
        {
            var row = UIKit.Row(_list, 100, 10);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = highlight ? new Color(0.2f, 0.5f, 0.5f, 0.5f) : new Color(1, 1, 1, 0.05f);
            bg.raycastTarget = false;
            row.padding = new RectOffset(20, 20, 0, 0);
            UIKit.Size(UIKit.Label(row.transform, rank, 40, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), -1, 140, 0);
            UIKit.Size(UIKit.Label(row.transform, name, 38, UIKit.Sand, TextAnchor.MiddleLeft), -1, -1, 1);
            UIKit.Size(UIKit.Label(row.transform, score, 34, UIKit.Sand, TextAnchor.MiddleRight), -1, 320, 0);
        }
    }
}
