using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Phone-first friends hub: profile card (name, country, share code), Amis / Demandes tabs with large tappable
    /// cards, and one big "Ajouter un ami" button within thumb reach. Text entry happens in dialogs.
    /// </summary>
    public sealed class FriendsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;

        Image _portrait;
        Text _name, _code;
        Button _countryButton;
        UIKit.Segmented _tabs;
        RectTransform _list;
        ScrollRect _scroll;
        int _tab;
        int _reloadId;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Amis");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 22);

            // Profile.
            var card = UIKit.Card(body, 30, 20);
            var who = UIKit.Row(card, 130, 26);
            _portrait = UIKit.Image(who.transform, null, Color.white);
            _portrait.preserveAspect = true; // the outfit sprite is 32x40
            UIKit.Size(_portrait, 130, 130);
            var texts = UIKit.Rect("Texts", who.transform);
            UIKit.Size(texts, -1, -1, 1);
            _name = UIKit.Label(texts, "", 44, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_name, 30);
            UIKit.TopBand(_name.rectTransform, 74, 4);
            _code = UIKit.Label(texts, "", 30, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.BottomBand(_code.rectTransform, 46, 4);

            var actions = UIKit.Row(card, UIKit.SmallButtonHeight, 14);
            UIKit.Size(UIKit.Button(actions.transform, "Mon nom", EditName, 34), -1, -1, 1);
            _countryButton = UIKit.Button(actions.transform, "Pays", () => Router.Open<CountryPickerScreen>(), 34);
            UIKit.Size(_countryButton, -1, -1, 1);
            UIKit.Size(UIKit.Button(actions.transform, "Partager", ShareCode, 34), -1, -1, 1);
            var titles = UIKit.Button(actions.transform, "Titres", () => Router.Open<TitlesScreen>(), 34);
            UIKit.Size(titles, -1, -1, 1);

            _tabs = new UIKit.Segmented(body, new[] { "Amis", "Demandes" }, i => { _tab = i; Reload(); });

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;

            UIKit.Size(UIKit.Button(body, "+  Ajouter un ami", AddFriend, 36, ButtonStyle.Primary), 104);
        }

        public override void OnShow()
        {
            MummyAnimator.Show(_portrait, App.Art, App.Save.Loadout);
            string title = TitleBook.Equipped(App);
            _name.text = App.Online.PlayerName + (title == null ? "" : "  " + TitleBook.Line(title));
            _code.text = Loc.T(App.Online.IsAvailable ? "Ton code ami : donne-le à tes amis"
                       : App.Online.IsDemo ? "Démo hors ligne (amis fictifs)" : "Hors ligne");
            string country = App.Save.Country;
            UIKit.SetLabel(_countryButton, string.IsNullOrEmpty(country) ? "Pays ?" : Loc.F("Pays : {0}", country));
            _tabs.Select(_tab);
            Reload();
        }

        async void Reload()
        {
            int id = ++_reloadId;
            UIKit.ClearChildren(_list);
            _scroll.verticalNormalizedPosition = 1f;
            var online = App.Online;
            if (!online.IsAvailable && !online.IsDemo)
            {
                Message(Loc.T(online.Status) + "\n\n" + Loc.T("Les amis font partie du mode en ligne : Paramètres › Confidentialité."));
                return;
            }

            var requests = await online.GetFriendRequestsAsync();
            var friends = await online.GetFriendsAsync();
            if (id != _reloadId || this == null) return;
            _tabs.SetLabel(0, friends.Count > 0 ? Loc.F("Amis ({0})", friends.Count) : Loc.T("Amis"));
            _tabs.SetLabel(1, requests.Count > 0 ? Loc.F("Demandes ({0})", requests.Count) : Loc.T("Demandes"));

            if (_tab == 0)
            {
                if (friends.Count == 0) Message("Aucun ami pour l'instant.\nPartage ton code pour qu'on t'ajoute !");
                foreach (var f in friends) FriendItem(f);
            }
            else
            {
                if (requests.Count == 0) Message("Aucune demande en attente.");
                foreach (var r in requests) RequestItem(r);
            }
        }

        void FriendItem(FriendInfo f)
        {
            UIKit.ListItem(_list, 150, () => Router.Open<FriendDetailScreen>().Show(f), out var h);
            var dot = UIKit.Label(h.transform, "●", 40, f.Online ? UIKit.Turquoise : new Color(1, 1, 1, 0.2f));
            UIKit.Size(dot, -1, 44, 0);
            var texts = UIKit.Rect("Texts", h.transform);
            UIKit.Size(texts, -1, -1, 1);
            var name = UIKit.Label(texts, f.Name, 44, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 28);
            UIKit.TopBand(name.rectTransform, 70, 10);
            var progress = UIKit.Label(texts, f.Online ? "En ligne" : "…", 30, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.BottomBand(progress.rectTransform, 46, 10);
            UIKit.Size(UIKit.Label(h.transform, "►", 40, UIKit.Gold), -1, 50, 0);
            LoadProgress(f, progress);
        }

        async void LoadProgress(FriendInfo f, Text target)
        {
            var p = await App.Online.GetProgressAsync(f.PlayerId);
            if (target == null) return;
            string status = f.Online ? Loc.T("En ligne") + " · " : "";
            target.text = status + (p == null ? Loc.T("progression non partagée") : Loc.F("niveau {0} · {1} étoiles", p.FurthestLevel, p.TotalStars));
        }

        void RequestItem(FriendRequest r)
        {
            UIKit.ListItem(_list, 170, null, out var h);
            h.padding = new RectOffset(28, 20, 22, 22);
            var texts = UIKit.Rect("Texts", h.transform);
            UIKit.Size(texts, -1, -1, 1);
            var name = UIKit.Label(texts, r.Name, 42, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 26);
            UIKit.TopBand(name.rectTransform, 64, 0);
            var sub = UIKit.Label(texts, "veut devenir ton ami", 30, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.BottomBand(sub.rectTransform, 44, 0);
            string id = r.PlayerId;
            UIKit.Size(UIKit.Button(h.transform, "Refuser", async () => { await App.Online.DeclineFriendRequestAsync(id); Reload(); }, 32), -1, 180, 0);
            UIKit.Size(UIKit.Button(h.transform, "Accepter", async () => { await App.Online.AcceptFriendRequestAsync(id); Reload(); }, 32), -1, 200, 0);
        }

        void Message(string text) => UIKit.Size(UIKit.Label(_list, text, 36, UIKit.Dim), 200);

        void EditName() =>
            Router.Open<PromptDialog>().Configure("Ton nom de momie", "Visible dans les classements.\nN'utilise pas ton vrai nom.",
                "Nom", StripTag(App.Online.PlayerName), "Enregistrer", async n =>
                {
                    if (n.Length < 3) return "3 caractères minimum.";
                    await App.Online.SetPlayerNameAsync(n);
                    if (this != null) OnShow();
                    return null;
                });

        void AddFriend() =>
            Router.Open<PromptDialog>().Configure("Ajouter un ami", "Demande-lui son code ami\n(exemple : Nefertari#2041).",
                "Code ami", "", "Inviter", async code =>
                {
                    if (!code.Contains("#")) return "Le code contient un # suivi de 4 chiffres.";
                    if (!App.Online.IsAvailable && !App.Online.IsDemo) return App.Online.Status;
                    bool ok = await App.Online.SendFriendRequestAsync(code);
                    return ok ? null : "Joueur introuvable.";
                });

        void ShareCode()
        {
            string name = App.Online.PlayerName;
            App.Share.ShareText(Loc.F("Ajoute-moi sur Mummy Escape ! Mon code ami : {0}", name) + "\n" + ShareService.GameUrl);
        }

        static string StripTag(string name)
        {
            int hash = name?.IndexOf('#') ?? -1;
            return hash > 0 ? name.Substring(0, hash) : name;
        }
    }

    /// <summary>A friend's progression and per-level scores side by side with yours.</summary>
    public sealed class FriendDetailScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;

        public override bool IsModal => true;

        Text _title;
        Text _furthest, _stars;
        RectTransform _list;
        Button _remove;
        FriendInfo _friend;
        bool _confirmRemove;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            _title = Header("", () => Router.Close(this));
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);

            var stats = UIKit.Row(body, 190, 20);
            _furthest = StatTile(stats.transform, "le plus loin");
            _stars = StatTile(stats.transform, "étoiles");

            var head = UIKit.Row(body, 60, 12);
            head.padding = new RectOffset(28, 28, 0, 0);
            UIKit.Size(UIKit.Label(head.transform, "Niveau", 32, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), -1, 200, 0);
            UIKit.Size(UIKit.Label(head.transform, "Ami", 32, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), -1, -1, 1);
            UIKit.Size(UIKit.Label(head.transform, "Toi", 32, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), -1, -1, 1);

            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 6;

            _remove = UIKit.Button(body, "Retirer des amis", Remove, UIKit.TextSize, ButtonStyle.Danger);

            UIKit.Size(_remove, UIKit.ButtonHeight);
        }

        Text StatTile(Transform parent, string caption)
        {
            var tile = UIKit.Panel(parent);
            tile.raycastTarget = false;
            UIKit.Size(tile, -1, -1, 1);
            var value = UIKit.Label(tile.transform, "", 64, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(value.rectTransform, 10, 20, 10, 60);
            var cap = UIKit.Label(tile.transform, caption, 30, UIKit.Dim);
            UIKit.BottomBand(cap.rectTransform, 50, 24);
            return value;
        }

        public async void Show(FriendInfo friend)
        {
            _friend = friend;
            _confirmRemove = false;
            UIKit.SetLabel(_remove, "Retirer des amis");
            _title.text = friend.Name;
            _furthest.text = _stars.text = "…";
            UIKit.ClearChildren(_list);

            var p = await App.Online.GetProgressAsync(friend.PlayerId);
            if (_friend != friend || this == null) return;
            if (p == null)
            {
                _furthest.text = _stars.text = "—";
                Row("Progression non partagée pour l'instant.", "", "", false, null);
                return;
            }
            _furthest.text = p.FurthestLevel;
            int mine = App.Save.TotalStars;
            _stars.text = $"{p.TotalStars} <size=34><color=#9C8B70>" + Loc.F("/ toi {0}", mine) + "</color></size>";

            foreach (var id in DifficultyTable.AllLevels())
            {
                var theirs = p.Records.Find(r => r.Key == id.Key);
                var me = App.Save.GetRecord(id);
                bool theyHave = theirs != null && theirs.HasBest, iHave = me != null && me.HasBest;
                if (!theyHave && !iHave) continue;
                bool iWin = iHave && (!theyHave || LevelResult.CompareRuns(me.BestOverPar, me.BestTimeMs, theirs.BestOverPar, theirs.BestTimeMs) < 0);
                var level = id;
                Row(id.ToString(), Format(theirs), Format(me), iWin, () =>
                {
                    Router.Close(this);
                    var lb = Router.Get<LeaderboardScreen>();
                    lb.Focus(level, LeaderboardScope.Friends);
                    Router.Open<LeaderboardScreen>();
                });
            }
        }

        void Row(string level, string theirs, string mine, bool iWin, System.Action onClick)
        {
            UIKit.ListItem(_list, 96, onClick, out var h);
            h.spacing = 12;
            UIKit.Size(UIKit.Label(h.transform, level, 36, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold), -1, 200, 0);
            UIKit.Size(UIKit.Label(h.transform, theirs, 34, iWin ? UIKit.Dim : UIKit.Sand), -1, -1, 1);
            UIKit.Size(UIKit.Label(h.transform, mine, 34, iWin ? UIKit.Turquoise : UIKit.Dim, TextAnchor.MiddleCenter, iWin ? FontStyle.Bold : FontStyle.Normal), -1, -1, 1);
        }

        static string Format(LevelRecord r) => r == null || !r.HasBest ? "—" : LevelResult.FormatScore(r.BestOverPar, r.BestTimeMs);

        async void Remove()
        {
            if (!_confirmRemove)
            {
                _confirmRemove = true;
                UIKit.SetLabel(_remove, "Confirmer le retrait ?");
                return;
            }
            await App.Online.RemoveFriendAsync(_friend.PlayerId);
            if (this == null) return;
            Router.Close(this);
            if (Router.Current is FriendsScreen friends) friends.OnShow();
        }
    }
}
