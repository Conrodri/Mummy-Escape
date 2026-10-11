using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// The challenge tower's exclusive cosmetics (<see cref="Tower.RewardSkins"/>), one per reward floor: never sold,
    /// only won by escaping that floor in a run. Shown in the shop once owned.
    /// </summary>
    public static class TowerSkins
    {
        public const string Badge = "Tour de défi";

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            new SkinDef { Id = Tower.RewardSkins[0], Name = "Phare de la tour", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Crystal,
                Torch = new Color(0.8f, 0.6f, 1f), Tint = new Color32(170, 120, 255, 255), Badge = Badge },
            new SkinDef { Id = Tower.RewardSkins[1], Name = "Bandelettes du sommet", Slot = CosmeticSlot.Color, Badge = Badge,
                Bandage = new Color32(54, 58, 72, 255), Shadow = new Color32(26, 28, 38, 255), Eyes = new Color32(140, 230, 255, 255), Torch = new Color(0.55f, 0.85f, 1f) },
            new SkinDef { Id = Tower.RewardSkins[2], Name = "Couronne du sommet", Slot = CosmeticSlot.Hat, Hat = HatStyle.GuildCrown,
                Tint = new Color32(150, 230, 255, 255), Badge = Badge },
        };

        public static SkinDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        /// <summary>The floor whose escape gives this item.</summary>
        public static int FloorOf(string id)
        {
            int i = System.Array.IndexOf(Tower.RewardSkins, id);
            return i < 0 ? 0 : Tower.RewardFloors[i];
        }
    }
}
