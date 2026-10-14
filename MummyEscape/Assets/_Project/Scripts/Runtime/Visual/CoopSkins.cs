using System.Collections.Generic;
using MummyEscape.Monetization;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// The 2v2 campaign's cosmetics (<see cref="ScarabShop.CoopItems"/>): sold for ordinary scarabs, each once the player
    /// has its 2v2 stars (the server checks them). Existing shapes in the duo's turquoise and gold.
    /// </summary>
    public static class CoopSkins
    {
        public const string Badge = "Campagne 2v2";

        static readonly Color32 Duo = new Color32(90, 230, 210, 255);

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            new SkinDef { Id = "coop_bandages", Name = "Bandelettes jumelles", Price = ScarabShop.PriceOf("coop_bandages"), Slot = CosmeticSlot.Color, Badge = Badge,
                Bandage = new Color32(64, 150, 140, 255), Shadow = new Color32(30, 82, 76, 255), Eyes = new Color32(255, 210, 90, 255), Torch = new Color(0.5f, 1f, 0.9f) },
            new SkinDef { Id = "coop_torch", Name = "Ankh des deux souffles", Price = ScarabShop.PriceOf("coop_torch"), Slot = CosmeticSlot.Torch, Badge = Badge,
                TorchStyle = TorchStyle.Ankh, Torch = new Color(0.45f, 1f, 0.9f), Tint = Duo },
            new SkinDef { Id = "coop_hat", Name = "Diadème du duo", Price = ScarabShop.PriceOf("coop_hat"), Slot = CosmeticSlot.Hat, Badge = Badge,
                Hat = HatStyle.ScarabCirclet, Tint = Duo },
            new SkinDef { Id = "coop_shoes", Name = "Sandales des deux rives", Price = ScarabShop.PriceOf("coop_shoes"), Slot = CosmeticSlot.Shoes, Badge = Badge,
                Shoes = ShoeStyle.WingedSandals, Tint = Duo },
        };

        public static SkinDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        /// <summary>The 2v2 stars this piece asks for (0: not a 2v2 piece).</summary>
        public static int MinStars(SkinDef s) => ScarabShop.Get(s.Id)?.MinCoopStars ?? 0;

        public static IEnumerable<SkinDef> InSlot(CosmeticSlot slot)
        {
            foreach (var s in All) if (s.Slot == slot) yield return s;
        }
    }
}
