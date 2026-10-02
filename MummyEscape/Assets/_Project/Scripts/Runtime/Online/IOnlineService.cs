using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Online
{
    public enum LeaderboardScope { Global, Country, Friends }

    public sealed class LeaderboardRow
    {
        public int Rank;          // 1-based
        public string PlayerId;
        public string PlayerName;
        /// <summary>Moves above the optimal route of the maze that player drew (0 = perfect run).</summary>
        public int OverPar;
        public int HpLost;
        public int Interactions;
        public bool IsMe;
        /// <summary>ISO 3166 alpha-2 code ("FR"), empty when unknown.</summary>
        public string Country = "";
    }

    public sealed class LeaderboardPage
    {
        /// <summary>Best first, at most the requested limit.</summary>
        public List<LeaderboardRow> Rows = new List<LeaderboardRow>();
        /// <summary>The local player's entry in this scope (Rank 0 = ranked but outside the fetched range), null if no score.</summary>
        public LeaderboardRow Me;
    }

    public sealed class FriendInfo
    {
        public string PlayerId;
        public string Name;
        public bool Online;
    }

    public sealed class FriendRequest
    {
        public string PlayerId;
        public string Name;
    }

    /// <summary>Public progression snapshot a player publishes so friends can follow them.</summary>
    [Serializable]
    public sealed class ProgressSnapshot
    {
        public string FurthestLevel;
        public int TotalStars;
        public List<LevelRecord> Records = new List<LevelRecord>();
    }

    /// <summary>
    /// Everything social: identity, per-level leaderboards, friends, progression sharing.
    /// The game must stay fully playable when <see cref="IsAvailable"/> is false.
    /// </summary>
    public interface IOnlineService
    {
        bool IsAvailable { get; }
        string PlayerId { get; }
        string PlayerName { get; }
        /// <summary>Human readable reason when unavailable (shown in the UI).</summary>
        string Status { get; }
        /// <summary>True when the data shown is generated demo content (offline, editor and development builds only).</summary>
        bool IsDemo { get; }
        /// <summary>Player country (ISO alpha-2), attached to submitted scores for the per-country ranking.</summary>
        string Country { get; set; }

        Task InitializeAsync();
        Task<string> SetPlayerNameAsync(string name);

        Task SubmitScoreAsync(LevelResult result);
        /// <summary>Top <paramref name="limit"/> of a level. Country scope uses <see cref="Country"/>.</summary>
        Task<LeaderboardPage> GetLeaderboardAsync(LevelId level, LeaderboardScope scope, int limit);

        Task<IReadOnlyList<FriendInfo>> GetFriendsAsync();
        Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync();
        Task<bool> SendFriendRequestAsync(string playerName);
        Task AcceptFriendRequestAsync(string playerId);
        Task DeclineFriendRequestAsync(string playerId);
        Task RemoveFriendAsync(string playerId);

        Task PublishProgressAsync(ProgressSnapshot snapshot);
        Task<ProgressSnapshot> GetProgressAsync(string playerId);
    }

    /// <summary>Picks the best available implementation (UGS registers itself when its packages are installed).</summary>
    public static class OnlineServiceFactory
    {
        public static Func<IOnlineService> Create = () => new OfflineOnlineService();

        /// <summary>Leaderboard id for a level. Includes the generator version so different layouts never mix.</summary>
        public static string LeaderboardId(LevelId id) => $"v{DifficultyTable.GeneratorVersion}_{id.Key}";
    }
}
