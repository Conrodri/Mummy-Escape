using System;
using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// The Halloween event's cosmetics (<see cref="Halloween"/>): the act's reward, the shop's piece (scarabs, on sale during
    /// the event only) and the Treasure's (golden scarabs, sold by the server during the event). The scarab wheel's
    /// legendary lives with the others in <see cref="LegendarySkins"/>. Once owned, they stay.
    /// </summary>
    public static class HalloweenSkins
    {
        public const string Badge = "Halloween";

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            new SkinDef { Id = Halloween.RewardSkin, Name = "Citrouille d'Halloween", Slot = CosmeticSlot.Hat, Hat = HatStyle.Pumpkin, Badge = Badge },
            new SkinDef { Id = Halloween.ShopSkin, Name = "Momie de minuit", Price = 350, Slot = CosmeticSlot.Color, Badge = Badge,
                Bandage = new Color32(92, 56, 128, 255), Shadow = new Color32(48, 26, 72, 255), Eyes = new Color32(255, 150, 40, 255), Torch = new Color(1f, 0.55f, 0.2f) },
            new SkinDef { Id = Halloween.GoldSkin, Name = "Chapeau de sorcière", Slot = CosmeticSlot.Hat, Hat = HatStyle.Witch, Badge = Badge },
        };

        public static SkinDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        /// <summary>What the shop shows in a slot: the owned pieces, and the scarab piece while the event is open.</summary>
        public static IEnumerable<SkinDef> InShop(CosmeticSlot slot, ICollection<string> owned, DateTime utc)
        {
            foreach (var s in All)
                if (s.Slot == slot && (owned.Contains(s.Id) || s.Id == Halloween.ShopSkin && Halloween.IsOpen(utc))) yield return s;
        }
    }
}
