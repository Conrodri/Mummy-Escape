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
        public const string BoardCustomId = "pvp_board";              // classements vérifiés, en cache
        public const string HistoryKey = "pvp_history";               // derniers duels du joueur (protégé)
        public const string ReportsCustomId = "pvp_reports";          // dossiers de triche, une clé par joueur signalé

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

        public async Task<List<DuelRecord>> UpdateHistoryAsync(string playerId, Action<List<DuelRecord>> mutate)
        {
            Exception last = null;
            for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
            {
                var (history, writeLock) = await GetProtectedAsync<List<DuelRecord>>(playerId, HistoryKey);
                history ??= new List<DuelRecord>();
                if (mutate == null) return history;
                mutate(history);
                try
                {
                    await SetProtectedAsync(playerId, HistoryKey, history, writeLock);
                    return history;
                }
                catch (Exception e)
                {
                    last = e;
                    await Task.Delay(50 * (attempt + 1));
                }
            }
            _logger.LogWarning("Historique de {player} non enregistré : {msg}", playerId, last?.Message);
            return new List<DuelRecord>();
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

        Guid ProjectGuid => Guid.Parse(_ctx.ProjectId);

        static BoardEntry Entry(string playerId, string playerName, double score, int rank) => new BoardEntry
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Score = (int)Math.Round(score),
            Rank = rank + 1, // le service compte à partir de 0
        };

        string _lastVersionId;

        /// <summary>La version archivée la plus récente du classement (le mois dernier), null s'il n'y en a pas encore.</summary>
        async Task<string> LastVersionIdAsync()
        {
            if (_lastVersionId != null) return _lastVersionId;
            var versions = await _api.Leaderboards.GetLeaderboardVersionsAsync(_ctx, _ctx.ServiceToken, ProjectGuid, LeaderboardId, 1);
            return _lastVersionId = versions.Data.Results?.OrderByDescending(v => v.End).FirstOrDefault()?.Id;
        }

        public async Task<List<BoardEntry>> ReadBoardAsync(int seasonsAgo, int limit)
        {
            try
            {
                if (seasonsAgo == 0)
                {
                    var res = await _api.Leaderboards.GetLeaderboardScoresAsync(_ctx, _ctx.ServiceToken, ProjectGuid, LeaderboardId, null, 0, limit);
                    return res.Data.Results.Select(e => Entry(e.PlayerId, e.PlayerName, e.Score, e.Rank)).ToList();
                }
                string version = await LastVersionIdAsync();
                if (version == null) return new List<BoardEntry>();
                var past = await _api.Leaderboards.GetLeaderboardVersionScoresAsync(_ctx, _ctx.ServiceToken, ProjectGuid, LeaderboardId, version, null, 0, limit);
                return past.Data.Results.Select(e => Entry(e.PlayerId, e.PlayerName, e.Score, e.Rank)).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError("Classement illisible ({ago}) : {msg}", seasonsAgo, e.Message);
                return new List<BoardEntry>();
            }
        }

        public async Task<BoardEntry> ReadBoardEntryAsync(string playerId, int seasonsAgo)
        {
            try
            {
                if (seasonsAgo == 0)
                {
                    var res = await _api.Leaderboards.GetLeaderboardPlayerScoreAsync(_ctx, _ctx.ServiceToken, ProjectGuid, LeaderboardId, playerId);
                    return Entry(res.Data.PlayerId, res.Data.PlayerName, res.Data.Score, res.Data.Rank);
                }
                string version = await LastVersionIdAsync();
                if (version == null) return null;
                var past = await _api.Leaderboards.GetLeaderboardVersionPlayerScoreAsync(_ctx, _ctx.ServiceToken, ProjectGuid, LeaderboardId, version, playerId);
                return Entry(past.Data.PlayerId, past.Data.PlayerName, past.Data.Score, past.Data.Rank);
            }
            catch
            {
                return null; // pas classé ce mois-là
            }
        }

        /// <summary>Lit les données protégées par petits groupes, pour ne pas lancer cent requêtes d'un coup.</summary>
        public async Task<Dictionary<string, PlayerPvpData>> ReadPlayersAsync(IReadOnlyCollection<string> playerIds)
        {
            var found = new Dictionary<string, PlayerPvpData>();
            foreach (var chunk in playerIds.Distinct().Chunk(10))
            {
                var reads = chunk.Select(async id =>
                {
                    try { return (id, data: (await GetProtectedAsync<PlayerPvpData>(id, PlayerDataKey)).value); }
                    catch { return (id, data: (PlayerPvpData)null); } // joueur inconnu
                });
                foreach (var (id, data) in await Task.WhenAll(reads))
                    if (data != null) found[id] = data;
            }
            return found;
        }

        // ------------------------------------------------------------------ cheat reports (custom items)

        public async Task<CheatDossier> UpdateDossierAsync(string playerId, Action<CheatDossier> mutate)
        {
            Exception last = null;
            for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
            {
                var res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, ReportsCustomId, new List<string> { playerId });
                var item = res.Data.Results.FirstOrDefault(i => i.Key == playerId);
                var dossier = FromItem<CheatDossier>(item?.Value) ?? new CheatDossier { PlayerId = playerId };
                mutate?.Invoke(dossier);
                var body = item?.WriteLock == null ? new SetItemBody(playerId, ToToken(dossier)) : new SetItemBody(playerId, ToToken(dossier), item.WriteLock);
                try
                {
                    await _api.CloudSaveData.SetPrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, ReportsCustomId, body);
                    if (dossier.Flagged) _logger.LogWarning("Joueur {player} à vérifier : {n} signalements de joueurs différents", playerId, dossier.Reporters.Count);
                    return dossier;
                }
                catch (Exception e)
                {
                    last = e;
                    await Task.Delay(50 * (attempt + 1));
                }
            }
            throw new Exception("Signalement non enregistré pour " + playerId, last);
        }

        // ------------------------------------------------------------------ team objects (custom items)

        /// <summary>
        /// Combats, duos, guildes, index et files : un « private custom item » par objet (illisible par les joueurs), réécrit
        /// sous verrou d'écriture. <paramref name="mutate"/> peut être rappelé si un autre écrivain passe avant ; s'il renvoie
        /// null, rien n'est écrit.
        /// </summary>
        public async Task<T> UpdateSharedAsync<T>(string collection, string key, Func<T, T> mutate) where T : class
        {
            Exception last = null;
            for (int attempt = 0; attempt < SharedWriteRetries; attempt++)
            {
                var res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, collection, new List<string> { key });
                var item = res.Data.Results.FirstOrDefault(i => i.Key == key);
                var value = FromItem<T>(item?.Value);
                if (mutate == null) return value;
                value = mutate(value);
                if (value == null) return null;
                var body = item?.WriteLock == null ? new SetItemBody(key, ToToken(value)) : new SetItemBody(key, ToToken(value), item.WriteLock);
                try
                {
                    await _api.CloudSaveData.SetPrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, collection, body);
                    return value;
                }
                catch (Exception e)
                {
                    last = e;
                    await Task.Delay(50 * (attempt + 1));
                }
            }
            throw new Exception($"Écriture refusée : {collection}/{key}", last);
        }

        const int SharedWriteRetries = 8;

        public async Task DeleteSharedAsync(string collection, string key)
        {
            try { await _api.CloudSaveData.DeletePrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, collection, key, null); }
            catch (Exception e) { _logger.LogInformation("Rien à effacer ({c}/{k}) : {msg}", collection, key, e.Message); }
        }

        // ------------------------------------------------------------------ erasure (account deletion)

        /// <summary>Compte de service (Dashboard › Secret Manager) : seul autorisé à retirer un joueur du classement.</summary>
        public const string ServiceAccountKeySecret = "UGS_SERVICE_ACCOUNT_KEY";
        public const string ServiceAccountSecretSecret = "UGS_SERVICE_ACCOUNT_SECRET";

        static readonly Lazy<IAdminApiClient> Admin =
            new Lazy<IAdminApiClient>(Unity.Services.CloudCode.Apis.Admin.AdminApiClient.Create);

        public async Task DeletePlayerAsync(string playerId)
        {
            foreach (var key in new[] { PlayerDataKey, PendingDuelKey, HistoryKey })
            {
                try { await _api.CloudSaveData.DeleteProtectedItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId, key, null); }
                catch (Exception e) { _logger.LogInformation("Rien à effacer ({k}) : {msg}", key, e.Message); }
            }
            await DeleteSharedAsync(ReportsCustomId, playerId);
            try
            {
                var key = await _api.SecretManager.GetSecret(_ctx, ServiceAccountKeySecret);
                var secret = await _api.SecretManager.GetSecret(_ctx, ServiceAccountSecretSecret);
                await Admin.Value.Leaderboards.DeleteLeaderboardPlayerScoreAllLiveLeaderboardsAsync(_ctx, key.Value, secret.Value,
                    Guid.Parse(_ctx.ProjectId), Guid.Parse(_ctx.EnvironmentId), playerId);
            }
            catch (Exception e)
            {
                // Sans compte de service, l'entrée reste jusqu'à la suppression du joueur par Unity (Authentication).
                _logger.LogWarning("Entrée du classement non effacée pour {p} : {msg}", playerId, e.Message);
            }
        }

        // ------------------------------------------------------------------ verified ranking cache (custom items)

        static string BoardKey(string season) => "board_" + season;

        public async Task<PvpBoardPage> GetCachedBoardAsync(string season)
        {
            try
            {
                string key = BoardKey(season);
                var res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, BoardCustomId, new List<string> { key });
                var board = FromItem<PvpBoardPage>(res.Data.Results.FirstOrDefault(i => i.Key == key)?.Value);
                return board != null && board.BuiltAtUnixMs > 0 ? board : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Un cache effacé est réécrit vide et daté de 0 : périmé, il sera reconstruit.</summary>
        public async Task SetCachedBoardAsync(string season, PvpBoardPage board)
        {
            try
            {
                await _api.CloudSaveData.SetPrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, BoardCustomId,
                    new SetItemBody(BoardKey(season), ToToken(board ?? new PvpBoardPage())));
            }
            catch (Exception e)
            {
                _logger.LogWarning("Cache du classement non écrit : {msg}", e.Message);
            }
        }
    }
}
