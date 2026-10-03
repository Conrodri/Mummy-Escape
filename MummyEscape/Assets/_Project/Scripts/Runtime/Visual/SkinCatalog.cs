using System.Collections.Generic;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>What a shop item dresses: the mummy itself, its colours, the torch, a hat or shoes.</summary>
    public enum CosmeticSlot { Mummy, Color, Torch, Hat, Shoes }

    /// <summary>Silhouette of the mummy.</summary>
    public enum MummyShape { Classic, Cat, Jackal }

    /// <summary>Texture painted over the bandages (on top of the colours).</summary>
    public enum BandagePattern { Plain, Hieroglyphs, Moss, Stone, Circuit, Lava }

    public enum TorchStyle { Classic, Scepter, Lantern, Bone, Plasma, Obsidian }

    public enum HatStyle { None, Explorer, Nemes, Lotus, BrokenHelm, CyberEars, SunDisk }

    public enum ShoeStyle { None, Slippers, GoldSandals, MudBoots, StoneBoots, JetBoots, ObsidianHooves }

    /// <summary>One shop item. Only the fields of its <see cref="Slot"/> matter.</summary>
    public sealed class SkinDef
    {
        public string Id;
        public string Name;
        public int Price;
        public CosmeticSlot Slot = CosmeticSlot.Color;
        /// <summary>Act whose theme the item belongs to (1-5), 0 for the classics.</summary>
        public int Theme;

        /// <summary>Stars the player must have won before buying it: <see cref="SkinCatalog.StarsPerTheme"/> per act of its theme.</summary>
        public int MinStars => Theme * SkinCatalog.StarsPerTheme;

        // Mummy
        public MummyShape Shape;
        public BandagePattern Pattern;
        // Colour
        public Color32 Bandage;
        public Color32 Shadow;
        public Color32 Eyes;
        /// <summary>Tint of the classic torch's light (themed torches have their own).</summary>
        public Color Torch;
        // Torch, hat, shoes
        public TorchStyle TorchStyle;
        public HatStyle Hat;
        public ShoeStyle Shoes;
    }

    /// <summary>Everything the mummy wears at once.</summary>
    public readonly struct Loadout
    {
        public readonly SkinDef Mummy, Color, Torch, Hat, Shoes;

        public Loadout(SkinDef mummy, SkinDef color, SkinDef torch, SkinDef hat, SkinDef shoes)
        {
            Mummy = mummy; Color = color; Torch = torch; Hat = hat; Shoes = shoes;
        }

        /// <summary>The same outfit with one item swapped (shop previews).</summary>
        public Loadout With(SkinDef item) => item.Slot switch
        {
            CosmeticSlot.Mummy => new Loadout(item, Color, Torch, Hat, Shoes),
            CosmeticSlot.Color => new Loadout(Mummy, item, Torch, Hat, Shoes),
            CosmeticSlot.Torch => new Loadout(Mummy, Color, item, Hat, Shoes),
            CosmeticSlot.Hat => new Loadout(Mummy, Color, Torch, item, Shoes),
            _ => new Loadout(Mummy, Color, Torch, Hat, item),
        };

        public string Key => $"{Mummy.Id}_{Color.Id}_{Torch.Id}_{Hat.Id}_{Shoes.Id}";

        /// <summary>Colour of the torch light: the colour's tint for the classic torch, the torch's own otherwise.</summary>
        public UnityEngine.Color Light => Torch.TorchStyle == TorchStyle.Classic ? Color.Torch : Torch.Torch;
    }

    /// <summary>
    /// Shop catalogue, bought with scarabs (earned by collecting new stars). Five slots: the mummy (silhouette and
    /// texture), its colours (every recolour fits every mummy), the torch, a hat and shoes. Each act has its themed
    /// collection: a textured mummy, a torch, a hat and shoes in the colours of its tomb.
    /// </summary>
    public static class SkinCatalog
    {
        public const string DefaultSkinId = "classic";
        public const string DefaultMummyId = "mummy_classic";
        public const string DefaultTorchId = "torch_classic";
        public const string NoHatId = "hat_none";
        public const string NoShoesId = "shoes_none";

        /// <summary>An act's collection unlocks with 25 stars per act: 25 for the first, 125 for the fifth (150 in the game).</summary>
        public const int StarsPerTheme = 25;

        static SkinDef Mummy(string id, string name, int price, int theme, MummyShape shape, BandagePattern pattern) =>
            new SkinDef { Id = id, Name = name, Price = price, Slot = CosmeticSlot.Mummy, Theme = theme, Shape = shape, Pattern = pattern };

        static SkinDef Torch(string id, string name, int price, int theme, TorchStyle style, Color light) =>
            new SkinDef { Id = id, Name = name, Price = price, Slot = CosmeticSlot.Torch, Theme = theme, TorchStyle = style, Torch = light };

        static SkinDef Hat(string id, string name, int price, int theme, HatStyle hat) =>
            new SkinDef { Id = id, Name = name, Price = price, Slot = CosmeticSlot.Hat, Theme = theme, Hat = hat };

        static SkinDef Shoes(string id, string name, int price, int theme, ShoeStyle shoes) =>
            new SkinDef { Id = id, Name = name, Price = price, Slot = CosmeticSlot.Shoes, Theme = theme, Shoes = shoes };

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            // ---- Mummies: silhouettes, then the textured mummy of each act.
            Mummy(DefaultMummyId, "Momie classique", 0, 0, MummyShape.Classic, BandagePattern.Plain),
            Mummy("mummy_cat", "Momie de Bastet", 150, 0, MummyShape.Cat, BandagePattern.Plain),
            Mummy("mummy_jackal", "Momie chacal", 200, 0, MummyShape.Jackal, BandagePattern.Plain),
            Mummy("mummy_glyphs", "Momie aux hiéroglyphes", 120, 1, MummyShape.Classic, BandagePattern.Hieroglyphs),
            Mummy("mummy_moss", "Momie des marais", 160, 2, MummyShape.Classic, BandagePattern.Moss),
            Mummy("mummy_stone", "Momie de pierre", 200, 3, MummyShape.Classic, BandagePattern.Stone),
            Mummy("mummy_circuit", "Momie cybernétique", 240, 4, MummyShape.Classic, BandagePattern.Circuit),
            Mummy("mummy_lava", "Momie de braise", 280, 5, MummyShape.Classic, BandagePattern.Lava),

            // ---- Colours (the original skins): they recolour any mummy.
            new SkinDef { Id = DefaultSkinId, Name = "Momie du Nil", Price = 0,
                Bandage = new Color32(232, 220, 192, 255), Shadow = new Color32(168, 149, 122, 255), Eyes = new Color32(124, 240, 255, 255), Torch = new Color(1f, 0.72f, 0.42f) },
            new SkinDef { Id = "gold", Name = "Pharaon doré", Price = 120,
                Bandage = new Color32(242, 201, 76, 255), Shadow = new Color32(184, 134, 43, 255), Eyes = new Color32(255, 255, 255, 255), Torch = new Color(1f, 0.82f, 0.45f) },
            new SkinDef { Id = "anubis", Name = "Ombre d'Anubis", Price = 180,
                Bandage = new Color32(54, 52, 64, 255), Shadow = new Color32(22, 22, 28, 255), Eyes = new Color32(255, 201, 60, 255), Torch = new Color(1f, 0.6f, 0.3f) },
            new SkinDef { Id = "jade", Name = "Momie de jade", Price = 240,
                Bandage = new Color32(111, 207, 151, 255), Shadow = new Color32(46, 125, 91, 255), Eyes = new Color32(224, 255, 240, 255), Torch = new Color(0.6f, 1f, 0.75f) },
            new SkinDef { Id = "lapis", Name = "Gardien lapis", Price = 300,
                Bandage = new Color32(59, 91, 169, 255), Shadow = new Color32(30, 47, 94, 255), Eyes = new Color32(255, 211, 110, 255), Torch = new Color(0.55f, 0.7f, 1f) },
            new SkinDef { Id = "scarab", Name = "Scarabée sacré", Price = 400,
                Bandage = new Color32(31, 111, 107, 255), Shadow = new Color32(14, 59, 57, 255), Eyes = new Color32(155, 255, 92, 255), Torch = new Color(0.7f, 1f, 0.5f) },

            // ---- Torches.
            Torch(DefaultTorchId, "Torche de bois", 0, 0, TorchStyle.Classic, new Color(1f, 0.72f, 0.42f)),
            Torch("torch_scepter", "Sceptre d'or", 100, 1, TorchStyle.Scepter, new Color(1f, 0.85f, 0.5f)),
            Torch("torch_lantern", "Lanterne de nacre", 150, 2, TorchStyle.Lantern, new Color(0.55f, 0.95f, 1f)),
            Torch("torch_bone", "Torche d'os", 200, 3, TorchStyle.Bone, new Color(0.75f, 1f, 0.6f)),
            Torch("torch_plasma", "Torche à plasma", 250, 4, TorchStyle.Plasma, new Color(0.45f, 0.95f, 1f)),
            Torch("torch_obsidian", "Torche d'obsidienne", 300, 5, TorchStyle.Obsidian, new Color(1f, 0.4f, 0.2f)),

            // ---- Hats.
            Hat(NoHatId, "Tête nue", 0, 0, HatStyle.None),
            Hat("hat_explorer", "Casque d'explorateur", 120, 0, HatStyle.Explorer),
            Hat("hat_nemes", "Némès du pharaon", 100, 1, HatStyle.Nemes),
            Hat("hat_lotus", "Couronne de lotus", 150, 2, HatStyle.Lotus),
            Hat("hat_helm", "Heaume de bronze brisé", 200, 3, HatStyle.BrokenHelm),
            Hat("hat_cyber", "Oreilles d'Anubis", 250, 4, HatStyle.CyberEars),
            Hat("hat_sun", "Disque de Râ", 300, 5, HatStyle.SunDisk),

            // ---- Shoes.
            Shoes(NoShoesId, "Pieds bandés", 0, 0, ShoeStyle.None),
            Shoes("shoes_slippers", "Babouches", 80, 0, ShoeStyle.Slippers),
            Shoes("shoes_sandals", "Sandales dorées", 100, 1, ShoeStyle.GoldSandals),
            Shoes("shoes_mud", "Bottes de vase", 150, 2, ShoeStyle.MudBoots),
            Shoes("shoes_stone", "Bottes de pierre", 200, 3, ShoeStyle.StoneBoots),
            Shoes("shoes_jet", "Bottes à réacteurs", 250, 4, ShoeStyle.JetBoots),
            Shoes("shoes_hooves", "Sabots d'obsidienne", 300, 5, ShoeStyle.ObsidianHooves),
        };

        /// <summary>Items every player owns from the start: one per slot.</summary>
        public static readonly string[] Defaults = { DefaultMummyId, DefaultSkinId, DefaultTorchId, NoHatId, NoShoesId };

        public static SkinDef Get(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return All[0];
        }

        /// <summary>Item of that slot, or the slot's default when the id is unknown or belongs to another slot.</summary>
        public static SkinDef Get(string id, CosmeticSlot slot)
        {
            foreach (var s in All) if (s.Id == id && s.Slot == slot) return s;
            foreach (var s in All) if (s.Slot == slot && s.Price == 0) return s;
            return All[0];
        }

        public static IEnumerable<SkinDef> InSlot(CosmeticSlot slot)
        {
            foreach (var s in All) if (s.Slot == slot) yield return s;
        }

        /// <summary>Act collection: its textured mummy, torch, hat and shoes (worn with any colour).</summary>
        public static List<SkinDef> ThemeSet(int act)
        {
            var set = new List<SkinDef>();
            foreach (var s in All) if (s.Theme == act && s.Slot != CosmeticSlot.Color) set.Add(s);
            return set;
        }

        /// <summary>A whole collection costs a fifth less than its pieces bought one by one.</summary>
        public static int SetPrice(int piecesPrice) => piecesPrice * 4 / 5 / 10 * 10;

        /// <summary>The classic outfit (store icon, previews).</summary>
        public static Loadout Classic => new Loadout(Get(DefaultMummyId, CosmeticSlot.Mummy), Get(DefaultSkinId, CosmeticSlot.Color),
                                                     Get(DefaultTorchId, CosmeticSlot.Torch), Get(NoHatId, CosmeticSlot.Hat), Get(NoShoesId, CosmeticSlot.Shoes));
    }
}
