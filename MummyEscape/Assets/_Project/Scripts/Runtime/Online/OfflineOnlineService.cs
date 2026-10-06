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
        string _playerName; // null = default name, in the current language
        public string PlayerName { get => _playerName ?? Loc.T("Momie"); private set => _playerName = value; }
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
                    PlayerId = PlayerId, PlayerName = PlayerName, OverPar = r.OverPar, TimeMs = r.TimeMs, IsMe = true, Country = Country,
                }));
            else if (local != null && local.HasBest)
                all.Add((LevelResult.EncodeScore(local.BestOverPar, local.BestTimeMs), new LeaderboardRow
                {
                    PlayerId = PlayerId, PlayerName = PlayerName, OverPar = local.BestOverPar, TimeMs = local.BestTimeMs, IsMe = true, Country = Country,
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

        // ---- account: simulated in the editor / development builds (to test the screens), unavailable otherwise.

        public AccountState Account { get; private set; } = AccountState.Offline;
        public string Username { get; private set; } = "";
        string _demoPassword = "";
        string _demoCloudSave;

        string Unavailable => IsDemo ? null : "Les comptes nécessitent une connexion à Unity Gaming Services.";

        public Task<string> CreateAccountAsync(string username, string password)
        {
            if (Unavailable != null) return Task.FromResult(Unavailable);
            Account = AccountState.Account;
            Username = username;
            _demoPassword = password;
            return Task.FromResult<string>(null);
        }

        public Task<string> SignInAsync(string username, string password)
        {
            if (Unavailable != null) return Task.FromResult(Unavailable);
            if (Username != "" && (username != Username || password != _demoPassword)) return Task.FromResult("Identifiant ou mot de passe incorrect.");
            Account = AccountState.Account;
            Username = username;
            _demoPassword = password;
            return Task.FromResult<string>(null);
        }

        public bool HasPassword => Username != "";
        public bool GoogleLinked => false;
        public Task<string> LinkGoogleAsync() => Task.FromResult("Google Play Jeux n'est disponible que sur Android, en ligne.");
        public Task<string> SignInWithGoogleAsync() => LinkGoogleAsync();

        public Task SignOutAsync()
        {
            Account = AccountState.Offline;
            return Task.CompletedTask;
        }

        public Task<string> ChangePasswordAsync(string current, string next)
        {
            if (Unavailable != null) return Task.FromResult(Unavailable);
            if (current != _demoPassword) return Task.FromResult("Mot de passe actuel incorrect.");
            _demoPassword = next;
            return Task.FromResult<string>(null);
        }

        public Task<string> DeleteAccountAsync()
        {
            Account = AccountState.Offline;
            Username = _demoPassword = "";
            _demoCloudSave = null;
            _best.Clear();
            return Task.FromResult<string>(null);
        }

        public Task<string> LoadCloudSaveAsync() => Task.FromResult(_demoCloudSave);

        public Task SaveCloudSaveAsync(string json)
        {
            if (Account == AccountState.Account) _demoCloudSave = json;
            return Task.CompletedTask;
        }

        public Task ClearPublishedProgressAsync() => Task.CompletedTask;

        public Task<string> ExportOnlineDataAsync() =>
            Task.FromResult(IsDemo ? "{\"demo\":true,\"note\":\"Données simulées (aucun serveur).\"}" : "{}");

        public Task PublishProgressAsync(ProgressSnapshot snapshot) => Task.CompletedTask;

        public Task<ProgressSnapshot> GetProgressAsync(string playerId) =>
            Task.FromResult(_progress.TryGetValue(playerId, out var p) ? p : null);

        // ------------------------------------------------------------------ demo content (Editor / dev builds)

        static readonly string[] DemoNames =
        {
            "Nefertari", "Ahmose", "Imhotep", "Tiye", "Khaemwaset", "Merit", "Senenmut", "Hatshepsout", "Kha", "Iset", // noloc: names
            "Ramses", "Bastet", "Thoutmosis", "Neith", "Amenhotep", "Meritamon", "Sobek", "Henutsen", "Djoser", "Nebet", // noloc
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
                // Many players get close to the optimal route, a long tail wanders much more.
                int overPar = (int)(Mathf.Pow(rng.Range(0, 1000) / 1000f, 1.3f) * spec.MinMoves * 1.2f);
                // About 0.9-2.4 s per move (the route is played from memory), detours included.
                int timeMs = (spec.MinMoves + 4 + overPar) * (900 + rng.Range(0, 1500)) + rng.Range(0, 1000);
                string name = scope == LeaderboardScope.Friends ? _friends[i].Name : $"{DemoNames[i % DemoNames.Length]}#{1000 + rng.Range(0, 9000)}";
                long score = LevelResult.EncodeScore(overPar, timeMs);
                all.Add((score, new LeaderboardRow
                {
                    PlayerId = "demo" + i, PlayerName = name, OverPar = overPar, TimeMs = timeMs, Country = country,
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
                snapshot.Records.Add(new LevelRecord { Key = lvl.Key, BestOverPar = rng.Range(0, 8), BestTimeMs = spec.MinMoves * (600 + rng.Range(0, 700)), BestMoves = spec.MinMoves + rng.Range(0, 8), BestStars = 1 + rng.Range(0, 3), Completions = 1 });
            }
            _progress[id] = snapshot;
        }
    }
}
