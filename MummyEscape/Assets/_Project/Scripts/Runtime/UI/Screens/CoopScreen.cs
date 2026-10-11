using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The 2v2 campaign, from the Campagne tab: both mummies of a duo in the same tomb, live. The page shows the player's
    /// duos (formed by invitation, as for the 2v2 duel), the tombs of the chosen duo with its progress and the player's
    /// own 2v2 stars, invitations and friends to invite. "Play" waits for the teammate (who presses "Play" too), then the
    /// try starts on both phones. A try costs a point of energy to each player; the reward of a tomb comes once per player.
    /// </summary>
    public sealed class CoopScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        /// <summary>Any act from 2 on: every try of the 2v2 campaign costs a point of the solo energy.</summary>
        static readonly LevelId EnergyCost = new LevelId(Difficulty.Easy, 2, 0);

        RectTransform _list;
        ScrollRect _scroll;
        Text _info, _plays;
        TeamsResponse _teams;
        IReadOnlyList<FriendInfo> _friends;
        string _duoId;
        bool _busy;
        int _request;
        RectTransform _searching;
        Text _searchStatus;
        CancellationTokenSource _search;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Campagne 2v2");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _info = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 64);
            _plays = UIKit.Label(body, "", 24, UIKit.Dim);
            UIKit.Size(_plays, 34);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 14;

            _searching = UIKit.Rect("Searching", Root); // noloc
            UIKit.Stretch(_searching, -600, -600, -600, -600);
            var shade = UIKit.Image(_searching, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform);
            var card = UIKit.Card(_searching, 40, 24);
            UIKit.FitInParent(UIKit.Place(card, 0.5f, 0.5f, 820, 0));
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.Size(UIKit.Title(card, "Campagne 2v2", 56), 90);
            _searchStatus = UIKit.Label(card, "", 32, UIKit.Sand);
            UIKit.FitText(_searchStatus, 20);
            UIKit.Size(_searchStatus, 120);
            UIKit.Size(UIKit.Button(card, "Annuler", CancelSearch, UIKit.TextSize, ButtonStyle.Ghost), 80);
            _searching.gameObject.SetActive(false);
        }

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            App.Lighting.SetMood(false);
            _plays.text = PlayGate.Status(App, Monetization.PlayMode.Solo);
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
                _info.text = Loc.T("La campagne 2v2 se joue en ligne : active le mode en ligne (Paramètres › Confidentialité).");
                return;
            }
            int request = ++_request;
            if (_teams == null) _info.text = Loc.T("Chargement…");
            var teams = await App.Pvp.GetTeamsAsync();
            var friends = await App.Online.GetFriendsAsync();
            await App.RefreshPvpProfile(); // the player's own 2v2 stars
            if (request != _request || this == null) return;
            if (teams == null || teams.Error != null)
            {
                _info.text = TeamView.ErrorText(teams?.Error);
                return;
            }
            _teams = teams;
            _friends = friends;
            if (_teams.Duos.All(d => d.Id != _duoId)) _duoId = _teams.Duos.FirstOrDefault()?.Id;
            if (!_busy) _info.text = App.Pvp.IsDemo ? "<color=#E8C35A>" + Loc.T("Démo hors ligne : coéquipier simulé") + "</color>" : "";
            Fill();
        }

        void Fill()
        {
            UIKit.ClearChildren(_list);
            var rules = UIKit.Label(_list, "Vos deux momies dans le même tombeau, en direct : l'un tient la dalle, l'autre passe la porte. La sortie attend les deux. Une momie morte, et le tombeau recommence.", 24, UIKit.Dim);
            UIKit.FitText(rules, 16);
            UIKit.Size(rules, 100);
            int stars = CoopLogic.TotalStars(App.PvpProfile?.Data?.CoopStars);
            var mine = UIKit.Label(_list, Loc.F("Tes étoiles 2v2 : {0}", stars), 30, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(mine, 44);
            if (_teams == null) return;

            if (_teams.Invites.Count > 0)
            {
                UIKit.SectionTitle(_list, "Invitations");
                foreach (var invite in _teams.Invites) InviteRow(invite);
            }

            UIKit.SectionTitle(_list, Loc.F("Mes duos ({0}/{1})", _teams.Duos.Count, TeamConfig.MaxDuosPerPlayer));
            if (_teams.Duos.Count == 0)
                UIKit.Size(UIKit.Label(_list, "Invite un ami ci-dessous pour former ton premier duo.", 26, UIKit.Dim), 60);
            if (_teams.Duos.Count > 1)
            {
                var names = _teams.Duos.Select(d => d.Name).ToArray();
                var tabs = new UIKit.Segmented(_list, names, i => { _duoId = _teams.Duos[i].Id; Fill(); }, 76);
                tabs.Select(Mathf.Max(0, _teams.Duos.FindIndex(d => d.Id == _duoId)));
            }
            var duo = _teams.Duos.Find(d => d.Id == _duoId);
            if (duo != null) DuoTombs(duo);

            UIKit.Size(UIKit.Button(_list, "Classement campagne 2v2", () => Router.Open<TeamLeaderboardScreen>().ShowCoop(), 30), 88);

            if (_teams.Duos.Count < TeamConfig.MaxDuosPerPlayer)
            {
                UIKit.SectionTitle(_list, "Inviter un ami");
                var partners = new HashSet<string>(_teams.Duos.SelectMany(d => d.Members));
                int shown = 0;
                foreach (var f in _friends ?? new List<FriendInfo>())
                {
                    if (partners.Contains(f.PlayerId)) continue;
                    FriendRow(f);
                    shown++;
                }
                if (shown == 0) UIKit.Size(UIKit.Label(_list, "Ajoute des amis (onglet Amis) pour les inviter.", 26, UIKit.Dim), 60);
            }
        }

        /// <summary>The duo's tombs, act by act: its best clear, the player's stars, and "Play" on the open ones.</summary>
        void DuoTombs(Duo duo)
        {
            var head = UIKit.Label(_list, duo.Name + "  ·  " + Loc.F("{0}/{1} tombeaux", CoopLogic.Cleared(duo), CoopCampaign.Count)
                                         + "  ·  " + Loc.F("Elo 2v2 {0}", duo.Elo), 28, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(head, 18);
            UIKit.Size(head, 44);
            string partner = duo.Names.Count >= 2 ? duo.Names[duo.Members[0] == _teams.Me ? 1 : 0] : "?";
            var hint = UIKit.Label(_list, Loc.F("Appuyez tous les deux sur « Jouer », {0} et toi.", partner), 24, UIKit.Dim);
            UIKit.FitText(hint, 16);
            UIKit.Size(hint, 36);
            var myStars = App.PvpProfile?.Data?.CoopStars;
            int act = 0;
            foreach (var tomb in CoopCampaign.Tombs)
            {
                if (tomb.Act != act)
                {
                    act = tomb.Act;
                    UIKit.SectionTitle(_list, Loc.F("Acte {0} — {1}", act, Loc.T(CoopCampaign.ActName(act))));
                }
                TombRow(duo, tomb, myStars?.Find(s => s.Tomb == tomb.Number)?.Stars ?? 0);
            }
        }

        void TombRow(Duo duo, CoopTomb tomb, int stars)
        {
            bool open = CoopLogic.IsOpen(duo, tomb.Number);
            var record = CoopLogic.Record(duo, tomb.Number);
            UIKit.ListItem(_list, 104, null, out var h);
            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 2, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(col, $"{tomb.Act}-{tomb.Index}  {Loc.T(tomb.Name)}", 30, open ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold); // noloc
            UIKit.FitText(name, 18);
            UIKit.Size(name, 42);
            string sub = new string('★', stars) + new string('☆', 3 - stars);
            if (record != null) sub += "  ·  " + Loc.F("record du duo : {0}", LevelResult.FormatTime(record.TimeMs));
            else if (!open) sub += "  ·  " + Loc.T("passez le tombeau précédent");
            var s = UIKit.Label(col, sub, 24, stars > 0 ? UIKit.Gold : UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(s, 14);
            UIKit.Size(s, 32);
            var play = UIKit.Button(h.transform, "Jouer", () => Play(duo, tomb.Number), 28, ButtonStyle.Primary);
            UIKit.Size(play, 76, 170);
            play.interactable = open;
        }

        void InviteRow(DuoInvite invite)
        {
            UIKit.ListItem(_list, 110, null, out var h);
            var name = UIKit.Label(h.transform, Loc.F("{0} veut faire équipe", invite.FromName), 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            UIKit.Size(UIKit.Button(h.transform, "Refuser", () => Respond(invite, false), 26), 80, 170);
            UIKit.Size(UIKit.Button(h.transform, "Accepter", () => Respond(invite, true), 26, ButtonStyle.Primary), 80, 190);
        }

        void FriendRow(FriendInfo f)
        {
            UIKit.ListItem(_list, 100, null, out var h);
            var name = UIKit.Label(h.transform, f.Name, 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            UIKit.Size(UIKit.Button(h.transform, "Inviter", () => Invite(f), 26, ButtonStyle.Primary), 76, 180);
        }

        async void Invite(FriendInfo f)
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
            if (r?.Ok == true && accept) App.Audio.Play(Sfx.Coin);
            Reload();
        }

        /// <summary>A point of energy, then the wait for the teammate; once both phones are connected, the try starts.</summary>
        void Play(Duo duo, int tomb)
        {
            if (_search != null || _teams == null) return;
            PlayGate.Solo(App, EnergyCost, () => Search(duo, tomb));
        }

        async void Search(Duo duo, int tomb)
        {
            if (_search != null) return;
            var matchmaker = CoopMatchmakerFactory.For(App.Pvp);
            if (matchmaker == null)
            {
                _info.text = Loc.T("La campagne 2v2 n'est pas disponible sur cette version.");
                return;
            }
            var search = _search = new CancellationTokenSource();
            _searching.gameObject.SetActive(true);
            var look = PvpSkins.Look(App.Save.Loadout);
            look.Title = TitleBook.Equipped(App);
            var start = await matchmaker.FindAsync(new CoopSearch { Duo = duo, Me = _teams.Me, MyName = App.Online.PlayerName, Look = look, Tomb = tomb, Pvp = App.Pvp },
                status => { if (this != null && _search == search) _searchStatus.text = status; }, search.Token);
            if (this == null) { start?.Link?.Dispose(); return; }
            if (_search == search) _search = null;
            _searching.gameObject.SetActive(false);
            if (start == null || search.IsCancellationRequested)
            {
                start?.Link?.Dispose();
                if (!search.IsCancellationRequested) _info.text = _searchStatus.text;
                return;
            }
            Router.Open<HudScreen>();
            App.Game.StartCoop(start);
        }

        public override void OnHide() => CancelSearch();

        void CancelSearch()
        {
            _search?.Cancel();
            _search = null;
            if (_searching != null) _searching.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ end of a try

        /// <summary>A try ended (or the teammate left): the clear goes to the server, then the choices.</summary>
        public static async void ShowOutcome(GameApp app, CoopOutcome o)
        {
            string mate = o.Info?.MateName ?? "?";
            if (o.MateLeft)
            {
                app.UI.Open<OfferDialog>().Configure("Partie terminée", Loc.F("{0} a quitté la partie.", mate),
                    ("OK", ButtonStyle.Primary, () => BackToCampaign(app))).OnDismissed(() => BackToCampaign(app)); // noloc
                return;
            }
            if (!o.Won)
            {
                bool me = o.Info != null && o.Fallen == o.Info.MyMummy;
                string text = (me ? Loc.T("Ta momie est tombée.") : Loc.F("La momie de {0} est tombée.", mate)) + "\n" + Loc.T("Le tombeau recommence pour vous deux.");
                Choices(app, o, "Une momie est tombée", text, false);
                return;
            }

            var result = o.Result;
            string summary = Loc.F("Évadés en {0} coups, {1}", result.Moves, LevelResult.FormatTime(result.TimeMs)) + "\n" + new string('★', result.Stars) + new string('☆', 3 - result.Stars);
            app.UI.Open<OfferDialog>().Configure("Évadés !", summary + "\n\n" + Loc.T("Envoi au serveur…"));
            var pvp = app.Pvp;
            CoopClearResponse r = pvp == null ? null : await pvp.SubmitCoopAsync(o.Info.Duo.Id, o.Tomb.Number, o.Inputs, result.TimeMs);
            if (!app.Game.InCoop) return; // left meanwhile
            if (r?.Duo != null) o.Info.Duo = r.Duo;
            if (r != null && r.Error == null)
            {
                if (r.FirstClear)
                {
                    app.Save.EarnCoins(CoopConfig.FirstClearScarabs);
                    app.Audio.Play(Sfx.Coin);
                    summary += "\n" + Loc.F("Premier passage : +{0} scarabées", CoopConfig.FirstClearScarabs);
                }
                if (r.NewStars > 0) summary += "\n" + Loc.F("+{0} étoiles 2v2 (total {1})", r.NewStars, r.TotalStars);
                if (app.PvpProfile?.Data != null) _ = app.RefreshPvpProfile();
            }
            else summary += "\n" + Loc.T("Le passage n'a pas pu être enregistré.");
            Choices(app, o, "Évadés !", summary, true);
        }

        static void Choices(GameApp app, CoopOutcome o, string title, string text, bool won)
        {
            var choices = new List<(string, ButtonStyle, System.Action)>();
            int next = o.Tomb.Number + 1;
            if (won && CoopCampaign.Get(next) != null && CoopLogic.IsOpen(o.Info.Duo, next))
                choices.Add(("Tombeau suivant", ButtonStyle.Primary, () => Again(app, next)));
            choices.Add((won ? "Rejouer" : "Recommencer", won ? ButtonStyle.Secondary : ButtonStyle.Primary, () => Again(app, o.Tomb.Number)));
            choices.Add(("Quitter", ButtonStyle.Ghost, () => BackToCampaign(app)));
            app.UI.Open<OfferDialog>().Configure(title, text, choices.ToArray()).OnDismissed(() => BackToCampaign(app)); // tap outside = quit
        }

        /// <summary>A new try costs a point of energy; it starts once the teammate asks for the same tomb.</summary>
        static void Again(GameApp app, int tomb) => PlayGate.Solo(app, EnergyCost, () => app.Game.CoopWish(tomb));

        /// <summary>Leaves the try (the teammate is told) and goes back to the campaign page.</summary>
        internal static void BackToCampaign(GameApp app)
        {
            app.Game.LeaveCoop();
            app.UI.Reset<MainMenuScreen>();
            app.UI.Open<LevelSelectScreen>();
            app.UI.Open<CoopScreen>();
        }
    }
}
