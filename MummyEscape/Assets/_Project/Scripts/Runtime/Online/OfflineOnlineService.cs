using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Fallback when no backend is configured. In the Editor and development builds clearly labelled demo friends and
    /// requests fill the social screens so their layout can be tested without Unity Gaming Services.
    /// </summary>
    public sealed class OfflineOnlineService : IOnlineService
    {
        readonly List<FriendInfo> _friends = new List<FriendInfo>();
        readonly List<FriendRequest> _requests = new List<FriendRequest>();
        readonly Dictionary<string, ProgressSnapshot> _progress = new Dictionary<string, ProgressSnapshot>();

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

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<string> SetPlayerNameAsync(string name)
        {
            PlayerName = string.IsNullOrWhiteSpace(name) ? PlayerName : name.Trim().Replace(' ', '_');
            return Task.FromResult(PlayerName);
        }

        public Task<IReadOnlyList<FriendInfo>> GetFriendsAsync() => Task.FromResult<IReadOnlyList<FriendInfo>>(new List<FriendInfo>(_friends));
        public Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync() => Task.FromResult<IReadOnlyList<FriendRequest>>(new List<FriendRequest>(_requests));

        public Task<string> SendFriendRequestAsync(string playerName) =>
            Task.FromResult(IsDemo && !string.IsNullOrWhiteSpace(playerName) && playerName.Contains("#") ? null : IsDemo ? "Joueur introuvable." : Status);

        public Task<string> SendFriendRequestToIdAsync(string playerId) =>
            Task.FromResult(IsDemo && !string.IsNullOrEmpty(playerId) ? null : IsDemo ? "Joueur introuvable." : Status);

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

        // ---- account: none offline (accounts are Google Play Games ones, through the backend).

        public AccountState Account => AccountState.Offline;
        public string Username => "";
        public bool GoogleLinked => false;
        public Task<string> LinkGoogleAsync() => Task.FromResult("Google Play Jeux n'est disponible que sur Android, en ligne.");
        public Task<string> SignInWithGoogleAsync() => LinkGoogleAsync();
        public Task SignOutAsync() => Task.CompletedTask;

        public Task<string> DeleteAccountAsync()
        {
            return Task.FromResult<string>(null);
        }

        public Task<string> LoadCloudSaveAsync() => Task.FromResult<string>(null);
        public Task<bool> SaveCloudSaveAsync(string json) => Task.FromResult(true);

        public Task ClearPublishedProgressAsync() => Task.CompletedTask;

        public Task<string> ExportOnlineDataAsync() =>
            Task.FromResult(IsDemo ? "{\"demo\":true,\"note\":\"Données simulées (aucun serveur).\"}" : "{}");

        public Task PublishProgressAsync(ProgressSnapshot snapshot) => Task.CompletedTask;

        public Task<ProgressSnapshot> GetProgressAsync(string playerId) =>
            Task.FromResult(_progress.TryGetValue(playerId, out var p) ? p : null);

        // ------------------------------------------------------------------ demo content (Editor / dev builds)

        void SeedDemoFriends()
        {
            AddDemoFriend("Nefertari#2041", true, "N2-4", 31, 1480);
            AddDemoFriend("Imhotep#7310", false, "F4-3", 22, 0);
            AddDemoFriend("Tiye#5562", true, "X1-2", 47, 1720);
            _requests.Add(new FriendRequest { PlayerId = "demo-req", Name = "Ahmose#8127" });
        }

        void AddDemoFriend(string name, bool online, string furthest, int stars, int elo)
        {
            string id = "demo-" + name;
            _friends.Add(new FriendInfo { PlayerId = id, Name = name, Online = online });
            var snapshot = new ProgressSnapshot
            {
                FurthestLevel = furthest, TotalStars = stars, Elo = elo,
                Look = Visual.PvpSkins.RandomLook(new System.Random(name.Length * 7919 + name[0])),
            };
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
