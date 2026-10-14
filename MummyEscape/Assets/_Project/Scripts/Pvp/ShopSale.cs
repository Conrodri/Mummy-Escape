using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// The weekly sale, the same on every device and on the server: each week (Monday 00:00 UTC), 3 commons, 2 rares,
    /// 1 epic and 1 legendary at -25 %. Each pool is dealt like a shuffled deck: an item comes back only once all the
    /// others of its pool have been on sale, and never two weeks running across two decks. The server only prices the golden scarab items (epic and
    /// legendary Treasure exclusives); the scarab pieces are priced by <see cref="ScarabShop"/>.
    /// </summary>
    public static class ShopSale
    {
        public const int PercentOff = 25;
        public const int Commons = 3, Rares = 2, Epics = 1, Legendaries = 1;
        public const ulong CommonSalt = 1, RareSalt = 2, EpicSalt = 3, LegendarySalt = 4;

        /// <summary>A purchase started just before the change still gets the price it was shown.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

        /// <summary>Length of one sale.</summary>
        public const int PeriodDays = 7;

        static readonly DateTime Epoch = new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc); // a Monday

        public static int Price(int full) => full - full * PercentOff / 100;

        /// <summary>Number of the sale running at <paramref name="utc"/> (one per week).</summary>
        public static int Day(DateTime utc) => Math.Max(0, (int)Math.Floor((utc - Epoch).TotalDays / PeriodDays));

        /// <summary>When this week's sale ends (next Monday 00:00 UTC).</summary>
        public static DateTime Ends(DateTime utc) => Epoch.AddDays((Day(utc) + 1) * PeriodDays);

        /// <summary>The treasure exclusives the sale deals from (event items stay out: they are only sold during their event).</summary>
        public static readonly IReadOnlyList<GoldItem> GoldEpics = Exclusives(false);
        public static readonly IReadOnlyList<GoldItem> GoldLegendaries = Exclusives(true);

        static List<GoldItem> Exclusives(bool legendary)
        {
            var list = new List<GoldItem>();
            foreach (var i in GoldShop.Exclusives)
                if (!i.Halloween && i.SkinId.StartsWith("gold_leg_", StringComparison.Ordinal) == legendary) list.Add(i); // noloc
            return list;
        }

        /// <summary>The golden scarab items on sale that week: the epic then the legendary.</summary>
        public static List<GoldItem> GoldOnSale(DateTime utc)
        {
            int day = Day(utc);
            var list = Deal(GoldEpics, Epics, day, EpicSalt);
            list.AddRange(Deal(GoldLegendaries, Legendaries, day, LegendarySalt));
            return list;
        }

        /// <summary>What a treasure exclusive costs now: -25 % while on sale (or in the grace minutes after it).</summary>
        public static int GoldPrice(GoldItem item, DateTime utc) =>
            GoldOnSale(utc).Contains(item) || GoldOnSale(utc - Grace).Contains(item) ? Price(item.Gold) : item.Gold;

        /// <summary>
        /// The <paramref name="count"/> items of <paramref name="pool"/> on sale on <paramref name="day"/>: the pool is
        /// dealt <paramref name="count"/> a day from a sequence of decks, each a shuffle of the whole pool seeded by its
        /// number. A new deck never starts with the items that ended the previous one.
        /// </summary>
        public static List<T> Deal<T>(IReadOnlyList<T> pool, int count, int day, ulong salt)
        {
            int n = pool.Count;
            var result = new List<T>();
            if (n <= count) { result.AddRange(pool); return result; }
            long start = (long)day * count;
            int first = (int)(start / n), last = (int)((start + count - 1) / n);
            List<int> deck = null;
            for (int c = 0; c <= last; c++)
            {
                deck = Deck(n, count, c, salt, deck); // each deck depends on the one before: dealt from the first
                if (c < first) continue;
                long from = Math.Max(start, (long)c * n), to = Math.Min(start + count, (long)(c + 1) * n);
                for (long p = from; p < to; p++) result.Add(pool[deck[(int)(p - (long)c * n)]]);
            }
            return result;
        }

        static List<int> Deck(int n, int count, int number, ulong salt, List<int> previous)
        {
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            new Pcg32(Pcg32.Hash(salt, (ulong)number)).Shuffle(order);
            if (previous == null) return order;
            // The items that ended the previous deck wait: the first ones dealt are taken among the others.
            var tail = new HashSet<int>(previous.GetRange(n - count, count));
            var front = new List<int>(n);
            var rest = new List<int>(n);
            foreach (int i in order)
                if (front.Count < count && !tail.Contains(i)) front.Add(i);
                else rest.Add(i);
            front.AddRange(rest);
            return front;
        }
    }
}
