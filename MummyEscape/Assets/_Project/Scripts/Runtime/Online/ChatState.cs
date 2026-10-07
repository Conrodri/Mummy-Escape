using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// The chat as the phone follows it: the last inbox from the server, what the player has read (per channel, on the
    /// device), and whether the chat is switched on. The global channel is closed to minors.
    /// </summary>
    public static class ChatState
    {
        const string EnabledKey = "chat_enabled"; // noloc
        const string ReadPrefix = "chat_read_"; // noloc
        const string BlockedNamePrefix = "chat_blocked_name_"; // noloc

        public static ChatInboxResponse Inbox { get; private set; }
        public static event Action Changed;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>The global channel: not for players under the age of digital consent, even with their parents' consent.</summary>
        public static bool GlobalAllowed(App.GameApp app) => !app.Privacy.Data.IsMinor;

        static long ReadSeq(string channel) => long.TryParse(PlayerPrefs.GetString(ReadPrefix + channel, "0"), out var n) ? n : 0;

        public static void MarkRead(string channel, long seq)
        {
            if (string.IsNullOrEmpty(channel) || seq <= ReadSeq(channel)) return;
            PlayerPrefs.SetString(ReadPrefix + channel, seq.ToString());
            Changed?.Invoke();
        }

        /// <summary>A private message from this friend not read yet.</summary>
        public static bool Unread(ChatConversation c, string me) =>
            c != null && c.LastFrom != me && c.LastSeq > ReadSeq(ChatConfig.Direct(c.Other));

        public static bool GuildUnread => Inbox?.GuildId != null && Inbox.GuildLastSeq > ReadSeq(ChatConfig.Guild + "_" + Inbox.GuildId);

        /// <summary>Something new for the player: a private message, or the guild's channel (the global one never lights up).</summary>
        public static bool AnyUnread(string me)
        {
            if (!Enabled || Inbox == null) return false;
            if (GuildUnread) return true;
            foreach (var c in Inbox.Conversations) if (Unread(c, me)) return true;
            return false;
        }

        /// <summary>The read marker of a channel: the guild's is per guild (a new guild starts unread).</summary>
        public static string ReadKey(string channel) => channel == ChatConfig.Guild && Inbox?.GuildId != null ? ChatConfig.Guild + "_" + Inbox.GuildId : channel;

        public static async Task<ChatInboxResponse> RefreshAsync(IPvpService pvp)
        {
            if (pvp == null || !Enabled) return Inbox;
            var inbox = await pvp.GetChatInboxAsync();
            if (inbox != null && inbox.Error == null)
            {
                Inbox = inbox;
                Changed?.Invoke();
            }
            return Inbox;
        }

        public static bool IsBlocked(string playerId) => Inbox?.Blocked != null && Inbox.Blocked.Contains(playerId);

        /// <summary>The name of a blocked player, remembered on the device (the server keeps the identifier only).</summary>
        public static string BlockedName(string playerId) => PlayerPrefs.GetString(BlockedNamePrefix + playerId, "");

        public static void SetBlocked(string playerId, bool blocked, string name = null)
        {
            if (blocked && !string.IsNullOrEmpty(name)) PlayerPrefs.SetString(BlockedNamePrefix + playerId, name);
            if (Inbox == null) return;
            Inbox.Blocked.Remove(playerId);
            if (blocked) Inbox.Blocked.Add(playerId);
            Changed?.Invoke();
        }

        static string _syncedProfile;
        static string _registeredName;

        /// <summary>The friend-code directory learns the player's public name: once per session, and again after a rename.</summary>
        public static async Task RegisterNameAsync(App.GameApp app)
        {
            var pvp = app.Pvp;
            string name = app.Online.PlayerName;
            if (pvp == null || !app.Online.IsAvailable || string.IsNullOrEmpty(name)) return;
            string signature = app.Online.PlayerId + "|" + name;
            if (signature == _registeredName) return;
            var r = await pvp.RegisterNameAsync(name);
            if (r != null && r.Ok) _registeredName = signature;
        }

        /// <summary>
        /// The server lets only friends write privately and keeps minors out of the global channel: it is told the friends
        /// and the age choice once per session, and again whenever they change.
        /// </summary>
        public static async Task SyncProfileAsync(App.GameApp app, IReadOnlyList<FriendInfo> friends = null)
        {
            _ = RegisterNameAsync(app);
            var pvp = app.Pvp;
            if (pvp == null || !Enabled) return;
            friends ??= await app.Online.GetFriendsAsync();
            if (friends == null) return;
            var ids = friends.Select(f => f.PlayerId).Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
            bool minor = app.Privacy.Data.IsMinor;
            string signature = app.Online.PlayerId + "|" + minor + "|" + string.Join(",", ids);
            if (signature == _syncedProfile) return;
            var synced = await pvp.SyncChatProfileAsync(ids, minor);
            if (synced != null && synced.Ok) _syncedProfile = signature;
        }
    }
}
