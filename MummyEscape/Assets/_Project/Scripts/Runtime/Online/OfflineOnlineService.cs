using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Fallback when no backend is configured. Rankings only contain the local best, except in the Editor and
    /// development builds where clearly labelled demo players, friends and requests fill the social screens so
    /// their layout can be tested without Unity Gaming Services.
    /// </summary>
    public sealed class OfflineOnlineService : IOnlineService
    {
        readonly Dictionary<string, LevelResult> _best = new Dictionary<string, LevelResult>();
        readonly List<FriendInfo> _friends = new List<FriendInfo>();
        readonly List<FriendRequest> _requests = new List<FriendRequest>();
        readonly Dictionary<string, ProgressSnapshot> _progress = new Dictionary<string, ProgressSnapshot>();

        /// <summary>Local save lookup so records made before this session still appear in the rankings.</summary>
        public System.Func<LevelId, LevelRecord> LocalRecord;

        public OfflineOnlineService(string status = null)
        {
            if (status != null) Status = status;
            IsDemo = Application.isEditor || Debug.isDebugBuild;
            if (IsDemo) SeedDemoFriends();
        }

        public bool IsAvailable => false;
        public bool IsDemo { get; }
        public string PlayerId => "local";
        public string PlayerName { get; private set; } = "Momie";
        public string Status { get; } = "Hors ligne — configure Unity Gaming Services pour les classements et les amis.";
        public string Country { get; set; } = "";

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<string> SetPlayerNameAsync(string name)
        {
            PlayerName = string.IsNullOrWhiteSpace(name) ? PlayerName : name.Trim().Replace(' ', '_');
            return Task.FromResult(PlayerName);
        }

        public Task SubmitScoreAsync(LevelResult result)
        {
            if (!result.Won) return Task.CompletedTask;
            string key = result.Level.Key;
            if (!_best.TryGetValue(key, out var prev) || result.LeaderboardScore < prev.LeaderboardScore) _best[key] = result;
            return Task.CompletedTask;
        }

        public Task<LeaderboardPage> GetLeaderboardAsync(LevelId level, LeaderboardScope scope, int limit)
        {
            var all = new List<(long score, LeaderboardRow row)>();
            var local = LocalRecord?.Invoke(level);
            if (_best.TryGetValue(level.Key, out var r))
                all.Add((r.LeaderboardScore, new LeaderboardRow
                {
                    PlayerId = PlayerId, PlayerName = PlayerName, Moves = r.Moves, HpLost = r.MaxHp - r.HpLeft,
                    Interactions = r.Interactions, IsMe = true, Country = Country,
                }));
            else if (local != null && local.BestMoves > 0)
                all.Add(((long)local.BestMoves * 10000, new LeaderboardRow
                {
                    PlayerId = PlayerId, PlayerName = PlayerName, Moves = local.BestMoves, IsMe = true, Country = Country,
                }));
            if (IsDemo) AddDemoScores(level, scope, all);

            all.Sort((a, b) => a.score.CompareTo(b.score));
            var page = new LeaderboardPage();
            for (int i = 0; i < all.Count; i++)
            {
                var row = all[i].row;
                row.Rank = i + 1;
                if (row.IsMe) page.Me = row;
                if (i < limit) page.Rows.Add(row);
            }
            return Task.FromResult(page);
        }

        public Task<IReadOnlyList<FriendInfo>> GetFriendsAsync() => Task.FromResult<IReadOnlyList<FriendInfo>>(new List<FriendInfo>(_friends));
        public Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync() => Task.FromResult<IReadOnlyList<FriendRequest>>(new List<FriendRequest>(_requests));

        public Task<bool> SendFriendRequestAsync(string playerName) =>
            Task.FromResult(IsDemo && !string.IsNullOrWhiteSpace(playerName) && playerName.Contains("#"));

        public Task AcceptFriendRequestAsync(string playerId)
        {
            var req = _requests.Find(q => q.PlayerId == playerId);
            if (req != null)
            {
                _requests.Remove(req);
                _friends.Add(new FriendInfo { PlayerId = req.PlayerId, Name = req.Name, Online = true });
            }
            return Task.CompletedTask;
        }

        public Task DeclineFriendRequestAsync(string playerId)
        {
            _requests.RemoveAll(q => q.PlayerId == playerId);
            return Task.CompletedTask;
        }

        public Task RemoveFriendAsync(string playerId)
        {
            _friends.RemoveAll(f => f.PlayerId == playerId);
            return Task.CompletedTask;
        }

        public Task PublishProgressAsync(ProgressSnapshot snapshot) => Task.CompletedTask;

        public Task<ProgressSnapshot> GetProgressAsync(string playerId) =>
            Task.FromResult(_progress.TryGetValue(playerId, out var p) ? p : null);

        // ------------------------------------------------------------------ demo content (Editor / dev builds)

        static readonly string[] DemoNames =
        {
            "Nefertari", "Ahmose", "Imhotep", "Tiye", "Khaemwaset", "Merit", "Senenmut", "Hatshepsout", "Kha", "Iset",
            "Ramses", "Bastet", "Thoutmosis", "Neith", "Amenhotep", "Meritamon", "Sobek", "Henutsen", "Djoser", "Nebet",
        };
        static readonly string[] DemoCountries = { "FR", "FR", "BE", "CH", "CA", "US", "DE", "ES", "IT", "MA", "EG", "BR", "JP", "GB" };

        void AddDemoScores(LevelId level, LeaderboardScope scope, List<(long, LeaderboardRow)> all)
        {
            var spec = DifficultyTable.Spec(level);
            var rng = new Pcg32(Pcg32.Hash(77, (ulong)(level.Act * 1000 + level.Index)));
            int count = scope == LeaderboardScope.Friends ? _friends.Count : 160;
            for (int i = 0; i < count; i++)
            {
                string country = DemoCountries[rng.Range(0, DemoCountries.Length)];
                if (scope == LeaderboardScope.Country && country != Country) continue;
                // Most players land a few moves above the act minimum, a long tail wanders much more.
                int moves = spec.MinMoves + (int)(Mathf.Pow(rng.Range(0, 1000) / 1000f, 2.2f) * spec.MinMoves * 2.5f) + rng.Range(0, 3);
                int hpLost = rng.Range(0, 10) < 2 ? 1 : 0;
                int interactions = 1 + rng.Range(0, 6);
                string name = scope == LeaderboardScope.Friends ? _friends[i].Name : $"{DemoNames[i % DemoNames.Length]}#{1000 + rng.Range(0, 9000)}";
                long score = (long)moves * 10000 + hpLost * 1000 + interactions;
                all.Add((score, new LeaderboardRow
                {
                    PlayerId = "demo" + i, PlayerName = name, Moves = moves, HpLost = hpLost, Interactions = interactions, Country = country,
                }));
            }
        }

        void SeedDemoFriends()
        {
            AddDemoFriend("Nefertari#2041", true, "2-4", 31);
            AddDemoFriend("Imhotep#7310", false, "1-9", 22);
            AddDemoFriend("Tiye#5562", true, "3-2", 47);
            _requests.Add(new FriendRequest { PlayerId = "demo-req", Name = "Ahmose#8127" });
        }

        void AddDemoFriend(string name, bool online, string furthest, int stars)
        {
            string id = "demo-" + name;
            _friends.Add(new FriendInfo { PlayerId = id, Name = name, Online = online });
            var snapshot = new ProgressSnapshot { FurthestLevel = furthest, TotalStars = stars };
            var rng = new Pcg32(Pcg32.Hash(3, (ulong)name.Length * 7919 + name[0]));
            foreach (var lvl in DifficultyTable.AllLevels())
            {
                if (lvl.ToString() == furthest) break;
                var spec = DifficultyTable.Spec(lvl);
                snapshot.Records.Add(new LevelRecord { Key = lvl.Key, BestMoves = spec.MinMoves + rng.Range(0, 8), BestStars = 1 + rng.Range(0, 3) });
            }
            _progress[id] = snapshot;
        }
    }
}
