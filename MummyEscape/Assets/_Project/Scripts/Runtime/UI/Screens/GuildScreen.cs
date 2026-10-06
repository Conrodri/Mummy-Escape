using System.Collections.Generic;
using System.Linq;
using MummyEscape.Pvp;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Guilds: create one (500 scarabs) or join one, then its members and roles, its points (every duel, 2v2 and war
    /// won by a member) and the guild skins they unlock, and its wars: the leader or an officer picks the size (3, 5 or
    /// 10 rounds) and the running order, the rival guild does the same, and the guild with more round wins takes it.
    /// </summary>
    public sealed class GuildScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;

        RectTransform _list;
        ScrollRect _scroll;
        Text _info;
        GuildResponse _guild;
        GuildSearchResponse _search;
        InputField _name, _tag, _query;
        readonly List<string> _order = new List<string>();
        int _size = TeamConfig.WarSizes[0];
        bool _busy;
        UIKit.Segmented _social;
        int _request;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Guilde");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _social = FriendsScreen.Social(body, Router, 1);
            _info = UIKit.Label(body, "", 28, UIKit.Sand);
            UIKit.FitText(_info, 18);
            UIKit.Size(_info, 64);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            App.Lighting.SetMood(false);
            _social.Select(1);
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
            if (_guild == null) _info.text = Loc.T("Chargement…");
            var guild = await App.Pvp.GetGuildAsync();
            if (request != _request || this == null) return;
            if (guild == null || guild.Error != null)
            {
                _info.text = TeamView.ErrorText(guild?.Error);
                return;
            }
            Take(guild);
            if (guild.Guild == null && _search == null) _search = await App.Pvp.SearchGuildsAsync("", 30);
            if (request != _request || this == null) return;
            if (!_busy && guild.NewRewards.Count == 0)
                _info.text = App.Pvp.IsDemo ? "<color=#E8C35A>" + Loc.T("Démo hors ligne : membres et guildes adverses simulés") + "</color>" : "";
            Fill();
        }

        /// <summary>Keeps the answer and hands over the guild skins it just unlocked.</summary>
        void Take(GuildResponse guild)
        {
            _guild = guild;
            if (guild.NewRewards.Count == 0) return;
            App.Save.GrantSkins(guild.NewRewards);
            var names = guild.NewRewards.Select(id => Loc.T(SkinCatalog.Get(id).Name));
            _info.text = Loc.F("Débloqué : {0}", string.Join(", ", names)) + "\n<size=22>" + Loc.T("À porter depuis la Boutique") + "</size>";
            App.Audio.Play(Services.Sfx.Win);
        }

        void Fill()
        {
            UIKit.ClearChildren(_list);
            if (_guild == null) return;
            if (_guild.Guild == null) FillNoGuild();
            else FillGuild(_guild.Guild);
        }

        // ------------------------------------------------------------------ without a guild

        void FillNoGuild()
        {
            UIKit.SectionTitle(_list, "Fonder une guilde");
            _name = UIKit.Input(_list, "Nom de la guilde");
            _name.characterLimit = TeamConfig.GuildNameMax;
            _tag = UIKit.Input(_list, "Tag (2 à 4 lettres)");
            _tag.characterLimit = TeamConfig.GuildTagMax;
            bool afford = App.Save.Data.Coins >= TeamConfig.GuildCreationScarabs;
            var create = UIKit.Button(_list, Loc.F("Fonder ({0} scarabées)", TeamConfig.GuildCreationScarabs), Create, 32, ButtonStyle.Primary);
            UIKit.Size(create, 96);
            create.interactable = afford;
            if (!afford)
            {
                var poor = UIKit.Label(_list, Loc.F("Il te faut {0} scarabées (tu en as {1}).", TeamConfig.GuildCreationScarabs, App.Save.Data.Coins), 24, UIKit.Dim);
                UIKit.Size(poor, 40);
            }

            UIKit.SectionTitle(_list, "Rejoindre une guilde");
            var row = UIKit.Row(_list, 96, 12);
            _query = UIKit.Input(row.transform, "Nom ou tag");
            UIKit.Size(_query, 96, 0, 1);
            var go = UIKit.IconButton(row.transform, UISprites.Next, Search, 96, ButtonStyle.Primary);
            UIKit.Size(go, 96, 96, 0);
            var guilds = _search?.Guilds ?? new List<GuildSummary>();
            if (guilds.Count == 0)
                UIKit.Size(UIKit.Label(_list, _search == null ? "" : Loc.T("Aucune guilde trouvée."), 26, UIKit.Dim), 50);
            foreach (var g in guilds) GuildRow(g);

            var board = UIKit.Button(_list, "Classement des guildes", () => Router.Open<TeamLeaderboardScreen>().Show(true), 30);
            UIKit.Size(board, 88);
        }

        void GuildRow(GuildSummary g)
        {
            UIKit.ListItem(_list, 116, null, out var h);
            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 0, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(col, $"[{g.Tag}] {g.Name}", 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold); // noloc
            UIKit.FitText(name, 18);
            UIKit.Size(name, 46);
            var sub = UIKit.Label(col, Loc.F("{0}/{1} membres", g.MemberCount, TeamConfig.GuildMaxMembers) + "  ·  " + Loc.F("{0} points", g.Points), 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.Size(sub, 32);
            var join = UIKit.Button(h.transform, "Rejoindre", () => Join(g), 26, ButtonStyle.Primary);
            UIKit.Size(join, 80, 200);
            join.interactable = g.MemberCount < TeamConfig.GuildMaxMembers;
        }

        async void Create()
        {
            if (_busy) return;
            if (App.Save.Data.Coins < TeamConfig.GuildCreationScarabs) return;
            _busy = true;
            _info.text = Loc.T("Fondation…");
            var r = await App.Pvp.CreateGuildAsync(_name.text.Trim(), _tag.text.Trim(), App.Online.PlayerName);
            if (this == null) return;
            _busy = false;
            if (r?.Guild == null)
            {
                _info.text = TeamView.ErrorText(r?.Error);
                return;
            }
            // The scarabs live in the save: paid once the server has the guild.
            App.Save.SpendCoins(TeamConfig.GuildCreationScarabs);
            _info.text = Loc.F("Guilde {0} fondée !", r.Guild.Name);
            App.Audio.Play(Services.Sfx.Win);
            Take(r);
            Fill();
        }

        async void Search()
        {
            if (_busy) return;
            _busy = true;
            _search = await App.Pvp.SearchGuildsAsync(_query.text, 30);
            if (this == null) return;
            _busy = false;
            string query = _query.text;
            Fill();
            _query.text = query;
        }

        async void Join(GuildSummary g)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.JoinGuildAsync(g.Id, App.Online.PlayerName);
            if (this == null) return;
            _busy = false;
            if (r?.Guild == null)
            {
                _info.text = TeamView.ErrorText(r?.Error);
                return;
            }
            _info.text = Loc.F("Bienvenue dans {0} !", r.Guild.Name);
            Take(r);
            Fill();
        }

        // ------------------------------------------------------------------ in a guild

        void FillGuild(Guild g)
        {
            var me = g.Member(_guild.Me);
            bool boss = me != null && me.Role >= GuildRole.Officer;

            // Identity and record.
            var card = UIKit.Panel(_list, "Guild"); // noloc
            card.raycastTarget = false;
            var col = UIKit.Column(card.transform, 6);
            col.padding = new RectOffset(26, 26, 22, 22);
            var title = UIKit.Title(card.transform, $"[{g.Tag}] {g.Name}", 46); // noloc
            UIKit.FitText(title, 24);
            UIKit.Size(title, 64);
            var points = UIKit.Label(card.transform, Loc.F("{0} points de guilde", g.Points), 32, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(points, 46);
            var stats = UIKit.Label(card.transform, Loc.F("{0}/{1} membres", g.Members.Count, TeamConfig.GuildMaxMembers) + "  ·  "
                + Loc.F("Elo de guerre {0}", g.WarElo) + "  ·  " + Loc.F("{0} V · {1} N · {2} D", g.WarWins, g.WarDraws, g.WarLosses), 24, UIKit.Dim);
            UIKit.FitText(stats, 16);
            UIKit.Size(stats, 36);

            // The guild's channel in the chat.
            var talk = UIKit.Button(_list, "Tchat de guilde", () => Router.Open<ChatScreen>().OpenGuild(), 32, ButtonStyle.Primary);
            UIKit.Size(talk, 92);

            // Guild skins.
            UIKit.SectionTitle(_list, "Skins de guilde");
            var how = UIKit.Label(_list, Loc.F("Chaque victoire d'un membre rapporte des points : duel +{0}, 2v2 +{1}, manche de guerre +{2}, guerre gagnée +{3} par manche.",
                TeamConfig.PointsPerDuelWin, TeamConfig.PointsPerDuoWin, TeamConfig.PointsPerWarRaceWin, TeamConfig.WarWinBonusPerSlot), 22, UIKit.Dim);
            UIKit.FitText(how, 16);
            UIKit.Size(how, 64);
            foreach (var (need, reward) in TeamConfig.GuildSkinTiers) SkinTier(g, need, reward);

            // Wars.
            UIKit.SectionTitle(_list, "Guerre de guildes");
            var active = _guild.Wars.FirstOrDefault(w => w.Id == g.ActiveWar);
            if (active != null) TeamView.BattleCard(_list, active, _guild.Me, Router, RunRound);
            else if (boss) WarLauncher(g);
            else UIKit.Size(UIKit.Label(_list, "Le chef et les officiers lancent les guerres et choisissent l'ordre de passage.", 26, UIKit.Dim), 70);
            foreach (var w in _guild.Wars)
                if (w != active) TeamView.BattleCard(_list, w, _guild.Me, Router, null);

            // Members.
            UIKit.SectionTitle(_list, "Membres");
            foreach (var m in g.Members.OrderByDescending(x => x.Role).ThenByDescending(x => x.Points)) MemberRow(g, m, me);

            var board = UIKit.Button(_list, "Classement des guildes", () => Router.Open<TeamLeaderboardScreen>().Show(true), 30);
            UIKit.Size(board, 88);
            var leave = UIKit.Button(_list, "Quitter la guilde", Leave, 28, ButtonStyle.Danger);
            UIKit.Size(leave, 84);
        }

        void SkinTier(Guild g, int need, string reward)
        {
            var def = SkinCatalog.Get(reward);
            bool unlocked = g.Points >= need;
            UIKit.ListItem(_list, 112, null, out var h, unlocked);
            var portrait = ItemPreview.Create(h.transform, def);
            UIKit.Size(portrait, 92, 92, 0);
            var name = UIKit.Label(h.transform, Loc.T(def.Name), 30, unlocked ? UIKit.Gold : UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, -1, 0, 1);
            var state = UIKit.Label(h.transform, unlocked ? Loc.T("Débloqué") : Loc.F("{0}/{1} pts", g.Points, need), 26, unlocked ? UIKit.Turquoise : UIKit.Dim, TextAnchor.MiddleRight);
            UIKit.Size(state, -1, 200, 0);
        }

        /// <summary>The leader or an officer: the war's size, then its running order, member by member.</summary>
        void WarLauncher(Guild g)
        {
            var sizes = new UIKit.Segmented(_list, TeamConfig.WarSizes.Select(s => s + "v" + s).ToArray(), i => // noloc
            {
                _size = TeamConfig.WarSizes[i];
                if (_order.Count > _size) _order.RemoveRange(_size, _order.Count - _size);
                Fill();
            }, 84);
            sizes.Select(System.Array.IndexOf(TeamConfig.WarSizes, _size));
            _order.RemoveAll(id => g.Member(id) == null);

            bool enough = g.Members.Count >= _size;
            var hint = UIKit.Label(_list, !enough ? Loc.F("Il faut {0} membres pour une guerre en {0}v{0}.", _size)
                                                  : Loc.F("Touche les coureurs dans l'ordre de passage ({0}/{1}).", _order.Count, _size), 24, enough ? UIKit.Sand : UIKit.Danger);
            UIKit.FitText(hint, 16);
            UIKit.Size(hint, 44);
            foreach (var m in g.Members.OrderByDescending(x => x.Points))
            {
                var member = m;
                int at = _order.IndexOf(m.PlayerId);
                UIKit.ListItem(_list, 84, () => ToggleRunner(member), out var h, at >= 0);
                var n = UIKit.Title(h.transform, at >= 0 ? (at + 1).ToString() : "·", 36, at >= 0 ? UIKit.Gold : UIKit.Dim); // noloc
                UIKit.Size(n, -1, 60, 0);
                var name = UIKit.Label(h.transform, m.Name, 28, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.FitText(name, 18);
                UIKit.Size(name, -1, 0, 1);
            }
            var start = UIKit.Button(_list, Loc.F("Lancer la guerre {0}v{0}", _size), StartWar, 34, ButtonStyle.Primary);
            UIKit.Size(start, 100);
            start.interactable = _order.Count == _size;
        }

        void ToggleRunner(GuildMember m)
        {
            if (!_order.Remove(m.PlayerId) && _order.Count < _size) _order.Add(m.PlayerId);
            float at = _scroll.verticalNormalizedPosition;
            Fill();
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = at;
        }

        async void StartWar()
        {
            if (_busy || _order.Count != _size) return;
            _busy = true;
            _info.text = Loc.T("Recherche d'une guilde adverse…");
            var r = await App.Pvp.StartWarAsync(_size, new List<string>(_order));
            if (this == null) return;
            _busy = false;
            if (r?.Guild == null)
            {
                _info.text = TeamView.ErrorText(r?.Error);
                return;
            }
            _order.Clear();
            var war = r.Wars.FirstOrDefault(w => w.Id == r.Guild.ActiveWar);
            _info.text = war?.B == null ? Loc.T("Guerre déclarée : elle attend une guilde adverse.") : Loc.F("Guerre contre {0} !", war.B.TeamName);
            Take(r);
            Fill();
        }

        void MemberRow(Guild g, GuildMember m, GuildMember me)
        {
            bool self = m.PlayerId == _guild.Me;
            UIKit.ListItem(_list, 100, null, out var h, self);
            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 0, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(col, m.Name, 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 18);
            UIKit.Size(name, 44);
            string role = m.Role == GuildRole.Leader ? Loc.T("Chef") : m.Role == GuildRole.Officer ? Loc.T("Officier") : Loc.T("Membre");
            var sub = UIKit.Label(col, role + "  ·  " + Loc.F("{0} points", m.Points), 22, m.Role == GuildRole.Member ? UIKit.Dim : UIKit.Gold, TextAnchor.MiddleLeft);
            UIKit.Size(sub, 30);

            if (me == null || self) return;
            if (me.Role == GuildRole.Leader)
            {
                bool officer = m.Role == GuildRole.Officer;
                var promote = UIKit.Button(h.transform, officer ? "Rétrograder" : "Officier", () => SetRole(m, !officer), 22);
                UIKit.FitText(promote.GetComponentInChildren<Text>(), 14);
                UIKit.Size(promote, 72, 190);
            }
            if (me.Role >= GuildRole.Officer && m.Role < me.Role)
                UIKit.IconButton(h.transform, UISprites.Close, () => Kick(m), 64, ButtonStyle.Danger);
        }

        async void SetRole(GuildMember m, bool officer)
        {
            if (_busy) return;
            _busy = true;
            var r = await App.Pvp.SetGuildRoleAsync(m.PlayerId, officer);
            if (this == null) return;
            _busy = false;
            if (r?.Guild == null) _info.text = TeamView.ErrorText(r?.Error);
            else { Take(r); Fill(); }
        }

        void Kick(GuildMember m)
        {
            Router.Open<ConfirmDialog>().Configure(Loc.F("Exclure {0} ?", m.Name), "Ses points restent à la guilde.", "Exclure", async () =>
            {
                var r = await App.Pvp.KickGuildMemberAsync(m.PlayerId);
                if (r?.Guild == null) return TeamView.ErrorText(r?.Error);
                if (this != null) { Take(r); Fill(); }
                return null;
            });
        }

        void Leave()
        {
            var g = _guild?.Guild;
            if (g == null) return;
            Router.Open<ConfirmDialog>().Configure(Loc.F("Quitter {0} ?", g.Name),
                "Tes skins de guilde restent à toi. Fonder une autre guilde coûte 500 scarabées.", "Quitter", async () =>
                {
                    var r = await App.Pvp.LeaveGuildAsync();
                    if (r == null || r.Error != null) return TeamView.ErrorText(r?.Error);
                    if (this != null)
                    {
                        _guild = r;
                        _search = null;
                        Reload();
                    }
                    return null;
                });
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
