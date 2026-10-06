// Mummy Rush PvP — règles du jeu en C# pur, testables sans serveur.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MummyEscape.Pvp
{
    public static class Elo
    {
        /// <summary>Probabilité que A batte B.</summary>
        public static double Expected(int ratingA, int ratingB)
        {
            return 1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));
        }

        public static int KFactor(int rating, int totalDuels)
        {
            if (totalDuels < PvpConfig.PlacementDuels) return PvpConfig.KPlacement;
            if (rating >= PvpConfig.KHighThreshold) return PvpConfig.KHigh;
            return PvpConfig.KNormal;
        }

        public static double Score(DuelResult result)
        {
            switch (result)
            {
                case DuelResult.Win: return 1.0;
                case DuelResult.Draw: return 0.5;
                default: return 0.0;
            }
        }

        /// <summary>
        /// Nouveau classement de A après un duel contre B. Contre un bot (<paramref name="vsBot"/>), le K est divisé par deux :
        /// un bot tient l'Elo de la division sans en être un vrai membre.
        /// </summary>
        public static int NewRating(int ratingA, int ratingB, DuelResult resultForA, int totalDuelsA, bool vsBot = false)
        {
            int k = KFactor(ratingA, totalDuelsA);
            if (vsBot) k = (k + 1) / 2;
            double delta = k * (Score(resultForA) - Expected(ratingA, ratingB));
            int updated = ratingA + (int)Math.Round(delta, MidpointRounding.AwayFromZero);
            return Math.Max(PvpConfig.MinElo, updated);
        }
    }

    public static class Leagues
    {
        public static League FromElo(int elo)
        {
            if (elo >= PvpConfig.DiamantMin) return League.Diamant;
            if (elo >= PvpConfig.PlatineMin) return League.Platine;
            if (elo >= PvpConfig.OrMin) return League.Or;
            if (elo >= PvpConfig.ArgentMin) return League.Argent;
            return League.Bronze;
        }

        /// <summary>Elo minimal et maximal de la ligue (Bronze commence à l'Elo plancher, Diamant n'a pas de plafond).</summary>
        public static (int Min, int Max) Range(League league)
        {
            switch (league)
            {
                case League.Diamant: return (PvpConfig.DiamantMin, int.MaxValue);
                case League.Platine: return (PvpConfig.PlatineMin, PvpConfig.DiamantMin - 1);
                case League.Or: return (PvpConfig.OrMin, PvpConfig.PlatineMin - 1);
                case League.Argent: return (PvpConfig.ArgentMin, PvpConfig.OrMin - 1);
                default: return (PvpConfig.MinElo, PvpConfig.ArgentMin - 1);
            }
        }

        /// <summary>Ramène un Elo dans la ligue donnée (adversaires simulés : toujours de la division du joueur).</summary>
        public static int ClampTo(int elo, League league)
        {
            var (min, max) = Range(league);
            return Math.Max(min, Math.Min(max, elo));
        }

        /// <summary>La division d'un duo : celle de son meilleur joueur en duel.</summary>
        public static League OfDuo(int eloA, int eloB) => FromElo(Math.Max(eloA, eloB));

        /// <summary>Nom affiché : « Top 100 » si le rang mondial le permet, sinon la ligue.</summary>
        public static string DisplayName(int elo, int? worldRank)
        {
            if (worldRank.HasValue && worldRank.Value >= 1 && worldRank.Value <= PvpConfig.Top100Size)
                return "Top 100";
            return FromElo(elo).ToString();
        }
    }

    public static class DuelResolver
    {
        /// <summary>Résultat du point de vue de A. Règles : la sortie bat tout ; deux sorties = le plus rapide ;
        /// sinon (mort, limite de temps) le plus proche de la sortie ; l'abandon perd toujours.</summary>
        public static DuelResult Resolve(RunOutcome outcomeA, int timeA, float progressA,
                                         RunOutcome outcomeB, int timeB, float progressB)
        {
            bool abandonA = outcomeA == RunOutcome.Abandoned;
            bool abandonB = outcomeB == RunOutcome.Abandoned;
            if (abandonA && abandonB) return DuelResult.Draw;
            if (abandonA) return DuelResult.Loss;
            if (abandonB) return DuelResult.Win;

            bool finishedA = outcomeA == RunOutcome.Finished;
            bool finishedB = outcomeB == RunOutcome.Finished;

            if (finishedA && finishedB)
            {
                int gap = timeA - timeB;
                if (Math.Abs(gap) < PvpConfig.DrawTimeThresholdMs) return DuelResult.Draw;
                return gap < 0 ? DuelResult.Win : DuelResult.Loss;
            }
            if (finishedA) return DuelResult.Win;
            if (finishedB) return DuelResult.Loss;

            float diff = progressA - progressB;
            if (Math.Abs(diff) < PvpConfig.DrawProgressThreshold) return DuelResult.Draw;
            return diff > 0 ? DuelResult.Win : DuelResult.Loss;
        }

        public static DuelResult Invert(DuelResult r)
        {
            if (r == DuelResult.Win) return DuelResult.Loss;
            if (r == DuelResult.Loss) return DuelResult.Win;
            return DuelResult.Draw;
        }
    }

    public static class RunValidator
    {
        /// <summary>Contrôles de base ; le serveur rejoue ensuite la course sur le tombeau (<see cref="RunReplay"/>).</summary>
        public static bool IsPlausible(RunSubmission run, out string reason)
        {
            reason = null;
            if (run == null) { reason = "empty"; return false; }
            if (run.Inputs == null) run.Inputs = new List<RunInput>();
            if (run.Inputs.Count > PvpConfig.MaxInputs) { reason = "too_many_inputs"; return false; }
            if (run.TimeMs < 0 || run.TimeMs > PvpConfig.TimeLimitMs + 1000) { reason = "time_out_of_range"; return false; }
            if (run.Outcome == RunOutcome.Finished && run.TimeMs < PvpConfig.MinPlausibleTimeMs) { reason = "too_fast"; return false; }
            if (float.IsNaN(run.Progress) || run.Progress < 0f || run.Progress > 1f) { reason = "bad_progress"; return false; }

            int lastTick = int.MinValue;
            int maxTick = (run.TimeMs * PvpConfig.TickRate) / 1000 + PvpConfig.TickRate;
            foreach (var input in run.Inputs)
            {
                if (input == null || input.Tick < 0 || input.Tick > maxTick || input.Tick < lastTick) { reason = "bad_ticks"; return false; }
                if (lastTick != int.MinValue && input.Tick - lastTick < PvpConfig.MinInputGapTicks) { reason = "inputs_too_close"; return false; }
                if (input.Direction < 1 || input.Direction > 8) { reason = "bad_direction"; return false; }
                lastTick = input.Tick;
            }
            return true;
        }
    }

    public static class GhostPicker
    {
        public static int BucketOf(int elo) => Math.Max(0, elo / PvpConfig.QueueBucketSize);

        /// <summary>Tranches à fouiller, de la plus proche à la plus lointaine (±300 Elo).</summary>
        public static List<int> BucketsToSearch(int elo)
        {
            int center = BucketOf(elo);
            int reach = (PvpConfig.MaxEloGap + PvpConfig.QueueBucketSize - 1) / PvpConfig.QueueBucketSize;
            var result = new List<int> { center };
            for (int d = 1; d <= reach; d++)
            {
                result.Add(center + d);
                if (center - d >= 0) result.Add(center - d);
            }
            return result;
        }

        public static bool IsExpired(GhostRun g, long nowMs)
        {
            return nowMs - g.CreatedAtUnixMs > (long)PvpConfig.GhostLifetimeHours * 3600_000L;
        }

        /// <summary>Choisit le fantôme le plus proche en Elo et de la même ligue, en excluant soi-même,
        /// les adversaires déjà affrontés 3 fois aujourd'hui et les fantômes expirés.</summary>
        public static GhostRun Pick(IEnumerable<GhostRun> candidates, string playerId, int elo,
                                    IDictionary<string, int> opponentsToday, long nowMs)
        {
            GhostRun best = null;
            int bestGap = int.MaxValue;
            foreach (var g in candidates)
            {
                if (g == null || g.PlayerId == playerId || IsExpired(g, nowMs)) continue;
                int gap = Math.Abs(g.Elo - elo);
                if (gap > PvpConfig.MaxEloGap || Leagues.FromElo(g.Elo) != Leagues.FromElo(elo)) continue; // toujours sa ligue
                if (opponentsToday != null && opponentsToday.TryGetValue(g.PlayerId, out int n)
                    && n >= PvpConfig.MaxDuelsVsSameOpponentPerDay) continue;
                if (gap < bestGap || (gap == bestGap && best != null && g.CreatedAtUnixMs < best.CreatedAtUnixMs))
                {
                    best = g;
                    bestGap = gap;
                }
            }
            return best;
        }
    }

    public static class Seasons
    {
        public static string SeasonOf(DateTime utc) => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public static string DayOf(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static bool IsLastWeekOfSeason(DateTime utc)
        {
            int daysInMonth = DateTime.DaysInMonth(utc.Year, utc.Month);
            return utc.Day > daysInMonth - PvpConfig.LastWeekDays;
        }

        public static int SoftReset(int elo)
        {
            return (int)Math.Round(PvpConfig.StartingElo + PvpConfig.SeasonSoftResetFactor * (elo - PvpConfig.StartingElo),
                                   MidpointRounding.AwayFromZero);
        }

        /// <summary>À appeler à chaque chargement : change de jour et de saison si besoin.
        /// Le passage de mois se fait « paresseusement », sans tâche planifiée.</summary>
        public static void Roll(PlayerPvpData d, DateTime utc)
        {
            string season = SeasonOf(utc);
            if (d.Season == null)
            {
                d.Season = season;
            }
            else if (d.Season != season)
            {
                d.LastSeason = new SeasonSummary
                {
                    Season = d.Season,
                    FinalElo = d.Elo,
                    Duels = d.SeasonDuels,
                    DuelsLastWeek = d.SeasonDuelsLastWeek,
                    CountedDuels = d.SeasonCountedDuels,
                    Wins = d.SeasonWins,
                    Losses = d.SeasonLosses,
                    Draws = d.SeasonDraws,
                    Ranked = d.Ranked,
                    RewardsClaimed = false
                };
                d.Elo = SoftReset(d.Elo);
                d.BestEloThisSeason = d.Elo;
                d.Ranked = false;
                d.SeasonDuels = 0;
                d.SeasonDuelsLastWeek = 0;
                d.SeasonCountedDuels = 0;
                d.SeasonWins = d.SeasonLosses = d.SeasonDraws = 0;
                d.Season = season;
            }

            string day = DayOf(utc);
            if (d.Day != day)
            {
                d.Day = day;
                d.DuelsToday = 0;
                d.CountedDuelsToday = 0;
                d.WinsToday = 0;
                d.ReportsToday = 0;
                d.DailyChestGranted = false;
                d.OpponentsToday = new Dictionary<string, int>();
            }
            if (d.OpponentsToday == null) d.OpponentsToday = new Dictionary<string, int>();
        }
    }

    public static class SeasonRewards
    {
        public static readonly int[] ParticipationSteps = { 10, 25, 50, 100 };
        public const int MinDuelsForRankReward = 15;
        public const int MinDuelsLastWeekForRankReward = 5;

        /// <summary>Identifiants des récompenses d'une saison terminée.
        /// Classement : la version de la ligue finale et toutes celles en dessous.
        /// Participation : chaque étape atteinte. Le Top 100 est traité à part (voir le guide).</summary>
        public static List<string> Compute(SeasonSummary s, out bool rankEligible)
        {
            var list = new List<string>();
            rankEligible = s.Duels >= MinDuelsForRankReward && s.DuelsLastWeek >= MinDuelsLastWeekForRankReward;
            if (rankEligible)
            {
                var final = Leagues.FromElo(s.FinalElo);
                for (var l = League.Bronze; l <= final; l++)
                    list.Add("pvp_" + s.Season + "_rank_" + l.ToString().ToLowerInvariant());
            }
            foreach (int step in ParticipationSteps)
                if (s.CountedDuels >= step)
                    list.Add("pvp_" + s.Season + "_participation_" + step);
            return list;
        }

        /// <summary>Récompense des 100 premiers du classement final (rang lu dans l'archive du mois, 1 = premier).</summary>
        public static string Top100(string season, int finalRank, bool rankEligible) =>
            rankEligible && finalRank >= 1 && finalRank <= PvpConfig.Top100Size ? "pvp_" + season + "_rank_top100" : null;
    }

    /// <summary>Un article de la boutique PvP : payé en sceaux de Maât, réservé à ceux qui ont atteint sa ligue.</summary>
    public sealed class SealItem
    {
        public string Id;
        public int Price;
        public League MinLeague;
    }

    /// <summary>Boutique des sceaux : les mêmes prix sur le serveur (qui encaisse) et dans le jeu (qui affiche).</summary>
    public static class SealShop
    {
        public static readonly IReadOnlyList<SealItem> Items = new[]
        {
            new SealItem { Id = "pvp_shop_scales", Price = 300, MinLeague = League.Bronze },
            new SealItem { Id = "pvp_shop_feather", Price = 500, MinLeague = League.Argent },
            new SealItem { Id = "pvp_shop_obsidian", Price = 800, MinLeague = League.Or },
            new SealItem { Id = "pvp_shop_star", Price = 1200, MinLeague = League.Platine },
            new SealItem { Id = "pvp_shop_diamond", Price = 1500, MinLeague = League.Diamant },
        };

        public static SealItem Get(string id)
        {
            foreach (var i in Items) if (i.Id == id) return i;
            return null;
        }

        /// <summary>Débite les sceaux et donne l'article ; null en cas de succès, sinon le code d'erreur.</summary>
        public static string TryBuy(PlayerPvpData d, string id)
        {
            var item = Get(id);
            if (item == null) return "UNKNOWN_ITEM";
            if (d.UnlockedRewards.Contains(id)) return "OWNED";
            if (d.HighestLeague < item.MinLeague) return "LEAGUE";
            if (d.Seals < item.Price) return "SEALS";
            d.Seals -= item.Price;
            d.UnlockedRewards.Add(id);
            return null;
        }
    }

    public static class DuelBookkeeping
    {
        /// <summary>Compte un duel joué par ce joueur (compteurs, coffre quotidien, participation).
        /// Retourne les sceaux gagnés.</summary>
        public static int RecordDuelPlayed(PlayerPvpData d, int durationMs, DateTime utc, bool isTop100)
        {
            int seals = 0;
            d.DuelsToday++;
            d.SeasonDuels++;
            if (Seasons.IsLastWeekOfSeason(utc)) d.SeasonDuelsLastWeek++;

            if (durationMs >= PvpConfig.MinCountedDuelMs && d.CountedDuelsToday < PvpConfig.MaxCountedDuelsPerDay)
            {
                d.CountedDuelsToday++;
                d.SeasonCountedDuels++;
            }
            if (!d.DailyChestGranted)
            {
                d.DailyChestGranted = true;
                seals += PvpConfig.DailyChestSeals(Leagues.FromElo(d.Elo), isTop100);
            }
            d.Seals += seals;
            return seals;
        }

        /// <summary>Applique le résultat d'un duel résolu. Retourne les sceaux gagnés (bonus 1re victoire).</summary>
        public static int ApplyResult(PlayerPvpData d, string opponentId, int opponentElo, DuelResult result, bool vsBot = false)
        {
            int seals = 0;
            d.Elo = Elo.NewRating(d.Elo, opponentElo, result, d.TotalDuels, vsBot);
            d.TotalDuels++;
            if (result == DuelResult.Win) { d.Wins++; d.SeasonWins++; }
            else if (result == DuelResult.Loss) { d.Losses++; d.SeasonLosses++; }
            else { d.Draws++; d.SeasonDraws++; }

            if (result == DuelResult.Win)
            {
                if (d.WinsToday == 0) seals += PvpConfig.FirstWinOfDaySeals;
                d.WinsToday++;
            }

            if (!string.IsNullOrEmpty(opponentId))
            {
                d.OpponentsToday.TryGetValue(opponentId, out int n);
                d.OpponentsToday[opponentId] = n + 1;
            }

            if (d.Elo > d.BestEloThisSeason) d.BestEloThisSeason = d.Elo;
            var league = Leagues.FromElo(d.Elo);
            if (league > d.HighestLeague) d.HighestLeague = league;

            d.Seals += seals;
            return seals;
        }
    }
}
