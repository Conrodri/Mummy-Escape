using System;
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
    }
}
