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
        Task<FindDuelResponse> FindDuelAsync();
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
