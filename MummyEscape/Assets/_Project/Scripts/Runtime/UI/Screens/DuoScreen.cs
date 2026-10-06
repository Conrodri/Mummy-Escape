using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The 2v2: duos formed with friends (invitations sent and received), each with its own 2v2 Elo. A match is a live
    /// relay against another duo: both teammates search at the same time, four players meet, and each duo runs its two
    /// mazes in turn (Game.GameController). Duos of bots stand in when nobody is around, for those who accept.
    /// </summary>
    public sealed class DuoScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Duel;

        RectTransform _list;
        ScrollRect _scroll;
        Text _info, _plays;
        TeamsResponse _teams;
        IReadOnlyList<Online.FriendInfo> _friends;
        bool _busy;
        int _request;
        // Live search of a match: the panel over the list, and the duo to search again after a match.
        RectTransform _searching;
        Text _searchStatus;
        CancellationTokenSource _search;
        Duo _lastDuo;
        UIKit.Segmented _modes;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Duel");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _modes = PvpScreen.Modes(body, Router, 1);
            _info = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 64);
            _plays = UIKit.Label(body, "", 24, UIKit.Dim);
            UIKit.Size(_plays, 34);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;

            _searching = UIKit.Rect("Searching", Root); // noloc
            UIKit.Stretch(_searching, -600, -600, -600, -600);
            var shade = UIKit.Image(_searching, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform);
            var card = UIKit.Card(_searching, 40, 24);
            UIKit.FitInParent(UIKit.Place(card, 0.5f, 0.5f, 820, 0));
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.Size(UIKit.Title(card, "2v2", 64), 90);
            _searchStatus = UIKit.Label(card, "", 32, UIKit.Sand);
            UIKit.FitText(_searchStatus, 20);
            UIKit.Size(_searchStatus, 120);
            UIKit.Size(UIKit.Button(card, "Annuler", CancelSearch, UIKit.TextSize, ButtonStyle.Ghost), 80);
            _searching.gameObject.SetActive(false);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            _modes.Select(1);
            _plays.text = PlayGate.Status(App, Monetization.PlayMode.Duo);
            _busy = false;
            _info.text = "";
            _scroll.verticalNormalizedPosition = 1f;
            Fill();
            Reload();
        }

        async void Reload()
        {
            if (App.Pvp == null)
            {
                _info.text = Loc.T("Les duels se jouent en ligne : active le mode en ligne (Paramètres › Confidentialité).");
                return;
            }
            int request = ++_request;
            if (_teams == null) _info.text = Loc.T("Chargement…");
            var teams = await App.Pvp.GetTeamsAsync();
            var friends = await App.Online.GetFriendsAsync();
            if (request != _request || this == null) return;
            if (teams == null || teams.Error != null)
            {
                _info.text = TeamView.ErrorText(teams?.Error);
                return;
            }
            _teams = teams;
            _friends = friends;
            if (!_busy) _info.text = App.Pvp.IsDemo ? "<color=#E8C35A>" + Loc.T("Démo hors ligne : coéquipiers et adversaires simulés") + "</color>" : "";
            Fill();
            // The server's 2v2 history: the replays of matches played on another phone, both relays complete.
            var history = await App.Pvp.GetRelayHistoryAsync();
            if (request != _request || this == null || history?.Matches == null) return;
            RelayReplayStore.Merge(history.Matches, App.Online.PlayerId);
            Fill();
        }

        void Fill()
        {
            UIKit.ClearChildren(_list);
            var rules = UIKit.Label(_list, "En direct contre un autre duo, sur les mêmes labyrinthes. Chacun a le sien : tu cours jusqu'à ta dalle, elle libère ton coéquipier, qui te libère à son tour… Le premier duo sorti gagne. Une momie morte fait perdre son duo.", 24, UIKit.Dim);
            UIKit.FitText(rules, 16);
            UIKit.Size(rules, 100);
            if (_teams != null) FillTeams();
            FillReplays();
        }

        void FillTeams()
        {
            if (_teams.Invites.Count > 0)
            {
                UIKit.SectionTitle(_list, "Invitations");
                foreach (var invite in _teams.Invites) InviteRow(invite);
            }

            UIKit.SectionTitle(_list, Loc.F("Mes duos ({0}/{1})", _teams.Duos.Count, TeamConfig.MaxDuosPerPlayer));
            if (_teams.Duos.Count == 0)
            {
                var none = UIKit.Label(_list, "Invite un ami ci-dessous pour former ton premier duo.", 26, UIKit.Dim);
                UIKit.Size(none, 60);
            }
            foreach (var duo in _teams.Duos) DuoCard(duo);

            var board = UIKit.Button(_list, "Classement 2v2", () => Router.Open<TeamLeaderboardScreen>().Show(false), 30);
            UIKit.Size(board, 88);

            if (_teams.Duos.Count < TeamConfig.MaxDuosPerPlayer)
            {
                UIKit.SectionTitle(_list, "Inviter un ami");
                var partners = new HashSet<string>(_teams.Duos.SelectMany(d => d.Members));
                int shown = 0;
                foreach (var f in _friends ?? new List<Online.FriendInfo>())
                {
                    if (partners.Contains(f.PlayerId)) continue;
                    FriendRow(f);
                    shown++;
                }
                if (shown == 0)
                {
                    var hint = UIKit.Label(_list, "Ajoute des amis (onglet Amis) pour les inviter.", 26, UIKit.Dim);
                    UIKit.Size(hint, 60);
                }
            }
        }

        /// <summary>The matches kept apart (until the player removes them), then the last ones played, to watch again.</summary>
        void FillReplays()
        {
            var saved = RelayReplayStore.Saved;
            UIKit.SectionTitle(_list, Loc.F("Replays enregistrés ({0}/{1})", saved.Count, RelayReplayStore.SavedSize));
            if (saved.Count == 0)
            {
                var hint = UIKit.Label(_list, "Enregistre un match (+) pour le garder : il restera ici tant que tu ne le retires pas.", 24, UIKit.Dim, TextAnchor.MiddleLeft);
                UIKit.FitText(hint, 16);
                UIKit.Size(hint, 60);
            }
            foreach (var record in saved)
            {
                var r = record;
                var h = MatchRow(r);
                UIKit.IconButton(h.transform, UISprites.Close, () => { RelayReplayStore.Forget(r.Id); Fill(); }, 72);
                UIKit.IconButton(h.transform, UISprites.Play, () => Watch(r), 84);
            }

            UIKit.SectionTitle(_list, "Derniers matchs");
            var recent = RelayReplayStore.Recent;
            var note = UIKit.Label(_list, recent.Count == 0 ? "Tes 10 derniers matchs 2v2 apparaîtront ici, à revoir des deux côtés."
                                                          : "Les 10 derniers, à revoir des deux côtés. Les plus anciens laissent la place aux nouveaux.", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(note, 16);
            UIKit.Size(note, 60);
            foreach (var record in recent)
            {
                var r = record;
                var h = MatchRow(r);
                bool kept = RelayReplayStore.IsSaved(r.Id);
                var keep = UIKit.IconButton(h.transform, kept ? UISprites.Check : UISprites.Plus, () => Keep(r), 72);
                keep.interactable = !kept;
                UIKit.IconButton(h.transform, UISprites.Play, () => Watch(r), 84);
            }
        }

        void Keep(RelayRecord r)
        {
            if (RelayReplayStore.Keep(r)) App.Audio.Play(Sfx.Coin);
            else _info.text = Loc.T("Les 10 places sont prises : retire un replay enregistré pour garder celui-ci.");
            Fill();
        }

        void Watch(RelayRecord r)
        {
            if (_busy || _search != null) return;
            Router.Open<ReplayScreen>().ShowRelay(r);
        }

        /// <summary>One match: result, rival duo, date and Elo change; the caller adds the buttons at the end.</summary>
        HorizontalLayoutGroup MatchRow(RelayRecord r)
        {
            UIKit.ListItem(_list, 132, () => Watch(r), out var h);
            var color = r.Result == DuelResult.Win ? UIKit.Gold : r.Result == DuelResult.Draw ? UIKit.Sand : UIKit.Danger;
            var badge = UIKit.Image(h.transform, UISprites.Circle, r.Resolved ? color : UIKit.Dim, false, "Result"); // noloc
            UIKit.Size(badge, 84, 84, 0);
            var letter = UIKit.Title(badge.transform, r.Result == DuelResult.Win ? Loc.T("V") : r.Result == DuelResult.Draw ? Loc.T("N") : Loc.T("D"), 44, UIKit.Ink);
            UIKit.Stretch(letter.rectTransform);

            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 2, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(col, Loc.F("contre {0}", r.Rival?.Name ?? "—"), 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold); // noloc
            UIKit.FitText(name, 20);
            UIKit.Size(name, 44);
            string when = System.DateTimeOffset.FromUnixTimeMilliseconds(r.PlayedAtUnixMs).ToLocalTime().ToString("dd/MM HH:mm"); // noloc
            string elo = r.Resolved ? "  ·  Elo " + (r.EloDelta > 0 ? "+" : "") + r.EloDelta : ""; // noloc
            var sub = UIKit.Label(col, (r.Mine?.Name ?? "") + "  ·  " + when + elo, 24, UIKit.Dim, TextAnchor.MiddleLeft); // noloc
            UIKit.FitText(sub, 16);
            UIKit.Size(sub, 34);
            return h;
        }

        void InviteRow(DuoInvite invite)
        {
            UIKit.ListItem(_list, 110, null, out var h);
            var name = UIKit.Label(h.transform, Loc.F("{0} veut faire équipe", invite.FromName), 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            var no = UIKit.Button(h.transform, "Refuser", () => Respond(invite, false), 26);
            UIKit.Size(no, 80, 170);
            var yes = UIKit.Button(h.transform, "Accepter", () => Respond(invite, true), 26, ButtonStyle.Primary);
            UIKit.Size(yes, 80, 190);
        }

        void FriendRow(Online.FriendInfo f)
        {
            UIKit.ListItem(_list, 100, null, out var h);
            var name = UIKit.Label(h.transform, f.Name, 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            var invite = UIKit.Button(h.transform, "Inviter", () => Invite(f), 26, ButtonStyle.Primary);
            UIKit.Size(invite, 76, 180);
        }

        void DuoCard(Duo duo)
        {
            var card = UIKit.Panel(_list, "Duo"); // noloc
            card.raycastTarget = false;
            var col = UIKit.Column(card.transform, 10);
            col.padding = new RectOffset(26, 26, 22, 24);

            var top = UIKit.Row(card.transform, 60, 12);
            var name = UIKit.Title(top.transform, duo.Name, 40, UIKit.Gold, TextAnchor.MiddleLeft);
            UIKit.FitText(name, 22);
            UIKit.Size(name, -1, 0, 1);
            UIKit.IconButton(top.transform, UISprites.Close, () => Leave(duo), 60);
            var stats = UIKit.Label(card.transform, Loc.F("Elo 2v2 {0}", duo.Elo) + "  ·  " + Loc.F("{0} V · {1} N · {2} D", duo.Wins, duo.Draws, duo.Losses), 26, UIKit.Sand);
            UIKit.Size(stats, 40);

            var battles = _teams.Battles.Where(b => TeamLogic.SideOfTeam(b, duo.Id) != null).ToList();
            var active = battles.FirstOrDefault(b => b.Id == duo.ActiveBattle);
            if (active != null) TeamView.BattleCard(card.transform, active, _teams.Me, Router, RunRound);
            else
            {
                string partner = duo.Names[duo.Members[0] == _teams.Me ? 1 : 0];
                var hint = UIKit.Label(card.transform, Loc.F("Lancez la recherche tous les deux, {0} et toi.", partner), 24, UIKit.Dim);
                UIKit.FitText(hint, 16);
                UIKit.Size(hint, 36);
                var find = UIKit.Button(card.transform, "Chercher un match", () => Search(duo), 34, ButtonStyle.Primary);
                UIKit.Size(find, 100);
                var bots = UIKit.Toggle(card.transform, "Bots de notre division si personne en vue (1 min)", App.Settings.PvpBots, App.Settings.SetPvpBots);
                UIKit.Size(bots, 70);
            }
            foreach (var b in battles)
                if (b != active) TeamView.BattleCard(card.transform, b, _teams.Me, Router, null);
        }

        async void Invite(Online.FriendInfo f)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.InviteDuoAsync(f.PlayerId, App.Online.PlayerName);
            if (this == null) return;
            _busy = false;
            _info.text = r?.Ok == true ? Loc.F("Invitation envoyée à {0}.", f.Name) : TeamView.ErrorText(r?.Error);
            Reload();
        }

        async void Respond(DuoInvite invite, bool accept)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.RespondDuoAsync(invite.Id, accept, App.Online.PlayerName);
            if (this == null) return;
            _busy = false;
            _info.text = r?.Ok == true ? (accept ? Loc.F("Duo formé avec {0} !", invite.FromName) : "") : TeamView.ErrorText(r?.Error);
            if (r?.Ok == true && accept) App.Audio.Play(Services.Sfx.Coin);
            Reload();
        }

        void Leave(Duo duo)
        {
            Router.Open<ConfirmDialog>().Configure(Loc.F("Dissoudre le duo {0} ?", duo.Name),
                "Son Elo 2v2 et son bilan seront perdus.", "Dissoudre", async () =>
                {
                    var r = await App.Pvp.LeaveDuoAsync(duo.Id);
                    if (r == null || !r.Ok) return TeamView.ErrorText(r?.Error);
                    if (this != null) Reload();
                    return null;
                });
        }

        /// <summary>After a match: the same duo searches again.</summary>
        public void SearchAgain()
        {
            if (_lastDuo != null) Search(_lastDuo);
        }

        /// <summary>
        /// Looks for another duo with the teammate (both search), then draws the mazes, plays the VS screen and starts the
        /// match: preview, vote, relay.
        /// </summary>
        async void Search(Duo duo)
        {
            if (_search != null || _teams == null) return;
            if (!PlayGate.Ensure(App, () => Search(duo))) return;
            var matchmaker = RelayMatchmakerFactory.For(App.Pvp);
            if (matchmaker == null)
            {
                _info.text = Loc.T("Le 2v2 en direct n'est pas disponible sur cette version.");
                return;
            }
            _lastDuo = duo;
            var search = _search = new CancellationTokenSource();
            _searching.gameObject.SetActive(true);
            _searchStatus.text = Loc.T("Recherche d'un duo adverse…");
            var look = PvpSkins.Look(App.Save.Loadout);
            look.Title = TitleBook.Equipped(App);
            var me = new RelayRunner { PlayerId = _teams.Me, Name = App.Online.PlayerName, Look = look };
            // The division of the duo is its best duel player's: each phone brings its own duel Elo.
            var profile = await App.Pvp.GetProfileAsync();
            if (this == null || _search != search) return;
            int duelElo = profile?.Data?.Elo ?? PvpConfig.StartingElo;
            var link = await matchmaker.FindAsync(new RelaySearch { Duo = duo, Me = _teams.Me, MeRunner = me, MyDuelElo = duelElo, AllowBots = App.Settings.PvpBots, Pvp = App.Pvp },
                status => { if (this != null && _search == search) _searchStatus.text = status; }, search.Token);
            if (this == null) { link?.Dispose(); return; }
            if (link == null || search.IsCancellationRequested)
            {
                link?.Dispose();
                if (_search == search) _search = null;
                if (!search.IsCancellationRequested) _info.text = _searchStatus.text;
                _searching.gameObject.SetActive(false);
                return;
            }
            _searchStatus.text = Loc.T("Les dieux scellent les labyrinthes…");
            bool ready = await App.Game.StartRelay(link);
            if (this == null) return;
            _search = null;
            _searching.gameObject.SetActive(false);
            if (!ready)
            {
                App.Game.Abandon();
                _info.text = Loc.T("Impossible de préparer le match.");
                return;
            }
            PlayGate.SpendPvp(App);
            Router.Open<HudScreen>();
            Router.Open<RelayVsScreen>().Show(link.Match, link.Me, App.Game.BeginRelayPreview);
        }

        public override void OnHide() => CancelSearch();

        void CancelSearch()
        {
            _search?.Cancel();
            _search = null;
            if (_searching != null) _searching.gameObject.SetActive(false);
        }

        void RunRound(TeamBattle b)
        {
            if (_busy) return;
            _busy = true;
            _info.text = Loc.T("Préparation du tombeau…");
            TeamView.RunRound(Router, b, error =>
            {
                if (this == null) return;
                _busy = false;
                _info.text = error;
            });
        }
    }
}
