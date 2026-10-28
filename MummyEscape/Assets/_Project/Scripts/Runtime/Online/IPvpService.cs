using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Pvp;

namespace MummyEscape.Online
{
    /// <summary>
    /// Ghost duels: matchmaking, run submission, profile, season rewards and the seal shop. Every rule is decided by
    /// the server (<see cref="PvpServer"/>, in a Cloud Code module online); the game only shows what it answers.
    /// Calls never throw: a failure comes back as a response whose Error is "NETWORK".
    /// </summary>
    public interface IPvpService : IPvpDuels, IPvpTeams, IPvpLiveDuels, IPvpRelays, IPvpChat, IPvpDataRights, IPvpWallet, IPvpGuilds
    {
        /// <summary>Offline duels against simulated rivals (editor and development builds only).</summary>
        bool IsDemo { get; }
    }

    /// <summary>Ghost duels: matchmaking, run submission, profile, season rewards and the seal shop.</summary>
    public interface IPvpDuels
    {
        Task<PvpProfileResponse> GetProfileAsync();
        /// <summary>A rival of the player's league; <see cref="FindDuelResponse.Searching"/> while the server looks (ask again).</summary>
        Task<FindDuelResponse> FindDuelAsync(bool allowBots);
        Task<SubmitRunResponse> SubmitRunAsync(RunSubmission run, string playerName);
        Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync();
        /// <summary>The team's maintenance message and duel pause (null when the server cannot be reached).</summary>
        Task<ServiceStatus> GetStatusAsync();
        Task<SealPurchaseResponse> BuyWithSealsAsync(string itemId);
        /// <summary>A seal-wheel legendary bought outright (<see cref="WheelDef.DirectPrice"/>).</summary>
        Task<SealPurchaseResponse> BuySealLegendaryAsync(string id);
        /// <summary>One turn of the casino's seal wheel (paid and drawn by the server).</summary>
        /// <summary>Un tour de la roue des sceaux, ou <see cref="Casino.MultiSpins"/> (payés <see cref="Casino.MultiSpinsPaid"/>).</summary>
        Task<WheelSpinResponse> SpinSealWheelAsync(int count = 1);
        /// <summary>Top of the monthly Elo ranking, checked by the server: <paramref name="seasonsAgo"/> 0 = this month, 1 = last month.</summary>
        Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit);
        /// <summary>Publishes the solo star count, which unlocks the duels (<see cref="PvpConfig.RequiredSoloStars"/>). False if it was not saved.</summary>
        Task<bool> SyncSoloStarsAsync(int stars);
        /// <summary>The player's last duels (<see cref="PvpConfig.HistorySize"/>), both runs of each: the replays. Null on failure.</summary>
        Task<DuelHistoryResponse> GetHistoryAsync();
        /// <summary>Reports the rival of a duel of the history for cheating: the server files the whole duel for review.</summary>
        Task<ReportResponse> ReportCheatAsync(string matchId);

    }

    /// <summary>The duos of friends: group, 2v2 campaign, 2v2 battles.</summary>
    public interface IPvpTeams
    {
        Task<TeamsResponse> GetTeamsAsync();
        Task<TeamActionResponse> InviteDuoAsync(string friendId, string playerName);
        Task<TeamActionResponse> RespondDuoAsync(string inviteId, bool accept, string playerName);
        Task<TeamActionResponse> LeaveDuoAsync(string duoId);
        /// <summary>The group (the duo as a team of two): its state, my readiness, the leader's launch.</summary>
        Task<DuoResponse> GetDuoAsync(string duoId, PlayerLook look);
        Task<DuoResponse> SetGroupReadyAsync(string duoId, bool ready, PlayerLook look);
        Task<DuoResponse> LaunchGroupAsync(string duoId, string mode);
        /// <summary>Looks for a teammate for this mode (called again every few seconds while searching): the group once formed.</summary>
        Task<DuoResponse> SearchMateAsync(string mode, string playerName);
        Task<TeamActionResponse> CancelMateSearchAsync();
        /// <summary>Starts a 2v2 battle; <paramref name="meFirst"/>: the player runs rounds 1 and 3, the partner round 2.</summary>
        Task<TeamActionResponse> FindDuoMatchAsync(string duoId, bool meFirst);
        Task<DuoBoardResponse> GetDuoBoardAsync(int limit);
        /// <summary>2v2 campaign: a clear of the duo (the actions in the host's order), replayed by the server.</summary>
        Task<CoopClearResponse> SubmitCoopAsync(string duoId, int tomb, List<CoopRunInput> inputs, int timeMs);
        /// <summary>2v2 campaign ranking: the same duos, by stars then total time.</summary>
        Task<DuoBoardResponse> GetCoopBoardAsync(int limit);
        /// <summary>Starts the player's round in a team battle (2v2 or guild war); the run goes back through SubmitRun.</summary>
        Task<FindDuelResponse> StartBattleRunAsync(string battleId);
        /// <summary>A battle as this player may see it (undecided rival runs hidden), null on failure.</summary>
        Task<TeamBattle> GetBattleAsync(string battleId);

    }

    /// <summary>Live 1v1 duels (the matchmaking itself goes through IDuelMatchmaker).</summary>
    public interface IPvpLiveDuels
    {
        Task<LiveDuelResponse> StartLiveDuelAsync(string matchKey, LiveDuelist a, LiveDuelist b);
        Task<LiveDuelResponse> StartLiveBotDuelAsync(string matchKey, LiveDuelist mine);
        /// <summary>The duel the lobby's host created (for the guest), and the map draft as it goes (the server closes it when due).</summary>
        Task<LiveDuelResponse> GetLiveDuelAsync(string matchId);
        /// <summary>The player bans a map of the draft (0-2; -1: no choice in time, a random ban).</summary>
        Task<LiveDuelResponse> BanLiveDuelAsync(string matchId, int map);
        /// <summary>The player's run at the end; <see cref="SubmitRunResponse.Resolved"/> false while the rival's is awaited.</summary>
        Task<SubmitRunResponse> SubmitLiveDuelAsync(string matchId, RunSubmission run, string playerName);
        Task<SubmitRunResponse> GetLiveDuelResultAsync(string matchId);

    }

    /// <summary>Live 2v2 relays (the matchmaking itself goes through IRelayMatchmaker).</summary>
    public interface IPvpRelays
    {
        /// <summary>The lobby host creates the match of the two duos that met (same key = same match).</summary>
        Task<RelayMatchResponse> StartRelayMatchAsync(string matchKey, RelaySide a, RelaySide b);
        /// <summary>No duo found and the players accept bots: a match against a simulated duo, played in advance.</summary>
        Task<RelayMatchResponse> StartRelayBotsAsync(string matchKey, RelaySide mine);
        /// <summary>The duo's relay at the end of the match; the result once both duos have sent theirs (Pending meanwhile).</summary>
        Task<RelayResultResponse> SubmitRelayAsync(string matchId, string starter, List<RelayInput> inputs, List<string> quitters);
        /// <summary>The match (its map draft too, closed by the server when due) and its result once settled.</summary>
        Task<RelayResultResponse> GetRelayResultAsync(string matchId);
        /// <summary>The player's vote in the map draft: the map his duo bans (0-2; -1: no choice in time).</summary>
        Task<RelayMatchResponse> BanRelayAsync(string matchId, int map);
        /// <summary>"Quit and report": the teammate who left the match.</summary>
        Task<ReportResponse> ReportRelayQuitAsync(string matchId, string quitterId);

        /// <summary>The player's last 2v2 matches (<see cref="ChatConfig.RelayHistorySize"/>), both relays of each. Null on failure.</summary>
        Task<RelayHistoryResponse> GetRelayHistoryAsync();

    }

    /// <summary>Chat: "global", "guild" or ChatConfig.Direct(friend), names and shared replays.</summary>
    public interface IPvpChat
    {
        /// <summary>The messages of a channel after <paramref name="afterSeq"/>.</summary>
        Task<ChatPage> GetChatAsync(string channel, long afterSeq);
        Task<ChatSendResponse> SendChatAsync(string channel, string text, string playerName);
        /// <summary>Private conversations, blocked players, the last message number of the global and guild channels.</summary>
        Task<ChatInboxResponse> GetChatInboxAsync();
        Task<ReportResponse> BlockChatAsync(string playerId, bool block);
        /// <summary>Tells the chat server who may write to the player privately (their friends) and whether they are a minor.</summary>
        Task<ReportResponse> SyncChatProfileAsync(List<string> friendIds, bool minor);
        /// <summary>Puts the player's public name (Name#1234) in the friend-code directory.</summary>
        Task<ReportResponse> RegisterNameAsync(string name);
        /// <summary>The player behind a friend code, case aside (or behind the name alone when only one player has it).</summary>
        Task<FindPlayerResponse> FindPlayerAsync(string code);
        Task<ReportResponse> ReportChatAsync(string channel, long seq);
        /// <summary>Shares one of the player's replays (<see cref="ChatConfig.DuelReplay"/> or relay) in a channel.</summary>
        Task<ChatSendResponse> ShareReplayAsync(string kind, string matchId, string channel, string text, string playerName);
        Task<SharedReplayResponse> GetSharedReplayAsync(string id);

    }

    /// <summary>Data rights: export and deletion.</summary>
    public interface IPvpDataRights
    {
        /// <summary>Everything the PvP server holds about the player (profile, history, duos, guild, chat).</summary>
        Task<PvpDataExportResponse> ExportDataAsync();
        /// <summary>Erases the player's PvP data on the server (guild, duos, messages, history, leaderboard).</summary>
        Task<ReportResponse> DeleteDataAsync();

        /// <summary>After a rewarded ad: one more point of combat energy (a few times a day).</summary>
        Task<EnergyResponse> RefillEnergyAsync();

    }

    /// <summary>The golden wallet and the scarab economy, kept by the server.</summary>
    public interface IPvpWallet
    {
        Task<WalletResponse> GetWalletAsync();
        /// <summary>Credits a Play Store purchase once the server has checked it with Google (the Play Billing purchase token).</summary>
        Task<WalletResponse> VerifyPurchaseAsync(string productId, string purchaseToken);
        Task<WalletResponse> BuyPassAsync();
        /// <summary>
        /// The phone delivers these two itself: <paramref name="requestId"/> is never debited twice, and the wallet lists it
        /// (Wallet.Requests) so a lost answer is delivered later.
        /// </summary>
        Task<WalletResponse> BuyTiersAsync(string requestId);
        Task<WalletResponse> BuyScarabsAsync(string offerId, string requestId);
        Task<WalletResponse> BuyGoldItemAsync(string skinId);
        /// <summary>Carries the phone's scarabs over to the server, once per account (then they are held there).</summary>
        Task<WalletResponse> OpenScarabsAsync(ScarabSeed seed);
        Task<WalletResponse> BuyScarabPieceAsync(string id);
        Task<WalletResponse> BuyScarabSetAsync(int act);
        Task<WalletResponse> BuyWheelLegendaryAsync(string id);
        Task<WheelSpinResponse> SpinScarabWheelAsync(int count);
        /// <summary>The pass rewards that give golden scarabs or a skin (the scarab ones stay on the device).</summary>
        Task<WalletResponse> ClaimPassRewardsAsync(string season, List<int> freeTiers, List<int> premiumTiers);
        /// <summary>Solo runs (actions and times) the server replays to grant their pass XP, in batches.</summary>
        Task<WalletResponse> SubmitSoloRunsAsync(List<SoloRunReport> runs);
        /// <summary>Today's quests and this week's, counted by the server from the games it checks.</summary>
        Task<QuestResponse> GetQuestsAsync();
        /// <summary>A screen seen (<see cref="Quests.VisitSale"/>, <see cref="Quests.VisitDuoBoard"/>).</summary>
        Task<QuestResponse> VisitQuestAsync(string id);
        /// <summary>The reward of a finished quest, paid by the server (errors UNKNOWN_QUEST, NOT_DONE, CLAIMED).</summary>
        Task<QuestResponse> ClaimQuestAsync(string id);

    }

    /// <summary>Guilds and their wars.</summary>
    public interface IPvpGuilds
    {
        Task<GuildResponse> GetGuildAsync();
        Task<GuildResponse> CreateGuildAsync(string name, string tag, string playerName);
        Task<GuildSearchResponse> SearchGuildsAsync(string query, int limit);
        Task<GuildSearchResponse> GetGuildBoardAsync(int limit);
        Task<GuildResponse> JoinGuildAsync(string guildId, string playerName);
        Task<GuildResponse> LeaveGuildAsync();
        Task<GuildResponse> SetGuildRoleAsync(string memberId, bool officer);
        Task<GuildResponse> KickGuildMemberAsync(string memberId);
        /// <summary>Leader: who gets in (on request, anyone, nobody).</summary>
        Task<GuildResponse> SetGuildPolicyAsync(GuildJoinPolicy policy);
        /// <summary>Leader or officer: accepts or refuses an application.</summary>
        Task<GuildResponse> AnswerGuildRequestAsync(string playerId, bool accept);
        /// <summary>Invites a player (seen in a chat) into the guild.</summary>
        Task<TeamActionResponse> InviteToGuildAsync(string playerId, string playerName);
        Task<GuildResponse> RespondGuildInviteAsync(string guildId, bool accept, string playerName);
        /// <summary>Leader or officer: a war of <paramref name="size"/> rounds with this running order.</summary>
        Task<GuildResponse> StartWarAsync(int size, List<string> order);
    }

    public static class PvpServiceFactory
    {
        /// <summary>Set by the UGS module when its packages are installed (Cloud Code).</summary>
        public static Func<IOnlineService, IPvpService> CreateOnline;

        /// <summary>Called when the server refuses this version of the game ("OUTDATED"): the game asks for the update.</summary>
        public static Action UpdateRequired;

        public static void Inspect(string error)
        {
            if (error == "OUTDATED") UpdateRequired?.Invoke(); // noloc
        }

        /// <summary>The duel service for this online state: the server when connected, local rivals in demo builds, else none.</summary>
        public static IPvpService For(IOnlineService online, Func<int> soloStars)
        {
            if (online.IsAvailable && !online.IsDemo && CreateOnline != null) return CreateOnline(online);
            if (online.IsDemo) return new LocalPvpService(soloStars);
            return null;
        }

        public const string NetworkError = "NETWORK"; // noloc
    }
}
