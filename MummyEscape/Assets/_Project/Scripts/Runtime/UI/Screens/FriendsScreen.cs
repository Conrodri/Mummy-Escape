using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Your identity, friend requests, friends list with their progression.</summary>
    public sealed class FriendsScreen : UIScreen
    {
        InputField _name;
        InputField _add;
        Text _status;
        RectTransform _list;

        protected override void Build()
        {
            Header("Amis");
            var body = Body(200, 60);
            UIKit.Column(body, 20);

            UIKit.Size(UIKit.Label(body, "Ton nom de momie", 38, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 56);
            var nameRow = UIKit.Row(body, 100);
            _name = UIKit.Input(nameRow.transform, "Nom (sans espace)");
            UIKit.Size(_name, -1, -1, 1);
            UIKit.Size(UIKit.Button(nameRow.transform, "OK", SaveName, 40), -1, 160, 0);

            UIKit.Size(UIKit.Label(body, "Ajouter un ami (nom complet avec #1234)", 38, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 56);
            var addRow = UIKit.Row(body, 100);
            _add = UIKit.Input(addRow.transform, "Momie#1234");
            UIKit.Size(_add, -1, -1, 1);
            UIKit.Size(UIKit.Button(addRow.transform, "Inviter", SendRequest, 40), -1, 220, 0);

            _status = UIKit.Label(body, "", 32, UIKit.Dim);
            UIKit.Size(_status, 80);

            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
        }

        public override void OnShow()
        {
            _name.text = App.Online.PlayerName;
            Reload();
        }

        async void SaveName()
        {
            _status.text = "Enregistrement…";
            string n = await App.Online.SetPlayerNameAsync(_name.text);
            _name.text = n;
            _status.text = App.Online.IsAvailable ? $"Tes amis peuvent t'ajouter avec : {n}" : App.Online.Status;
        }

        async void SendRequest()
        {
            bool ok = await App.Online.SendFriendRequestAsync(_add.text);
            _status.text = ok ? "Invitation envoyée !" : App.Online.IsAvailable ? "Joueur introuvable." : App.Online.Status;
            if (ok) _add.text = "";
        }

        async void Reload()
        {
            UIKit.ClearChildren(_list);
            if (!App.Online.IsAvailable)
            {
                _status.text = App.Online.Status;
                return;
            }
            _status.text = $"Tu es : {App.Online.PlayerName}";

            var requests = await App.Online.GetFriendRequestsAsync();
            foreach (var r in requests)
            {
                var row = Row();
                UIKit.Size(UIKit.Label(row.transform, $"{r.Name} veut être ton ami", 34, UIKit.Sand, TextAnchor.MiddleLeft), -1, -1, 1);
                string id = r.PlayerId;
                UIKit.Size(UIKit.Button(row.transform, "Accepter", async () => { await App.Online.AcceptFriendRequestAsync(id); Reload(); }, 34), -1, 230, 0);
            }

            var friends = await App.Online.GetFriendsAsync();
            if (friends.Count == 0 && requests.Count == 0)
                UIKit.Size(UIKit.Label(_list, "Aucun ami pour l'instant. Partage ton nom !", 34, UIKit.Dim), 100);

            foreach (var f in friends)
            {
                var row = Row();
                UIKit.Size(UIKit.Label(row.transform, (f.Online ? "● " : "○ ") + f.Name, 38, f.Online ? UIKit.Turquoise : UIKit.Sand, TextAnchor.MiddleLeft), -1, -1, 1);
                var progress = UIKit.Label(row.transform, "…", 30, UIKit.Dim, TextAnchor.MiddleRight);
                UIKit.Size(progress, -1, 260, 0);
                var friend = f;
                UIKit.Size(UIKit.Button(row.transform, "Voir", () => Router.Open<FriendDetailScreen>().Show(friend), 34), -1, 160, 0);
                LoadProgress(friend.PlayerId, progress);
            }
        }

        async void LoadProgress(string playerId, Text target)
        {
            var p = await App.Online.GetProgressAsync(playerId);
            if (target == null) return;
            target.text = p == null ? "—" : $"{p.FurthestLevel} · {p.TotalStars} étoiles";
        }

        HorizontalLayoutGroup Row()
        {
            var row = UIKit.Row(_list, 110, 12);
            row.padding = new RectOffset(16, 16, 6, 6);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.05f);
            bg.raycastTarget = false;
            return row;
        }
    }

    /// <summary>A friend's progression and per-level scores, compared with yours.</summary>
    public sealed class FriendDetailScreen : UIScreen
    {
        public override bool IsModal => true;

        Text _title;
        Text _summary;
        RectTransform _list;
        Online.FriendInfo _friend;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0.04f, 0.03f, 0.02f, 0.97f), true);
            UIKit.Stretch(shade.rectTransform);
            _title = Header("", () => Router.Close(this));
            var body = Body(200, 60);
            UIKit.Column(body, 20);
            _summary = UIKit.Label(body, "", 38, UIKit.Sand);
            UIKit.Size(_summary, 120);
            var head = UIKit.Row(body, 70);
            UIKit.Label(head.transform, "Niveau", 34, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Label(head.transform, "Ami", 34, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Label(head.transform, "Toi", 34, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
        }

        public async void Show(Online.FriendInfo friend)
        {
            _friend = friend;
            _title.text = friend.Name;
            _summary.text = "Chargement…";
            UIKit.ClearChildren(_list);
            var p = await App.Online.GetProgressAsync(friend.PlayerId);
            if (_friend != friend) return;
            if (p == null) { _summary.text = "Progression non partagée pour l'instant."; return; }
            _summary.text = $"Plus loin : niveau {p.FurthestLevel}\n{p.TotalStars} étoiles (toi : {App.Save.TotalStars})";

            foreach (var id in DifficultyTable.AllLevels())
            {
                var theirs = p.Records.Find(r => r.Key == id.Key);
                var mine = App.Save.GetRecord(id);
                if (theirs == null && mine == null) continue;
                var row = UIKit.Row(_list, 80, 8);
                UIKit.Label(row.transform, id.ToString(), 34, UIKit.Sand, TextAnchor.MiddleLeft);
                UIKit.Label(row.transform, Format(theirs), 34, UIKit.Sand);
                bool better = mine != null && theirs != null && mine.BestMoves > 0 && (theirs.BestMoves == 0 || mine.BestMoves < theirs.BestMoves);
                UIKit.Label(row.transform, Format(mine), 34, better ? UIKit.Turquoise : UIKit.Sand);
            }
        }

        static string Format(LevelRecord r) => r == null || r.BestMoves == 0 ? "—" : $"{r.BestMoves} ({r.BestStars}/3)";
    }
}
