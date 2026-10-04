// Mummy Escape PvP — stockage en mémoire : tests, et duels hors ligne contre des adversaires simulés.
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Classement du mois : le score de chaque joueur, tel qu'envoyé (un tricheur peut y écrire).</summary>
        public readonly Dictionary<string, int> Board = new Dictionary<string, int>();
        /// <summary>Classement archivé du mois précédent.</summary>
        public readonly Dictionary<string, int> LastBoard = new Dictionary<string, int>();
        /// <summary>Classements vérifiés gardés par le serveur, par saison.</summary>
        public readonly Dictionary<string, PvpBoardPage> BoardCache = new Dictionary<string, PvpBoardPage>();
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

        public Task SubmitEloAsync(string playerId, int elo)
        {
            Board[playerId] = elo;
            return Task.CompletedTask;
        }

        /// <summary>Par score décroissant, comme le service.</summary>
        List<BoardEntry> Ranked(int seasonsAgo) =>
            (seasonsAgo == 0 ? Board : LastBoard)
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select((kv, i) => new BoardEntry { PlayerId = kv.Key, PlayerName = kv.Key, Score = kv.Value, Rank = i + 1 })
                .ToList();

        public Task<List<BoardEntry>> ReadBoardAsync(int seasonsAgo, int limit) =>
            Task.FromResult(Ranked(seasonsAgo).Take(limit).ToList());

        public Task<BoardEntry> ReadBoardEntryAsync(string playerId, int seasonsAgo) =>
            Task.FromResult(Ranked(seasonsAgo).Find(e => e.PlayerId == playerId));

        public Task<Dictionary<string, PlayerPvpData>> ReadPlayersAsync(IReadOnlyCollection<string> playerIds)
        {
            var found = new Dictionary<string, PlayerPvpData>();
            foreach (var id in playerIds)
                if (Players.TryGetValue(id, out var d)) found[id] = d;
            return Task.FromResult(found);
        }

        public Task<PvpBoardPage> GetCachedBoardAsync(string season) =>
            Task.FromResult(BoardCache.TryGetValue(season, out var b) ? b : null);

        public Task SetCachedBoardAsync(string season, PvpBoardPage board)
        {
            if (board == null) BoardCache.Remove(season); else BoardCache[season] = board;
            return Task.CompletedTask;
        }
    }
}
