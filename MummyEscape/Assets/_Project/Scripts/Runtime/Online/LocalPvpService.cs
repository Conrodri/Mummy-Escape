using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class LocalPvpService : IPvpService
    {
        const string PrefsKey = "pvp_demo";
        const string Me = "local";

        [Serializable]
        sealed class Stored
        {
            public PlayerPvpData Data;
            public PendingDuel Pending;
            public List<DuelRecord> History;
            // Team objects (duos, guilds, battles) and their indexes.
            public List<Duo> Duos;
            public List<Guild> Guilds;
            public List<TeamBattle> Battles;
            public List<DuoSummary> DuoIndex;
            public List<GuildSummary> GuildIndex;
        }

        static readonly (string name, string tag)[] DemoGuilds =
        {
            ("Fils d'Anubis", "ANUB"), ("Scarabées d'Or", "SCAR"), ("Vents du Désert", "VENT"), ("Gardiens du Nil", "NIL"), // noloc
            ("Ombres de Karnak", "KRNK"), ("Lames de Sekhmet", "LAME"), // noloc
        };

        /// <summary>Simulated members given to a guild the player creates offline, so a 10v10 war can be tried.</summary>
        const int DemoRecruits = 11;

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
            _server = new PvpServer(_store) { AllowTestPurchases = true };
            _store.MakeGhost = (player, elo) =>
            {
                GhostRun ghost;
                lock (_rng)
                {
                    ghost = PvpBots.Make(_rng, elo, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    ghost.Look = Visual.PvpSkins.RandomLook(_rng);
                }
                lock (_names) _names[ghost.PlayerId] = ghost.PlayerName;
                return ghost;
            };
            // Teams offline: every other player is simulated (friends accept at once, rivals run their rounds at once).
            _server.IsBot = id => id != Me;
            _server.BotName = BotNameOf;
            _server.BotRun = (id, seed, elo) =>
            {
                lock (_rng)
                {
                    var g = PvpBots.Make(_rng, elo, seed, id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    return new SlotRun
                    {
                        PlayerId = id, PlayerName = BotNameOf(id), Look = Visual.PvpSkins.RandomLook(_rng), Outcome = g.Outcome,
                        TimeMs = g.TimeMs, Progress = g.Progress, Inputs = g.Inputs, RunAtUnixMs = g.CreatedAtUnixMs,
                    };
                }
            };
            _server.BotSide = (kind, slots, elo) =>
            {
                // A war is fought against one of the demo guilds, which records it.
                if (kind == BattleKind.GuildWar && DemoWarSide(slots, elo) is BattleSide guild) return guild;
                lock (_rng)
                {
                    string team = "botteam_" + _rng.Next(); // noloc
                    var order = new List<string>();
                    if (kind == BattleKind.Duo)
                    {
                        string a = team + "_a", b = team + "_b"; // noloc
                        order.AddRange(new[] { a, b, a });
                    }
                    else for (int i = 0; i < slots; i++) order.Add(team + "_" + i);
                    foreach (var id in order.Distinct())
                        lock (_names) _names[id] = DemoNames[_rng.Next(DemoNames.Length)] + "#" + (1000 + _rng.Next(9000));
                    var g = DemoGuilds[_rng.Next(DemoGuilds.Length)];
                    string name = kind == BattleKind.Duo ? BotNameOf(order[0]) + " & " + BotNameOf(order[1]) : $"[{g.tag}] {g.name}";
                    return TeamLogic.NewSide(team, name, Math.Max(PvpConfig.MinElo, elo + _rng.Next(-120, 121)), order, BotNameOf);
                }
            };
            Load();
            SeedDemoRivals();
            SeedDemoGuilds();
            EnrollDemoMembers();
        }

        string BotNameOf(string id)
        {
            lock (_names) if (_names.TryGetValue(id, out var n)) return n;
            if (id.StartsWith("demo-")) return id.Substring(5); // offline friends are "demo-Name#1234" // noloc
            return PvpBots.NameOf(id);
        }

        /// <summary>A few guilds to find and join offline (the same every launch).</summary>
        void SeedDemoGuilds()
        {
            if (_store.Shared.ContainsKey(PvpServer.IndexCollection + "/" + PvpServer.GuildIndexKey)) return;
            var rng = new System.Random(0x6e11d);
            var index = new List<GuildSummary>();
            for (int g = 0; g < DemoGuilds.Length; g++)
            {
                var guild = new Guild { Id = "demo_guild_" + g, Name = DemoGuilds[g].name, Tag = DemoGuilds[g].tag, WarElo = 850 + rng.Next(500) }; // noloc
                int members = 6 + rng.Next(20);
                for (int i = 0; i < members; i++)
                {
                    string id = $"gm_{g}_{i}"; // noloc
                    string name = DemoNames[rng.Next(DemoNames.Length)] + "#" + (1000 + rng.Next(9000));
                    _names[id] = name;
                    int points = rng.Next(0, 120);
                    guild.Members.Add(new GuildMember { PlayerId = id, Name = name, Role = i == 0 ? GuildRole.Leader : i < 3 ? GuildRole.Officer : GuildRole.Member, Points = points });
                    guild.Points += points;
                }
                guild.WarWins = rng.Next(30);
                guild.WarLosses = rng.Next(30);
                _store.Shared[PvpServer.GuildsCollection + "/" + guild.Id] = guild;
                index.Add(new GuildSummary
                {
                    Id = guild.Id, Name = guild.Name, Tag = guild.Tag, MemberCount = guild.Members.Count, Points = guild.Points,
                    WarElo = guild.WarElo, WarWins = guild.WarWins, WarLosses = guild.WarLosses,
                });
            }
            _store.Shared[PvpServer.IndexCollection + "/" + PvpServer.GuildIndexKey] = index;
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
                    s.Data.Duos ??= new List<string>();
                    s.Data.DuoInvites ??= new List<DuoInvite>();
                    if (string.IsNullOrEmpty(s.Data.GuildId)) s.Data.GuildId = null;
                    if (s.Data.SeasonDuels > 0) s.Data.Ranked = true; // demo saves made before the flag existed
                    // Demo saves made before the month's record existed: the overall record is the closest guess.
                    if (s.Data.SeasonDuels > 0 && s.Data.SeasonWins + s.Data.SeasonLosses + s.Data.SeasonDraws == 0)
                    {
                        s.Data.SeasonWins = s.Data.Wins;
                        s.Data.SeasonLosses = s.Data.Losses;
                        s.Data.SeasonDraws = s.Data.Draws;
                    }
                    _store.Players[Me] = s.Data;
                    // The demo leaderboard is not saved: put the player back on it.
                    if (s.Data.Ranked && s.Data.Season == Seasons.SeasonOf(DateTime.UtcNow)) _store.Board[Me] = s.Data.Elo;
                }
                if (s.Pending != null && !string.IsNullOrEmpty(s.Pending.MatchId))
                {
                    if (s.Pending.Ghost != null && string.IsNullOrEmpty(s.Pending.Ghost.GhostId)) s.Pending.Ghost = null;
                    if (string.IsNullOrEmpty(s.Pending.BattleId)) s.Pending.BattleId = null;
                    if (s.Pending.Ghost != null && s.Pending.Ghost.Look != null && string.IsNullOrEmpty(s.Pending.Ghost.Look.Mummy)) s.Pending.Ghost.Look = null;
                    _store.Pending[Me] = s.Pending;
                }
                if (s.History != null)
                {
                    foreach (var r in s.History)
                    {
                        if (r.Rival != null && string.IsNullOrEmpty(r.Rival.PlayerId)) r.Rival = null;
                        FixLook(r.Me);
                        FixLook(r.Rival);
                    }
                    _store.History[Me] = s.History;
                }
                LoadTeams(s);
            }
            catch (Exception e) { Debug.LogWarning("[Pvp] Unreadable demo data: " + e.Message); }
        }

        /// <summary>Team objects back in the store; JsonUtility turns null objects into empty ones, so the nulls go back.</summary>
        void LoadTeams(Stored s)
        {
            foreach (var d in s.Duos ?? new List<Duo>())
            {
                if (string.IsNullOrEmpty(d.ActiveBattle)) d.ActiveBattle = null;
                _store.Shared[PvpServer.DuosCollection + "/" + d.Id] = d;
            }
            foreach (var g in s.Guilds ?? new List<Guild>())
            {
                if (string.IsNullOrEmpty(g.ActiveWar)) g.ActiveWar = null;
                _store.Shared[PvpServer.GuildsCollection + "/" + g.Id] = g;
                foreach (var m in g.Members) _names[m.PlayerId] = m.Name;
            }
            foreach (var b in s.Battles ?? new List<TeamBattle>())
            {
                if (b.B != null && string.IsNullOrEmpty(b.B.TeamId)) b.B = null;
                foreach (var side in new[] { b.A, b.B })
                {
                    if (side == null) continue;
                    for (int k = 0; k < side.Runs.Count; k++)
                    {
                        var r = side.Runs[k];
                        if (r == null || string.IsNullOrEmpty(r.PlayerId)) side.Runs[k] = null;
                        else if (r.Look != null && string.IsNullOrEmpty(r.Look.Mummy)) r.Look = null;
                    }
                    for (int k = 0; k < side.Order.Count && k < side.Names.Count; k++) _names[side.Order[k]] = side.Names[k];
                }
                _store.Shared[PvpServer.BattlesCollection + "/" + b.Id] = b;
            }
            if (s.DuoIndex != null) _store.Shared[PvpServer.IndexCollection + "/" + PvpServer.DuoIndexKey] = s.DuoIndex;
            if (s.GuildIndex != null && s.GuildIndex.Count > 0) _store.Shared[PvpServer.IndexCollection + "/" + PvpServer.GuildIndexKey] = s.GuildIndex;
        }

        T SharedOf<T>(string collection, string key) where T : class =>
            _store.Shared.TryGetValue(collection + "/" + key, out var v) ? v as T : null;

        // The whole index (old single key and shards), saved back under the single key: the server merges it again.
        List<T> IndexOf<T>(string baseKey, Func<T, string> id) =>
            PvpServer.MergeIndex(PvpServer.IndexKeys(baseKey).Select(k => SharedOf<List<T>>(PvpServer.IndexCollection, k)), id);

        void Persist()
        {
            _store.Players.TryGetValue(Me, out var data);
            _store.Pending.TryGetValue(Me, out var pending);
            _store.History.TryGetValue(Me, out var history);
            var stored = new Stored
            {
                Data = data, Pending = pending, History = history,
                Duos = _store.Shared.Values.OfType<Duo>().ToList(),
                Guilds = _store.Shared.Values.OfType<Guild>().ToList(),
                Battles = _store.Shared.Values.OfType<TeamBattle>().ToList(),
                DuoIndex = IndexOf<DuoSummary>(PvpServer.DuoIndexKey, s => s.Id),
                GuildIndex = IndexOf<GuildSummary>(PvpServer.GuildIndexKey, s => s.Id),
            };
            // Only the battles still listed by a duo or a guild are kept.
            var kept = new HashSet<string>(stored.Duos.SelectMany(d => d.RecentBattles.Append(d.ActiveBattle))
                                                       .Concat(stored.Guilds.SelectMany(g => g.RecentWars.Append(g.ActiveWar))).Where(id => id != null));
            if (pending?.BattleId != null) kept.Add(pending.BattleId);
            stored.Battles.RemoveAll(b => !kept.Contains(b.Id));
            // JsonUtility.ToJson fills the null entries of a list with empty objects, in the live objects too: a round not run
            // yet would come out as a run "finished in 0 ms". The holes are put back afterwards.
            var holes = new List<(List<SlotRun> runs, int slot)>();
            foreach (var b in stored.Battles)
                foreach (var side in new[] { b.A, b.B })
                    if (side != null)
                        for (int k = 0; k < side.Runs.Count; k++)
                            if (side.Runs[k] == null) holes.Add((side.Runs, k));
            string json = JsonUtility.ToJson(stored);
            foreach (var (runs, slot) in holes) runs[slot] = null;
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();
        }

        /// <summary>JsonUtility reads a missing look as an empty one.</summary>
        static void FixLook(DuelRun run)
        {
            if (run != null && run.Look != null && string.IsNullOrEmpty(run.Look.Mummy)) run.Look = null;
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
                int duels = 5 + rng.Next(60);
                var (wins, draws, losses) = DemoRecord(rng, duels, elo);
                _store.Players[id] = new PlayerPvpData
                {
                    Elo = elo, Season = season, SeasonDuels = duels, BestEloThisSeason = elo, Ranked = true,
                    SeasonWins = wins, SeasonDraws = draws, SeasonLosses = losses,
                };
                _store.Board[id] = elo;
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

        public Task<FindDuelResponse> FindDuelAsync(bool allowBots) => Run(() => _server.FindDuelAsync(Me, Core.DifficultyTable.GeneratorVersion, allowBots));

        public Task<SubmitRunResponse> SubmitRunAsync(RunSubmission run, string playerName) => Run(() => _server.SubmitRunAsync(Me, run, playerName));

        public Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync() => Run(() => _server.ClaimSeasonRewardsAsync(Me));

        public Task<SealPurchaseResponse> BuyWithSealsAsync(string itemId) => Run(() => _server.BuyWithSealsAsync(Me, itemId));

        public Task<WheelSpinResponse> SpinSealWheelAsync() => Run(() => _server.SpinSealWheelAsync(Me));

        public Task SyncSoloStarsAsync(int stars) => Task.CompletedTask; // read live from the save

        public Task<DuelHistoryResponse> GetHistoryAsync() => Run(() => _server.GetHistoryAsync(Me));

        public Task<ReportResponse> ReportCheatAsync(string matchId) => Run(() => _server.ReportCheatAsync(Me, matchId));

        // ------------------------------------------------------------------ teams

        static int Gen => Core.DifficultyTable.GeneratorVersion;

        public Task<TeamsResponse> GetTeamsAsync() => Run(() => _server.GetTeamsAsync(Me));

        public Task<TeamActionResponse> InviteDuoAsync(string friendId, string playerName) => Run(() => _server.InviteDuoAsync(Me, playerName, friendId));

        public Task<TeamActionResponse> RespondDuoAsync(string inviteId, bool accept, string playerName) =>
            Run(() => _server.RespondDuoAsync(Me, playerName, inviteId, accept));

        public Task<TeamActionResponse> LeaveDuoAsync(string duoId) => Run(() => _server.LeaveDuoAsync(Me, duoId));

        public Task<TeamActionResponse> FindDuoMatchAsync(string duoId, bool meFirst) => Run(() => _server.FindDuoMatchAsync(Me, duoId, meFirst, Gen));

        public Task<DuoBoardResponse> GetDuoBoardAsync(int limit) => Run(async () =>
        {
            var board = await _server.GetDuoBoardAsync(limit);
            // A demo ranking around the player's duos.
            var rng = new System.Random(0xd00);
            for (int i = 0; i < 20; i++)
            {
                string a = DemoNames[rng.Next(DemoNames.Length)], b = DemoNames[rng.Next(DemoNames.Length)];
                int w = rng.Next(4, 40), l = rng.Next(4, 40);
                board.Rows.Add(new DuoSummary { Id = "demo_duo_" + i, Name = a + " & " + b, Elo = 1000 + (w - l) * 12 + rng.Next(-60, 61), Wins = w, Losses = l }); // noloc
            }
            board.Rows = board.Rows.OrderByDescending(r => r.Elo).Take(limit).ToList();
            return board;
        });

        public Task<FindDuelResponse> StartBattleRunAsync(string battleId) => Run(() => _server.StartBattleRunAsync(Me, battleId, Gen));

        public Task<TeamBattle> GetBattleAsync(string battleId) => Run(() => _server.GetBattleAsync(Me, battleId));

        public Task<RelayMatchResponse> StartRelayMatchAsync(string matchKey, RelaySide a, RelaySide b) =>
            Run(() => _server.StartRelayMatchAsync(Me, Gen, matchKey, a, b));

        public Task<RelayMatchResponse> StartRelayBotsAsync(string matchKey, RelaySide mine) => Run(async () =>
        {
            var r = await _server.StartRelayBotsAsync(Me, Gen, matchKey, mine);
            // Simulated players wear random outfits, as in the duels.
            if (r.Match != null)
                lock (_rng)
                    foreach (var side in new[] { r.Match.A, r.Match.B })
                        foreach (var runner in side.Runners)
                            if (runner.PlayerId != Me && runner.Look == null) runner.Look = Visual.PvpSkins.RandomLook(_rng);
            return r;
        });

        public Task<RelayResultResponse> SubmitRelayAsync(string matchId, string starter, List<RelayInput> inputs, List<string> quitters) =>
            Run(() => _server.SubmitRelayAsync(Me, matchId, starter, inputs, quitters));

        public Task<RelayResultResponse> GetRelayResultAsync(string matchId) => Run(() => _server.RelayResultAsync(Me, matchId));

        public Task<RelayHistoryResponse> GetRelayHistoryAsync() => Run(() => _server.GetRelayHistoryAsync(Me));

        public Task<ChatPage> GetChatAsync(string channel, long afterSeq) => Run(async () =>
        {
            SeedDemoChat();
            var guild = MyGuild();
            if (guild != null) SeedGuildChat(guild, new System.Random(guild.Id.GetHashCode()));
            await AmbientAsync(channel);
            return await _server.GetChatAsync(Me, channel, afterSeq);
        });

        public async Task<ChatSendResponse> SendChatAsync(string channel, string text, string playerName)
        {
            var r = await Run(() => _server.SendChatAsync(Me, playerName, channel, text));
            if (r?.Ok == true) _ = ReplyLaterAsync(channel, text ?? "");
            return r;
        }

        public Task<ChatInboxResponse> GetChatInboxAsync() => Run(() =>
        {
            SeedDemoChat();
            return _server.GetChatInboxAsync(Me);
        });

        public Task<ReportResponse> BlockChatAsync(string playerId, bool block) => Run(() => _server.BlockChatAsync(Me, playerId, block));
        public Task<ReportResponse> SyncChatProfileAsync(List<string> friendIds, bool minor) => Run(() => _server.SyncChatProfileAsync(Me, friendIds, minor));

        public Task<ReportResponse> ReportChatAsync(string channel, long seq) => Run(() => _server.ReportChatAsync(Me, channel, seq));

        public Task<ChatSendResponse> ShareReplayAsync(string kind, string matchId, string channel, string text, string playerName) =>
            Run(() => _server.ShareReplayAsync(Me, playerName, kind, matchId, channel, text));

        public Task<SharedReplayResponse> GetSharedReplayAsync(string id) => Run(() => _server.GetSharedReplayAsync(Me, id));
        public Task<PvpDataExportResponse> ExportDataAsync() => Run(() => _server.ExportPlayerDataAsync(Me));
        public Task<ReportResponse> DeleteDataAsync() => Run(() => _server.DeletePlayerDataAsync(Me));
        public Task<EnergyResponse> RefillEnergyAsync() => Run(() => _server.RefillEnergyAsync(Me));
        public Task<WalletResponse> GetWalletAsync() => Run(() => _server.GetWalletAsync(Me));
        public Task<WalletResponse> VerifyPurchaseAsync(string productId, string purchaseToken) => Run(() => _server.VerifyPurchaseAsync(Me, productId, purchaseToken));
        public Task<WalletResponse> BuyPassAsync() => Run(() => _server.BuyPassAsync(Me));
        public Task<WalletResponse> BuyTiersAsync() => Run(() => _server.BuyTiersAsync(Me));
        public Task<WalletResponse> BuyScarabsAsync(string offerId) => Run(() => _server.BuyScarabsAsync(Me, offerId));
        public Task<WalletResponse> BuyGoldItemAsync(string skinId) => Run(() => _server.BuyGoldItemAsync(Me, skinId));
        public Task<WalletResponse> ClaimPassRewardsAsync(string season, List<int> freeTiers, List<int> premiumTiers) =>
            Run(() => _server.ClaimPassRewardsAsync(Me, season, freeTiers, premiumTiers));

        public Task<LiveDuelResponse> StartLiveDuelAsync(string matchKey, LiveDuelist a, LiveDuelist b) =>
            Run(() => _server.StartLiveDuelAsync(Me, Gen, matchKey, a, b));

        public Task<LiveDuelResponse> StartLiveBotDuelAsync(string matchKey, LiveDuelist mine) => Run(async () =>
        {
            var r = await _server.StartLiveBotDuelAsync(Me, Gen, matchKey, mine);
            // Simulated players wear random outfits.
            if (r.Match != null && r.Match.B.Look == null)
                lock (_rng) r.Match.B.Look = r.Match.BotRun.Look = Visual.PvpSkins.RandomLook(_rng);
            return r;
        });

        public Task<LiveDuelResponse> GetLiveDuelAsync(string matchId) => Run(() => _server.GetLiveDuelAsync(Me, matchId));

        public Task<SubmitRunResponse> SubmitLiveDuelAsync(string matchId, RunSubmission run, string playerName) =>
            Run(() => _server.SubmitLiveDuelAsync(Me, matchId, run, playerName));

        public Task<SubmitRunResponse> GetLiveDuelResultAsync(string matchId) => Run(() => _server.LiveDuelResultAsync(Me, matchId));

        public Task<ReportResponse> ReportRelayQuitAsync(string matchId, string quitterId) => Run(() => _server.ReportRelayQuitAsync(Me, matchId, quitterId));

        public Task<GuildResponse> GetGuildAsync() => Run(async () =>
        {
            SeedDemoWars();
            var r = await _server.GetGuildAsync(Me);
            await AnnounceWarsAsync(r);
            return r;
        });

        public Task<GuildResponse> CreateGuildAsync(string name, string tag, string playerName) => Run(async () =>
        {
            var r = await _server.CreateGuildAsync(Me, playerName, name, tag);
            if (r.Guild == null) return r;
            // Offline, a few simulated recruits join at once.
            for (int i = 0; i < DemoRecruits; i++)
            {
                string id = "recruit_" + r.Guild.Id + "_" + i; // noloc
                lock (_rng) lock (_names) _names[id] = DemoNames[_rng.Next(DemoNames.Length)] + "#" + (1000 + _rng.Next(9000));
                await _server.JoinGuildAsync(id, BotNameOf(id), r.Guild.Id);
            }
            EnrollDemoMembers();
            return await _server.GetGuildAsync(Me);
        });

        public Task<GuildSearchResponse> SearchGuildsAsync(string query, int limit) => Run(() =>
        {
            SeedDemoWars();
            return _server.SearchGuildsAsync(query, limit);
        });

        public Task<GuildSearchResponse> GetGuildBoardAsync(int limit) => Run(() => _server.GetGuildBoardAsync(limit));

        public Task<GuildResponse> JoinGuildAsync(string guildId, string playerName) => Run(async () =>
        {
            SeedDemoWars();
            EnrollDemoMembers();
            var r = await _server.JoinGuildAsync(Me, playerName, guildId);
            if (r.Guild == null) return r;
            await WelcomeAsync(playerName);
            return await _server.GetGuildAsync(Me);
        });

        public Task<GuildResponse> LeaveGuildAsync() => Run(() => _server.LeaveGuildAsync(Me));

        public Task<GuildResponse> SetGuildRoleAsync(string memberId, bool officer) => Run(() => _server.SetGuildRoleAsync(Me, memberId, officer));

        public Task<GuildResponse> KickGuildMemberAsync(string memberId) => Run(() => _server.KickGuildMemberAsync(Me, memberId));

        public Task<GuildResponse> StartWarAsync(int size, List<string> order) => Run(async () =>
        {
            var r = await _server.StartWarAsync(Me, size, order, Gen);
            await AnnounceWarsAsync(r);
            return r;
        });

        public async Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit)
        {
            var profile = await GetProfileAsync(); // rolls the season first
            if (seasonsAgo == 0)
            {
                // This month: the server's checked ranking, as online.
                var board = await Run(() => _server.GetBoardAsync(Me, 0, limit));
                foreach (var row in board.Rows) row.PlayerName = NameOf(row.PlayerId);
                if (board.Me != null) board.Me.PlayerName = NameOf(Me);
                return board;
            }

            // Past months: a frozen demo ranking, with the player at their final Elo if they played that month.
            var page = new PvpBoardPage { Season = Seasons.SeasonOf(DateTime.UtcNow.AddMonths(-seasonsAgo)) };
            var entries = new List<PvpBoardRow>();
            var rng = new System.Random(SeasonSeed(page.Season));
            for (int i = 0; i < DemoNames.Length; i++)
            {
                int elo = 700 + rng.Next(0, 1000);
                var (wins, draws, losses) = DemoRecord(rng, 15 + rng.Next(70), elo);
                entries.Add(new PvpBoardRow { PlayerId = "old_" + i, Elo = elo, Wins = wins, Draws = draws, Losses = losses, PlayerName = DemoNames[(i * 7 + 3) % DemoNames.Length] + "#" + (1000 + rng.Next(9000)) });
            }
            var last = profile.Data?.LastSeason;
            if (seasonsAgo == 1 && last != null && last.Duels > 0)
                entries.Add(new PvpBoardRow { PlayerId = Me, Elo = last.FinalElo, Wins = last.Wins, Losses = last.Losses, Draws = last.Draws, IsMe = true, PlayerName = NameOf(Me) });
            entries.Sort((a, b) => b.Elo.CompareTo(a.Elo));
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = i + 1;
                if (entries[i].IsMe) page.Me = entries[i];
                if (i < limit) page.Rows.Add(entries[i]);
            }
            return page;
        }

        /// <summary>A record that fits the Elo: the better rivals win more often.</summary>
        static (int wins, int draws, int losses) DemoRecord(System.Random rng, int duels, int elo)
        {
            double rate = Math.Max(0.2, Math.Min(0.8, 0.5 + (elo - 1100) / 1600.0));
            int draws = duels / 12;
            int wins = (int)Math.Round((duels - draws) * rate) + rng.Next(-2, 3);
            wins = Math.Max(0, Math.Min(duels - draws, wins));
            return (wins, draws, duels - draws - wins);
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
