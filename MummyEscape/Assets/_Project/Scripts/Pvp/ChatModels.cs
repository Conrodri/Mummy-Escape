// Mummy Rush — le tchat (global, de guilde, privé entre amis), les replays partagés et l'historique 2v2 : ce que le jeu
// et le serveur s'échangent.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public static class ChatConfig
    {
        public const string Global = "global";
        public const string Guild = "guild";
        /// <summary>Préfixe d'une conversation privée : "dm:" + l'identifiant de l'autre joueur.</summary>
        public const string DirectPrefix = "dm:";

        public const int MaxLength = 200;
        /// <summary>Messages gardés par canal (les plus anciens s'effacent).</summary>
        public const int ChannelSize = 100;
        /// <summary>Messages renvoyés au plus par lecture.</summary>
        public const int PageSize = 60;
        public const int MinGapMs = 1500;
        public const int MaxPerMinute = 12;
        /// <summary>Joueurs différents qui signalent un message pour qu'il soit masqué en attendant un humain.</summary>
        public const int ReportersToHide = 3;
        public const int MaxReportsPerDay = 10;
        public const int MaxConversations = 50;
        public const int MaxBlocked = 200;
        public const int RelayHistorySize = 10;

        public const string DuelReplay = "duel";
        public const string RelayReplay = "relay";

        public static string Direct(string otherId) => DirectPrefix + otherId;
    }

    /// <summary>Un replay joint à un message : le jeu le charge à la demande (<see cref="SharedReplayResponse"/>).</summary>
    [Serializable]
    public class ChatReplayRef
    {
        public string Id;
        /// <summary><see cref="ChatConfig.DuelReplay"/> ou <see cref="ChatConfig.RelayReplay"/>.</summary>
        public string Kind;
        /// <summary>« Toutânkhamon contre Néfertiti », « Duo A contre Duo B ».</summary>
        public string Title;
        public DuelResult Result;
    }

    [Serializable]
    public class ChatMessage
    {
        public long Seq;
        public string From;
        public string FromName;
        public long AtUnixMs;
        public string Text;
        /// <summary>Null pour un message sans replay.</summary>
        public ChatReplayRef Replay;
        /// <summary>Masqué après des signalements (en attente d'un humain).</summary>
        public bool Hidden;
        public List<string> Reporters = new List<string>();
    }

    /// <summary>Un canal tel que le serveur le garde (les derniers <see cref="ChatConfig.ChannelSize"/> messages).</summary>
    [Serializable]
    public class ChatChannel
    {
        public string Key;
        public long NextSeq = 1;
        public List<ChatMessage> Messages = new List<ChatMessage>();
    }

    [Serializable]
    public class ChatConversation
    {
        public string Other;
        public string OtherName;
        public long LastSeq;
        public long LastAtUnixMs;
        public string LastFrom;
        public string LastText;
    }

    /// <summary>Ce que le serveur garde du tchat d'un joueur : ses conversations privées, qui il bloque, son débit.</summary>
    [Serializable]
    public class ChatInbox
    {
        public List<ChatConversation> Conversations = new List<ChatConversation>();
        public List<string> Blocked = new List<string>();
        public long LastSentUnixMs;
        public long MinuteStartUnixMs;
        public int SentThisMinute;
        public int ReportsToday;
        public string ReportsDay;
        /// <summary>Tchat coupé jusqu'à cet instant (posé à la main depuis le Dashboard après examen des signalements).</summary>
        public long BannedUntilUnixMs;
        /// <summary>Les canaux où le joueur a écrit (clés de stockage) : de quoi effacer ou exporter ses messages.</summary>
        public List<string> Posted = new List<string>();
        /// <summary>Les replays qu'il a partagés.</summary>
        public List<string> SharedReplays = new List<string>();
        /// <summary>Ses amis, envoyés par son jeu : seuls eux peuvent lui écrire en privé.</summary>
        public List<string> Contacts = new List<string>();
        /// <summary>Mineur d'après son choix de confidentialité : le tchat global lui est fermé.</summary>
        public bool Minor;
    }

    [Serializable]
    public class ChatPage
    {
        public string Channel;
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public long LastSeq;
        public string Error;
    }

    [Serializable]
    public class ChatSendResponse
    {
        public bool Ok;
        public ChatMessage Message;
        public string Error;
    }

    [Serializable]
    public class ChatInboxResponse
    {
        public List<ChatConversation> Conversations = new List<ChatConversation>();
        public List<string> Blocked = new List<string>();
        public long GlobalLastSeq;
        /// <summary>Null hors guilde.</summary>
        public string GuildId;
        public string GuildName;
        public long GuildLastSeq;
        public long BannedUntilUnixMs;
        public string Error;
    }

    /// <summary>Un replay partagé : copié par le serveur depuis ses propres données au moment du partage.</summary>
    [Serializable]
    public class SharedReplay
    {
        public string Id;
        public string Kind;
        public string OwnerId;
        public string OwnerName;
        public long SharedAtUnixMs;
        public DuelRecord Duel;
        public RelayMatch Relay;
    }

    [Serializable]
    public class SharedReplayResponse
    {
        public SharedReplay Replay;
        public string Error;
    }

    /// <summary>Les messages signalés d'un joueur, pour qu'un humain les examine (Cloud Save › pvp_chat_reports).</summary>
    [Serializable]
    public class ChatDossier
    {
        public string PlayerId;
        public string PlayerName;
        public int TotalReports;
        public long LastReportUnixMs;
        public List<ChatReportEntry> Reports = new List<ChatReportEntry>();
    }

    [Serializable]
    public class ChatReportEntry
    {
        public string Channel;
        public long Seq;
        public string Text;
        public string ReporterId;
        public long ReportedAtUnixMs;
    }

    /// <summary>Les derniers matchs 2v2 d'un joueur, les deux relais de chacun (les replays).</summary>
    [Serializable]
    public class RelayHistory
    {
        public List<RelayMatch> Matches = new List<RelayMatch>();
    }

    [Serializable]
    public class RelayHistoryResponse
    {
        public List<RelayMatch> Matches = new List<RelayMatch>();
        public string Error;
    }
}
