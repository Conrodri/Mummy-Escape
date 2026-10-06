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
    public interface IPvpService
    {
        /// <summary>Offline duels against simulated rivals (editor and development builds only).</summary>
        bool IsDemo { get; }
        Task<PvpProfileResponse> GetProfileAsync();
        /// <summary>A rival of the player's league; <see cref="FindDuelResponse.Searching"/> while the server looks (ask again).</summary>
        Task<FindDuelResponse> FindDuelAsync(bool allowBots);
        Task<SubmitRunResponse> SubmitRunAsync(RunSubmission run, string playerName);
        Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync();
        Task<SealPurchaseResponse> BuyWithSealsAsync(string itemId);
        /// <summary>One turn of the casino's seal wheel (paid and drawn by the server).</summary>
        Task<WheelSpinResponse> SpinSealWheelAsync();
        /// <summary>Top of the monthly Elo ranking, checked by the server: <paramref name="seasonsAgo"/> 0 = this month, 1 = last month.</summary>
        Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit);
        /// <summary>Publishes the solo star count, which unlocks the duels (<see cref="PvpConfig.RequiredSoloStars"/>).</summary>
        Task SyncSoloStarsAsync(int stars);
        /// <summary>The player's last duels (<see cref="PvpConfig.HistorySize"/>), both runs of each: the replays. Null on failure.</summary>
        Task<DuelHistoryResponse> GetHistoryAsync();
        /// <summary>Reports the rival of a duel of the history for cheating: the server files the whole duel for review.</summary>
        Task<ReportResponse> ReportCheatAsync(string matchId);

        // --- 2v2 (duos of friends) ---
        Task<TeamsResponse> GetTeamsAsync();
        Task<TeamActionResponse> InviteDuoAsync(string friendId, string playerName);
        Task<TeamActionResponse> RespondDuoAsync(string inviteId, bool accept, string playerName);
        Task<TeamActionResponse> LeaveDuoAsync(string duoId);
        /// <summary>Starts a 2v2 battle; <paramref name="meFirst"/>: the player runs rounds 1 and 3, the partner round 2.</summary>
        Task<TeamActionResponse> FindDuoMatchAsync(string duoId, bool meFirst);
        Task<DuoBoardResponse> GetDuoBoardAsync(int limit);
        /// <summary>Starts the player's round in a team battle (2v2 or guild war); the run goes back through SubmitRun.</summary>
        Task<FindDuelResponse> StartBattleRunAsync(string battleId);
        /// <summary>A battle as this player may see it (undecided rival runs hidden), null on failure.</summary>
        Task<TeamBattle> GetBattleAsync(string battleId);

        // --- live duel (the matchmaking itself goes through IDuelMatchmaker) ---
        Task<LiveDuelResponse> StartLiveDuelAsync(string matchKey, LiveDuelist a, LiveDuelist b);
        Task<LiveDuelResponse> StartLiveBotDuelAsync(string matchKey, LiveDuelist mine);
        /// <summary>The duel the lobby's host created (for the guest).</summary>
        Task<LiveDuelResponse> GetLiveDuelAsync(string matchId);
        /// <summary>The player's run at the end; <see cref="SubmitRunResponse.Resolved"/> false while the rival's is awaited.</summary>
        Task<SubmitRunResponse> SubmitLiveDuelAsync(string matchId, RunSubmission run, string playerName);
        Task<SubmitRunResponse> GetLiveDuelResultAsync(string matchId);

        // --- 2v2 relay, live (the matchmaking itself goes through IRelayMatchmaker) ---
        /// <summary>The lobby host creates the match of the two duos that met (same key = same match).</summary>
        Task<RelayMatchResponse> StartRelayMatchAsync(string matchKey, RelaySide a, RelaySide b);
        /// <summary>No duo found and the players accept bots: a match against a simulated duo, played in advance.</summary>
        Task<RelayMatchResponse> StartRelayBotsAsync(string matchKey, RelaySide mine);
        /// <summary>The duo's relay at the end of the match; the result once both duos have sent theirs (Pending meanwhile).</summary>
        Task<RelayResultResponse> SubmitRelayAsync(string matchId, string starter, List<RelayInput> inputs, List<string> quitters);
        Task<RelayResultResponse> GetRelayResultAsync(string matchId);
        /// <summary>"Quit and report": the teammate who left the match.</summary>
        Task<ReportResponse> ReportRelayQuitAsync(string matchId, string quitterId);

        /// <summary>The player's last 2v2 matches (<see cref="ChatConfig.RelayHistorySize"/>), both relays of each. Null on failure.</summary>
        Task<RelayHistoryResponse> GetRelayHistoryAsync();

        // --- Chat: "global", "guild" or ChatConfig.Direct(friend) ---
        /// <summary>The messages of a channel after <paramref name="afterSeq"/>.</summary>
        Task<ChatPage> GetChatAsync(string channel, long afterSeq);
        Task<ChatSendResponse> SendChatAsync(string channel, string text, string playerName);
        /// <summary>Private conversations, blocked players, the last message number of the global and guild channels.</summary>
        Task<ChatInboxResponse> GetChatInboxAsync();
        Task<ReportResponse> BlockChatAsync(string playerId, bool block);
        /// <summary>Tells the chat server who may write to the player privately (their friends) and whether they are a minor.</summary>
        Task<ReportResponse> SyncChatProfileAsync(List<string> friendIds, bool minor);
        Task<ReportResponse> ReportChatAsync(string channel, long seq);
        /// <summary>Shares one of the player's replays (<see cref="ChatConfig.DuelReplay"/> or relay) in a channel.</summary>
        Task<ChatSendResponse> ShareReplayAsync(string kind, string matchId, string channel, string text, string playerName);
        Task<SharedReplayResponse> GetSharedReplayAsync(string id);

        // --- Data rights ---
        /// <summary>Everything the PvP server holds about the player (profile, history, duos, guild, chat).</summary>
        Task<PvpDataExportResponse> ExportDataAsync();
        /// <summary>Erases the player's PvP data on the server (guild, duos, messages, history, leaderboard).</summary>
        Task<ReportResponse> DeleteDataAsync();

        /// <summary>After a rewarded ad: one more point of combat energy (a few times a day).</summary>
        Task<EnergyResponse> RefillEnergyAsync();

        // --- Golden wallet (kept by the server) ---
        Task<WalletResponse> GetWalletAsync();
        /// <summary>Credits a Play Store purchase once the server has checked it with Google (the Play Billing purchase token).</summary>
        Task<WalletResponse> VerifyPurchaseAsync(string productId, string purchaseToken);
        Task<WalletResponse> BuyPassAsync();
        Task<WalletResponse> BuyTiersAsync();
        Task<WalletResponse> BuyScarabsAsync(string offerId);
        Task<WalletResponse> BuyGoldItemAsync(string skinId);
        /// <summary>The pass rewards that give golden scarabs or a skin (the scarab ones stay on the device).</summary>
        Task<WalletResponse> ClaimPassRewardsAsync(string season, List<int> freeTiers, List<int> premiumTiers);

        // --- Guilds ---
        Task<GuildResponse> GetGuildAsync();
        Task<GuildResponse> CreateGuildAsync(string name, string tag, string playerName);
        Task<GuildSearchResponse> SearchGuildsAsync(string query, int limit);
        Task<GuildSearchResponse> GetGuildBoardAsync(int limit);
        Task<GuildResponse> JoinGuildAsync(string guildId, string playerName);
        Task<GuildResponse> LeaveGuildAsync();
        Task<GuildResponse> SetGuildRoleAsync(string memberId, bool officer);
        Task<GuildResponse> KickGuildMemberAsync(string memberId);
        /// <summary>Leader or officer: a war of <paramref name="size"/> rounds with this running order.</summary>
        Task<GuildResponse> StartWarAsync(int size, List<string> order);
    }

    public static class PvpServiceFactory
    {
        /// <summary>Set by the UGS module when its packages are installed (Cloud Code).</summary>
        public static Func<IOnlineService, IPvpService> CreateOnline;

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
