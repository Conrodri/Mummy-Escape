using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Duels without a server (editor and development builds): the same <see cref="PvpServer"/> rules run on the device
    /// against simulated rivals (<see cref="PvpBots"/>), and a demo ranking fills the leaderboard. The player's PvP data
    /// is kept in PlayerPrefs, apart from the real save.
    /// </summary>
    public sealed class LocalPvpService : IPvpService
    {
        const string PrefsKey = "pvp_demo";
        const string Me = "local";

        [Serializable]
        sealed class Stored
        {
            public PlayerPvpData Data;
            public PendingDuel Pending;
        }

        static readonly string[] DemoNames =
        {
            "Nefertari", "Khépri", "Imhotep", "Sobek", "Hatchepsout", "Ramsès", "Bastet", "Thot", "Meritaton", "Ptah", // noloc
            "Horus", "Seth", "Isis", "Osiris", "Neith", "Sekhmet", "Amon", "Hathor", "Khonsou", "Maât", // noloc
            "Akhenaton", "Tiyi", "Séthi", "Ahmès", "Ouadjet", "Nekhbet", "Montou", "Taouret", "Bès", "Rénénoutet", // noloc
        };

        readonly Func<int> _soloStars;
        readonly MemoryPvpStore _store = new MemoryPvpStore();
        readonly PvpServer _server;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly System.Random _rng = new System.Random();
        readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        public bool IsDemo => true;

        public LocalPvpService(Func<int> soloStars)
        {
            _soloStars = soloStars;
            _server = new PvpServer(_store);
            _store.MakeGhost = (player, elo) =>
            {
                GhostRun ghost;
                lock (_rng) ghost = PvpBots.Make(_rng, elo, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                lock (_names) _names[ghost.PlayerId] = ghost.PlayerName;
                return ghost;
            };
            Load();
            SeedDemoRivals();
        }

        // ------------------------------------------------------------------ storage

        void Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(PrefsKey, "");
                if (string.IsNullOrEmpty(json)) return;
                var s = JsonUtility.FromJson<Stored>(json);
                // JsonUtility turns null objects into empty ones: put the nulls back.
                if (s.Data != null)
                {
                    if (s.Data.LastSeason != null && string.IsNullOrEmpty(s.Data.LastSeason.Season)) s.Data.LastSeason = null;
                    s.Data.OpponentsToday ??= new Dictionary<string, int>();
                    s.Data.UnlockedRewards ??= new List<string>();
                    _store.Players[Me] = s.Data;
                }
                if (s.Pending != null && !string.IsNullOrEmpty(s.Pending.MatchId))
                {
                    if (s.Pending.Ghost != null && string.IsNullOrEmpty(s.Pending.Ghost.GhostId)) s.Pending.Ghost = null;
                    _store.Pending[Me] = s.Pending;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Pvp] Unreadable demo data: " + e.Message); }
        }

        void Persist()
        {
            _store.Players.TryGetValue(Me, out var data);
            _store.Pending.TryGetValue(Me, out var pending);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Stored { Data = data, Pending = pending }));
            PlayerPrefs.Save();
        }

        /// <summary>Thirty rivals ranked this month (the same every launch of the month), so the ranking looks alive.</summary>
        void SeedDemoRivals()
        {
            string season = Seasons.SeasonOf(DateTime.UtcNow);
            var rng = new System.Random(SeasonSeed(season) ^ 0x5eed);
            for (int i = 0; i < DemoNames.Length; i++)
            {
                string id = "demo_" + i;
                int elo = 700 + rng.Next(0, 1000);
                _store.Players[id] = new PlayerPvpData { Elo = elo, Season = season, SeasonDuels = 5 + rng.Next(60), BestEloThisSeason = elo };
                _names[id] = DemoNames[i] + "#" + (1000 + rng.Next(9000));
            }
        }

        /// <summary>The server's work (maze generation, replays) off the main thread, one call at a time; saved afterwards.</summary>
        async Task<T> Run<T>(Func<Task<T>> call)
        {
            await _gate.WaitAsync();
            try
            {
                _store.SoloStars[Me] = _soloStars();
                var result = await Task.Run(call);
                Persist(); // back on the main thread (PlayerPrefs)
                return result;
            }
            finally { _gate.Release(); }
        }

        // ------------------------------------------------------------------ IPvpService

        public Task<PvpProfileResponse> GetProfileAsync() => Run(() => _server.GetProfileAsync(Me));

        public Task<FindDuelResponse> FindDuelAsync() => Run(() => _server.FindDuelAsync(Me, Core.DifficultyTable.GeneratorVersion));

        public Task<SubmitRunResponse> SubmitRunAsync(RunSubmission run, string playerName) => Run(() => _server.SubmitRunAsync(Me, run, playerName));

        public Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync() => Run(() => _server.ClaimSeasonRewardsAsync(Me));

        public Task<SealPurchaseResponse> BuyWithSealsAsync(string itemId) => Run(() => _server.BuyWithSealsAsync(Me, itemId));

        public Task SyncSoloStarsAsync(int stars) => Task.CompletedTask; // read live from the save

        public async Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit)
        {
            var profile = await GetProfileAsync(); // rolls the season first
            var now = DateTime.UtcNow;
            var page = new PvpBoardPage { Season = Seasons.SeasonOf(now.AddMonths(-seasonsAgo)) };
            var entries = new List<PvpBoardRow>();
            if (seasonsAgo == 0)
            {
                foreach (var kv in _store.Players)
                    if (kv.Value.SeasonDuels > 0 && kv.Value.Season == page.Season)
                        entries.Add(new PvpBoardRow { PlayerId = kv.Key, Elo = kv.Value.Elo, IsMe = kv.Key == Me, PlayerName = NameOf(kv.Key) });
            }
            else
            {
                // Past months: a frozen demo ranking, with the player at their final Elo if they played that month.
                var rng = new System.Random(SeasonSeed(page.Season));
                for (int i = 0; i < DemoNames.Length; i++)
                    entries.Add(new PvpBoardRow { PlayerId = "old_" + i, Elo = 700 + rng.Next(0, 1000), PlayerName = DemoNames[(i * 7 + 3) % DemoNames.Length] + "#" + (1000 + rng.Next(9000)) });
                var last = profile.Data?.LastSeason;
                if (seasonsAgo == 1 && last != null && last.Duels > 0)
                    entries.Add(new PvpBoardRow { PlayerId = Me, Elo = last.FinalElo, IsMe = true, PlayerName = NameOf(Me) });
            }
            entries.Sort((a, b) => b.Elo.CompareTo(a.Elo));
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = i + 1;
                if (entries[i].IsMe) page.Me = entries[i];
                if (i < limit) page.Rows.Add(entries[i]);
            }
            return page;
        }

        /// <summary>Stable seed for a month ("2026-10" → 202610): the demo rankings stay the same across launches.</summary>
        static int SeasonSeed(string season) => int.TryParse(season.Replace("-", ""), out int n) ? n : 0;

        /// <summary>Name shown for the local player in the demo ranking.</summary>
        public string PlayerName = "";

        string NameOf(string id)
        {
            if (id == Me) return string.IsNullOrEmpty(PlayerName) ? Loc.T("Toi") : PlayerName;
            lock (_names) return _names.TryGetValue(id, out var n) ? n : id;
        }
    }
}
