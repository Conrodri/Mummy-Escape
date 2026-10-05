using System.Collections.Generic;
using System.Linq;
using MummyEscape.Pvp;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The 2v2: duos formed with friends (invitations sent and received), each with its own 2v2 Elo; a battle is three
    /// rounds on three tombs, the order chosen before it starts and alternating (the first runner runs rounds 1 and 3),
    /// the first duo to two wins takes it. Rounds are run whenever each player wants, within 24 hours.
    /// </summary>
    public sealed class DuoScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Duel;

        RectTransform _list;
        ScrollRect _scroll;
        Text _info;
        TeamsResponse _teams;
        IReadOnlyList<Online.FriendInfo> _friends;
        bool _busy;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("2v2");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _info = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 64);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
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
        }

        void Fill()
        {
            UIKit.ClearChildren(_list);
            var rules = UIKit.Label(_list, "3 manches sur 3 tombeaux. L'ordre alterne : le premier coureur fait les manches 1 et 3, son partenaire la 2. Le premier duo à 2 victoires gagne. 24 h pour courir.", 24, UIKit.Dim);
            UIKit.FitText(rules, 16);
            UIKit.Size(rules, 100);
            if (_teams == null) return;

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
                var hint = UIKit.Label(card.transform, "Qui court les manches 1 et 3 ?", 24, UIKit.Dim);
                UIKit.Size(hint, 36);
                var row = UIKit.Row(card.transform, 96, 14);
                var me = UIKit.Button(row.transform, "Moi", () => Find(duo, true), 30, ButtonStyle.Primary);
                UIKit.Size(me, -1, 0, 1);
                var other = UIKit.Button(row.transform, partner, () => Find(duo, false), 30, ButtonStyle.Primary);
                UIKit.FitText(other.GetComponentInChildren<Text>(), 18);
                UIKit.Size(other, -1, 0, 1);
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

        async void Find(Duo duo, bool meFirst)
        {
            if (_busy) return;
            _busy = true;
            _info.text = Loc.T("Recherche d'un duo adverse…");
            var r = await App.Pvp.FindDuoMatchAsync(duo.Id, meFirst);
            if (this == null) return;
            _busy = false;
            _info.text = r?.Ok == true ? (r.Battle?.B == null ? Loc.T("Combat créé : il attend un duo adverse.") : Loc.T("Adversaires trouvés !")) : TeamView.ErrorText(r?.Error);
            Reload();
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
