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
    /// Phone-first friends hub: the player's card (mummy, name, title, duel rank, country, share), Amis / Demandes tabs
    /// with a card per friend showing their mummy, title and duel rank, and one big "Ajouter un ami" button within thumb
    /// reach. The name and country are chosen once, at the first online connection (<see cref="ProfileSetupScreen"/>).
    /// </summary>
    public sealed class FriendsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;

        Image _portrait;
        Text _name, _title, _code;
        RectTransform _badges;
        UIKit.Segmented _tabs, _social;
        RectTransform _list;
        ScrollRect _scroll;
        int _tab;
        int _reloadId;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Amis");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);
            _social = Social(body, Router, 0);

            // The player's card.
            var card = UIKit.Plate(body, Color.white, 34, UIKit.Rim, false, "Me"); // noloc
            UIFx.Gradient(card, new Color32(66, 52, 38, 255), new Color32(30, 23, 17, 255));
            UIKit.DropShadow(card, 10, 0.45f);
            UIKit.Size(card, 250);
            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(20, 26, 16, 16);
            row.spacing = 22;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var stage = UIKit.Rect("Stage", card.transform); // noloc
            UIKit.Size(stage, 210, 200);
            UIFx.Halo(stage, new Color(1f, 0.7f, 0.35f, 0.45f), 300);
            _portrait = UIKit.Image(stage, null, Color.white);
            _portrait.preserveAspect = true; // the outfit sprite is 32x40
            UIKit.Stretch(_portrait.rectTransform);
            var texts = UIKit.Rect("Texts", card.transform); // noloc
            UIKit.Size(texts, -1, -1, 1);
            UIKit.Column(texts, 4, 0, TextAnchor.MiddleLeft);
            _name = UIKit.Label(texts, "", 44, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_name, 28);
            UIKit.Size(_name, 58);
            _title = UIKit.Label(texts, "", 28, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_title, 18);
            UIKit.Size(_title, 38);
            var badges = UIKit.Row(texts, 52, 10);
            badges.childAlignment = TextAnchor.MiddleLeft;
            _badges = badges.GetComponent<RectTransform>();
            _code = UIKit.Label(texts, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_code, 16);
            UIKit.Size(_code, 34);
            UIKit.IconButton(card.transform, UISprites.Share, ShareCode, 92, ButtonStyle.Primary);

            _tabs = new UIKit.Segmented(body, new[] { "Amis", "Demandes" }, i => { _tab = i; Reload(); });

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;

            // Adding a friend, and the community server beside it (friends to be found there too).
            var actions = UIKit.Row(body, 104, 20);
            UIKit.Size(UIKit.Button(actions.transform, "+  Ajouter un ami", AddFriend, 36, ButtonStyle.Primary), -1, -1, 2);
            var discord = UIKit.Button(actions.transform, "Discord", () => Application.OpenURL(DiscordInvite), 36);
            discord.image.color = new Color(0.45f, 0.5f, 0.95f); // Discord's blurple, on the pixel button
            UIKit.Size(discord, -1, -1, 1);
        }

        /// <summary>The Mummy Rush community server (a permanent invitation).</summary>
        public const string DiscordInvite = "https://discord.gg/myuCgZXmGK"; // noloc

        /// <summary>Friends (this screen) or the guild (<see cref="GuildScreen"/>), one tab away from each other; Back leads home.</summary>
        internal static UIKit.Segmented Social(Transform body, UIRouter router, int selected) =>
            new UIKit.Segmented(body, new[] { "Amis", "Guilde" }, i =>
            {
                if (i == selected) return;
                router.Reset<MainMenuScreen>();
                if (i == 0) router.Open<FriendsScreen>();
                else router.Open<GuildScreen>();
            }, 96);

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            _social.Select(0);
            MummyAnimator.Show(_portrait, App.Art, App.Save.Loadout);
            _name.text = App.Online.PlayerName;
            string title = TitleBook.Equipped(App);
            _title.text = title == null ? Loc.T("Aucun titre") : TitleBook.Line(title);
            var pvp = App.PvpProfile?.Data;
            UIKit.ClearChildren(_badges);
            RankChip(_badges, pvp != null && pvp.TotalDuels > 0 ? pvp.Elo : 0, 48);
            string country = App.Save.Country;
            if (!string.IsNullOrEmpty(country)) UIKit.Chip(_badges, UISprites.Globe, CountryService.NameOf(country), UIKit.Sand, 48);
            _code.text = Loc.T(App.Online.IsAvailable ? "Ton code ami, c'est ton nom avec son #"
                       : App.Online.IsDemo ? "Démo hors ligne (amis fictifs)" : "Hors ligne");
            _tabs.Select(_tab);
            Reload();
        }

        /// <summary>League and Elo of a duel player ("Pas classé" before their first duel).</summary>
        internal static void RankChip(Transform parent, int elo, float height)
        {
            if (elo <= 0)
            {
                UIKit.Chip(parent, UISprites.Swords, Loc.T("Pas classé"), UIKit.Dim, height);
                return;
            }
            var league = Leagues.FromElo(elo);
            UIKit.Chip(parent, UISprites.Swords, Loc.T(PvpSkins.LeagueName(league)) + " · " + elo, PvpSkins.LeagueColor(league), height); // noloc
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
            _ = Online.ChatState.SyncProfileAsync(App, friends);
            _tabs.SetLabel(0, friends.Count > 0 ? Loc.F("Amis ({0})", friends.Count) : Loc.T("Amis"));
            _tabs.SetLabel(1, requests.Count > 0 ? Loc.F("Demandes ({0})", requests.Count) : Loc.T("Demandes"));

            if (_tab == 0)
            {
                if (friends.Count == 0) Message("Aucun ami pour l'instant.\nPartage ton code pour qu'on t'ajoute !");
                // Online friends first.
                var sorted = new System.Collections.Generic.List<FriendInfo>(friends);
                sorted.Sort((a, b) => b.Online.CompareTo(a.Online));
                for (int k = 0; k < sorted.Count; k++) FriendItem(sorted[k], k);
            }
            else
            {
                if (requests.Count == 0) Message("Aucune demande en attente.");
                foreach (var r in requests) RequestItem(r);
            }
        }

        void FriendItem(FriendInfo f, int index)
        {
            var item = UIKit.ListItem(_list, 200, () => Router.Open<FriendDetailScreen>().Show(f), out var h);
            h.padding = new RectOffset(16, 24, 10, 10);
            h.spacing = 18;
            UIFx.PopIn(item, Mathf.Min(index, 10) * 0.05f, 0.94f);

            var stage = UIKit.Rect("Stage", h.transform); // noloc
            UIKit.Size(stage, -1, 160, 0);
            var halo = UIFx.Halo(stage, new Color(1f, 0.7f, 0.35f, 0.3f), 220);
            var portrait = UIKit.Image(stage, null, new Color(1, 1, 1, 0.35f));
            portrait.preserveAspect = true;
            UIKit.Stretch(portrait.rectTransform, 0, 6, 0, 6);
            MummyAnimator.Show(portrait, App.Art, SkinCatalog.Classic); // until their look arrives
            var dot = UIKit.Image(stage, UISprites.Circle, f.Online ? UIKit.Success : new Color(0.4f, 0.36f, 0.32f, 1f), false, "Online"); // noloc
            UIKit.Place(dot.rectTransform, 1f, 0f, 30, 30, -10, 14);
            if (f.Online) UIFx.Pulse(dot, 0.12f, 1.4f);

            var texts = UIKit.Rect("Texts", h.transform); // noloc
            UIKit.Size(texts, -1, -1, 1);
            UIKit.Column(texts, 2, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(texts, "", 40, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.text = f.Name;
            UIKit.FitText(name, 26);
            UIKit.Size(name, 54);
            var title = UIKit.Label(texts, "", 26, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(title, 16);
            UIKit.Size(title, 34);
            var badges = UIKit.Row(texts, 46, 10);
            badges.childAlignment = TextAnchor.MiddleLeft;
            var progress = UIKit.Label(texts, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            progress.text = f.Online ? Loc.T("En ligne") : "…";
            UIKit.FitText(progress, 16);
            UIKit.Size(progress, 32);
            // Write to the friend (the chat's private conversation).
            UIKit.IconButton(h.transform, UISprites.Chat, () => Router.Open<ChatScreen>().OpenDirect(f.PlayerId, f.Name), 84);
            UIKit.Size(UIKit.Image(h.transform, UISprites.Next, UIKit.Gold), 40, 40);
            LoadProgress(f, portrait, halo, title, badges.transform, progress);
        }

        async void LoadProgress(FriendInfo f, Image portrait, Image halo, Text title, Transform badges, Text progress)
        {
            var p = await App.Online.GetProgressAsync(f.PlayerId);
            if (progress == null) return;
            string status = f.Online ? Loc.T("En ligne") + " · " : "";
            progress.text = status + (p == null || !p.HasProgress ? Loc.T("progression indisponible") : Loc.F("niveau {0} · {1} étoiles", p.FurthestLevel, p.TotalStars));
            var look = p?.Look;
            var loadout = PvpSkins.Loadout(look);
            portrait.color = Color.white;
            MummyAnimator.Show(portrait, App.Art, loadout);
            if (loadout.Color.Legendary)
            {
                var a = LegendarySkins.Accent(loadout.Color.Fx);
                halo.color = new Color(a.r, a.g, a.b, 0.5f);
            }
            title.text = look != null && Titles.Get(look.Title) != null ? TitleBook.Line(look.Title) : "";
            RankChip(badges, p?.Elo ?? 0, 42);
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

        void AddFriend() =>
            Router.Open<PromptDialog>().Configure("Ajouter un ami", "Demande-lui son code ami\n(exemple : Nefertari#2041).",
                "Code ami", "", "Inviter", async code =>
                {
                    var candidates = FriendCode.Candidates(code);
                    string typed = (code ?? "").Trim();
                    if (candidates.Count == 0 && (App.Pvp == null || typed.Length == 0)) return "Le code ami, c'est le nom, un # et des chiffres (exemple : Nefertari#2041).";
                    if (!App.Online.IsAvailable && !App.Online.IsDemo) return App.Online.Status;
                    // Typed without its #: each place it could go, until one is a player.
                    string error = "Joueur introuvable.";
                    foreach (var c in candidates)
                    {
                        error = await App.Online.SendFriendRequestAsync(c);
                        if (error != "Joueur introuvable.") break;
                    }
                    if (error != "Joueur introuvable." || App.Pvp == null) return error;
                    // Not found as typed: the game's own directory, which ignores capitals and finds the name alone
                    // (a rename gives a new #number) when only one player has it.
                    var found = await App.Pvp.FindPlayerAsync(candidates.Count > 0 ? candidates[0] : typed);
                    if (found?.PlayerId != null) return await App.Online.SendFriendRequestToIdAsync(found.PlayerId);
                    if (found?.Error == "SELF") return "C'est ton propre code ami.";
                    if (found?.Error == "AMBIGUOUS") return "Plusieurs joueurs portent ce nom : il faut le code complet, avec son # et ses chiffres.";
                    return error;
                });

        void ShareCode()
        {
            string name = App.Online.PlayerName;
            App.Share.ShareText(Loc.F("Ajoute-moi sur Mummy Rush ! Mon code ami : {0}", name) + "\n" + ShareService.GameUrl);
        }
    }

    /// <summary>A friend's progression and per-level scores side by side with yours.</summary>
    public sealed class FriendDetailScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;

        public override bool IsModal => true;

        Text _title;
        Text _furthest, _stars, _titleLine;
        Image _portrait;
        RectTransform _badges;
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

            // Their mummy, title and duel rank.
            var who = UIKit.Row(body, 220, 24);
            var stage = UIKit.Rect("Stage", who.transform); // noloc
            UIKit.Size(stage, -1, 220, 0);
            UIFx.Halo(stage, new Color(1f, 0.7f, 0.35f, 0.45f), 320);
            _portrait = UIKit.Image(stage, null, Color.white);
            _portrait.preserveAspect = true;
            UIKit.Stretch(_portrait.rectTransform);
            var texts = UIKit.Rect("Texts", who.transform); // noloc
            UIKit.Size(texts, -1, -1, 1);
            UIKit.Column(texts, 10, 0, TextAnchor.MiddleLeft);
            _titleLine = UIKit.Label(texts, "", 32, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_titleLine, 18);
            UIKit.Size(_titleLine, 46);
            var badges = UIKit.Row(texts, 56, 10);
            badges.childAlignment = TextAnchor.MiddleLeft;
            _badges = badges.GetComponent<RectTransform>();

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
            _titleLine.text = "";
            UIKit.ClearChildren(_badges);
            MummyAnimator.Show(_portrait, App.Art, SkinCatalog.Classic);
            UIKit.ClearChildren(_list);

            var p = await App.Online.GetProgressAsync(friend.PlayerId);
            if (_friend != friend || this == null) return;
            var look = p?.Look;
            MummyAnimator.Show(_portrait, App.Art, PvpSkins.Loadout(look));
            _titleLine.text = look != null && Titles.Get(look.Title) != null ? TitleBook.Line(look.Title) : Loc.T("Aucun titre");
            FriendsScreen.RankChip(_badges, p?.Elo ?? 0, 52);
            if (p == null || !p.HasProgress)
            {
                _furthest.text = _stars.text = "—";
                // One line across the whole list (the level column is far too narrow for it).
                var note = UIKit.Label(_list, Loc.F("La progression de {0} n'est pas encore disponible (son jeu doit être à jour).", friend.Name), 28, UIKit.Dim);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                UIKit.FitText(note, 18);
                UIKit.Size(note, 120);
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
