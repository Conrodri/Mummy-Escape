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
    /// The party invite sheet, sliding up over the home screen: the search for a teammate (2v2 duel or campaign, with
    /// another player searching the same; the longest searching leads), the duo invitations received (accept forms the
    /// group) and the friends to invite, online ones first. Invitations are sent and accepted here only.
    /// </summary>
    public sealed class InviteSheet : UIScreen
    {
        public override bool IsModal => true;

        /// <summary>Name of the friend invited during this app session; local only, the server does not return sent invites.</summary>
        public static string PendingInvite;

        const float Height = 1400, SlideSeconds = 0.22f, SearchPollSeconds = 3f;

        RectTransform _panel, _list;
        HorizontalLayoutGroup _modes, _searching;
        Text _info, _searchText;
        bool _busy, _polling;
        int _request;
        float _slide, _searchSince, _nextSearch;
        /// <summary>The mode searched for (<see cref="GroupLaunch"/>), null when not searching.</summary>
        string _searchMode;

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

            UIKit.SectionTitle(_panel, "Chercher un coéquipier");
            _modes = UIKit.Row(_panel, 90, 16);
            UIKit.Size(UIKit.Button(_modes.transform, "Duel 2v2", () => Search(GroupLaunch.Relay), 28, ButtonStyle.Primary), 90, -1, 1);
            UIKit.Size(UIKit.Button(_modes.transform, "Campagne 2v2", () => Search(GroupLaunch.Coop), 28, ButtonStyle.Primary), 90, -1, 1);
            _searching = UIKit.Row(_panel, 90, 16);
            _searchText = UIKit.Label(_searching.transform, "", 28, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_searchText, 18);
            UIKit.Size(_searchText, -1, 0, 1);
            UIKit.Size(UIKit.Button(_searching.transform, "Annuler", StopSearch, 26, ButtonStyle.Leave), 80, 190);

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
            _searchMode = null;
            ShowSearch();
            UIKit.ClearChildren(_list);
            Reload();
        }

        public override void OnHide() => StopSearch();

        void Update()
        {
            if (_searchMode != null)
            {
                int s = (int)(Time.unscaledTime - _searchSince);
                _searchText.text = Loc.F("Recherche : {0}… {1}", _searchMode == GroupLaunch.Relay ? Loc.T("Duel 2v2") : Loc.T("Campagne 2v2"), $"{s / 60}:{s % 60:00}"); // noloc
                if (!_polling && Time.unscaledTime >= _nextSearch) PollSearch();
            }
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

        void ShowSearch()
        {
            _modes.gameObject.SetActive(_searchMode == null);
            _searching.gameObject.SetActive(_searchMode != null);
        }

        void Search(string mode)
        {
            if (App.Pvp == null) return;
            if (App.Session.Group != null) { _info.text = Loc.T("Tu es déjà dans un groupe."); return; }
            _searchMode = mode;
            _searchSince = Time.unscaledTime;
            _nextSearch = 0;
            ShowSearch();
        }

        void StopSearch()
        {
            if (_searchMode == null) return;
            _searchMode = null;
            ShowSearch();
            _ = App.Pvp?.CancelMateSearchAsync();
        }

        /// <summary>Asks the server again; once a teammate is found, the group opens (its leader launches the mode).</summary>
        async void PollSearch()
        {
            _polling = true;
            _nextSearch = Time.unscaledTime + SearchPollSeconds;
            string mode = _searchMode;
            var r = await App.Pvp.SearchMateAsync(mode, App.Online.PlayerName);
            if (this == null) return;
            _polling = false;
            if (_searchMode != mode) return; // cancelled meanwhile
            if (r == null || r.Error != null) { StopSearch(); _info.text = TeamView.ErrorText(r?.Error); return; }
            if (r.Duo == null) return;
            _searchMode = null;
            App.Audio.Play(Sfx.Coin);
            App.Session.SetGroup(r.Duo);
            Close();
            Router.Open<GroupScreen>();
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
