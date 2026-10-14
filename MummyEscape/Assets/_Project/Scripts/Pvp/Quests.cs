using System;
using System.Collections.Generic;
using System.Globalization;

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
    }

    /// <summary>What a quest pays once claimed.</summary>
    public enum QuestRewardKind { PassXp, Scarabs, Seals, Gold }

    public sealed class QuestDef
    {
        public string Id;
        public QuestKind Kind;
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
    }

    /// <summary>
    /// Daily quests (about ten games of PvE and PvP plus two visits) and the weekly quest (all daily quests done on five days
    /// of the week). Progress is counted by the server from the runs and matches it checks; a reward is paid when the player
    /// claims a finished quest.
    /// </summary>
    public static class Quests
    {
        public const string VisitSale = "visit_sale", VisitDuoBoard = "visit_duo_board"; // noloc
        public const string WeeklyId = "weekly_days"; // noloc
        public const int WeeklyDays = 5;

        public static readonly IReadOnlyList<QuestDef> Daily = new[]
        {
            new QuestDef { Id = "campaign_win", Kind = QuestKind.CampaignWin, Target = 3, Reward = QuestRewardKind.Scarabs, Amount = 30, Text = "Réussis {0} niveaux de la campagne" }, // noloc
            new QuestDef { Id = "endless_win", Kind = QuestKind.EndlessWin, Target = 2, Reward = QuestRewardKind.PassXp, Amount = 50, Text = "Échappe-toi de {0} tombeaux sans fin" }, // noloc
            new QuestDef { Id = "duel_play", Kind = QuestKind.DuelPlay, Target = 3, Reward = QuestRewardKind.Seals, Amount = 5, Text = "Joue {0} duels" }, // noloc
            new QuestDef { Id = "duel_win", Kind = QuestKind.DuelWin, Target = 1, Reward = QuestRewardKind.Seals, Amount = 5, Text = "Gagne {0} duel" }, // noloc
            new QuestDef { Id = "duo_play", Kind = QuestKind.DuoPlay, Target = 1, Reward = QuestRewardKind.Scarabs, Amount = 30, Text = "Joue {0} partie en 2v2" }, // noloc
            new QuestDef { Id = VisitSale, Kind = QuestKind.Visit, Target = 1, Reward = QuestRewardKind.PassXp, Amount = 20, Text = "Va voir les soldes de la boutique" },
            new QuestDef { Id = VisitDuoBoard, Kind = QuestKind.Visit, Target = 1, Reward = QuestRewardKind.PassXp, Amount = 20, Text = "Va voir le classement 2v2" },
        };

        public static readonly QuestDef Weekly =
            new QuestDef { Id = WeeklyId, Kind = QuestKind.DailyDays, Target = WeeklyDays, Reward = QuestRewardKind.Gold, Amount = 5, Text = "Termine les quêtes du jour {0} jours cette semaine", Weekly = true };

        public static QuestDef Get(string id)
        {
            if (id == WeeklyId) return Weekly;
            foreach (var q in Daily) if (q.Id == id) return q;
            return null;
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
            }
        }

        /// <summary>Daily: its progress; weekly: the days done; capped at the target.</summary>
        public static int CountOf(QuestBook b, QuestDef q) => Math.Min(q.Target, q.Weekly ? b.DaysDone?.Count ?? 0 : Progress(b, q.Id));

        public static bool IsDone(QuestBook b, QuestDef q) => CountOf(b, q) >= q.Target;

        public static bool IsClaimed(QuestBook b, QuestDef q) => (q.Weekly ? b.WeeklyClaimed : b.Claimed)?.Contains(q.Id) == true;

        /// <summary>
        /// Rolls, adds n to every daily quest of that kind (not Visit), records the day in DaysDone if all dailies are now
        /// done; returns the quests (daily and weekly) that became done with this call.
        /// </summary>
        public static List<QuestDef> Add(QuestBook b, QuestKind kind, int n, DateTime utc)
        {
            Roll(b, utc);
            if (n <= 0 || kind == QuestKind.Visit || kind == QuestKind.DailyDays) return new List<QuestDef>();
            return Change(b, () =>
            {
                foreach (var q in Daily)
                    if (q.Kind == kind) SetProgress(b, q.Id, Math.Min(q.Target, Progress(b, q.Id) + n));
            });
        }

        /// <summary>Rolls, marks a Visit quest done; returns the quests that became done with this call.</summary>
        public static List<QuestDef> Visit(QuestBook b, string id, DateTime utc)
        {
            Roll(b, utc);
            var q = Get(id);
            if (q == null || q.Kind != QuestKind.Visit) return new List<QuestDef>();
            return Change(b, () => SetProgress(b, q.Id, q.Target));
        }

        /// <summary>Number of finished unclaimed quests (for a menu badge).</summary>
        public static int Claimable(QuestBook b, DateTime utc)
        {
            Roll(b, utc);
            int n = IsDone(b, Weekly) && !IsClaimed(b, Weekly) ? 1 : 0;
            foreach (var q in Daily) if (IsDone(b, q) && !IsClaimed(b, q)) n++;
            return n;
        }

        /// <summary>Applies a change of today's progress, notes the day if every daily quest is now done, returns what became done.</summary>
        static List<QuestDef> Change(QuestBook b, Action change)
        {
            var before = new List<QuestDef>();
            foreach (var q in Daily) if (IsDone(b, q)) before.Add(q);
            bool weeklyBefore = IsDone(b, Weekly);
            change();
            bool all = true;
            foreach (var q in Daily) all &= IsDone(b, q);
            if (all && !b.DaysDone.Contains(b.Day)) b.DaysDone.Add(b.Day);
            var done = new List<QuestDef>();
            foreach (var q in Daily) if (IsDone(b, q) && !before.Contains(q)) done.Add(q);
            if (!weeklyBefore && IsDone(b, Weekly)) done.Add(Weekly);
            return done;
        }

        static int Progress(QuestBook b, string id)
        {
            if (b.Progress == null) return 0;
            foreach (var entry in b.Progress)
                if (entry.Length > id.Length && entry[id.Length] == '=' && entry.StartsWith(id, StringComparison.Ordinal)
                    && int.TryParse(entry.Substring(id.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    return n;
            return 0;
        }

        static void SetProgress(QuestBook b, string id, int n)
        {
            b.Progress.RemoveAll(e => e.StartsWith(id + "=", StringComparison.Ordinal));
            b.Progress.Add(id + "=" + n.ToString(CultureInfo.InvariantCulture));
        }
    }
}
