using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Online
{
    /// <summary>Fallback when no backend is configured: the leaderboard only shows the local best.</summary>
    public sealed class OfflineOnlineService : IOnlineService
    {
        readonly Dictionary<string, LevelResult> _best = new Dictionary<string, LevelResult>();

        public bool IsAvailable => false;
        public string PlayerId => "local";
        public string PlayerName { get; private set; } = "Momie";
        public string Status => "Hors ligne — configure Unity Gaming Services pour les classements et les amis.";

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<string> SetPlayerNameAsync(string name)
        {
            PlayerName = string.IsNullOrWhiteSpace(name) ? PlayerName : name.Trim();
            return Task.FromResult(PlayerName);
        }

        public Task SubmitScoreAsync(LevelResult result)
        {
            if (!result.Won) return Task.CompletedTask;
            string key = result.Level.Key;
            if (!_best.TryGetValue(key, out var prev) || result.LeaderboardScore < prev.LeaderboardScore) _best[key] = result;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LeaderboardRow>> GetLeaderboardAsync(LevelId level, LeaderboardScope scope, int limit)
        {
            var rows = new List<LeaderboardRow>();
            if (_best.TryGetValue(level.Key, out var r))
                rows.Add(new LeaderboardRow { Rank = 1, PlayerId = PlayerId, PlayerName = PlayerName, Moves = r.Moves, HpLost = r.MaxHp - r.HpLeft, Interactions = r.Interactions, IsMe = true });
            return Task.FromResult<IReadOnlyList<LeaderboardRow>>(rows);
        }

        public Task<IReadOnlyList<FriendInfo>> GetFriendsAsync() => Task.FromResult<IReadOnlyList<FriendInfo>>(new List<FriendInfo>());
        public Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync() => Task.FromResult<IReadOnlyList<FriendRequest>>(new List<FriendRequest>());
        public Task<bool> SendFriendRequestAsync(string playerName) => Task.FromResult(false);
        public Task AcceptFriendRequestAsync(string playerId) => Task.CompletedTask;
        public Task RemoveFriendAsync(string playerId) => Task.CompletedTask;
        public Task PublishProgressAsync(ProgressSnapshot snapshot) => Task.CompletedTask;
        public Task<ProgressSnapshot> GetProgressAsync(string playerId) => Task.FromResult<ProgressSnapshot>(null);
    }
}
