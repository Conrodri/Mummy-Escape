using System.Collections.Generic;
using System.Linq;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The group: two friends side by side, their mummies in their outfits, the leader (who formed the group) on the
    /// left. The teammate presses "Prêt"; the leader then launches the 2v2 campaign or the 2v2 duel, and both phones go
    /// straight into it. The group is the duo of the server (one per player): invitations are sent and accepted here,
    /// nowhere else. Both phones poll the duo while this screen is open.
    /// </summary>
    public sealed class GroupScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Home;

        const float PollSeconds = 3f;

        Text _info;
        RectTransform _list;
        ScrollRect _scroll;
        readonly Image[] _portraits = new Image[2];
        readonly Text[] _names = new Text[2], _states = new Text[2];
        readonly string[] _shown = new string[2];
        Button _readyButton, _coop, _relay, _leave;
        Text _readyLabel;

        TeamsResponse _teams;
        Duo _duo;
        IReadOnlyList<FriendInfo> _friends;
        bool _busy, _ready;
        int _request;
        long _seenLaunch;
        float _nextPoll;

        string Me => App.Online.PlayerId;
        bool IsLeader => _duo != null && _duo.Leader == Me;
        string Mate => _duo?.Members.Find(m => m != Me);
        /// <summary>A simulated teammate (editor and development builds) is always ready.</summary>
        bool MateReady => _duo != null && Mate != null && (_duo.IsReady(Mate) || App.Pvp.IsDemo);

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Groupe");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 14);
            _info = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 64);

            // The two mummies, side by side: the leader on the left.
            var slots = UIKit.Row(body, 380, 24);
            for (int i = 0; i < 2; i++)
            {
                var card = UIKit.Panel(slots.transform, "Slot"); // noloc
                UIKit.Size(card, -1, -1, 1);
                var col = UIKit.Column(card.transform, 6);
                col.padding = new RectOffset(16, 16, 16, 16);
                var holder = UIKit.Rect("Portrait", card.transform); // noloc
                UIKit.Size(holder, 240);
                _portraits[i] = UIKit.Image(holder, null, Color.white, false, "Mummy"); // noloc
                _portraits[i].preserveAspect = true;
                UIKit.Place(_portraits[i].rectTransform, 0.5f, 0.5f, 240, 240);
                _names[i] = UIKit.Label(card.transform, "", 30, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.FitText(_names[i], 18);
                UIKit.Size(_names[i], 44);
                _states[i] = UIKit.Label(card.transform, "", 24, UIKit.Dim);
                UIKit.FitText(_states[i], 16);
                UIKit.Size(_states[i], 36);
            }

            _readyButton = UIKit.Button(body, "Je suis prêt", ToggleReady, 34, ButtonStyle.Primary);
            UIKit.Size(_readyButton, 100);
            _readyLabel = _readyButton.GetComponentInChildren<Text>();
            _coop = UIKit.Button(body, "Campagne 2v2", () => Launch(GroupLaunch.Coop), 34, ButtonStyle.Primary);
            UIKit.Size(_coop, 100);
            _relay = UIKit.Button(body, "Duel 2v2", () => Launch(GroupLaunch.Relay), 34, ButtonStyle.Primary);
            UIKit.Size(_relay, 100);
            _leave = UIKit.Button(body, "Quitter le groupe", Leave, 28, ButtonStyle.Ghost);
            UIKit.Size(_leave, 76);

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;
        }

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            App.Lighting.SetMood(false);
            _busy = false;
            _ready = false;
            _info.text = "";
            _shown[0] = _shown[1] = null;
            // A launch made before this screen opened is not followed: only what the leader does from now on.
            _seenLaunch = App.Group?.LaunchAtUnixMs ?? 0;
            _duo = App.Group;
            Fill();
            Reload();
        }

        public override void OnHide()
        {
            if (_ready && _duo != null) _ = App.Pvp.SetGroupReadyAsync(_duo.Id, false, null);
            _ready = false;
        }

        void Update()
        {
            if (_duo == null || _busy || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            Poll();
        }

        async void Reload()
        {
            if (App.Pvp == null)
            {
                _info.text = Loc.T("Le groupe se joue en ligne : active le mode en ligne (Paramètres › Confidentialité).");
                _duo = null;
                Fill();
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
            _duo = teams.Duos.FirstOrDefault();
            App.SetGroup(_duo);
            if (!_busy) _info.text = App.Pvp.IsDemo ? "<color=#E8C35A>" + Loc.T("Démo hors ligne : coéquipier simulé") + "</color>" : "";
            Fill();
            if (_duo != null) Poll();
        }

        /// <summary>The duo as the server has it now: the teammate's readiness and outfit, the leader's launch.</summary>
        async void Poll()
        {
            var duo = _duo;
            if (duo == null) return;
            var r = await App.Pvp.GetDuoAsync(duo.Id, MyLook());
            if (this == null || _duo != duo) return;
            if (r?.Error == "UNKNOWN") { Reload(); return; } // noloc, dissolved by the other
            if (r?.Duo == null) return;
            _duo = r.Duo;
            App.SetGroup(_duo);
            FillSlots();
            FollowLaunch();
        }

        /// <summary>The leader launched since the last look: this phone goes where he went.</summary>
        void FollowLaunch()
        {
            if (_duo?.Launch == null || _duo.LaunchAtUnixMs <= _seenLaunch) return;
            long age = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _duo.LaunchAtUnixMs;
            _seenLaunch = _duo.LaunchAtUnixMs;
            if (age > GroupLaunch.WindowMs) return;
            Go(_duo.Launch);
        }

        void Go(string mode)
        {
            _ready = false; // the server cleared the readiness with the launch
            if (mode == GroupLaunch.Coop) Router.Open<CoopScreen>().Show(_duo, IsLeader);
            else Router.Open<DuoScreen>().SearchWith(_duo);
        }

        PlayerLook MyLook()
        {
            var look = PvpSkins.Look(App.Save.Loadout);
            look.Title = TitleBook.Equipped(App);
            return look;
        }

        // ------------------------------------------------------------------ drawing

        void Fill()
        {
            FillSlots();
            UIKit.ClearChildren(_list);
            if (_teams == null) return;
            if (_teams.Invites.Count > 0)
            {
                UIKit.SectionTitle(_list, "Invitations");
                foreach (var invite in _teams.Invites) InviteRow(invite);
            }
            if (_duo == null)
            {
                UIKit.SectionTitle(_list, "Inviter un ami");
                int shown = 0;
                foreach (var f in _friends ?? new List<FriendInfo>()) { FriendRow(f); shown++; }
                if (shown == 0) UIKit.Size(UIKit.Label(_list, "Ajoute des amis (Social › Amis) pour les inviter.", 26, UIKit.Dim), 60);
            }
            else
            {
                var stats = UIKit.Label(_list, Loc.F("Elo 2v2 {0}", _duo.Elo) + "  ·  " + Loc.F("{0} V · {1} N · {2} D", _duo.Wins, _duo.Draws, _duo.Losses)
                                              + "  ·  " + Loc.F("Campagne 2v2 : {0}/{1} tombeaux", CoopLogic.Cleared(_duo), Core.CoopCampaign.Count), 24, UIKit.Dim);
                UIKit.FitText(stats, 14);
                UIKit.Size(stats, 40);
                UIKit.Size(UIKit.Button(_list, "Classement 2v2", () => Router.Open<TeamLeaderboardScreen>().Show(false), 28), 80);
                UIKit.Size(UIKit.Button(_list, "Classement campagne 2v2", () => Router.Open<TeamLeaderboardScreen>().ShowCoop(), 28), 80);
            }
        }

        /// <summary>The two slots: the leader, then the teammate (or the empty place that invites one).</summary>
        void FillSlots()
        {
            bool leader = IsLeader;
            string mate = Mate;
            Slot(0, _duo != null ? _duo.Leader : Me, Loc.T("Chef"));
            if (_duo != null && mate != null) Slot(1, mate, MateReady ? "<color=#E8C35A>" + Loc.T("Prêt") + "</color>" : Loc.T("Pas prêt"));
            else Slot(1, null, Loc.T("Invite un ami"));
            if (_duo != null && !leader) _states[1].text = _ready ? "<color=#E8C35A>" + Loc.T("Prêt") + "</color>" : Loc.T("Pas prêt");

            bool mine = _duo != null && mate != null;
            _readyButton.gameObject.SetActive(mine && !leader);
            _readyLabel.text = Loc.T(_ready ? "Prêt ✓" : "Je suis prêt");
            _readyButton.interactable = !_busy;
            _coop.gameObject.SetActive(mine && leader);
            _relay.gameObject.SetActive(mine && leader);
            bool canLaunch = mine && leader && MateReady && !_busy;
            _coop.interactable = canLaunch;
            _relay.interactable = canLaunch;
            _leave.gameObject.SetActive(mine);
            _leave.interactable = !_busy;
        }

        void Slot(int i, string playerId, string state)
        {
            string key = playerId ?? "";
            if (playerId == null)
            {
                _portraits[i].enabled = false;
                _names[i].text = "—"; // noloc
            }
            else
            {
                _portraits[i].enabled = true;
                var look = playerId == Me ? App.Save.Loadout : PvpSkins.Loadout(_duo?.LookOf(playerId));
                key += "|" + look.Key; // noloc
                if (_shown[i] != key) MummyAnimator.Show(_portraits[i], App.Art, look);
                int n = _duo?.Members.IndexOf(playerId) ?? -1;
                _names[i].text = playerId == Me ? App.Online.PlayerName : n >= 0 && n < _duo.Names.Count ? _duo.Names[n] : "?";
            }
            _shown[i] = key;
            _states[i].text = state;
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

        // ------------------------------------------------------------------ actions

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
            _info.text = r?.Ok == true ? (accept ? Loc.F("Groupe formé avec {0} !", invite.FromName) : "") : TeamView.ErrorText(r?.Error);
            if (r?.Ok == true && accept) App.Audio.Play(Sfx.Coin);
            Reload();
        }

        async void ToggleReady()
        {
            if (_busy || _duo == null) return;
            _busy = true;
            bool ready = !_ready;
            var r = await App.Pvp.SetGroupReadyAsync(_duo.Id, ready, MyLook());
            if (this == null) return;
            _busy = false;
            if (r?.Duo != null && r.Error == null) { _ready = ready; _duo = r.Duo; App.SetGroup(_duo); }
            else _info.text = TeamView.ErrorText(r?.Error);
            FillSlots();
        }

        async void Launch(string mode)
        {
            if (_busy || _duo == null) return;
            _busy = true;
            FillSlots();
            var r = await App.Pvp.LaunchGroupAsync(_duo.Id, mode);
            if (this == null) return;
            _busy = false;
            if (r?.Duo == null || r.Error != null)
            {
                _info.text = r?.Error == "NOT_READY" ? Loc.F("{0} n'est pas prêt.", _duo.Names.ElementAtOrDefault(1) ?? "?") : TeamView.ErrorText(r?.Error);
                if (r?.Duo != null) _duo = r.Duo;
                FillSlots();
                return;
            }
            _duo = r.Duo;
            App.SetGroup(_duo);
            _seenLaunch = _duo.LaunchAtUnixMs;
            Go(mode);
        }

        void Leave()
        {
            var duo = _duo;
            if (duo == null) return;
            Router.Open<ConfirmDialog>().Configure(Loc.F("Quitter le groupe {0} ?", duo.Name),
                "Son Elo 2v2 et son bilan seront perdus.", "Quitter", async () =>
                {
                    var r = await App.Pvp.LeaveDuoAsync(duo.Id);
                    if (r == null || !r.Ok) return TeamView.ErrorText(r?.Error);
                    if (this != null) { _ready = false; Reload(); }
                    return null;
                });
        }
    }
}
