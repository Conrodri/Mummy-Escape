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
        };

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
            if (tier % 25 == 0) return new PassReward(PassRewardKind.Scarabs, 300);
            if (tier % 5 == 0) return new PassReward(PassRewardKind.Scarabs, 150);
            return new PassReward(PassRewardKind.Scarabs, 40);
        }

        /// <summary>Paid track: a set piece every 10 tiers, 3 golden scarabs at every 5 in between, scarabs otherwise.</summary>
        public static PassReward Premium(PassSeason season, int tier)
        {
            if (tier % 10 == 0) return new PassReward(PassRewardKind.Skin, 0, season.Set[tier / 10 - 1]);
            if (tier % 5 == 0) return new PassReward(PassRewardKind.Gold, 3);
            return new PassReward(PassRewardKind.Scarabs, 60);
        }

        /// <summary>XP of a solo run: more for an escape and for each star.</summary>
        public static int SoloXp(bool won, int stars) => won ? 40 + 10 * stars : 10;

        /// <summary>XP for playing a duel or a 2v2 match; a win doubles it.</summary>
        public const int MatchXp = 50;
        public const int WinBonusXp = 50;
    }
}
