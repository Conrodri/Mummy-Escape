using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Pvp;

namespace MummyEscape.Online
{
    public sealed class PvpBoardRow
    {
        public int Rank;          // 1-based
        public string PlayerId;
        public string PlayerName;
        public int Elo;
        public bool IsMe;
    }

    public sealed class PvpBoardPage
    {
        /// <summary>Month of the ranking ("2026-10"), empty when unknown.</summary>
        public string Season = "";
        public List<PvpBoardRow> Rows = new List<PvpBoardRow>();
        /// <summary>The local player's entry (Rank 0 = outside the fetched range), null when unranked.</summary>
        public PvpBoardRow Me;
    }

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
        /// <summary>Top of the monthly Elo ranking: <paramref name="seasonsAgo"/> 0 = this month, 1 = last month.</summary>
        Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit);
        /// <summary>Publishes the solo star count, which unlocks the duels (<see cref="PvpConfig.RequiredSoloStars"/>).</summary>
        Task SyncSoloStarsAsync(int stars);
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
