// Mummy Escape PvP — la logique du serveur, sans dépendance au stockage : le module Cloud Code l'exécute sur Unity
// Cloud (server/PvpMatchmaking), le jeu l'exécute en local pour jouer hors ligne contre des adversaires simulés.
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Haut du classement, tel que le service le renvoie : <paramref name="seasonsAgo"/> 0 = ce mois, 1 = le mois
        /// dernier (version archivée). Vide si ce mois n'a pas de classement.</summary>
        Task<List<BoardEntry>> ReadBoardAsync(int seasonsAgo, int limit);
        /// <summary>L'entrée du joueur dans ce classement, null s'il n'y figure pas.</summary>
        Task<BoardEntry> ReadBoardEntryAsync(string playerId, int seasonsAgo);
        /// <summary>Données PvP de plusieurs joueurs, telles qu'enregistrées (sans les modifier) ; les absents sont omis.</summary>
        Task<Dictionary<string, PlayerPvpData>> ReadPlayersAsync(IReadOnlyCollection<string> playerIds);
        /// <summary>Classement vérifié d'une saison gardé en cache, null si absent.</summary>
        Task<PvpBoardPage> GetCachedBoardAsync(string season);
        /// <summary>Null efface le cache de cette saison.</summary>
        Task SetCachedBoardAsync(string season, PvpBoardPage board);
        /// <summary>Derniers duels du joueur, du plus récent au plus ancien : lus, modifiés par <paramref name="mutate"/> puis réécrits
        /// (lecture seule si <paramref name="mutate"/> est null).</summary>
        Task<List<DuelRecord>> UpdateHistoryAsync(string playerId, Action<List<DuelRecord>> mutate);
        /// <summary>Dossier de triche d'un joueur (neuf si absent), dans un espace que les joueurs ne lisent pas : modifié puis réécrit.</summary>
        Task<CheatDossier> UpdateDossierAsync(string playerId, Action<CheatDossier> mutate);
        /// <summary>
        /// Objet partagé côté serveur (combats, duos, guildes, index, files) : lu (null si absent), passé à
        /// <paramref name="mutate"/> qui renvoie la valeur à écrire, puis réécrit. Lecture seule si <paramref name="mutate"/>
        /// est null. Renvoie la valeur finale.
        /// </summary>
        Task<T> UpdateSharedAsync<T>(string collection, string key, Func<T, T> mutate) where T : class;
    }

    /// <summary>
    /// Duels différés contre fantôme : FindDuel donne un tombeau (et le fantôme d'un adversaire proche en Elo s'il y en a un),
    /// SubmitRun rejoue la course, désigne le gagnant et met à jour l'Elo des deux joueurs.
    /// </summary>
    public sealed partial class PvpServer
    {
        readonly IPvpStore _store;
        readonly Func<DateTime> _utcNow;
        readonly Func<int> _newSeed;
        readonly Func<double> _random;

        public PvpServer(IPvpStore store, Func<DateTime> utcNow = null, Func<int> newSeed = null, Func<double> random = null)
        {
            _store = store;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            var rng = new Random();
            _newSeed = newSeed ?? (() => { lock (rng) return rng.Next(1, int.MaxValue); });
            _random = random ?? (() => { lock (rng) return rng.NextDouble(); });
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

        /// <summary>
        /// Trouve un adversaire de la ligue du joueur. Sans fantôme de sa ligue, le serveur cherche une minute (le jeu redemande,
        /// <see cref="FindDuelResponse.Searching"/>), puis donne un bot de la ligue si le joueur l'accepte ; sinon le joueur court
        /// le premier et sa course attend le prochain joueur de sa ligue.
        /// </summary>
        public async Task<FindDuelResponse> FindDuelAsync(string me, int generatorVersion, bool allowBots = false)
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
                    return new FindDuelResponse { MatchId = pending.MatchId, Seed = pending.Seed, Ghost = pending.Ghost, MyElo = current.Elo, BattleId = pending.BattleId, Slot = pending.Slot };
                }
                await DropPendingAsync(me, pending);
            }

            var data = await Update(me);
            var ghost = await _store.ClaimGhostAsync(me, data.Elo, generatorVersion, data.OpponentsToday, NowMs);
            if (ghost == null)
            {
                // Personne de la ligue : on cherche encore une minute (le jeu redemande), puis un bot de la ligue si le joueur
                // l'accepte, sinon il court le premier et devient le fantôme du prochain joueur de sa ligue.
                long since = data.DuelSearchSinceUnixMs;
                if (since <= 0 || NowMs - since > PvpConfig.SearchResetMs || since > NowMs)
                {
                    since = NowMs;
                    data = await Update(me, d => d.DuelSearchSinceUnixMs = since);
                }
                long searched = NowMs - since;
                if (searched < PvpConfig.BotFallbackMs)
                    return new FindDuelResponse { Searching = true, SearchedMs = (int)searched, MyElo = data.Elo };
                if (allowBots) ghost = PvpBots.Make(new Random(_newSeed()), data.Elo, NowMs);
            }
            if (data.DuelSearchSinceUnixMs != 0) data = await Update(me, d => d.DuelSearchSinceUnixMs = 0);
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
            // A forfeit is a loss whatever was played before it: nothing to check.
            var verified = run.Outcome == RunOutcome.Abandoned ? new RunSubmission { MatchId = pending.MatchId, Outcome = RunOutcome.Abandoned }
                         : RunValidator.IsPlausible(run, out _) ? RunReplay.Verify(Arena(pending.Seed), run)
                         : null;
            if (verified == null)
            {
                error = "INVALID_RUN";
                verified = new RunSubmission { MatchId = pending.MatchId, Outcome = RunOutcome.Abandoned };
            }
            verified.Look = PlayerLook.Sanitize(run.Look);
            if (Titles.Get(verified.Look?.Title)?.IsDuel == true)
            {
                // Duel titles are checked against the protected data: nobody shows "Légende de diamant" without the league.
                var mine = await _store.ReadPlayersAsync(new[] { me });
                mine.TryGetValue(me, out var data);
                verified.Look.Title = Titles.Check(verified.Look.Title, data);
            }
            await _store.SetPendingAsync(me, null);
            var response = pending.BattleId != null ? await ResolveBattleRunAsync(me, pending, verified, playerName)
                                                    : await ResolveAsync(me, pending, verified, playerName);
            response.Error = error;
            return response;
        }

        async Task<SubmitRunResponse> ResolveAsync(string me, PendingDuel pending, RunSubmission run, string playerName)
        {
            var utc = _utcNow();
            int worldRank = await WorldRankAsync(me);
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
                        Look = run.Look,
                    }, NowMs);
                    // Le duel reste ouvert dans l'historique : complété quand quelqu'un affrontera ce fantôme.
                    await RecordAsync(me, h => Remember(h, new DuelRecord
                    {
                        MatchId = pending.MatchId, Seed = pending.Seed, GeneratorVersion = DifficultyTable.GeneratorVersion,
                        PlayedAtUnixMs = NowMs, EloBefore = data.Elo, EloAfter = data.Elo,
                        Me = RunOf(me, playerName, data.Elo, run),
                    }));
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
            // Un bot n'a ni fiche, ni classement, ni historique : seul le vrai joueur est mis à jour.
            bool bot = ghost.Bot;
            int oppEloBefore = bot ? ghost.Elo : (await Update(ghost.PlayerId)).Elo;

            int gained = 0;
            var mine = await Update(me, d =>
            {
                gained = abandoned ? 0 : DuelBookkeeping.RecordDuelPlayed(d, durationMs, utc, isTop100);
                gained += DuelBookkeeping.ApplyResult(d, ghost.PlayerId, oppEloBefore, resultForMe);
                d.Ranked = true;
            });
            var theirs = bot ? null : await Update(ghost.PlayerId, d =>
            {
                DuelBookkeeping.ApplyResult(d, me, myEloBefore, DuelResolver.Invert(resultForMe));
                d.Ranked = true;
            });
            await _store.SubmitEloAsync(me, mine.Elo);
            if (!bot) await _store.SubmitEloAsync(ghost.PlayerId, theirs.Elo);
            // Every duel win also counts for the winner's guild.
            if (resultForMe == DuelResult.Win) await AddGuildPointsAsync(mine.GuildId, me, TeamConfig.PointsPerDuelWin);
            else if (resultForMe == DuelResult.Loss && !bot) await AddGuildPointsAsync(theirs.GuildId, ghost.PlayerId, TeamConfig.PointsPerDuelWin);
            await ForgetBoardIfChangedAsync(Math.Max(mine.Elo, theirs?.Elo ?? 0), me, ghost.PlayerId);

            // Le duel dans l'historique des deux joueurs, de quoi le revoir des deux points de vue.
            var myRun = RunOf(me, playerName, myEloBefore, run);
            var ghostRun = DuelRun.Of(ghost);
            await RecordAsync(me, h => Remember(h, new DuelRecord
            {
                MatchId = pending.MatchId, Seed = pending.Seed, GeneratorVersion = ghost.GeneratorVersion, PlayedAtUnixMs = NowMs,
                Resolved = true, Result = resultForMe, EloBefore = myEloBefore, EloAfter = mine.Elo, Me = myRun, Rival = ghostRun,
            }));
            if (!bot) await RecordAsync(ghost.PlayerId, h =>
            {
                var open = h.Find(r => r.MatchId == ghost.GhostId);
                Remember(h, new DuelRecord
                {
                    MatchId = ghost.GhostId, Seed = ghost.Seed, GeneratorVersion = ghost.GeneratorVersion,
                    PlayedAtUnixMs = open?.PlayedAtUnixMs ?? ghost.CreatedAtUnixMs, Resolved = true,
                    Result = DuelResolver.Invert(resultForMe), EloBefore = oppEloBefore, EloAfter = theirs.Elo,
                    Me = open?.Me ?? ghostRun, Rival = myRun,
                });
            });

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
            int rank = await WorldRankAsync(me);
            return new PvpProfileResponse { Data = data, WorldRank = rank, LeagueName = Leagues.DisplayName(data.Elo, rank) };
        }

        /// <summary>Débloque les skins de la saison précédente : classement final, Top 100, participation.</summary>
        public async Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync(string me)
        {
            var response = new SeasonRewardsResponse();
            // Lecture du rang archivé seulement s'il y a quelque chose à réclamer.
            var current = await Update(me);
            int lastRank = current.LastSeason != null && !current.LastSeason.RewardsClaimed
                ? (await MyRowAsync(me, 1, await VerifiedBoardAsync(1)))?.Rank ?? 0
                : 0;
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

        /// <summary>Un tour de la roue des sceaux du casino : prix, tirage et gain décidés ici, jamais par le client.</summary>
        public async Task<WheelSpinResponse> SpinSealWheelAsync(string me)
        {
            var response = new WheelSpinResponse();
            var data = await Update(me, d =>
            {
                response.Error = Casino.SpinSeals(d, _random(), _random(), out var result);
                response.Result = result;
                response.Ok = response.Error == null;
            });
            response.Seals = data.Seals;
            response.UnlockedRewards = data.UnlockedRewards;
            return response;
        }

        // ------------------------------------------------------------------ replays and reports

        /// <summary>Les derniers duels du joueur (<see cref="PvpConfig.HistorySize"/> au plus), les deux courses de chacun.</summary>
        public async Task<DuelHistoryResponse> GetHistoryAsync(string me) =>
            new DuelHistoryResponse { Duels = await _store.UpdateHistoryAsync(me, null) ?? new List<DuelRecord>() };

        /// <summary>
        /// Signale l'adversaire d'un duel de l'historique pour triche. Le serveur copie le duel en entier (les deux courses)
        /// dans le dossier du joueur signalé, qu'un humain examine ; plusieurs joueurs différents le marquent « à vérifier ».
        /// Rien n'est sanctionné automatiquement.
        /// </summary>
        public async Task<ReportResponse> ReportCheatAsync(string me, string matchId)
        {
            var duel = (await _store.UpdateHistoryAsync(me, null))?.Find(r => r.MatchId == matchId);
            if (duel == null) return new ReportResponse { Error = "UNKNOWN_DUEL" };
            if (duel.Rival == null || string.IsNullOrEmpty(duel.Rival.PlayerId) || duel.Rival.PlayerId == me) return new ReportResponse { Error = "NO_RIVAL" };
            if (duel.Reported) return new ReportResponse { Error = "ALREADY_REPORTED" };

            bool allowed = false;
            await Update(me, d =>
            {
                allowed = d.ReportsToday < PvpConfig.MaxReportsPerDay;
                if (allowed) d.ReportsToday++;
            });
            if (!allowed) return new ReportResponse { Error = "LIMIT" };

            long now = NowMs;
            await _store.UpdateDossierAsync(duel.Rival.PlayerId, f =>
            {
                f.PlayerId = duel.Rival.PlayerId;
                f.PlayerName = duel.Rival.PlayerName;
                if (f.Reporters == null) f.Reporters = new List<string>();
                if (f.Reports == null) f.Reports = new List<CheatReport>();
                if (!f.Reporters.Contains(me)) f.Reporters.Add(me);
                f.Reports.RemoveAll(r => r.MatchId == matchId && r.ReporterId == me);
                f.Reports.Add(new CheatReport
                {
                    MatchId = matchId, ReporterId = me, ReportedAtUnixMs = now, Seed = duel.Seed, GeneratorVersion = duel.GeneratorVersion,
                    Suspect = duel.Rival, Reporter = duel.Me,
                });
                if (f.Reports.Count > PvpConfig.MaxReportsPerDossier) f.Reports.RemoveRange(0, f.Reports.Count - PvpConfig.MaxReportsPerDossier);
                f.TotalReports++;
                f.LastReportUnixMs = now;
                f.Flagged = f.Reporters.Count >= PvpConfig.ReportersToFlag;
            });
            await RecordAsync(me, h =>
            {
                var r = h.Find(x => x.MatchId == matchId);
                if (r != null) r.Reported = true;
            });
            return new ReportResponse { Ok = true };
        }

        /// <summary>Range un duel dans un historique. Le duel est déjà joué et compté : un historique illisible n'y change rien.</summary>
        async Task RecordAsync(string playerId, Action<List<DuelRecord>> mutate)
        {
            try { await _store.UpdateHistoryAsync(playerId, mutate); }
            catch { /* replay perdu, verdict et Elo intacts */ }
        }

        static DuelRun RunOf(string playerId, string playerName, int elo, RunSubmission run) => new DuelRun
        {
            PlayerId = playerId, PlayerName = playerName, Look = run.Look, Elo = elo,
            Outcome = run.Outcome, TimeMs = run.TimeMs, Progress = run.Progress, Inputs = run.Inputs ?? new List<RunInput>(),
        };

        /// <summary>Range un duel dans l'historique (il remplace celui du même identifiant), du plus récent au plus ancien ; les plus
        /// anciens au-delà de <see cref="PvpConfig.HistorySize"/> sont oubliés.</summary>
        public static void Remember(List<DuelRecord> history, DuelRecord record)
        {
            history.RemoveAll(r => r == null || r.MatchId == record.MatchId);
            history.Add(record);
            var sorted = history.OrderByDescending(r => r.PlayedAtUnixMs).Take(PvpConfig.HistorySize).ToList();
            history.Clear();
            history.AddRange(sorted);
        }

        // ------------------------------------------------------------------ ranking

        /// <summary>Classement vérifié d'un mois (0 = celui-ci, 1 = le précédent) : les <paramref name="limit"/> premiers et le joueur.</summary>
        public async Task<PvpBoardPage> GetBoardAsync(string me, int seasonsAgo, int limit)
        {
            seasonsAgo = Math.Max(0, Math.Min(1, seasonsAgo));
            limit = Math.Max(1, Math.Min(PvpConfig.Top100Size, limit));
            var board = await VerifiedBoardAsync(seasonsAgo);
            var mine = await MyRowAsync(me, seasonsAgo, board);

            // La ligne du joueur est relue à chaque fois : le cache peut avoir quelques minutes de retard sur son Elo.
            var rows = board.Rows.Where(r => r.PlayerId != me)
                            .Select(r => new PvpBoardRow { PlayerId = r.PlayerId, PlayerName = r.PlayerName, Elo = r.Elo, Wins = r.Wins, Losses = r.Losses, Draws = r.Draws })
                            .ToList();
            if (mine != null && mine.Rank <= rows.Count + 1) rows.Insert(mine.Rank - 1, mine);
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            if (rows.Count > limit) rows.RemoveRange(limit, rows.Count - limit);
            return new PvpBoardPage { Season = board.Season, Rows = rows, Me = mine, BuiltAtUnixMs = board.BuiltAtUnixMs };
        }

        /// <summary>Rang mondial vérifié du mois (1 = premier), 0 si non classé.</summary>
        async Task<int> WorldRankAsync(string me) => (await MyRowAsync(me, 0, await VerifiedBoardAsync(0)))?.Rank ?? 0;

        string SeasonAgo(int seasonsAgo)
        {
            var now = _utcNow();
            return Seasons.SeasonOf(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-seasonsAgo));
        }

        /// <summary>L'Elo que le joueur doit avoir dans le classement de <paramref name="season"/>, null s'il n'y a pas sa place
        /// (aucun duel résolu ce mois-là).</summary>
        static int? TrueElo(PlayerPvpData d, string season)
        {
            if (d == null) return null;
            if (d.Season == season) return d.Ranked ? d.Elo : (int?)null;
            if (d.LastSeason != null && d.LastSeason.Season == season) return d.LastSeason.Ranked ? d.LastSeason.FinalElo : (int?)null;
            return null;
        }

        /// <summary>Le bilan du joueur sur <paramref name="season"/> (ce mois-ci, ou le résumé du mois dernier).</summary>
        static PvpBoardRow WithRecord(PvpBoardRow row, PlayerPvpData d, string season)
        {
            if (d?.Season == season) { row.Wins = d.SeasonWins; row.Losses = d.SeasonLosses; row.Draws = d.SeasonDraws; }
            else if (d?.LastSeason?.Season == season) { row.Wins = d.LastSeason.Wins; row.Losses = d.LastSeason.Losses; row.Draws = d.LastSeason.Draws; }
            return row;
        }

        /// <summary>
        /// Le classement d'un mois, vérifié. Le service Leaderboards garde le score que chacun y envoie, tricheurs compris :
        /// chaque entrée est comparée à l'Elo des données protégées du joueur, que seul ce serveur écrit. Ce mois-ci, un score
        /// faux est réécrit avec le vrai Elo et une entrée sans duel tombe à 0, si bien que le classement se répare ; un mois
        /// archivé, figé, est seulement recalculé. Gardé <see cref="PvpConfig.BoardCacheMinutes"/> minutes.
        /// </summary>
        async Task<PvpBoardPage> VerifiedBoardAsync(int seasonsAgo)
        {
            string season = SeasonAgo(seasonsAgo);
            var cached = await _store.GetCachedBoardAsync(season);
            if (cached != null && NowMs - cached.BuiltAtUnixMs < PvpConfig.BoardCacheMinutes * 60_000L) return cached;

            var entries = await _store.ReadBoardAsync(seasonsAgo, PvpConfig.Top100Size + PvpConfig.BoardMargin);
            var players = await _store.ReadPlayersAsync(entries.ConvertAll(e => e.PlayerId));
            var rows = new List<PvpBoardRow>();
            foreach (var e in entries)
            {
                players.TryGetValue(e.PlayerId, out var d);
                int? elo = TrueElo(d, season);
                if (seasonsAgo == 0 && (elo ?? 0) != e.Score) await _store.SubmitEloAsync(e.PlayerId, elo ?? 0);
                if (elo != null) rows.Add(WithRecord(new PvpBoardRow { PlayerId = e.PlayerId, PlayerName = e.PlayerName, Elo = elo.Value }, d, season));
            }
            rows = rows.OrderByDescending(r => r.Elo).Take(PvpConfig.Top100Size).ToList(); // tri stable : ex aequo dans l'ordre du service
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;

            var board = new PvpBoardPage { Season = season, Rows = rows, BuiltAtUnixMs = NowMs };
            await _store.SetCachedBoardAsync(season, board);
            return board;
        }

        /// <summary>Oublie le classement en cache quand un duel y fait entrer ou bouger quelqu'un : il sera vérifié de nouveau.</summary>
        async Task ForgetBoardIfChangedAsync(int bestElo, string playerA, string playerB)
        {
            string season = SeasonAgo(0);
            var cached = await _store.GetCachedBoardAsync(season);
            if (cached == null) return;
            bool changed = cached.Rows.Count < PvpConfig.Top100Size
                           || bestElo >= cached.Rows[cached.Rows.Count - 1].Elo
                           || cached.Rows.Exists(r => r.PlayerId == playerA || r.PlayerId == playerB);
            if (changed) await _store.SetCachedBoardAsync(season, null);
        }

        /// <summary>La ligne vérifiée du joueur dans le classement d'un mois, null s'il n'y figure pas.</summary>
        async Task<PvpBoardRow> MyRowAsync(string me, int seasonsAgo, PvpBoardPage board)
        {
            var entry = await _store.ReadBoardEntryAsync(me, seasonsAgo);
            if (entry == null) return null;
            (await _store.ReadPlayersAsync(new[] { me })).TryGetValue(me, out var d);
            int? elo = TrueElo(d, board.Season);
            if (seasonsAgo == 0 && (elo ?? 0) != entry.Score) await _store.SubmitEloAsync(me, elo ?? 0);
            if (elo == null) return null;

            // Placé parmi les lignes vérifiées ; plus bas qu'elles toutes, le rang du service (jamais avant elles).
            int others = 0, ahead = 0;
            foreach (var r in board.Rows)
                if (r.PlayerId != me)
                {
                    others++;
                    if (r.Elo > elo.Value) ahead++;
                }
            int rank = ahead < others || others < PvpConfig.Top100Size ? ahead + 1 : Math.Max(entry.Rank, others + 1);
            return WithRecord(new PvpBoardRow { Rank = rank, PlayerId = me, PlayerName = entry.PlayerName, Elo = elo.Value, IsMe = true }, d, board.Season);
        }
    }
}
