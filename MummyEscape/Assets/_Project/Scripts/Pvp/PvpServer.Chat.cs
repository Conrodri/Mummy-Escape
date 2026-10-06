// Mummy Rush — côté serveur du tchat : un canal global, un par guilde, un par paire d'amis. Chaque canal garde ses derniers
// messages ; le jeu les relit toutes les quelques secondes tant que le tchat est ouvert. Les replays partagés sont copiés
// depuis les données du serveur (jamais depuis le jeu), et l'historique 2v2 est rangé ici quand un match est jugé.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    public sealed partial class PvpServer
    {
        public const string ChatCollection = "pvp_chat";
        public const string ChatInboxCollection = "pvp_chat_inbox";
        public const string ChatReportsCollection = "pvp_chat_reports";
        public const string SharedReplaysCollection = "pvp_shared_replays";
        public const string RelayHistoryCollection = "pvp_relay_history";

        // ------------------------------------------------------------------ channels

        /// <summary>
        /// La clé de stockage d'un canal pour ce joueur, null s'il n'y a pas accès : "global", la guilde du joueur, ou la
        /// conversation privée avec un autre joueur (la même clé des deux côtés).
        /// </summary>
        async Task<(string Key, string GuildId)> ChannelKeyAsync(string me, string channel)
        {
            if (channel == ChatConfig.Global) return ("global", null);
            if (channel == ChatConfig.Guild)
            {
                var d = await Update(me);
                if (string.IsNullOrEmpty(d.GuildId)) return (null, null);
                var guild = await Shared<Guild>(GuildsCollection, d.GuildId);
                return guild?.Member(me) == null ? (null, null) : ("guild_" + guild.Id, guild.Id);
            }
            if (channel != null && channel.StartsWith(ChatConfig.DirectPrefix, StringComparison.Ordinal))
            {
                string other = channel.Substring(ChatConfig.DirectPrefix.Length);
                if (string.IsNullOrEmpty(other) || other == me || other.Length > 64) return (null, null);
                return (string.CompareOrdinal(me, other) < 0 ? "dm_" + me + "_" + other : "dm_" + other + "_" + me, null);
            }
            return (null, null);
        }

        Task<ChatInbox> Inbox(string playerId, Func<ChatInbox, ChatInbox> mutate = null) =>
            Shared<ChatInbox>(ChatInboxCollection, playerId, mutate == null ? (Func<ChatInbox, ChatInbox>)null : x => mutate(x ?? new ChatInbox()));

        /// <summary>Les messages d'un canal après <paramref name="afterSeq"/> (les derniers d'une page au plus), sans ceux des joueurs bloqués.</summary>
        public async Task<ChatPage> GetChatAsync(string me, string channel, long afterSeq)
        {
            var (key, _) = await ChannelKeyAsync(me, channel);
            if (key == null) return new ChatPage { Channel = channel, Error = "NO_ACCESS" };
            var room = await Shared<ChatChannel>(ChatCollection, key);
            var blocked = new HashSet<string>((await Inbox(me))?.Blocked ?? new List<string>());
            var page = new ChatPage { Channel = channel, LastSeq = room == null ? 0 : room.NextSeq - 1 };
            if (room == null) return page;
            page.Messages = room.Messages
                .Where(m => m.Seq > afterSeq && !m.Hidden && !blocked.Contains(m.From))
                .OrderBy(m => m.Seq)
                .ToList();
            if (page.Messages.Count > ChatConfig.PageSize) page.Messages = page.Messages.Skip(page.Messages.Count - ChatConfig.PageSize).ToList();
            // Les signalements des autres ne regardent personne.
            page.Messages = page.Messages.Select(PublicCopy).ToList();
            return page;
        }

        static ChatMessage PublicCopy(ChatMessage m) => new ChatMessage
        {
            Seq = m.Seq, From = m.From, FromName = m.FromName, AtUnixMs = m.AtUnixMs, Text = m.Text, Replay = m.Replay,
        };

        /// <summary>
        /// Envoie un message (nettoyé : balises, liens et grossièretés masqués). Refusé si le joueur écrit trop vite, est
        /// suspendu, ou si son correspondant l'a bloqué.
        /// </summary>
        public Task<ChatSendResponse> SendChatAsync(string me, string playerName, string channel, string text) =>
            PostAsync(me, playerName, channel, text, null);

        async Task<ChatSendResponse> PostAsync(string me, string playerName, string channel, string text, ChatReplayRef replay)
        {
            string clean = ChatFilter.Clean(text);
            if (clean.Length == 0 && replay == null) return new ChatSendResponse { Error = "EMPTY" };
            var (key, _) = await ChannelKeyAsync(me, channel);
            if (key == null) return new ChatSendResponse { Error = "NO_ACCESS" };
            string other = channel.StartsWith(ChatConfig.DirectPrefix, StringComparison.Ordinal) ? channel.Substring(ChatConfig.DirectPrefix.Length) : null;
            if (other != null && ((await Inbox(other))?.Blocked?.Contains(me) ?? false)) return new ChatSendResponse { Error = "BLOCKED" };

            // Le débit : un message toutes les 1,5 s, une douzaine par minute.
            long now = NowMs;
            string refused = null;
            await Inbox(me, box =>
            {
                if (box.BannedUntilUnixMs > now) { refused = "BANNED"; return box; }
                if (now - box.LastSentUnixMs < ChatConfig.MinGapMs) { refused = "TOO_FAST"; return box; }
                if (now - box.MinuteStartUnixMs >= 60_000) { box.MinuteStartUnixMs = now; box.SentThisMinute = 0; }
                if (box.SentThisMinute >= ChatConfig.MaxPerMinute) { refused = "TOO_FAST"; return box; }
                box.SentThisMinute++;
                box.LastSentUnixMs = now;
                if (!box.Posted.Contains(key)) box.Posted.Add(key);
                return box;
            });
            if (refused != null) return new ChatSendResponse { Error = refused };

            string name = ChatFilter.Name(playerName);
            ChatMessage posted = null;
            await Shared<ChatChannel>(ChatCollection, key, room =>
            {
                room = room ?? new ChatChannel { Key = key };
                posted = new ChatMessage { Seq = room.NextSeq++, From = me, FromName = name, AtUnixMs = now, Text = clean, Replay = replay };
                room.Messages.Add(posted);
                if (room.Messages.Count > ChatConfig.ChannelSize) room.Messages.RemoveRange(0, room.Messages.Count - ChatConfig.ChannelSize);
                return room;
            });

            if (other != null)
            {
                string preview = replay != null ? "▶ " + replay.Title : clean;
                await Converse(me, other, null, posted, preview);
                await Converse(other, me, name, posted, preview);
            }
            return new ChatSendResponse { Ok = true, Message = PublicCopy(posted) };
        }

        /// <summary>Range la conversation avec <paramref name="other"/> en tête de la liste de <paramref name="owner"/>.</summary>
        Task Converse(string owner, string other, string otherName, ChatMessage m, string preview) =>
            Inbox(owner, box =>
            {
                var c = box.Conversations.Find(x => x.Other == other) ?? new ChatConversation { Other = other };
                box.Conversations.Remove(c);
                if (otherName != null) c.OtherName = otherName;
                c.LastSeq = m.Seq;
                c.LastAtUnixMs = m.AtUnixMs;
                c.LastFrom = m.From;
                c.LastText = preview.Length > 80 ? preview.Substring(0, 80) + "…" : preview;
                box.Conversations.Insert(0, c);
                if (box.Conversations.Count > ChatConfig.MaxConversations) box.Conversations.RemoveRange(ChatConfig.MaxConversations, box.Conversations.Count - ChatConfig.MaxConversations);
                return box;
            });

        /// <summary>Les conversations privées du joueur, les joueurs qu'il bloque, et le dernier message du global et de sa guilde.</summary>
        public async Task<ChatInboxResponse> GetChatInboxAsync(string me)
        {
            var box = await Inbox(me) ?? new ChatInbox();
            var response = new ChatInboxResponse
            {
                Conversations = box.Conversations.Where(c => !box.Blocked.Contains(c.Other)).ToList(),
                Blocked = box.Blocked,
                BannedUntilUnixMs = box.BannedUntilUnixMs > NowMs ? box.BannedUntilUnixMs : 0,
            };
            var global = await Shared<ChatChannel>(ChatCollection, "global");
            response.GlobalLastSeq = global == null ? 0 : global.NextSeq - 1;
            var (key, guildId) = await ChannelKeyAsync(me, ChatConfig.Guild);
            if (key != null)
            {
                response.GuildId = guildId;
                response.GuildName = (await Shared<Guild>(GuildsCollection, guildId))?.Name;
                var room = await Shared<ChatChannel>(ChatCollection, key);
                response.GuildLastSeq = room == null ? 0 : room.NextSeq - 1;
            }
            return response;
        }

        /// <summary>Bloque (ou débloque) un joueur : ses messages disparaissent pour ce joueur et il ne peut plus lui écrire.</summary>
        public async Task<ReportResponse> BlockChatAsync(string me, string playerId, bool block)
        {
            if (string.IsNullOrEmpty(playerId) || playerId == me) return new ReportResponse { Error = "UNKNOWN" };
            await Inbox(me, box =>
            {
                box.Blocked.Remove(playerId);
                if (block) box.Blocked.Add(playerId);
                if (box.Blocked.Count > ChatConfig.MaxBlocked) box.Blocked.RemoveAt(0);
                return box;
            });
            return new ReportResponse { Ok = true };
        }

        /// <summary>
        /// Signale un message : il est copié dans le dossier de son auteur (Cloud Save › pvp_chat_reports) pour un humain ;
        /// signalé par <see cref="ChatConfig.ReportersToHide"/> joueurs différents, il est masqué pour tous en attendant.
        /// </summary>
        public async Task<ReportResponse> ReportChatAsync(string me, string channel, long seq)
        {
            var (key, _) = await ChannelKeyAsync(me, channel);
            if (key == null) return new ReportResponse { Error = "NO_ACCESS" };
            string today = Seasons.DayOf(_utcNow());
            bool allowed = false;
            await Inbox(me, box =>
            {
                if (box.ReportsDay != today) { box.ReportsDay = today; box.ReportsToday = 0; }
                allowed = box.ReportsToday < ChatConfig.MaxReportsPerDay;
                if (allowed) box.ReportsToday++;
                return box;
            });
            if (!allowed) return new ReportResponse { Error = "LIMIT" };

            ChatMessage reported = null;
            await Shared<ChatChannel>(ChatCollection, key, room =>
            {
                var m = room?.Messages.Find(x => x.Seq == seq);
                if (m == null || m.From == me) return room;
                if (!m.Reporters.Contains(me)) m.Reporters.Add(me);
                if (m.Reporters.Count >= ChatConfig.ReportersToHide) m.Hidden = true;
                reported = m;
                return room;
            });
            if (reported == null) return new ReportResponse { Error = "UNKNOWN" };
            long now = NowMs;
            await Shared<ChatDossier>(ChatReportsCollection, reported.From, f =>
            {
                f = f ?? new ChatDossier { PlayerId = reported.From };
                f.PlayerName = reported.FromName;
                f.Reports.RemoveAll(r => r.Channel == key && r.Seq == seq && r.ReporterId == me);
                f.Reports.Add(new ChatReportEntry { Channel = key, Seq = seq, Text = reported.Text, ReporterId = me, ReportedAtUnixMs = now });
                if (f.Reports.Count > 50) f.Reports.RemoveRange(0, f.Reports.Count - 50);
                f.TotalReports++;
                f.LastReportUnixMs = now;
                return f;
            });
            return new ReportResponse { Ok = true };
        }

        // ------------------------------------------------------------------ shared replays

        /// <summary>
        /// Partage un replay du joueur dans un canal : un duel de son historique (résolu) ou un de ses matchs 2v2 jugés. Le
        /// serveur en garde une copie, que tout le monde peut ouvrir depuis le message.
        /// </summary>
        public async Task<ChatSendResponse> ShareReplayAsync(string me, string playerName, string kind, string matchId, string channel, string text)
        {
            var shared = new SharedReplay { Id = NewId(), Kind = kind, OwnerId = me, OwnerName = ChatFilter.Name(playerName), SharedAtUnixMs = NowMs };
            var replayRef = new ChatReplayRef { Id = shared.Id, Kind = kind };
            if (kind == ChatConfig.DuelReplay)
            {
                var duel = (await _store.UpdateHistoryAsync(me, null))?.Find(r => r.MatchId == matchId);
                if (duel == null || !duel.Resolved || duel.Me == null || duel.Rival == null) return new ChatSendResponse { Error = "UNKNOWN_REPLAY" };
                shared.Duel = duel;
                replayRef.Result = duel.Result;
                replayRef.Title = (duel.Me.PlayerName ?? shared.OwnerName) + " — " + (duel.Rival.PlayerName ?? "?");
            }
            else if (kind == ChatConfig.RelayReplay)
            {
                var relay = (await Shared<RelayHistory>(RelayHistoryCollection, me))?.Matches.Find(m => m.Id == matchId)
                            ?? await Shared<RelayMatch>(RelaysCollection, matchId);
                var mine = relay?.SideOf(me);
                if (relay == null || !relay.Settled || mine == null) return new ChatSendResponse { Error = "UNKNOWN_REPLAY" };
                shared.Relay = relay;
                replayRef.Result = mine == relay.A ? relay.Result : DuelResolver.Invert(relay.Result);
                replayRef.Title = mine.Name + " — " + relay.OtherSide(me).Name;
            }
            else return new ChatSendResponse { Error = "UNKNOWN_REPLAY" };

            var (key, _) = await ChannelKeyAsync(me, channel);
            if (key == null) return new ChatSendResponse { Error = "NO_ACCESS" };
            var posted = await PostAsync(me, playerName, channel, text, replayRef);
            if (!posted.Ok) return posted;
            await Shared<SharedReplay>(SharedReplaysCollection, shared.Id, _ => shared);
            await Inbox(me, box =>
            {
                box.SharedReplays.Add(shared.Id);
                return box;
            });
            return posted;
        }

        public async Task<SharedReplayResponse> GetSharedReplayAsync(string me, string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return new SharedReplayResponse { Error = "UNKNOWN_REPLAY" };
            var shared = await Shared<SharedReplay>(SharedReplaysCollection, id);
            return shared == null ? new SharedReplayResponse { Error = "UNKNOWN_REPLAY" } : new SharedReplayResponse { Replay = shared };
        }

        // ------------------------------------------------------------------ 2v2 history

        public async Task<RelayHistoryResponse> GetRelayHistoryAsync(string me) =>
            new RelayHistoryResponse { Matches = (await Shared<RelayHistory>(RelayHistoryCollection, me))?.Matches ?? new List<RelayMatch>() };

        /// <summary>Range un match jugé dans l'historique de chaque joueur réel. Le match est déjà compté : un échec n'y change rien.</summary>
        async Task RecordRelayAsync(RelayMatch match)
        {
            foreach (var side in new[] { match.A, match.B })
            {
                if (side == null || side.Bot) continue;
                foreach (var runner in side.Runners)
                {
                    try
                    {
                        await Shared<RelayHistory>(RelayHistoryCollection, runner.PlayerId, h =>
                        {
                            h = h ?? new RelayHistory();
                            h.Matches.RemoveAll(m => m == null || m.Id == match.Id);
                            h.Matches.Insert(0, match);
                            h.Matches = h.Matches.OrderByDescending(m => m.CreatedAtUnixMs).Take(ChatConfig.RelayHistorySize).ToList();
                            return h;
                        });
                    }
                    catch { /* replay perdu, verdict et Elo intacts */ }
                }
            }
        }
    }

    /// <summary>Le nettoyage des messages : une ligne, sans balises ni liens, grossièretés masquées.</summary>
    public static class ChatFilter
    {
        static readonly Regex Links = new Regex(@"(https?://|www\.)\S+|\b[\w-]+\.(com|net|org|fr|io|gg|ly|xyz|ru|be|tk)\b(/\S*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Spaces = new Regex(@"\s+");

        // Mots masqués (français et anglais), comparés sans accents ni majuscules, en début de mot.
        static readonly string[] Banned =
        {
            "connard", "connasse", "salope", "encule", "enculer", "pute", "putain", "batard", "fdp", "ntm", "nique", "niquer",
            "pd", "tapette", "negro", "negre", "bougnoule", "youpin", "suce", "bite", "couille", "merde", "abruti", "debile",
            "fuck", "fucking", "shit", "bitch", "cunt", "dick", "pussy", "whore", "slut", "nigger", "nigga", "faggot", "fag",
            "asshole", "bastard", "motherfucker", "kys",
        };

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsControl(c)) sb.Append(' ');
                // Pas de texte enrichi : les chevrons deviendraient des balises à l'écran.
                else if (c == '<') sb.Append('‹');
                else if (c == '>') sb.Append('›');
                else sb.Append(c);
            }
            string s = Spaces.Replace(sb.ToString(), " ").Trim();
            if (s.Length > ChatConfig.MaxLength) s = s.Substring(0, ChatConfig.MaxLength).TrimEnd();
            s = Links.Replace(s, "***");
            return MaskWords(s);
        }

        /// <summary>Le nom affiché : celui du profil, nettoyé et court.</summary>
        public static string Name(string name)
        {
            string s = Clean(name ?? "");
            if (s.Length > 24) s = s.Substring(0, 24);
            return s.Length == 0 ? "?" : s;
        }

        static string MaskWords(string s)
        {
            var words = Regex.Matches(s, @"[\p{L}\p{N}']+");
            if (words.Count == 0) return s;
            var chars = s.ToCharArray();
            foreach (Match w in words)
            {
                string plain = Plain(w.Value);
                foreach (var bad in Banned)
                {
                    bool hit = bad.Length <= 3 ? plain == bad : plain.StartsWith(bad, StringComparison.Ordinal);
                    if (!hit) continue;
                    for (int i = w.Index; i < w.Index + w.Length; i++) chars[i] = '*';
                    break;
                }
            }
            return new string(chars);
        }

        static string Plain(string word)
        {
            var normalized = word.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            // Les chiffres déguisés en lettres.
            return sb.ToString().Replace('0', 'o').Replace('1', 'i').Replace('3', 'e').Replace('4', 'a').Replace('5', 's').Replace('@', 'a');
        }
    }
}
