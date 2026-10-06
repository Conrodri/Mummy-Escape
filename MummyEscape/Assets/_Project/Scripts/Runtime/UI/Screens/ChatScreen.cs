using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The chat: the global channel (not for minors), the guild's, and private conversations with friends. Messages are
    /// read again every few seconds while a channel is open. A message from someone else can be reported, its author
    /// blocked or written to privately (a friend); a shared replay opens from its message.
    /// </summary>
    public sealed class ChatScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Friends;
        /// <summary>A lively channel is read every 4 s; a quiet one less and less often, down to every 15 s.</summary>
        const float PollSeconds = 4f, QuietPollSeconds = 15f;
        float _pollGap = PollSeconds;

        UIKit.Segmented _tabs;
        RectTransform _list, _composer, _conversation, _menu;
        ScrollRect _scroll;
        Text _info, _conversationTitle, _menuTitle;
        InputField _input;
        Button _send, _menuDirect, _menuBlock;
        Toggle _enabled;
        string _channel, _directName;
        long _lastSeq;
        float _nextPoll;
        int _request;
        bool _polling, _sending;
        ChatMessage _target;
        IReadOnlyList<FriendInfo> _friends = new List<FriendInfo>();

        string Me => App.Online.PlayerId;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Tchat");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 14);
            _tabs = new UIKit.Segmented(body, new[] { "Global", "Guilde", "Amis" }, SelectTab);

            // A private conversation: back to the friends, the friend's name, block.
            _conversation = UIKit.Rect("Conversation", body); // noloc
            var row = UIKit.Row(_conversation, 80, 12);
            UIKit.Stretch((RectTransform)row.transform);
            UIKit.Size(_conversation, 80);
            UIKit.IconButton(row.transform, UISprites.Back, ShowFriends, 72);
            _conversationTitle = UIKit.Label(row.transform, "", 34, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_conversationTitle, 20);
            UIKit.Size(_conversationTitle, -1, 0, 1);
            var block = UIKit.Button(row.transform, "Bloquer", () => AskBlock(_channel?.Substring(ChatConfig.DirectPrefix.Length), _directName), 26, ButtonStyle.Danger);
            UIKit.Size(block, 72, 200, 0);

            _info = UIKit.Label(body, "", 26, UIKit.Dim);
            UIKit.FitText(_info, 16);
            UIKit.Size(_info, 44);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 10;

            _composer = UIKit.Rect("Composer", body); // noloc
            UIKit.Size(_composer, 100);
            var compose = UIKit.Row(_composer, 100, 12);
            UIKit.Stretch((RectTransform)compose.transform);
            _input = UIKit.Input(compose.transform, "Écris un message…", 32);
            _input.characterLimit = ChatConfig.MaxLength;
            _input.lineType = InputField.LineType.SingleLine;
            _input.onSubmit.AddListener(_ => Send());
            UIKit.Size(_input, -1, 0, 1);
            _send = UIKit.IconButton(compose.transform, UISprites.Send, Send, 96, ButtonStyle.Primary);

            _enabled = UIKit.Toggle(body, "Tchat activé", ChatState.Enabled, on => { ChatState.Enabled = on; Refresh(); });
            UIKit.Size(_enabled, 64);

            BuildMenu();
        }

        /// <summary>What can be done with someone else's message.</summary>
        void BuildMenu()
        {
            _menu = UIKit.Rect("Menu", Root); // noloc
            UIKit.Stretch(_menu, -600, -600, -600, -600);
            var shade = UIKit.Image(_menu, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(CloseMenu);
            var card = UIKit.Card(_menu, 40, 18);
            UIKit.FitInParent(UIKit.Place(card, 0.5f, 0.5f, 820, 0));
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _menuTitle = UIKit.Label(card, "", 36, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_menuTitle, 20);
            UIKit.Size(_menuTitle, 70);
            _menuDirect = UIKit.Button(card, "Message privé", () => { var t = _target; CloseMenu(); OpenDirect(t.From, t.FromName); }, 32);
            UIKit.Size(_menuDirect, 92);
            var report = UIKit.Button(card, "Signaler ce message", () => { var t = _target; CloseMenu(); AskReport(t); }, 32);
            UIKit.Size(report, 92);
            _menuBlock = UIKit.Button(card, "Bloquer", () => { var t = _target; CloseMenu(); AskBlock(t.From, t.FromName); }, 32, ButtonStyle.Danger);
            UIKit.Size(_menuBlock, 92);
            UIKit.Size(UIKit.Button(card, "Annuler", CloseMenu, 30, ButtonStyle.Ghost), 80);
            _menu.gameObject.SetActive(false);
        }

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            App.Lighting.SetMood(false);
            _enabled.SetIsOnWithoutNotify(ChatState.Enabled);
            CloseMenu();
            _request++;
            LoadFriends();
            _ = ChatState.RefreshAsync(App.Pvp);
            if (_channel == null) SelectTab(0);
            else Refresh();
        }

        public override void OnHide()
        {
            _request++;
            CloseMenu();
        }

        async void LoadFriends()
        {
            var friends = await App.Online.GetFriendsAsync();
            if (this == null || friends == null) return;
            _friends = friends;
            _ = ChatState.SyncProfileAsync(App, friends);
            if (_tabs.Selected == 2 && _channel == null) ShowFriends();
        }

        bool IsFriend(string playerId) => _friends.Any(f => f.PlayerId == playerId);

        // ------------------------------------------------------------------ channels

        /// <summary>Opens a private conversation (from the friends screen or a message).</summary>
        public void OpenDirect(string playerId, string name)
        {
            _tabs.Select(2);
            _directName = name;
            SetChannel(ChatConfig.Direct(playerId));
        }

        /// <summary>Opens the guild's channel (from the guild screen).</summary>
        public void OpenGuild()
        {
            _tabs.Select(1);
            SetChannel(ChatConfig.Guild);
        }

        void SelectTab(int i)
        {
            _tabs.Select(i);
            if (i == 0) SetChannel(ChatConfig.Global);
            else if (i == 1) SetChannel(ChatConfig.Guild);
            else ShowFriends();
        }

        void SetChannel(string channel)
        {
            _channel = channel;
            _lastSeq = 0;
            _request++;
            UIKit.ClearChildren(_list);
            Refresh();
        }

        /// <summary>The state of the screen for the channel: who may write, what to say when nothing can be shown.</summary>
        void Refresh()
        {
            bool direct = _channel != null && _channel.StartsWith(ChatConfig.DirectPrefix);
            _conversation.gameObject.SetActive(direct);
            if (direct) _conversationTitle.text = _directName ?? "";
            string blocker = Blocker();
            _info.text = blocker ?? "";
            bool open = blocker == null && _channel != null;
            _composer.gameObject.SetActive(open);
            if (!open && _channel != null) UIKit.ClearChildren(_list);
            if (open) { _nextPoll = 0f; _pollGap = PollSeconds; }
        }

        /// <summary>Why the channel cannot be used, or null.</summary>
        string Blocker()
        {
            if (App.Pvp == null) return Loc.T("Le tchat se joue en ligne : active le mode en ligne (Paramètres › Confidentialité).");
            if (!ChatState.Enabled) return Loc.T("Le tchat est désactivé.");
            if (_channel == ChatConfig.Global && !ChatState.GlobalAllowed(App)) return Loc.T("Le tchat global n'est pas ouvert aux joueurs mineurs. Discute avec ta guilde et tes amis.");
            var banned = ChatState.Inbox?.BannedUntilUnixMs ?? 0;
            if (banned > System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return Loc.F("Ton tchat est suspendu jusqu'au {0}.", System.DateTimeOffset.FromUnixTimeMilliseconds(banned).ToLocalTime().ToString("dd/MM HH:mm")); // noloc
            return null;
        }

        void Update()
        {
            if (_channel == null || !_composer.gameObject.activeSelf || _polling) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + _pollGap;
            Poll();
        }

        async void Poll()
        {
            _polling = true;
            int request = _request;
            string channel = _channel;
            var page = await App.Pvp.GetChatAsync(channel, _lastSeq);
            _polling = false;
            if (this == null || request != _request || channel != _channel) return;
            if (page == null || page.Error != null)
            {
                _info.text = page?.Error == "NO_ACCESS" && channel == ChatConfig.Guild ? Loc.T("Rejoins une guilde pour discuter avec elle.") // noloc
                           : page?.Error == "NO_ACCESS" ? Loc.T("Conversation indisponible.") // noloc
                           : Loc.T("Connexion au tchat impossible.");
                if (page?.Error == "NO_ACCESS") _composer.gameObject.SetActive(false); // noloc
                return;
            }
            _pollGap = page.Messages.Count > 0 ? PollSeconds : Mathf.Min(QuietPollSeconds, _pollGap * 1.5f);
            _info.text = _lastSeq == 0 && page.Messages.Count == 0 ? Loc.T("Aucun message pour l'instant. Lance la conversation !") : "";
            bool atBottom = _scroll.verticalNormalizedPosition < 0.05f || _lastSeq == 0;
            long after = _lastSeq;
            _lastSeq = System.Math.Max(_lastSeq, page.LastSeq);
            foreach (var m in page.Messages)
                if (m.Seq > after && !ChatState.IsBlocked(m.From)) Row(m);
            ChatState.MarkRead(ChatState.ReadKey(channel), page.LastSeq);
            if (atBottom && page.Messages.Count > 0) StartCoroutine(ScrollDown());
        }

        System.Collections.IEnumerator ScrollDown()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        // ------------------------------------------------------------------ messages

        void Row(ChatMessage m)
        {
            bool mine = m.From == Me;
            var bubble = UIKit.Plate(_list, mine ? new Color(0.35f, 0.26f, 0.08f, 0.55f) : new Color(0, 0, 0, 0.35f), 22, null, false, "Message"); // noloc
            var col = UIKit.Column(bubble.transform, 4);
            col.padding = new RectOffset(22, 22, 12, 14);
            bubble.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (!mine)
            {
                bubble.raycastTarget = true;
                var tap = bubble.gameObject.AddComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => OpenMenu(m));
            }

            string when = System.DateTimeOffset.FromUnixTimeMilliseconds(m.AtUnixMs).ToLocalTime().ToString("HH:mm"); // noloc
            var head = UIKit.Label(bubble.transform, "", 24, mine ? UIKit.Gold : UIKit.Turquoise, TextAnchor.MiddleLeft, FontStyle.Bold);
            head.text = (m.FromName ?? "?") + "  <color=#9C8B70><size=20>" + when + "</size></color>"; // noloc
            if (!string.IsNullOrEmpty(m.Text))
            {
                var text = UIKit.Label(bubble.transform, "", 28, UIKit.Sand, TextAnchor.UpperLeft);
                text.supportRichText = false;
                text.text = m.Text;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            if (m.Replay != null) ReplayCard(bubble.transform, m.Replay);
        }

        void ReplayCard(Transform parent, ChatReplayRef replay)
        {
            var color = replay.Result == DuelResult.Win ? UIKit.Gold : replay.Result == DuelResult.Draw ? UIKit.Sand : UIKit.Danger;
            var button = UIKit.Button(parent, "", () => OpenShared(App, Router, replay), 26);
            UIKit.Size(button, 88);
            var label = UIKit.Label(button.transform, "", 26, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(label.rectTransform, 18, 6, 18, 6);
            label.supportRichText = true;
            string kind = replay.Kind == ChatConfig.RelayReplay ? Loc.T("Replay 2v2") : Loc.T("Replay de duel");
            label.text = "▶  " + kind + " · <color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + (replay.Title ?? "") + "</color>"; // noloc
            UIKit.FitText(label, 16);
        }

        /// <summary>Loads a replay shared in the chat and opens it.</summary>
        public static async void OpenShared(App.GameApp app, UIRouter router, ChatReplayRef replay)
        {
            if (app.Pvp == null || replay == null) return;
            var r = await app.Pvp.GetSharedReplayAsync(replay.Id);
            var shared = r?.Replay;
            if (shared == null) { app.Audio.Play(Sfx.Bump); return; }
            if (shared.Duel != null)
            {
                var d = shared.Duel;
                Online.ReplayStore.Normalize(d);
                string verdict = (d.Me?.PlayerName ?? shared.OwnerName) + " · " + Loc.T(d.Result == DuelResult.Win ? "Victoire" : d.Result == DuelResult.Draw ? "Match nul" : "Défaite");
                router.Open<ReplayScreen>().ShowRound(d, d.Me?.PlayerName ?? "?", d.Rival?.PlayerName ?? "?", verdict);
            }
            else if (shared.Relay != null)
            {
                var m = shared.Relay;
                var mine = m.SideOf(shared.OwnerId);
                var record = new RelayRecord
                {
                    Me = shared.OwnerId, Match = m, Resolved = true, PlayedAtUnixMs = m.CreatedAtUnixMs,
                    Result = mine == m.A ? m.Result : DuelResolver.Invert(m.Result),
                    EloDelta = mine == m.A ? m.EloDeltaA : m.EloDeltaB,
                };
                router.Open<ReplayScreen>().ShowRelay(record, true);
            }
        }

        async void Send()
        {
            if (_sending || _channel == null || App.Pvp == null) return;
            string text = _input.text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _sending = true;
            _send.interactable = false;
            string channel = _channel;
            var r = await App.Pvp.SendChatAsync(channel, text, App.Online.PlayerName);
            _sending = false;
            if (this == null) return;
            _send.interactable = true;
            if (r == null || !r.Ok)
            {
                _info.text = SendError(r?.Error);
                return;
            }
            _input.text = "";
            App.Audio.Play(Sfx.Click);
            if (channel == _channel) { _nextPoll = 0f; _pollGap = PollSeconds; _scroll.verticalNormalizedPosition = 0f; }
        }

        public static string SendError(string code)
        {
            switch (code)
            {
                case "TOO_FAST": return Loc.T("Doucement : attends un instant avant le message suivant."); // noloc
                case "BANNED": return Loc.T("Ton tchat est suspendu."); // noloc
                case "BLOCKED": return Loc.T("Ce joueur ne reçoit pas tes messages."); // noloc
                case "NOT_FRIEND": return Loc.T("Les messages privés sont réservés aux amis : ce joueur ne t'a pas (encore) dans les siens."); // noloc
                case "NO_ACCESS": return Loc.T("Conversation indisponible."); // noloc
                case "UNKNOWN_REPLAY": return Loc.T("Ce replay n'est pas encore sur le serveur (match non jugé)."); // noloc
                case "EMPTY": return ""; // noloc
                default: return Loc.T("Envoi impossible : vérifie ta connexion.");
            }
        }

        // ------------------------------------------------------------------ friends

        /// <summary>The friends, those who wrote first, each with its conversation.</summary>
        void ShowFriends()
        {
            _channel = null;
            _request++;
            _conversation.gameObject.SetActive(false);
            _composer.gameObject.SetActive(false);
            UIKit.ClearChildren(_list);
            string blocker = App.Pvp == null || !ChatState.Enabled ? Blocker() : null;
            _info.text = blocker ?? "";
            if (blocker != null) return;
            var conversations = ChatState.Inbox?.Conversations ?? new List<ChatConversation>();
            var shown = new HashSet<string>();
            foreach (var c in conversations)
            {
                // Private messages come from friends only.
                var friend = _friends.FirstOrDefault(f => f.PlayerId == c.Other);
                if (friend == null) continue;
                shown.Add(c.Other);
                FriendRow(friend.PlayerId, friend.Name, c);
            }
            foreach (var f in _friends)
                if (!shown.Contains(f.PlayerId)) FriendRow(f.PlayerId, f.Name, null);
            if (_friends.Count == 0) _info.text = Loc.T("Ajoute des amis (onglet Amis) pour leur écrire.");
            BlockedList();
        }

        /// <summary>The players the player blocked, each to unblock.</summary>
        void BlockedList()
        {
            var blocked = ChatState.Inbox?.Blocked;
            if (blocked == null || blocked.Count == 0) return;
            UIKit.SectionTitle(_list, "Joueurs bloqués");
            foreach (var id in blocked.ToList())
            {
                string playerId = id;
                UIKit.ListItem(_list, 96, null, out var h);
                var name = UIKit.Label(h.transform, "", 28, UIKit.Dim, TextAnchor.MiddleLeft);
                string known = ChatState.BlockedName(playerId);
                name.text = string.IsNullOrEmpty(known) ? Loc.T("Joueur bloqué") : known;
                UIKit.FitText(name, 16);
                UIKit.Size(name, -1, 0, 1);
                var unblock = UIKit.Button(h.transform, "Débloquer", async () =>
                {
                    var r = await App.Pvp.BlockChatAsync(playerId, false);
                    if (r == null || !r.Ok || this == null) return;
                    ChatState.SetBlocked(playerId, false);
                    ShowFriends();
                }, 24);
                UIKit.Size(unblock, 72, 220, 0);
            }
        }

        void FriendRow(string id, string name, ChatConversation c)
        {
            UIKit.ListItem(_list, 116, () => OpenDirect(id, name), out var h);
            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 2, 0, TextAnchor.MiddleLeft);
            bool unread = ChatState.Unread(c, Me);
            var title = UIKit.Label(col, "", 30, unread ? UIKit.Gold : UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.text = name + (unread ? "  ●" : ""); // noloc
            UIKit.FitText(title, 18);
            UIKit.Size(title, 44);
            var last = UIKit.Label(col, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            last.supportRichText = false;
            last.text = c == null ? Loc.T("Pas encore de message") : (c.LastFrom == Me ? Loc.T("Toi : ") : "") + c.LastText;
            UIKit.FitText(last, 16);
            UIKit.Size(last, 34);
            UIKit.IconButton(h.transform, UISprites.Chat, () => OpenDirect(id, name), 80);
        }

        // ------------------------------------------------------------------ report, block

        void OpenMenu(ChatMessage m)
        {
            _target = m;
            _menuTitle.text = m.FromName ?? "?";
            _menuDirect.gameObject.SetActive(IsFriend(m.From) && _channel != ChatConfig.Direct(m.From));
            _menu.gameObject.SetActive(true);
            _menu.SetAsLastSibling();
        }

        void CloseMenu()
        {
            if (_menu != null) _menu.gameObject.SetActive(false);
        }

        void AskReport(ChatMessage m)
        {
            if (m == null) return;
            string channel = _channel;
            Router.Open<ConfirmDialog>().Configure("Signaler ce message ?",
                "Le message sera transmis à l'équipe qui l'examinera. Signalé par plusieurs joueurs, il est masqué en attendant.",
                "Signaler", async () =>
                {
                    var r = await App.Pvp.ReportChatAsync(channel, m.Seq);
                    if (r == null || r.Error == PvpServiceFactory.NetworkError) return Loc.T("Envoi impossible : vérifie ta connexion.");
                    if (r.Error == "LIMIT") return Loc.T("Trop de signalements aujourd'hui."); // noloc
                    if (r.Error != null) return Loc.T("Ce message n'est plus sur le serveur.");
                    return null;
                });
        }

        void AskBlock(string playerId, string name)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            Router.Open<ConfirmDialog>().Configure(Loc.F("Bloquer {0} ?", name ?? "?"),
                "Tu ne verras plus ses messages et il ne pourra plus t'écrire. Tu peux le débloquer en bas de l'onglet Amis du tchat.",
                "Bloquer", async () =>
                {
                    var r = await App.Pvp.BlockChatAsync(playerId, true);
                    if (r == null || !r.Ok) return Loc.T("Envoi impossible : vérifie ta connexion.");
                    ChatState.SetBlocked(playerId, true, name);
                    if (this != null)
                    {
                        if (_channel == ChatConfig.Direct(playerId)) ShowFriends();
                        else SetChannel(_channel);
                    }
                    return null;
                });
        }
    }

    /// <summary>Shares one of the player's replays: in the global channel, the guild's, or with a friend, with a word.</summary>
    public sealed class ShareReplayDialog : UIScreen
    {
        public override bool IsModal => true;

        RectTransform _targets;
        InputField _comment;
        Text _status;
        string _kind, _matchId;
        bool _busy;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.85f), true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            var panel = UIKit.Card(Root, 40, 18);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(940, 0);
            UIKit.FitInParent(panel);
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.Size(UIKit.Label(panel, "Partager le replay", 46, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), 90);
            _comment = UIKit.Input(panel, "Un mot pour l'accompagner (facultatif)", 30);
            _comment.characterLimit = ChatConfig.MaxLength;
            _targets = UIKit.Rect("Targets", panel); // noloc
            UIKit.Column(_targets, 12);
            _status = UIKit.Label(panel, "", 28, UIKit.Sand);
            UIKit.FitText(_status, 16);
            UIKit.Size(_status, 60);
            UIKit.Size(UIKit.Button(panel, "Fermer", () => Router.Close(this), 30, ButtonStyle.Ghost), 80);
        }

        public async void Configure(string kind, string matchId)
        {
            _kind = kind;
            _matchId = matchId;
            _comment.text = "";
            _status.text = "";
            UIKit.ClearChildren(_targets);
            if (App.Pvp == null || !ChatState.Enabled)
            {
                _status.text = App.Pvp == null ? Loc.T("Le tchat se joue en ligne.") : Loc.T("Le tchat est désactivé.");
                return;
            }
            if (ChatState.GlobalAllowed(App)) Target("Tchat global", ChatConfig.Global);
            var inbox = await ChatState.RefreshAsync(App.Pvp);
            var friends = await App.Online.GetFriendsAsync();
            if (this == null || _matchId != matchId) return;
            if (inbox?.GuildId != null) Target(Loc.F("Tchat de guilde · {0}", inbox.GuildName ?? ""), ChatConfig.Guild);
            foreach (var f in (friends ?? new List<FriendInfo>()).Take(12)) Target(Loc.F("À {0}", f.Name), ChatConfig.Direct(f.PlayerId));
        }

        void Target(string label, string channel)
        {
            var b = UIKit.Button(_targets, label, () => Share(channel), 30);
            UIKit.FitText(b.GetComponentInChildren<Text>(), 18);
            UIKit.Size(b, 84);
        }

        async void Share(string channel)
        {
            if (_busy) return;
            _busy = true;
            _status.text = Loc.T("Envoi…");
            var r = await App.Pvp.ShareReplayAsync(_kind, _matchId, channel, _comment.text, App.Online.PlayerName);
            _busy = false;
            if (this == null) return;
            if (r == null || !r.Ok)
            {
                _status.text = ChatScreen.SendError(r?.Error);
                return;
            }
            App.Audio.Play(Sfx.Coin);
            _status.text = Loc.T("Replay partagé !");
            await Task.Delay(700);
            if (this != null) Router.Close(this);
        }
    }
}
