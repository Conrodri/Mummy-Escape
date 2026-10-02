using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Online
{
    public enum LeaderboardScope { Global, Friends }

    public sealed class LeaderboardRow
    {
        public int Rank;          // 1-based
        public string PlayerId;
        public string PlayerName;
        public int Moves;
        public int HpLost;
        public int Interactions;
        public bool IsMe;
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

        Task InitializeAsync();
        Task<string> SetPlayerNameAsync(string name);

        Task SubmitScoreAsync(LevelResult result);
        Task<IReadOnlyList<LeaderboardRow>> GetLeaderboardAsync(LevelId level, LeaderboardScope scope, int limit);

        Task<IReadOnlyList<FriendInfo>> GetFriendsAsync();
        Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync();
        Task<bool> SendFriendRequestAsync(string playerName);
        Task AcceptFriendRequestAsync(string playerId);
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
