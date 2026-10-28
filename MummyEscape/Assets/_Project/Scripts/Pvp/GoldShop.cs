using System.Collections.Generic;

namespace MummyEscape.Monetization
{
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
        /// <summary>Sold only while the Halloween event is open (<see cref="Core.Halloween.IsOpen"/>), server-checked.</summary>
        public bool Halloween;
    }

    /// <summary>Treasure exclusives that go together, bought at once for less.</summary>
    public sealed class GoldSet
    {
        public string Id;
        public string Name;
        public string[] SkinIds;
    }

    /// <summary>
    /// The real-money economy, shared with the PvP server (it keeps the golden wallet and checks every price). Golden
    /// scarabs are the only thing the store sells (10 € = 100); everything else is priced in golden scarabs: the pass
    /// (40, ~3.99 €), 10 pass tiers (10, ~0.99 €), scarabs and the treasure exclusives.
    /// </summary>
    public static class GoldShop
    {
        public const int PassPrice = 40;
        public const int TierBundlePrice = 10;
        public const int TierBundleSize = 10;
        /// <summary>A change of name after the first one (free).</summary>
        public const int RenamePrice = 20;

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

        /// <summary>Skins only the server's wallet gives: treasure exclusives (gold_…) and pass pieces (pass_…).</summary>
        public static bool IsPaid(string skinId) =>
            skinId != null && (skinId.StartsWith("gold_", System.StringComparison.Ordinal) || skinId.StartsWith("pass_", System.StringComparison.Ordinal)); // noloc

        public static ScarabOffer Offer(string id)
        {
            foreach (var o in ScarabOffers) if (o.Id == id) return o;
            return null;
        }

        public static GoldItem Exclusive(string skinId)
        {
            foreach (var i in Exclusives) if (i.SkinId == skinId) return i;
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
            new GoldItem { SkinId = "gold_leg_lapis", Gold = 90 }, // noloc
            new GoldItem { SkinId = "gold_leg_nile", Gold = 90 }, // noloc
            new GoldItem { SkinId = "gold_col_saqqara", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_col_rosegold", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_hat_pschent", Gold = 30 }, // noloc
            new GoldItem { SkinId = "gold_torch_ruby", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_shoes_horus", Gold = 25 }, // noloc
            new GoldItem { SkinId = "gold_hat_sun", Gold = 30 }, // noloc
            new GoldItem { SkinId = Core.Halloween.GoldSkin, Gold = 30, Halloween = true },
        };

        /// <summary>Matching treasure exclusives sold together (bought through <c>BuyGoldItem</c> with the set's id).</summary>
        public static readonly IReadOnlyList<GoldSet> Sets = new[]
        {
            new GoldSet { Id = "set_gold_saqqara", Name = "Parure de Saqqarah", SkinIds = new[] { "gold_col_saqqara", "gold_hat_pschent", "gold_torch_ruby", "gold_shoes_horus" } },
            new GoldSet { Id = "set_gold_nefertari", Name = "Parure de Néfertari", SkinIds = new[] { "gold_col_rosegold", "gold_hat_sun", "gold_torch_ruby", "gold_shoes_horus" } },
        };

        public static GoldSet Set(string id)
        {
            foreach (var s in Sets) if (s.Id == id) return s;
            return null;
        }

        /// <summary>The pieces still missing, 20 % off (like the act collections); 0 when the set is complete.</summary>
        public static int SetPrice(GoldSet set, ICollection<string> owned)
        {
            int price = 0;
            foreach (var id in set.SkinIds) if (!owned.Contains(id)) price += Exclusive(id).Gold;
            return price * 4 / 5;
        }

        /// <summary>For sale at that instant: event items only during their event.</summary>
        public static bool OnSale(GoldItem item, System.DateTime utc) => item != null && (!item.Halloween || Core.Halloween.IsOpen(utc));
    }
}
