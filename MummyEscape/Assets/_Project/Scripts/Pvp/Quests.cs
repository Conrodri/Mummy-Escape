using System;
using System.Collections.Generic;
using System.Globalization;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>What a quest counts.</summary>
    public enum QuestKind
    {
        /// <summary>A campaign level escaped (solo run replayed by the server).</summary>
        CampaignWin,
        /// <summary>An endless tomb escaped (solo run replayed by the server).</summary>
        EndlessWin,
        /// <summary>A duel resolved (any result).</summary>
        DuelPlay,
        /// <summary>A duel won.</summary>
        DuelWin,
        /// <summary>A 2v2 game: a 2v2 campaign tomb cleared or a 2v2 duel resolved.</summary>
        DuoPlay,
        /// <summary>A screen seen, reported by the phone (cannot be checked, so it pays little).</summary>
        Visit,
        /// <summary>The weekly quest: days of the week with every daily quest done.</summary>
        DailyDays,
        /// <summary>A campaign run replayed, escaped or not (counts toward <see cref="PveGames"/>).</summary>
        CampaignPlay,
        /// <summary>Weekly: PvE games (campaign runs and endless escapes).</summary>
        PveGames,
        /// <summary>Weekly: PvP games (duels and 2v2 games).</summary>
        PvpGames,
    }

    /// <summary>What a quest pays once claimed.</summary>
    public enum QuestRewardKind { PassXp, Scarabs, Seals, Gold }

    /// <summary>The family of a daily quest: each day deals <see cref="Quests.PerDay"/> of each.</summary>
    public enum QuestGroup { Pve, Pvp, Visit }

    public sealed class QuestDef
    {
        public string Id;
        public QuestKind Kind;
        public QuestGroup Group;
        public int Target;
        public QuestRewardKind Reward;
        public int Amount;
        /// <summary>French text, the Loc key (formatted with the target).</summary>
        public string Text;
        public bool Weekly;
    }

    /// <summary>
    /// The player's quests, held by the server in <see cref="PlayerPvpData.Quests"/>: progress of today's daily quests (UTC
    /// day) and of this week's weekly quest (week starting Monday 00:00 UTC, like the shop sale).
    /// </summary>
    [Serializable]
    public class QuestBook
    {
        /// <summary>UTC day of <see cref="Progress"/> ("2026-10-14").</summary>
        public string Day;
        /// <summary>Monday (UTC) of the week of <see cref="DaysDone"/>.</summary>
        public string Week;
        /// <summary>Daily progress, "id=count".</summary>
        public List<string> Progress = new List<string>();
        /// <summary>Daily quests claimed today.</summary>
        public List<string> Claimed = new List<string>();
        /// <summary>Weekly quests claimed this week.</summary>
        public List<string> WeeklyClaimed = new List<string>();
        /// <summary>Days of this week with every daily quest done (each counted once).</summary>
        public List<string> DaysDone = new List<string>();
        /// <summary>Weekly progress of the game-count quests, "id=count".</summary>
        public List<string> WeekProgress = new List<string>();
    }

    /// <summary>
    /// Daily quests (each day 2 PvE, 2 PvP and 1 visit dealt from <see cref="Pool"/>) and the weekly quests (all daily quests done on five days of the week, 30 PvP
    /// games, 50 PvE games). Progress is counted by the server from the runs and matches it checks; a reward is paid when the player
    /// claims a finished quest.
    /// </summary>
    public static class Quests
    {
        public const string VisitSale = "visit_sale", VisitDuoBoard = "visit_duo_board"; // noloc
        public const string WeeklyId = "weekly_days"; // noloc
        public const int WeeklyDays = 5;

        /// <summary>Every daily quest; <see cref="Today"/> deals the day's.</summary>
        public static readonly IReadOnlyList<QuestDef> Pool = new[]
        {
            new QuestDef { Id = "campaign_win", Kind = QuestKind.CampaignWin, Group = QuestGroup.Pve, Target = 3, Reward = QuestRewardKind.Scarabs, Amount = 30, Text = "Réussis {0} niveaux de la campagne" }, // noloc
            new QuestDef { Id = "endless_win", Kind = QuestKind.EndlessWin, Group = QuestGroup.Pve, Target = 2, Reward = QuestRewardKind.PassXp, Amount = 50, Text = "Échappe-toi de {0} tombeaux sans fin" }, // noloc
            new QuestDef { Id = "duel_play", Kind = QuestKind.DuelPlay, Group = QuestGroup.Pvp, Target = 3, Reward = QuestRewardKind.Seals, Amount = 5, Text = "Joue {0} duels" }, // noloc
            new QuestDef { Id = "duel_win", Kind = QuestKind.DuelWin, Group = QuestGroup.Pvp, Target = 1, Reward = QuestRewardKind.Seals, Amount = 5, Text = "Gagne {0} duel" }, // noloc
            new QuestDef { Id = "duo_play", Kind = QuestKind.DuoPlay, Group = QuestGroup.Pvp, Target = 1, Reward = QuestRewardKind.Scarabs, Amount = 30, Text = "Joue {0} partie en 2v2" }, // noloc
            new QuestDef { Id = VisitSale, Kind = QuestKind.Visit, Group = QuestGroup.Visit, Target = 1, Reward = QuestRewardKind.PassXp, Amount = 20, Text = "Va voir les soldes de la boutique" },
            new QuestDef { Id = VisitDuoBoard, Kind = QuestKind.Visit, Group = QuestGroup.Visit, Target = 1, Reward = QuestRewardKind.PassXp, Amount = 20, Text = "Va voir le classement 2v2" },
        };

        public static readonly QuestDef Weekly =
            new QuestDef { Id = WeeklyId, Kind = QuestKind.DailyDays, Target = WeeklyDays, Reward = QuestRewardKind.Gold, Amount = 5, Text = "Termine les quêtes du jour {0} jours cette semaine", Weekly = true };

        /// <summary>The weekly quests: the days of daily quests, then the game counts.</summary>
        public static readonly IReadOnlyList<QuestDef> Weeklies = new[]
        {
            Weekly,
            new QuestDef { Id = "weekly_pvp", Kind = QuestKind.PvpGames, Target = 30, Reward = QuestRewardKind.Seals, Amount = 25, Text = "Joue {0} parties PvP cette semaine", Weekly = true }, // noloc
            new QuestDef { Id = "weekly_pve", Kind = QuestKind.PveGames, Target = 50, Reward = QuestRewardKind.Scarabs, Amount = 150, Text = "Joue {0} parties PvE cette semaine", Weekly = true }, // noloc
        };

        /// <summary>The weekly game count a game of that kind adds to (null: none).</summary>
        static QuestKind? GamesOf(QuestKind kind) =>
            kind == QuestKind.CampaignPlay || kind == QuestKind.EndlessWin ? QuestKind.PveGames
            : kind == QuestKind.DuelPlay || kind == QuestKind.DuoPlay ? QuestKind.PvpGames : (QuestKind?)null;

        public static QuestDef Get(string id)
        {
            foreach (var q in Weeklies) if (q.Id == id) return q;
            foreach (var q in Pool) if (q.Id == id) return q;
            return null;
        }

        /// <summary>Daily quests dealt per group: 2 PvE, 2 PvP, 1 visit.</summary>
        public static int PerDay(QuestGroup g) => g == QuestGroup.Visit ? 1 : 2;

        const ulong TodaySalt = 0x51E57;
        static string _todayDay;
        static List<QuestDef> _today;

        /// <summary>
        /// The daily quests of that UTC day ("yyyy-MM-dd"), the same on every phone and on the server: each group of
        /// <see cref="Pool"/> shuffled by the day, its first <see cref="PerDay"/> taken, in pool order.
        /// </summary>
        public static IReadOnlyList<QuestDef> Today(string day)
        {
            lock (Pool)
            {
                if (day == _todayDay) return _today;
                long number = DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) // noloc
                    ? (long)(date - DateTime.MinValue).TotalDays : 0;
                var picked = new HashSet<QuestDef>();
                foreach (QuestGroup g in new[] { QuestGroup.Pve, QuestGroup.Pvp, QuestGroup.Visit })
                {
                    var group = new List<QuestDef>();
                    foreach (var q in Pool) if (q.Group == g) group.Add(q);
                    new Pcg32(Pcg32.Hash(TodaySalt + (ulong)g, (ulong)number)).Shuffle(group);
                    for (int i = 0; i < Math.Min(PerDay(g), group.Count); i++) picked.Add(group[i]);
                }
                var list = new List<QuestDef>();
                foreach (var q in Pool) if (picked.Contains(q)) list.Add(q);
                _todayDay = day;
                _today = list;
                return list;
            }
        }

        public static string DayOf(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // noloc

        /// <summary>The Monday (UTC) of that week: the shop sale's week boundary.</summary>
        public static string WeekOf(DateTime utc) => DayOf(utc.Date.AddDays(-(((int)utc.DayOfWeek + 6) % 7)));

        /// <summary>Resets the book's daily part when utc is a new UTC day, weekly part when a new week.</summary>
        public static void Roll(QuestBook b, DateTime utc)
        {
            b.Progress ??= new List<string>();
            b.Claimed ??= new List<string>();
            b.WeeklyClaimed ??= new List<string>();
            b.DaysDone ??= new List<string>();
            b.WeekProgress ??= new List<string>();
            string day = DayOf(utc), week = WeekOf(utc);
            if (b.Day != day)
            {
                b.Day = day;
                b.Progress.Clear();
                b.Claimed.Clear();
            }
            if (b.Week != week)
            {
                b.Week = week;
                b.WeeklyClaimed.Clear();
                b.DaysDone.Clear();
                b.WeekProgress.Clear();
            }
        }

        /// <summary>Daily: its progress; weekly: the days done or the games; capped at the target.</summary>
        public static int CountOf(QuestBook b, QuestDef q) => Math.Min(q.Target,
            q.Kind == QuestKind.DailyDays ? b.DaysDone?.Count ?? 0 : Progress(q.Weekly ? b.WeekProgress : b.Progress, q.Id));

        public static bool IsDone(QuestBook b, QuestDef q) => CountOf(b, q) >= q.Target;

        public static bool IsClaimed(QuestBook b, QuestDef q) => (q.Weekly ? b.WeeklyClaimed : b.Claimed)?.Contains(q.Id) == true;

        /// <summary>
        /// Rolls, adds n to every daily quest of that kind (not Visit) and to the weekly game count it belongs to, records
        /// the day in DaysDone if all dailies are now done; returns the quests (daily and weekly) that became done with this call.
        /// </summary>
        public static List<QuestDef> Add(QuestBook b, QuestKind kind, int n, DateTime utc)
        {
            Roll(b, utc);
            if (n <= 0 || kind == QuestKind.Visit || kind == QuestKind.DailyDays) return new List<QuestDef>();
            var games = GamesOf(kind);
            return Change(b, () =>
            {
                foreach (var q in Today(b.Day))
                    if (q.Kind == kind) SetProgress(b.Progress, q.Id, Math.Min(q.Target, Progress(b.Progress, q.Id) + n));
                foreach (var q in Weeklies)
                    if (q.Kind == games) SetProgress(b.WeekProgress, q.Id, Math.Min(q.Target, Progress(b.WeekProgress, q.Id) + n));
            });
        }

        /// <summary>Rolls, marks a Visit quest done; returns the quests that became done with this call.</summary>
        public static List<QuestDef> Visit(QuestBook b, string id, DateTime utc)
        {
            Roll(b, utc);
            var q = Get(id);
            if (q == null || q.Kind != QuestKind.Visit || !IsToday(b, q)) return new List<QuestDef>();
            return Change(b, () => SetProgress(b.Progress, q.Id, q.Target));
        }

        /// <summary>Number of finished unclaimed quests (for a menu badge).</summary>
        public static int Claimable(QuestBook b, DateTime utc)
        {
            Roll(b, utc);
            int n = 0;
            foreach (var q in Weeklies) if (IsDone(b, q) && !IsClaimed(b, q)) n++;
            foreach (var q in Today(b.Day)) if (IsDone(b, q) && !IsClaimed(b, q)) n++;
            return n;
        }

        /// <summary>Applies a change of today's progress, notes the day if every daily quest is now done, returns what became done.</summary>
        static List<QuestDef> Change(QuestBook b, Action change)
        {
            var before = new List<QuestDef>();
            foreach (var q in Today(b.Day)) if (IsDone(b, q)) before.Add(q);
            foreach (var q in Weeklies) if (IsDone(b, q)) before.Add(q);
            change();
            bool all = true;
            foreach (var q in Today(b.Day)) all &= IsDone(b, q);
            if (all && !b.DaysDone.Contains(b.Day)) b.DaysDone.Add(b.Day);
            var done = new List<QuestDef>();
            foreach (var q in Today(b.Day)) if (IsDone(b, q) && !before.Contains(q)) done.Add(q);
            foreach (var q in Weeklies) if (IsDone(b, q) && !before.Contains(q)) done.Add(q);
            return done;
        }

        static bool IsToday(QuestBook b, QuestDef q)
        {
            foreach (var t in Today(b.Day)) if (t == q) return true;
            return false;
        }

        static int Progress(List<string> progress, string id)
        {
            if (progress == null) return 0;
            foreach (var entry in progress)
                if (entry.Length > id.Length && entry[id.Length] == '=' && entry.StartsWith(id, StringComparison.Ordinal)
                    && int.TryParse(entry.Substring(id.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    return n;
            return 0;
        }

        static void SetProgress(List<string> progress, string id, int n)
        {
            progress.RemoveAll(e => e.StartsWith(id + "=", StringComparison.Ordinal));
            progress.Add(id + "=" + n.ToString(CultureInfo.InvariantCulture));
        }
    }
}
