// Stockage du PvP sur Unity Cloud : données joueur protégées (le client les lit, seul le serveur les écrit), file de
// fantômes en « private custom items » (illisibles par les joueurs : une clé par version du générateur et tranche de
// 100 Elo), classement pvp_elo.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;
using Unity.Services.Leaderboards.Model;

namespace MummyEscape.Pvp.Server
{
    public sealed class CloudSavePvpStore : IPvpStore
    {
        public const string LeaderboardId = "pvp_elo";               // à créer dans le Dashboard (voir DEPLOY.md)
        public const string SoloStarsKey = "solo_total_stars";        // écrit par le jeu à chaque nouvelle étoile
        public const string PlayerDataKey = "pvp";
        public const string PendingDuelKey = "pvp_pending";
        public const string QueueCustomId = "pvp_queue";

        const int MaxWriteRetries = 4;

        readonly IGameApiClient _api;
        readonly IExecutionContext _ctx;
        readonly ILogger _logger;

        public CloudSavePvpStore(IGameApiClient api, IExecutionContext ctx, ILogger logger)
        {
            _api = api;
            _ctx = ctx;
            _logger = logger;
        }

        static T FromItem<T>(object value) where T : class
        {
            if (value == null) return null;
            string json = value is string s ? s : JsonConvert.SerializeObject(value);
            return JsonConvert.DeserializeObject<T>(json);
        }

        static JToken ToToken(object value) => value == null ? JValue.CreateNull() : JToken.FromObject(value);

        // ------------------------------------------------------------------ player data (protected)

        async Task<(T value, string writeLock)> GetProtectedAsync<T>(string playerId, string key) where T : class
        {
            var res = await _api.CloudSaveData.GetProtectedItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId, new List<string> { key });
            var item = res.Data.Results.FirstOrDefault(i => i.Key == key);
            return item == null ? (null, null) : (FromItem<T>(item.Value), item.WriteLock);
        }

        Task SetProtectedAsync(string playerId, string key, object value, string writeLock)
        {
            var body = writeLock == null ? new SetItemBody(key, ToToken(value)) : new SetItemBody(key, ToToken(value), writeLock);
            return _api.CloudSaveData.SetProtectedItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId, body);
        }

        /// <summary>Lecture, modification, écriture avec le verrou d'écriture ; recommence si quelqu'un a écrit entre-temps.</summary>
        public async Task<PlayerPvpData> UpdatePlayerAsync(string playerId, Action<PlayerPvpData> mutate)
        {
            Exception last = null;
            for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
            {
                var (data, writeLock) = await GetProtectedAsync<PlayerPvpData>(playerId, PlayerDataKey);
                data ??= new PlayerPvpData();
                mutate?.Invoke(data);
                try
                {
                    await SetProtectedAsync(playerId, PlayerDataKey, data, writeLock);
                    return data;
                }
                catch (Exception e)
                {
                    last = e;
                    _logger.LogWarning("Conflit d'écriture sur {player} (essai {n}) : {msg}", playerId, attempt + 1, e.Message);
                    await Task.Delay(50 * (attempt + 1));
                }
            }
            throw new Exception("Impossible d'enregistrer les données PvP de " + playerId, last);
        }

        public async Task<PendingDuel> GetPendingAsync(string playerId)
        {
            var (pending, _) = await GetProtectedAsync<PendingDuel>(playerId, PendingDuelKey);
            return pending != null && !string.IsNullOrEmpty(pending.MatchId) ? pending : null;
        }

        public Task SetPendingAsync(string playerId, PendingDuel pending) =>
            SetProtectedAsync(playerId, PendingDuelKey, pending ?? new PendingDuel(), null);

        public async Task<int> GetSoloStarsAsync(string playerId)
        {
            var res = await _api.CloudSaveData.GetItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId, new List<string> { SoloStarsKey });
            var item = res.Data.Results.FirstOrDefault(i => i.Key == SoloStarsKey);
            return item?.Value != null && int.TryParse(item.Value.ToString(), out int stars) ? stars : 0;
        }

        // ------------------------------------------------------------------ ghost queue (custom items)

        static string BucketKey(int version, int bucket) => "v" + version + "_b" + bucket;

        async Task<(List<GhostRun> ghosts, string writeLock)> GetBucketAsync(string key)
        {
            var res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, QueueCustomId, new List<string> { key });
            var item = res.Data.Results.FirstOrDefault(i => i.Key == key);
            return item == null ? (new List<GhostRun>(), null) : (FromItem<List<GhostRun>>(item.Value) ?? new List<GhostRun>(), item.WriteLock);
        }

        Task SetBucketAsync(string key, List<GhostRun> ghosts, string writeLock)
        {
            var body = writeLock == null ? new SetItemBody(key, ToToken(ghosts)) : new SetItemBody(key, ToToken(ghosts), writeLock);
            return _api.CloudSaveData.SetPrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, QueueCustomId, body);
        }

        /// <summary>Le meilleur fantôme, de la tranche la plus proche à ±300 Elo ; retiré de la file (il ne sert qu'une fois).</summary>
        public async Task<GhostRun> ClaimGhostAsync(string playerId, int elo, int generatorVersion, IDictionary<string, int> opponentsToday, long nowMs)
        {
            foreach (int bucket in GhostPicker.BucketsToSearch(elo))
            {
                string key = BucketKey(generatorVersion, bucket);
                for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
                {
                    var (ghosts, writeLock) = await GetBucketAsync(key);
                    int before = ghosts.Count;
                    ghosts.RemoveAll(g => g == null || GhostPicker.IsExpired(g, nowMs));
                    var pick = GhostPicker.Pick(ghosts, playerId, elo, opponentsToday, nowMs);
                    if (pick == null)
                    {
                        if (ghosts.Count != before)
                            try { await SetBucketAsync(key, ghosts, writeLock); } catch { /* nettoyage facultatif */ }
                        break; // tranche suivante
                    }
                    ghosts.Remove(pick);
                    try
                    {
                        await SetBucketAsync(key, ghosts, writeLock);
                        return pick;
                    }
                    catch (Exception e)
                    {
                        _logger.LogInformation("Fantôme disputé ({key}), nouvel essai : {msg}", key, e.Message);
                        await Task.Delay(30 * (attempt + 1));
                    }
                }
            }
            return null;
        }

        public async Task EnqueueGhostAsync(GhostRun ghost, long nowMs)
        {
            string key = BucketKey(ghost.GeneratorVersion, GhostPicker.BucketOf(ghost.Elo));
            for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
            {
                var (ghosts, writeLock) = await GetBucketAsync(key);
                ghosts.RemoveAll(g => g == null || GhostPicker.IsExpired(g, nowMs));
                ghosts.Add(ghost);
                if (ghosts.Count > PvpConfig.MaxGhostsPerBucket)
                    ghosts = ghosts.OrderByDescending(g => g.CreatedAtUnixMs).Take(PvpConfig.MaxGhostsPerBucket).ToList();
                try
                {
                    await SetBucketAsync(key, ghosts, writeLock);
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogInformation("File occupée ({key}), nouvel essai : {msg}", key, e.Message);
                    await Task.Delay(30 * (attempt + 1));
                }
            }
            _logger.LogWarning("Fantôme {id} non ajouté à la file après plusieurs essais", ghost.GhostId);
        }

        // ------------------------------------------------------------------ leaderboard

        public async Task SubmitEloAsync(string playerId, int elo)
        {
            try
            {
                await _api.Leaderboards.AddLeaderboardPlayerScoreAsync(_ctx, _ctx.ServiceToken, Guid.Parse(_ctx.ProjectId),
                    LeaderboardId, playerId, new AddLeaderboardScore(elo));
            }
            catch (Exception e)
            {
                _logger.LogError("Classement non mis à jour pour {player} : {msg}", playerId, e.Message);
            }
        }

        public async Task<int> GetWorldRankAsync(string playerId)
        {
            try
            {
                var res = await _api.Leaderboards.GetLeaderboardPlayerScoreAsync(_ctx, _ctx.ServiceToken, Guid.Parse(_ctx.ProjectId),
                    LeaderboardId, playerId);
                return Convert.ToInt32(res.Data.Rank) + 1; // le service compte à partir de 0
            }
            catch
            {
                return 0; // pas encore de score ce mois-ci
            }
        }

        /// <summary>Rang final au mois précédent : la dernière version archivée du classement.</summary>
        public async Task<int> GetLastSeasonRankAsync(string playerId)
        {
            try
            {
                var versions = await _api.Leaderboards.GetLeaderboardVersionsAsync(_ctx, _ctx.ServiceToken, Guid.Parse(_ctx.ProjectId), LeaderboardId);
                var latest = versions.Data.Results?.FirstOrDefault();
                if (latest == null) return 0;
                var res = await _api.Leaderboards.GetLeaderboardVersionPlayerScoreAsync(_ctx, _ctx.ServiceToken, Guid.Parse(_ctx.ProjectId),
                    LeaderboardId, latest.Id, playerId);
                return Convert.ToInt32(res.Data.Rank) + 1;
            }
            catch
            {
                return 0; // pas classé le mois dernier
            }
        }
    }
}
