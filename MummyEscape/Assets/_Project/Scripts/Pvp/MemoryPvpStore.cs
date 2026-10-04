// Mummy Escape PvP — stockage en mémoire : tests, et duels hors ligne contre des adversaires simulés.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    public sealed class MemoryPvpStore : IPvpStore
    {
        public readonly Dictionary<string, PlayerPvpData> Players = new Dictionary<string, PlayerPvpData>();
        public readonly Dictionary<string, PendingDuel> Pending = new Dictionary<string, PendingDuel>();
        public readonly Dictionary<string, int> SoloStars = new Dictionary<string, int>();
        /// <summary>Fantômes en attente, toutes tranches confondues.</summary>
        public readonly List<GhostRun> Queue = new List<GhostRun>();
        /// <summary>Rang final de chaque joueur au mois précédent (simulé).</summary>
        public readonly Dictionary<string, int> LastSeasonRanks = new Dictionary<string, int>();
        /// <summary>Appelé quand rien n'attend dans la file : un adversaire simulé (mode hors ligne), ou null.</summary>
        public Func<string, int, GhostRun> MakeGhost;

        public Task<int> GetSoloStarsAsync(string playerId) =>
            Task.FromResult(SoloStars.TryGetValue(playerId, out int s) ? s : 0);

        public Task<PlayerPvpData> UpdatePlayerAsync(string playerId, Action<PlayerPvpData> mutate)
        {
            if (!Players.TryGetValue(playerId, out var d)) Players[playerId] = d = new PlayerPvpData();
            mutate?.Invoke(d);
            return Task.FromResult(d);
        }

        public Task<PendingDuel> GetPendingAsync(string playerId) =>
            Task.FromResult(Pending.TryGetValue(playerId, out var p) ? p : null);

        public Task SetPendingAsync(string playerId, PendingDuel pending)
        {
            if (pending == null) Pending.Remove(playerId); else Pending[playerId] = pending;
            return Task.CompletedTask;
        }

        public Task<GhostRun> ClaimGhostAsync(string playerId, int elo, int generatorVersion, IDictionary<string, int> opponentsToday, long nowMs)
        {
            Queue.RemoveAll(g => g == null || GhostPicker.IsExpired(g, nowMs));
            var pick = GhostPicker.Pick(Queue.FindAll(g => g.GeneratorVersion == generatorVersion), playerId, elo, opponentsToday, nowMs);
            if (pick != null) Queue.Remove(pick);
            else pick = MakeGhost?.Invoke(playerId, elo);
            return Task.FromResult(pick);
        }

        public Task EnqueueGhostAsync(GhostRun ghost, long nowMs)
        {
            Queue.RemoveAll(g => g == null || GhostPicker.IsExpired(g, nowMs));
            Queue.Add(ghost);
            return Task.CompletedTask;
        }

        public Task SubmitEloAsync(string playerId, int elo) => Task.CompletedTask;

        /// <summary>Rang parmi les joueurs connus, par Elo décroissant.</summary>
        public Task<int> GetWorldRankAsync(string playerId)
        {
            if (!Players.TryGetValue(playerId, out var me) || me.SeasonDuels == 0) return Task.FromResult(0);
            int rank = 1;
            foreach (var kv in Players)
                if (kv.Key != playerId && kv.Value.SeasonDuels > 0 && kv.Value.Elo > me.Elo) rank++;
            return Task.FromResult(rank);
        }

        public Task<int> GetLastSeasonRankAsync(string playerId) =>
            Task.FromResult(LastSeasonRanks.TryGetValue(playerId, out int r) ? r : 0);
    }
}
