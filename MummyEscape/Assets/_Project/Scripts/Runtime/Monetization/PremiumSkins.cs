using System.Collections.Generic;
using MummyEscape.Visual;
using UnityEngine;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// Cosmetics never sold for scarabs: the treasure exclusives ("gold_*", bought with golden scarabs) and the season
    /// pass rewards ("pass_*"). They live in the save like the shop items and are worn from the Momie menu.
    /// </summary>
    public static class PremiumSkins
    {
        public const string LapisLegendary = "gold_leg_lapis";
        public const string NileLegendary = "gold_leg_nile";

        const string Treasure = "Trésor";
        const string Pass = "Pass";

        static readonly Color32 Gold = new Color32(240, 190, 60, 255);
        static readonly Color32 Ruby = new Color32(220, 40, 70, 255);
        /// <summary>Season 1, Thoth: moonlit silver-blue.</summary>
        static readonly Color32 Moon = new Color32(140, 170, 255, 255);

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            // ---- Treasure.
            Legendary(LapisLegendary, "Lapis d'Isis", LegendaryFx.Lapis, new Color32(255, 220, 110, 255), new Color(1f, 0.85f, 0.45f), Treasure),
            Legendary(NileLegendary, "Crue du Nil", LegendaryFx.Nile, new Color32(255, 255, 255, 255), new Color(0.45f, 1f, 0.9f), Treasure),
            Colour("gold_col_saqqara", "Nuit de Saqqarah", new Color32(34, 40, 82, 255), new Color32(16, 18, 44, 255), new Color32(255, 230, 140, 255), new Color(0.6f, 0.65f, 1f), Treasure),
            Colour("gold_col_rosegold", "Or rose de Néfertari", new Color32(236, 170, 150, 255), new Color32(176, 104, 92, 255), new Color32(255, 250, 220, 255), new Color(1f, 0.75f, 0.65f), Treasure),
            new SkinDef { Id = "gold_hat_pschent", Name = "Pschent d'or massif", Slot = CosmeticSlot.Hat, Hat = HatStyle.Pschent, Tint = Gold, Badge = Treasure },
            new SkinDef { Id = "gold_torch_ruby", Name = "Cristal de rubis", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Crystal, Tint = Ruby, Torch = new Color(1f, 0.35f, 0.4f), Badge = Treasure },
            new SkinDef { Id = "gold_shoes_horus", Name = "Ailes d'or d'Horus", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.WingedSandals, Tint = Gold, Badge = Treasure },
            new SkinDef { Id = "gold_hat_sun", Name = "Disque de rubis", Slot = CosmeticSlot.Hat, Hat = HatStyle.SunDisk, Tint = Ruby, Badge = Treasure },

            // ---- Season 1 pass: Thoth, the moon and the scribes.
            Legendary("pass_s1_leg", "Clair de lune de Thot", LegendaryFx.Moon, new Color32(255, 250, 220, 255), new Color(0.7f, 0.8f, 1f), Pass),
            Colour("pass_s1_ink", "Encre de Thot", new Color32(62, 66, 142, 255), new Color32(30, 30, 82, 255), new Color32(220, 230, 255, 255), new Color(0.7f, 0.8f, 1f), Pass),
            new SkinDef { Id = "pass_s1_sandals", Name = "Sandales du scribe", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.PapyrusSandals, Tint = Moon, Badge = Pass },
            new SkinDef { Id = "pass_s1_quill", Name = "Plume du scribe", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Feather, Tint = Moon, Torch = new Color(0.7f, 0.8f, 1f), Badge = Pass },
            new SkinDef { Id = "pass_s1_ibis", Name = "Couronne de l'ibis", Slot = CosmeticSlot.Hat, Hat = HatStyle.Atef, Tint = Moon, Badge = Pass },
            Colour("pass_s1_papyrus", "Papyrus lunaire", new Color32(214, 222, 240, 255), new Color32(140, 150, 186, 255), new Color32(90, 120, 255, 255), new Color(0.8f, 0.85f, 1f), Pass),
            new SkinDef { Id = "pass_s1_crescent", Name = "Croissant de Thot", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Moon, Tint = Moon, Torch = new Color(0.65f, 0.75f, 1f), Badge = Pass },
            new SkinDef { Id = "pass_s1_wings", Name = "Ailes de l'ibis", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.WingedSandals, Tint = Moon, Badge = Pass },
            new SkinDef { Id = "pass_s1_khepresh", Name = "Couronne bleue de Thot", Slot = CosmeticSlot.Hat, Hat = HatStyle.Khepresh, Tint = Moon, Badge = Pass },
            Colour("pass_s1_hermopolis", "Nuit d'Hermopolis", new Color32(24, 26, 40, 255), new Color32(10, 10, 18, 255), new Color32(170, 200, 255, 255), new Color(0.6f, 0.7f, 1f), Pass),
            new SkinDef { Id = "pass_s1_disc", Name = "Disque lunaire", Slot = CosmeticSlot.Hat, Hat = HatStyle.SunDisk, Tint = Moon, Badge = Pass },
        };

        public static SkinDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        static SkinDef Colour(string id, string name, Color32 bandage, Color32 shadow, Color32 eyes, Color torch, string badge) =>
            new SkinDef { Id = id, Name = name, Slot = CosmeticSlot.Color, Bandage = bandage, Shadow = shadow, Eyes = eyes, Torch = torch, Badge = badge };

        static SkinDef Legendary(string id, string name, LegendaryFx fx, Color32 eyes, Color torch, string badge) => new SkinDef
        {
            Id = id, Name = name, Slot = CosmeticSlot.Color, Fx = fx, Badge = badge,
            Bandage = LegendarySkins.MarkBandage, Shadow = LegendarySkins.MarkShadow, Eyes = eyes, Torch = torch,
        };
    }
}
