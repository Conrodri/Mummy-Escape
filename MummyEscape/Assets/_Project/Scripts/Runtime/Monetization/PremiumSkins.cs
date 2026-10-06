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
        /// <summary>Season 2, Anubis: embalmer's gold.</summary>
        static readonly Color32 AnubisGold = new Color32(232, 180, 70, 255);
        /// <summary>Season 3, Hathor: turquoise and copper.</summary>
        static readonly Color32 Turquoise = new Color32(64, 214, 200, 255);
        static readonly Color32 Copper = new Color32(222, 132, 82, 255);
        /// <summary>Season 4, Sobek: reed green.</summary>
        static readonly Color32 Reed = new Color32(110, 190, 80, 255);
        /// <summary>Season 5, Bastet: amber.</summary>
        static readonly Color32 Amber = new Color32(240, 160, 60, 255);

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

            // ---- The game's team (Pvp.Developers): never sold, given to its accounts.
            Legendary(Pvp.Developers.SkinId, "Code source d'Imhotep", LegendaryFx.Developer, new Color32(240, 255, 250, 255), new Color(0.4f, 1f, 0.8f), "Développeur"),

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

            // ---- Season 2 pass: Anubis, embalming and the Duat, obsidian and gold.
            Legendary("pass_s2_leg", "Nuit d'Anubis", LegendaryFx.Embalm, new Color32(255, 214, 90, 255), new Color(1f, 0.78f, 0.35f), Pass),
            Colour("pass_s2_obsidian", "Bandelettes d'obsidienne", new Color32(44, 40, 52, 255), new Color32(20, 18, 26, 255), new Color32(255, 204, 80, 255), new Color(1f, 0.8f, 0.4f), Pass),
            new SkinDef { Id = "pass_s2_sandals", Name = "Sandales de l'embaumeur", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.PapyrusSandals, Tint = AnubisGold, Badge = Pass },
            new SkinDef { Id = "pass_s2_was", Name = "Sceptre du chacal", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Was, Tint = AnubisGold, Torch = new Color(1f, 0.75f, 0.3f), Badge = Pass },
            new SkinDef { Id = "pass_s2_nemes", Name = "Némès du gardien", Slot = CosmeticSlot.Hat, Hat = HatStyle.Nemes, Tint = AnubisGold, Badge = Pass },
            Colour("pass_s2_natron", "Natron sacré", new Color32(228, 216, 192, 255), new Color32(162, 148, 122, 255), new Color32(50, 36, 24, 255), new Color(1f, 0.9f, 0.6f), Pass),
            new SkinDef { Id = "pass_s2_lamp", Name = "Lampe de la pesée", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.OilLamp, Tint = AnubisGold, Torch = new Color(1f, 0.85f, 0.5f), Badge = Pass },
            new SkinDef { Id = "pass_s2_greaves", Name = "Jambières de la Douat", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.SilverGreaves, Tint = AnubisGold, Badge = Pass },
            new SkinDef { Id = "pass_s2_vulture", Name = "Coiffe de l'embaumeur", Slot = CosmeticSlot.Hat, Hat = HatStyle.Vulture, Tint = AnubisGold, Badge = Pass },
            Colour("pass_s2_duat", "Ombre de la Douat", new Color32(54, 32, 72, 255), new Color32(26, 14, 38, 255), new Color32(255, 214, 90, 255), new Color(0.8f, 0.5f, 1f), Pass),
            new SkinDef { Id = "pass_s2_circlet", Name = "Diadème d'Anubis", Slot = CosmeticSlot.Hat, Hat = HatStyle.ScarabCirclet, Tint = AnubisGold, Badge = Pass },

            // ---- Season 3 pass: Hathor, music and love, turquoise and copper.
            Legendary("pass_s3_leg", "Chant d'Hathor", LegendaryFx.Hathor, new Color32(170, 255, 236, 255), new Color(0.4f, 1f, 0.9f), Pass),
            Colour("pass_s3_turquoise", "Turquoise de Sérabit", new Color32(62, 190, 180, 255), new Color32(26, 110, 110, 255), new Color32(255, 240, 200, 255), new Color(0.4f, 1f, 0.9f), Pass),
            new SkinDef { Id = "pass_s3_mules", Name = "Mules de Dendérah", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.LotusSlippers, Tint = Copper, Badge = Pass },
            new SkinDef { Id = "pass_s3_sistrum", Name = "Sistre de Dendérah", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Sistrum, Tint = Copper, Torch = new Color(0.4f, 1f, 0.9f), Badge = Pass },
            new SkinDef { Id = "pass_s3_horns", Name = "Cornes célestes", Slot = CosmeticSlot.Hat, Hat = HatStyle.HathorHorns, Tint = Turquoise, Badge = Pass },
            Colour("pass_s3_copper", "Cuivre de Timna", new Color32(198, 122, 76, 255), new Color32(122, 64, 38, 255), new Color32(120, 255, 230, 255), new Color(1f, 0.65f, 0.4f), Pass),
            new SkinDef { Id = "pass_s3_mirror", Name = "Miroir d'Hathor", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Crystal, Tint = Turquoise, Torch = new Color(0.45f, 1f, 0.9f), Badge = Pass },
            new SkinDef { Id = "pass_s3_malachite", Name = "Sandales de malachite", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.LapisSandals, Tint = Turquoise, Badge = Pass },
            new SkinDef { Id = "pass_s3_lotus", Name = "Lotus de Dendérah", Slot = CosmeticSlot.Hat, Hat = HatStyle.Lotus, Tint = Copper, Badge = Pass },
            Colour("pass_s3_rose", "Ivresse d'Hathor", new Color32(236, 152, 172, 255), new Color32(170, 86, 112, 255), new Color32(255, 255, 255, 255), new Color(1f, 0.6f, 0.75f), Pass),
            new SkinDef { Id = "pass_s3_disc", Name = "Disque de turquoise", Slot = CosmeticSlot.Hat, Hat = HatStyle.SunDisk, Tint = Turquoise, Badge = Pass },

            // ---- Season 4 pass: Sobek, the crocodile of the marshes, reed green.
            Legendary("pass_s4_leg", "Écailles de Sobek", LegendaryFx.Sobek, new Color32(255, 226, 90, 255), new Color(0.6f, 1f, 0.45f), Pass),
            Colour("pass_s4_fayum", "Vase du Fayoum", new Color32(92, 112, 62, 255), new Color32(46, 58, 30, 255), new Color32(255, 220, 80, 255), new Color(0.7f, 1f, 0.4f), Pass),
            new SkinDef { Id = "pass_s4_claws", Name = "Griffes de Sobek", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.CrocBoots, Tint = Reed, Badge = Pass },
            new SkinDef { Id = "pass_s4_crook", Name = "Crosse des marais", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Crook, Tint = Reed, Torch = new Color(0.55f, 1f, 0.5f), Badge = Pass },
            new SkinDef { Id = "pass_s4_atef", Name = "Couronne de Crocodilopolis", Slot = CosmeticSlot.Hat, Hat = HatStyle.Atef, Tint = Reed, Badge = Pass },
            Colour("pass_s4_reeds", "Roseaux du Delta", new Color32(152, 182, 92, 255), new Color32(86, 112, 48, 255), new Color32(40, 64, 30, 255), new Color(0.8f, 1f, 0.5f), Pass),
            new SkinDef { Id = "pass_s4_cobra", Name = "Cobra des roseaux", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Cobra, Tint = Reed, Torch = new Color(0.6f, 1f, 0.4f), Badge = Pass },
            new SkinDef { Id = "pass_s4_clogs", Name = "Sabots du Fayoum", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.ScarabClogs, Tint = Reed, Badge = Pass },
            new SkinDef { Id = "pass_s4_plumes", Name = "Plumes de Kom Ombo", Slot = CosmeticSlot.Hat, Hat = HatStyle.MaatFeather, Tint = Reed, Badge = Pass },
            Colour("pass_s4_deep", "Eaux sombres de Sobek", new Color32(30, 72, 72, 255), new Color32(12, 36, 38, 255), new Color32(180, 255, 140, 255), new Color(0.5f, 1f, 0.7f), Pass),
            new SkinDef { Id = "pass_s4_helm", Name = "Casque du crocodile", Slot = CosmeticSlot.Hat, Hat = HatStyle.Khepresh, Tint = Reed, Badge = Pass },

            // ---- Season 5 pass: Bastet, the cats of Bubastis, amber and night.
            Legendary("pass_s5_leg", "Pelage de Bastet", LegendaryFx.Bastet, new Color32(150, 255, 140, 255), new Color(1f, 0.7f, 0.3f), Pass),
            Colour("pass_s5_amber", "Ambre de Bubastis", new Color32(222, 152, 62, 255), new Color32(150, 90, 30, 255), new Color32(70, 255, 120, 255), new Color(1f, 0.7f, 0.3f), Pass),
            new SkinDef { Id = "pass_s5_slippers", Name = "Coussinets de velours", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.Slippers, Tint = Amber, Badge = Pass },
            new SkinDef { Id = "pass_s5_lantern", Name = "Lanterne de Bubastis", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Lantern, Tint = Amber, Torch = new Color(1f, 0.75f, 0.35f), Badge = Pass },
            new SkinDef { Id = "pass_s5_crown", Name = "Coiffe de la chatte", Slot = CosmeticSlot.Hat, Hat = HatStyle.Nefertiti, Tint = Amber, Badge = Pass },
            Colour("pass_s5_night", "Nuit de Bubastis", new Color32(32, 26, 42, 255), new Color32(12, 10, 18, 255), new Color32(255, 200, 40, 255), new Color(1f, 0.75f, 0.3f), Pass),
            new SkinDef { Id = "pass_s5_scepter", Name = "Sceptre félin", Slot = CosmeticSlot.Torch, TorchStyle = TorchStyle.Scepter, Tint = Amber, Torch = new Color(1f, 0.8f, 0.4f), Badge = Pass },
            new SkinDef { Id = "pass_s5_babouches", Name = "Babouches de Bastet", Slot = CosmeticSlot.Shoes, Shoes = ShoeStyle.RubyPointed, Tint = Amber, Badge = Pass },
            new SkinDef { Id = "pass_s5_turban", Name = "Turban parfumé", Slot = CosmeticSlot.Hat, Hat = HatStyle.Turban, Tint = Amber, Badge = Pass },
            Colour("pass_s5_sand", "Tigré des sables", new Color32(236, 190, 112, 255), new Color32(170, 112, 52, 255), new Color32(40, 200, 255, 255), new Color(1f, 0.85f, 0.5f), Pass),
            new SkinDef { Id = "pass_s5_pschent", Name = "Double couronne de Bastet", Slot = CosmeticSlot.Hat, Hat = HatStyle.Pschent, Tint = Amber, Badge = Pass },
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
