// Mummy Escape PvP — la logique du serveur, sans dépendance au stockage : le module Cloud Code l'exécute sur Unity
// Cloud (server/PvpMatchmaking), le jeu l'exécute en local pour jouer hors ligne contre des adversaires simulés.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>Ce que le serveur PvP lit et écrit. Chaque implémentation gère ses conflits d'écriture.</summary>
    public interface IPvpStore
    {
        /// <summary>Total d'étoiles solo du joueur (ouvre le PvP).</summary>
        Task<int> GetSoloStarsAsync(string playerId);
        /// <summary>Lit les données PvP (neuves si absentes), applique <paramref name="mutate"/> et les réécrit.</summary>
        Task<PlayerPvpData> UpdatePlayerAsync(string playerId, Action<PlayerPvpData> mutate);
        Task<PendingDuel> GetPendingAsync(string playerId);
        /// <summary>Null efface le duel en cours.</summary>
        Task SetPendingAsync(string playerId, PendingDuel pending);
        /// <summary>Retire de la file et renvoie le fantôme le plus proche en Elo (<see cref="GhostPicker"/>), ou null.</summary>
        Task<GhostRun> ClaimGhostAsync(string playerId, int elo, int generatorVersion, IDictionary<string, int> opponentsToday, long nowMs);
        Task EnqueueGhostAsync(GhostRun ghost, long nowMs);
        Task SubmitEloAsync(string playerId, int elo);
        /// <summary>Rang mondial du mois (1 = premier), 0 si non classé.</summary>
        Task<int> GetWorldRankAsync(string playerId);
        /// <summary>Rang final du joueur dans le classement archivé du mois précédent, 0 si inconnu.</summary>
        Task<int> GetLastSeasonRankAsync(string playerId);
    }

    /// <summary>
    /// Duels différés contre fantôme : FindDuel donne un tombeau (et le fantôme d'un adversaire proche en Elo s'il y en a un),
    /// SubmitRun rejoue la course, désigne le gagnant et met à jour l'Elo des deux joueurs.
    /// </summary>
    public sealed class PvpServer
    {
        readonly IPvpStore _store;
        readonly Func<DateTime> _utcNow;
        readonly Func<int> _newSeed;

        public PvpServer(IPvpStore store, Func<DateTime> utcNow = null, Func<int> newSeed = null)
        {
            _store = store;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            var rng = new Random();
            _newSeed = newSeed ?? (() => { lock (rng) return rng.Next(1, int.MaxValue); });
        }

        long NowMs => new DateTimeOffset(DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        Task<PlayerPvpData> Update(string playerId, Action<PlayerPvpData> mutate = null) =>
            _store.UpdatePlayerAsync(playerId, d =>
            {
                Seasons.Roll(d, _utcNow());
                mutate?.Invoke(d);
            });

        // ------------------------------------------------------------------ tombs

        static readonly Dictionary<int, Level> Arenas = new Dictionary<int, Level>();

        /// <summary>Tombeau d'une graine, gardé en mémoire : les deux joueurs d'un duel le demandent tour à tour.</summary>
        public static Level Arena(int seed)
        {
            lock (Arenas)
                if (Arenas.TryGetValue(seed, out var cached)) return cached;
            var level = PvpArena.Generate(seed);
            lock (Arenas)
            {
                if (Arenas.Count > 64) Arenas.Clear();
                Arenas[seed] = level;
            }
            return level;
        }

        // ------------------------------------------------------------------ endpoints

        /// <summary>Trouve un adversaire. La course commence tout de suite, avec ou sans fantôme : jamais d'attente.</summary>
        public async Task<FindDuelResponse> FindDuelAsync(string me, int generatorVersion)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new FindDuelResponse { Error = "OUTDATED" };
            if (await _store.GetSoloStarsAsync(me) < PvpConfig.RequiredSoloStars) return new FindDuelResponse { Error = "LOCKED" };

            var pending = await _store.GetPendingAsync(me);
            if (pending != null)
            {
                if (NowMs - pending.CreatedAtUnixMs < PvpConfig.PendingDuelLifetimeMinutes * 60_000L)
                {
                    // Relancer la recherche ne change pas d'adversaire.
                    var current = await Update(me);
                    return new FindDuelResponse { MatchId = pending.MatchId, Seed = pending.Seed, Ghost = pending.Ghost, MyElo = current.Elo };
                }
                // Duel jamais envoyé : abandon.
                await ResolveAsync(me, pending, new RunSubmission { MatchId = pending.MatchId, Outcome = RunOutcome.Abandoned }, null);
                await _store.SetPendingAsync(me, null);
            }

            var data = await Update(me);
            var ghost = await _store.ClaimGhostAsync(me, data.Elo, generatorVersion, data.OpponentsToday, NowMs);
            var duel = new PendingDuel
            {
                MatchId = Guid.NewGuid().ToString("N"),
                Seed = ghost?.Seed ?? _newSeed(),
                Ghost = ghost,
                CreatedAtUnixMs = NowMs,
            };
            await _store.SetPendingAsync(me, duel);
            return new FindDuelResponse { MatchId = duel.MatchId, Seed = duel.Seed, Ghost = ghost, MyElo = data.Elo };
        }

        /// <summary>
        /// Reçoit la course. Le serveur la rejoue sur le tombeau de la graine et ne retient que ce qu'il a vu (issue, temps,
        /// progression). Contre un fantôme le duel est résolu ; sinon la course attend le prochain adversaire.
        /// </summary>
        public async Task<SubmitRunResponse> SubmitRunAsync(string me, RunSubmission run, string playerName)
        {
            var pending = await _store.GetPendingAsync(me);
            if (pending == null || run == null || pending.MatchId != run.MatchId) return new SubmitRunResponse { Error = "NO_PENDING_DUEL" };

            string error = null;
            var verified = RunValidator.IsPlausible(run, out _) ? RunReplay.Verify(Arena(pending.Seed), run) : null;
            if (verified == null)
            {
                error = "INVALID_RUN";
                verified = new RunSubmission { MatchId = pending.MatchId, Outcome = RunOutcome.Abandoned };
            }
            await _store.SetPendingAsync(me, null);
            var response = await ResolveAsync(me, pending, verified, playerName);
            response.Error = error;
            return response;
        }

        async Task<SubmitRunResponse> ResolveAsync(string me, PendingDuel pending, RunSubmission run, string playerName)
        {
            var utc = _utcNow();
            int worldRank = await _store.GetWorldRankAsync(me);
            bool isTop100 = worldRank > 0 && worldRank <= PvpConfig.Top100Size;
            bool abandoned = run.Outcome == RunOutcome.Abandoned;
            int durationMs = abandoned ? 0 : run.TimeMs;

            // Pas de fantôme : la course devient le fantôme du prochain joueur.
            if (pending.Ghost == null)
            {
                int seals = 0;
                var data = await Update(me, d => seals = abandoned ? 0 : DuelBookkeeping.RecordDuelPlayed(d, durationMs, utc, isTop100));
                if (!abandoned)
                {
                    await _store.EnqueueGhostAsync(new GhostRun
                    {
                        GhostId = pending.MatchId,
                        PlayerId = me,
                        PlayerName = playerName,
                        Elo = data.Elo,
                        Seed = pending.Seed,
                        GeneratorVersion = DifficultyTable.GeneratorVersion,
                        Outcome = run.Outcome,
                        TimeMs = run.TimeMs,
                        Progress = run.Progress,
                        Inputs = run.Inputs,
                        CreatedAtUnixMs = NowMs,
                    }, NowMs);
                }
                return new SubmitRunResponse
                {
                    Resolved = false, EloBefore = data.Elo, EloAfter = data.Elo, League = Leagues.FromElo(data.Elo),
                    SealsGained = seals, Seals = data.Seals,
                };
            }

            // Contre un fantôme : résultat, puis l'Elo des deux joueurs (classements lus avant le duel).
            var ghost = pending.Ghost;
            var resultForMe = DuelResolver.Resolve(run.Outcome, run.TimeMs, run.Progress, ghost.Outcome, ghost.TimeMs, ghost.Progress);
            int myEloBefore = (await Update(me)).Elo;
            int oppEloBefore = (await Update(ghost.PlayerId)).Elo;

            int gained = 0;
            var mine = await Update(me, d =>
            {
                gained = abandoned ? 0 : DuelBookkeeping.RecordDuelPlayed(d, durationMs, utc, isTop100);
                gained += DuelBookkeeping.ApplyResult(d, ghost.PlayerId, oppEloBefore, resultForMe);
            });
            var theirs = await Update(ghost.PlayerId, d => DuelBookkeeping.ApplyResult(d, me, myEloBefore, DuelResolver.Invert(resultForMe)));
            await _store.SubmitEloAsync(me, mine.Elo);
            await _store.SubmitEloAsync(ghost.PlayerId, theirs.Elo);

            return new SubmitRunResponse
            {
                Resolved = true, Result = resultForMe, EloBefore = myEloBefore, EloAfter = mine.Elo,
                League = Leagues.FromElo(mine.Elo), SealsGained = gained, Seals = mine.Seals,
            };
        }

        /// <summary>Profil PvP (Elo, ligue, compteurs, sceaux, rang mondial).</summary>
        public async Task<PvpProfileResponse> GetProfileAsync(string me)
        {
            var data = await Update(me);
            int rank = await _store.GetWorldRankAsync(me);
            return new PvpProfileResponse { Data = data, WorldRank = rank, LeagueName = Leagues.DisplayName(data.Elo, rank) };
        }

        /// <summary>Débloque les skins de la saison précédente : classement final, Top 100, participation.</summary>
        public async Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync(string me)
        {
            var response = new SeasonRewardsResponse();
            // Lecture du rang archivé seulement s'il y a quelque chose à réclamer.
            var current = await Update(me);
            int lastRank = current.LastSeason != null && !current.LastSeason.RewardsClaimed ? await _store.GetLastSeasonRankAsync(me) : 0;
            await Update(me, d =>
            {
                response.NewRewards.Clear();
                response.Error = null;
                if (d.LastSeason == null || d.LastSeason.RewardsClaimed) { response.Error = "NOTHING_TO_CLAIM"; return; }
                response.Season = d.LastSeason.Season;
                var rewards = SeasonRewards.Compute(d.LastSeason, out bool eligible);
                var top = SeasonRewards.Top100(d.LastSeason.Season, lastRank, eligible);
                if (top != null) rewards.Add(top);
                foreach (var id in rewards)
                    if (!d.UnlockedRewards.Contains(id))
                    {
                        d.UnlockedRewards.Add(id);
                        response.NewRewards.Add(id);
                    }
                d.LastSeason.RewardsClaimed = true;
                if (response.NewRewards.Count == 0) response.Error = "NOT_ELIGIBLE";
            });
            return response;
        }

        /// <summary>Achète un article de la boutique PvP avec des sceaux (prix et ligue vérifiés ici, jamais par le client).</summary>
        public async Task<SealPurchaseResponse> BuyWithSealsAsync(string me, string itemId)
        {
            var response = new SealPurchaseResponse();
            var data = await Update(me, d =>
            {
                response.Error = SealShop.TryBuy(d, itemId);
                response.Ok = response.Error == null;
            });
            response.Seals = data.Seals;
            response.UnlockedRewards = data.UnlockedRewards;
            return response;
        }
    }
}
