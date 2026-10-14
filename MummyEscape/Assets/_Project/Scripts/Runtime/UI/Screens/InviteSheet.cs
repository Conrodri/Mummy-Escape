using System.Collections.Generic;
using System.Linq;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The party invite sheet, sliding up over the home screen: the duo invitations received (accept forms the group)
    /// and the friends to invite, online ones first. Invitations are sent and accepted here only.
    /// </summary>
    public sealed class InviteSheet : UIScreen
    {
        public override bool IsModal => true;

        /// <summary>Name of the friend invited during this app session; local only, the server does not return sent invites.</summary>
        public static string PendingInvite;

        const float Height = 1400, SlideSeconds = 0.22f;

        RectTransform _panel, _list;
        Text _info;
        bool _busy;
        int _request;
        float _slide;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            _panel = UIKit.Card(Root, 40, 16);
            UIKit.FitInParent(UIKit.Place(_panel, 0.5f, 0f, 1040, Height), 0);
            _panel.GetComponent<Image>().raycastTarget = true;

            UIKit.Size(UIKit.Title(_panel, "Inviter un ami", 60), 90);
            _info = UIKit.Label(_panel, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 56);
            _list = UIKit.Scroll(_panel, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;
            UIKit.Size(UIKit.Button(_panel, "Fermer", Close, UIKit.TextSize, ButtonStyle.Leave), 80);
        }

        public override void OnShow()
        {
            _busy = false;
            _slide = 0;
            _info.text = "";
            UIKit.ClearChildren(_list);
            Reload();
        }

        void Update()
        {
            if (_slide >= 1) return;
            _slide = Mathf.Min(1, _slide + Time.unscaledDeltaTime / SlideSeconds);
            float t = 1 - (1 - _slide) * (1 - _slide);
            _panel.anchoredPosition = new Vector2(0, -Height * (1 - t));
        }

        void Close() => Router.Close(this);

        async void Reload()
        {
            if (App.Pvp == null)
            {
                _info.text = Loc.T("Le groupe se joue en ligne : active le mode en ligne (Paramètres › Confidentialité).");
                return;
            }
            int request = ++_request;
            _info.text = Loc.T("Chargement…");
            var teams = await App.Pvp.GetTeamsAsync();
            var friends = await App.Online.GetFriendsAsync();
            if (request != _request || this == null) return;
            if (teams == null || teams.Error != null)
            {
                _info.text = TeamView.ErrorText(teams?.Error);
                return;
            }
            if (!_busy) _info.text = "";
            Fill(teams, friends);
        }

        void Fill(TeamsResponse teams, IReadOnlyList<FriendInfo> friends)
        {
            UIKit.ClearChildren(_list);
            if (teams.Invites.Count > 0)
            {
                UIKit.SectionTitle(_list, "Invitations");
                foreach (var invite in teams.Invites) InviteRow(invite);
            }
            UIKit.SectionTitle(_list, "Amis");
            var sorted = (friends ?? new List<FriendInfo>()).OrderBy(f => f.Online ? 0 : 1).ToList(); // stable: keeps the server order within each group
            foreach (var f in sorted) FriendRow(f);
            if (sorted.Count == 0) UIKit.Size(UIKit.Label(_list, "Ajoute des amis (Social › Amis) pour les inviter.", 26, UIKit.Dim), 60);
        }

        void InviteRow(DuoInvite invite)
        {
            UIKit.ListItem(_list, 110, null, out var h);
            var name = UIKit.Label(h.transform, Loc.F("{0} veut faire équipe", invite.FromName), 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            UIKit.Size(UIKit.Button(h.transform, "Refuser", () => Respond(invite, false), 26, ButtonStyle.Leave), 80, 170);
            UIKit.Size(UIKit.Button(h.transform, "Accepter", () => Respond(invite, true), 26, ButtonStyle.Primary), 80, 190);
        }

        void FriendRow(FriendInfo f)
        {
            UIKit.ListItem(_list, 100, null, out var h);
            var name = UIKit.Label(h.transform, f.Name, 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            if (f.Online)
            {
                var online = UIKit.Label(h.transform, "En ligne", 22, UIKit.Success, TextAnchor.MiddleRight);
                UIKit.Size(online, -1, 130);
            }
            UIKit.Size(UIKit.Button(h.transform, "Inviter", () => Invite(f), 26, ButtonStyle.Primary), 76, 180);
        }

        async void Invite(FriendInfo f)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.InviteDuoAsync(f.PlayerId, App.Online.PlayerName);
            if (this == null) return;
            _busy = false;
            if (r?.Ok == true) PendingInvite = f.Name;
            _info.text = r?.Ok == true ? Loc.F("Invitation envoyée à {0}.", f.Name) : TeamView.ErrorText(r?.Error);
        }

        async void Respond(DuoInvite invite, bool accept)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.RespondDuoAsync(invite.Id, accept, App.Online.PlayerName);
            if (this == null) return;
            if (r?.Ok != true) { _busy = false; _info.text = TeamView.ErrorText(r?.Error); return; }
            if (!accept) { _busy = false; Reload(); return; }
            App.Audio.Play(Sfx.Coin);
            var teams = await App.Pvp.GetTeamsAsync();
            if (this == null) return;
            _busy = false;
            if (teams?.Error != null) { _info.text = TeamView.ErrorText(teams.Error); return; }
            App.Session.SetGroup(teams?.Duos.FirstOrDefault());
            PendingInvite = null;
            Close();
        }
    }
}
