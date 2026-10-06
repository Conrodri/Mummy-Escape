using System;
using System.Collections.Generic;

namespace MummyEscape.Monetization
{
    /// <summary>One season of the pass: its legendary (given with the paid pass) and its set, one piece every 10 tiers.</summary>
    public sealed class PassSeason
    {
        public string Id;
        public string Name;
        /// <summary>First day (UTC) of the season; it lasts until the next one starts.</summary>
        public DateTime StartUtc;
        public string Legendary;
        /// <summary>The set: piece k is the paid reward of tier 10 × (k + 1).</summary>
        public string[] Set;
    }

    public enum PassRewardKind { None, Scarabs, Gold, Skin }

    public readonly struct PassReward
    {
        public readonly PassRewardKind Kind;
        public readonly int Amount;
        public readonly string SkinId;

        public PassReward(PassRewardKind kind, int amount, string skinId = null)
        {
            Kind = kind; Amount = amount; SkinId = skinId;
        }
    }

    /// <summary>
    /// The season pass: 100 tiers of 250 XP, earned by playing (solo runs, duels, 2v2). The free track gives resources,
    /// the paid one (40 golden scarabs) the season legendary at once, a set piece every 10 tiers, golden scarabs and
    /// more scarabs; it also lifts the daily game limits. Paid players collect the free track too. 10 tiers can be
    /// bought for 10 golden scarabs.
    /// </summary>
    public static class BattlePass
    {
        public const int Tiers = 100;
        public const int XpPerTier = 250;

        public static readonly IReadOnlyList<PassSeason> Seasons = new[]
        {
            new PassSeason
            {
                Id = "s1", Name = "Le Pass de Thot", StartUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), // noloc
                Legendary = "pass_s1_leg", // noloc
                Set = new[]
                {
                    "pass_s1_ink", "pass_s1_sandals", "pass_s1_quill", "pass_s1_ibis", "pass_s1_papyrus", // noloc
                    "pass_s1_crescent", "pass_s1_wings", "pass_s1_khepresh", "pass_s1_hermopolis", "pass_s1_disc", // noloc
                },
            },
            Season("s2", "Le Pass d'Anubis", 2026, 11, "pass_s2", // noloc
                "obsidian", "sandals", "was", "nemes", "natron", "lamp", "greaves", "vulture", "duat", "circlet"), // noloc
            Season("s3", "Le Pass d'Hathor", 2026, 12, "pass_s3", // noloc
                "turquoise", "mules", "sistrum", "horns", "copper", "mirror", "malachite", "lotus", "rose", "disc"), // noloc
            Season("s4", "Le Pass de Sobek", 2027, 1, "pass_s4", // noloc
                "fayum", "claws", "crook", "atef", "reeds", "cobra", "clogs", "plumes", "deep", "helm"), // noloc
            Season("s5", "Le Pass de Bastet", 2027, 2, "pass_s5", // noloc
                "amber", "slippers", "lantern", "crown", "night", "scepter", "babouches", "turban", "sand", "pschent"), // noloc
        };

        /// <summary>A monthly season starting on the 1st: its legendary is "{prefix}_leg", its set pieces "{prefix}_{piece}".</summary>
        static PassSeason Season(string id, string name, int year, int month, string prefix, params string[] pieces)
        {
            var set = new string[pieces.Length];
            for (int i = 0; i < pieces.Length; i++) set[i] = prefix + "_" + pieces[i];
            return new PassSeason
            {
                Id = id, Name = name, StartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
                Legendary = prefix + "_leg", Set = set, // noloc
            };
        }

        /// <summary>
        /// Golden scarabs of the paid track: exactly the price of the next pass, so a player who completes every
        /// season buys the pass once and keeps it for life (the free track's 10 come on top).
        /// </summary>
        public const int PremiumGoldPerStep = 4;

        public static int PremiumGoldTotal
        {
            get
            {
                int total = 0;
                for (int t = 1; t <= Tiers; t++)
                {
                    var r = Premium(Seasons[0], t);
                    if (r.Kind == PassRewardKind.Gold) total += r.Amount;
                }
                return total;
            }
        }

        /// <summary>The latest season already started (the first one before its date).</summary>
        public static PassSeason Current
        {
            get
            {
                var now = DateTime.UtcNow;
                var current = Seasons[0];
                foreach (var s in Seasons) if (s.StartUtc <= now) current = s;
                return current;
            }
        }

        /// <summary>When the season ends (the next one's start), null while no next season is planned.</summary>
        public static DateTime? EndOf(PassSeason season)
        {
            for (int i = 0; i < Seasons.Count - 1; i++) if (Seasons[i] == season) return Seasons[i + 1].StartUtc;
            return null;
        }

        public static int TierOf(int xp) => Math.Min(Tiers, xp / XpPerTier);

        /// <summary>Free track: scarabs every tier, more every 5, golden scarabs at 50 and 100.</summary>
        public static PassReward Free(int tier)
        {
            if (tier % 50 == 0) return new PassReward(PassRewardKind.Gold, 5);
            if (tier % 25 == 0) return new PassReward(PassRewardKind.Scarabs, 150);
            if (tier % 5 == 0) return new PassReward(PassRewardKind.Scarabs, 40);
            return new PassReward(PassRewardKind.Scarabs, 8);
        }

        // Scarab budget. A typical player (10 solo runs, 3 duels, 3 2v2 a day) earns ~900 XP a day, ~25 tiers a week:
        // the free track gives 1580 a season (~400 a week) and solo wins about 100 more (2 each), ~500 a week in all,
        // plus the stars of new levels while the campaign lasts (10 each, 1500 in all). The paid track adds ~250 a week.

        /// <summary>
        /// Paid track: a set piece every 10 tiers, <see cref="PremiumGoldPerStep"/> golden scarabs at every 5 in between
        /// (40 in all: the next pass), scarabs otherwise.
        /// </summary>
        public static PassReward Premium(PassSeason season, int tier)
        {
            if (tier % 10 == 0) return new PassReward(PassRewardKind.Skin, 0, season.Set[tier / 10 - 1]);
            if (tier % 5 == 0) return new PassReward(PassRewardKind.Gold, PremiumGoldPerStep);
            return new PassReward(PassRewardKind.Scarabs, 15);
        }

        /// <summary>XP of a solo run: more for an escape and for each star.</summary>
        public static int SoloXp(bool won, int stars) => won ? 40 + 10 * stars : 10;

        /// <summary>XP for playing a duel or a 2v2 match; a win doubles it.</summary>
        public const int MatchXp = 50;
        public const int WinBonusXp = 50;
    }
}
