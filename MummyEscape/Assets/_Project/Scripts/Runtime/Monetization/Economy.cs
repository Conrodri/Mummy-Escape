using System.Collections.Generic;

namespace MummyEscape.Monetization
{
    /// <summary>The three kinds of games: solo spends the solo energy, duels and 2v2 matches share the combat energy.</summary>
    public enum PlayMode { Solo, Duel, Duo }

    /// <summary>A pack of golden scarabs sold for real money through the store.</summary>
    public sealed class GoldPack
    {
        public string ProductId;
        public int Gold;
        /// <summary>Extra scarabs over the base rate (10 € = 100), shown on the card.</summary>
        public int Bonus;
        /// <summary>Price shown while the store has not sent its own localized one.</summary>
        public string FallbackPrice;
    }

    /// <summary>Scarabs bought with golden scarabs.</summary>
    public sealed class ScarabOffer
    {
        public string Id;
        public int Gold;
        public int Scarabs;
    }

    /// <summary>A treasure exclusive: a cosmetic sold only for golden scarabs.</summary>
    public sealed class GoldItem
    {
        public string SkinId;
        public int Gold;
    }

    /// <summary>
    /// The real-money economy. Golden scarabs are the only thing the store sells (10 € = 100); everything else is priced
    /// in golden scarabs: the pass (40, ~3.99 €), 10 pass tiers (10, ~0.99 €), scarabs and the treasure exclusives.
    /// </summary>
    public static class GoldShop
    {
        public const int PassPrice = 40;
        public const int TierBundlePrice = 10;
        public const int TierBundleSize = 10;

        public static readonly IReadOnlyList<GoldPack> Packs = new[]
        {
            new GoldPack { ProductId = "mummyrush.gold.10", Gold = 10, FallbackPrice = "0,99 €" }, // noloc
            new GoldPack { ProductId = "mummyrush.gold.40", Gold = 40, FallbackPrice = "3,99 €" }, // noloc
            new GoldPack { ProductId = "mummyrush.gold.100", Gold = 100, FallbackPrice = "9,99 €" }, // noloc
            new GoldPack { ProductId = "mummyrush.gold.250", Gold = 250, Bonus = 50, FallbackPrice = "19,99 €" }, // noloc
            new GoldPack { ProductId = "mummyrush.gold.700", Gold = 700, Bonus = 200, FallbackPrice = "49,99 €" }, // noloc
        };

        public static GoldPack Pack(string productId)
        {
            foreach (var p in Packs) if (p.ProductId == productId) return p;
            return null;
        }

        public static readonly IReadOnlyList<ScarabOffer> ScarabOffers = new[]
        {
            new ScarabOffer { Id = "scarabs_s", Gold = 10, Scarabs = 300 }, // noloc
            new ScarabOffer { Id = "scarabs_m", Gold = 40, Scarabs = 1400 }, // noloc
            new ScarabOffer { Id = "scarabs_l", Gold = 100, Scarabs = 4000 }, // noloc
        };

        public static readonly IReadOnlyList<GoldItem> Exclusives = new[]
        {
            new GoldItem { SkinId = PremiumSkins.LapisLegendary, Gold = 90 },
            new GoldItem { SkinId = PremiumSkins.NileLegendary, Gold = 90 },
            new GoldItem { SkinId = "gold_col_saqqara", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_col_rosegold", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_hat_pschent", Gold = 30 }, // noloc
            new GoldItem { SkinId = "gold_torch_ruby", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_shoes_horus", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_hat_sun", Gold = 30 }, // noloc
        };
    }
}
