using System.Collections.Generic;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>What a shop item dresses: the mummy itself, its colours, the torch, a hat or shoes.</summary>
    public enum CosmeticSlot { Mummy, Color, Torch, Hat, Shoes }

    /// <summary>Silhouette of the mummy.</summary>
    /// <summary>Animated effect of a legendary colour (casino): the bandages change every frame.</summary>
    public enum LegendaryFx { None, Rainbow, Fire, Galaxy, Aurora, Gold, Storm, Spectre, Neon, Prism, Magma, Moon, Lapis, Nile }

    public enum MummyShape { Classic, Cat, Jackal }

    /// <summary>Texture painted over the bandages (on top of the colours).</summary>
    public enum BandagePattern { Plain, Hieroglyphs, Moss, Stone, Circuit, Lava }

    public enum TorchStyle { Classic, Scepter, Lantern, Bone, Plasma, Obsidian, Ankh, Was, Crook, Papyrus, Cobra, Crystal, OilLamp, Moon, Feather, Sistrum, GuildBanner }

    public enum HatStyle { None, Explorer, Nemes, Lotus, BrokenHelm, CyberEars, SunDisk, Pschent, Khepresh, Atef, MaatFeather, Vulture, HathorHorns, ScarabCirclet, Turban, Nefertiti, GuildCrown }

    public enum ShoeStyle { None, Slippers, GoldSandals, MudBoots, StoneBoots, JetBoots, ObsidianHooves, PapyrusSandals, LapisSandals, CrocBoots, WingedSandals, ScarabClogs, SilverGreaves, DesertBoots, RubyPointed, LotusSlippers, GuildGreaves }

    /// <summary>One shop item. Only the fields of its <see cref="Slot"/> matter.</summary>
    public sealed class SkinDef
    {
        public string Id;
        public string Name;
        public int Price;
        public CosmeticSlot Slot = CosmeticSlot.Color;
        /// <summary>Act whose theme the item belongs to (1-5), 0 for the classics.</summary>
        public int Theme;
        /// <summary>Won in duels (see <see cref="PvpSkins"/>): never sold for scarabs.</summary>
        public bool Pvp;
        /// <summary>Month a season reward belongs to ("2026-10"), null otherwise.</summary>
        public string Season;

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
        /// <summary>Legendary colours (casino) are animated: see <see cref="ArtLibrary.LegendaryFrames"/>.</summary>
        public LegendaryFx Fx;
        public bool Legendary => Fx != LegendaryFx.None;
        // Torch, hat, shoes
        public TorchStyle TorchStyle;
        public HatStyle Hat;
        public ShoeStyle Shoes;
        /// <summary>Recolours the hat, shoes or torch in one hue (pass sets); transparent = as painted.</summary>
        public Color32 Tint;
        /// <summary>Where an item never sold for scarabs comes from ("Pass", "Trésor"), shown on its card; null otherwise.</summary>
        public string Badge;
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

        /// <summary>A colour or a mummy shown alone: the classic outfit (no hat, no shoes) with just that item.</summary>
        public static Loadout Bare(SkinDef item) => SkinCatalog.Classic.With(item);

        /// <summary>Colour of the torch light: the colour's tint for the classic torch, the torch's own otherwise.</summary>
        /// <summary>The outfit changes over time (a legendary colour): drawn with <see cref="MummyAnimator"/>.</summary>
        public bool Animated => Color != null && Color.Legendary;

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

        static SkinDef Colour(string id, string name, int price, Color32 bandage, Color32 shadow, Color32 eyes, Color torch) =>
            new SkinDef { Id = id, Name = name, Price = price, Slot = CosmeticSlot.Color, Bandage = bandage, Shadow = shadow, Eyes = eyes, Torch = torch };

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
            // Silhouettes wearing the act textures (Jackal + Lava and Cat + Circuit are duel rewards).
            Mummy("mummy_cat_glyphs", "Bastet aux hiéroglyphes", 220, 0, MummyShape.Cat, BandagePattern.Hieroglyphs),
            Mummy("mummy_jackal_glyphs", "Chacal aux hiéroglyphes", 240, 0, MummyShape.Jackal, BandagePattern.Hieroglyphs),
            Mummy("mummy_cat_moss", "Bastet des marais", 260, 0, MummyShape.Cat, BandagePattern.Moss),
            Mummy("mummy_jackal_moss", "Chacal des marais", 280, 0, MummyShape.Jackal, BandagePattern.Moss),
            Mummy("mummy_cat_stone", "Bastet de pierre", 300, 0, MummyShape.Cat, BandagePattern.Stone),
            Mummy("mummy_jackal_stone", "Chacal de pierre", 320, 0, MummyShape.Jackal, BandagePattern.Stone),
            Mummy("mummy_jackal_circuit", "Chacal cybernétique", 360, 0, MummyShape.Jackal, BandagePattern.Circuit),
            Mummy("mummy_cat_lava", "Bastet de braise", 400, 0, MummyShape.Cat, BandagePattern.Lava),

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
            Colour("sand", "Momie des sables", 80, new Color32(214, 178, 120, 255), new Color32(160, 124, 76, 255), new Color32(255, 240, 180, 255), new Color(1f, 0.75f, 0.45f)),
            Colour("rose", "Rose du désert", 140, new Color32(226, 150, 160, 255), new Color32(160, 90, 104, 255), new Color32(255, 255, 255, 255), new Color(1f, 0.6f, 0.7f)),
            Colour("copper", "Cuivre ancien", 160, new Color32(184, 110, 64, 255), new Color32(116, 64, 36, 255), new Color32(120, 240, 210, 255), new Color(1f, 0.6f, 0.35f)),
            Colour("ivory", "Ivoire royal", 180, new Color32(246, 240, 224, 255), new Color32(196, 184, 160, 255), new Color32(80, 140, 255, 255), new Color(1f, 0.9f, 0.7f)),
            Colour("amethyst", "Améthyste", 200, new Color32(150, 100, 200, 255), new Color32(90, 56, 130, 255), new Color32(255, 220, 120, 255), new Color(0.8f, 0.55f, 1f)),
            Colour("turquoise", "Turquoise du Sinaï", 220, new Color32(64, 200, 190, 255), new Color32(28, 120, 120, 255), new Color32(255, 250, 200, 255), new Color(0.5f, 1f, 0.95f)),
            Colour("crimson", "Sang de Seth", 260, new Color32(170, 40, 48, 255), new Color32(96, 18, 26, 255), new Color32(255, 200, 80, 255), new Color(1f, 0.4f, 0.3f)),
            Colour("frost", "Givre du Nil", 280, new Color32(200, 228, 246, 255), new Color32(130, 166, 200, 255), new Color32(60, 120, 255, 255), new Color(0.7f, 0.85f, 1f)),
            Colour("silver", "Argent lunaire", 320, new Color32(190, 196, 206, 255), new Color32(120, 126, 138, 255), new Color32(150, 220, 255, 255), new Color(0.8f, 0.9f, 1f)),
            Colour("ebony", "Ébène et or", 450, new Color32(40, 34, 30, 255), new Color32(16, 12, 10, 255), new Color32(242, 201, 76, 255), new Color(1f, 0.8f, 0.4f)),

            // ---- Torches.
            Torch(DefaultTorchId, "Torche de bois", 0, 0, TorchStyle.Classic, new Color(1f, 0.72f, 0.42f)),
            Torch("torch_scepter", "Sceptre d'or", 100, 1, TorchStyle.Scepter, new Color(1f, 0.85f, 0.5f)),
            Torch("torch_lantern", "Lanterne de nacre", 150, 2, TorchStyle.Lantern, new Color(0.55f, 0.95f, 1f)),
            Torch("torch_bone", "Torche d'os", 200, 3, TorchStyle.Bone, new Color(0.75f, 1f, 0.6f)),
            Torch("torch_plasma", "Torche à plasma", 250, 4, TorchStyle.Plasma, new Color(0.45f, 0.95f, 1f)),
            Torch("torch_obsidian", "Torche d'obsidienne", 300, 5, TorchStyle.Obsidian, new Color(1f, 0.4f, 0.2f)),
            Torch("torch_lamp", "Lampe à huile", 120, 0, TorchStyle.OilLamp, new Color(1f, 0.7f, 0.35f)),
            Torch("torch_papyrus", "Gerbe de papyrus", 150, 0, TorchStyle.Papyrus, new Color(0.75f, 1f, 0.55f)),
            Torch("torch_crook", "Crosse du pharaon", 180, 0, TorchStyle.Crook, new Color(1f, 0.85f, 0.5f)),
            Torch("torch_ankh", "Ankh de vie", 200, 0, TorchStyle.Ankh, new Color(0.5f, 1f, 0.9f)),
            Torch("torch_was", "Sceptre ouas", 220, 0, TorchStyle.Was, new Color(0.6f, 0.75f, 1f)),
            Torch("torch_feather", "Bâton à plume", 240, 0, TorchStyle.Feather, new Color(1f, 0.95f, 0.8f)),
            Torch("torch_sistrum", "Sistre d'Hathor", 260, 0, TorchStyle.Sistrum, new Color(1f, 0.8f, 0.55f)),
            Torch("torch_cobra", "Bâton du cobra", 300, 0, TorchStyle.Cobra, new Color(0.6f, 1f, 0.4f)),
            Torch("torch_moon", "Croissant de Khonsou", 340, 0, TorchStyle.Moon, new Color(0.75f, 0.85f, 1f)),
            Torch("torch_crystal", "Cristal d'améthyste", 400, 0, TorchStyle.Crystal, new Color(0.95f, 0.55f, 1f)),

            // ---- Hats.
            Hat(NoHatId, "Tête nue", 0, 0, HatStyle.None),
            Hat("hat_explorer", "Casque d'explorateur", 120, 0, HatStyle.Explorer),
            Hat("hat_nemes", "Némès du pharaon", 100, 1, HatStyle.Nemes),
            Hat("hat_lotus", "Couronne de lotus", 150, 2, HatStyle.Lotus),
            Hat("hat_helm", "Heaume de bronze brisé", 200, 3, HatStyle.BrokenHelm),
            Hat("hat_cyber", "Oreilles d'Anubis", 250, 4, HatStyle.CyberEars),
            Hat("hat_sun", "Disque de Râ", 300, 5, HatStyle.SunDisk),
            Hat("hat_feather", "Coiffe de Maât", 120, 0, HatStyle.MaatFeather),
            Hat("hat_turban", "Turban du désert", 140, 0, HatStyle.Turban),
            Hat("hat_scarab", "Diadème du scarabée", 180, 0, HatStyle.ScarabCirclet),
            Hat("hat_horns", "Cornes d'Hathor", 220, 0, HatStyle.HathorHorns),
            Hat("hat_khepresh", "Couronne bleue", 260, 0, HatStyle.Khepresh),
            Hat("hat_vulture", "Coiffe du vautour", 300, 0, HatStyle.Vulture),
            Hat("hat_atef", "Couronne atef", 340, 0, HatStyle.Atef),
            Hat("hat_nefertiti", "Couronne de Néfertiti", 380, 0, HatStyle.Nefertiti),
            Hat("hat_pschent", "Pschent des Deux Terres", 450, 0, HatStyle.Pschent),

            // ---- Shoes.
            Shoes(NoShoesId, "Pieds bandés", 0, 0, ShoeStyle.None),
            Shoes("shoes_slippers", "Babouches", 80, 0, ShoeStyle.Slippers),
            Shoes("shoes_sandals", "Sandales dorées", 100, 1, ShoeStyle.GoldSandals),
            Shoes("shoes_mud", "Bottes de vase", 150, 2, ShoeStyle.MudBoots),
            Shoes("shoes_stone", "Bottes de pierre", 200, 3, ShoeStyle.StoneBoots),
            Shoes("shoes_jet", "Bottes à réacteurs", 250, 4, ShoeStyle.JetBoots),
            Shoes("shoes_hooves", "Sabots d'obsidienne", 300, 5, ShoeStyle.ObsidianHooves),
            Shoes("shoes_papyrus", "Sandales de papyrus", 90, 0, ShoeStyle.PapyrusSandals),
            Shoes("shoes_desert", "Bottes du désert", 120, 0, ShoeStyle.DesertBoots),
            Shoes("shoes_lotus", "Chaussons de lotus", 150, 0, ShoeStyle.LotusSlippers),
            Shoes("shoes_lapis", "Sandales de lapis", 180, 0, ShoeStyle.LapisSandals),
            Shoes("shoes_scarab", "Sabots du scarabée", 220, 0, ShoeStyle.ScarabClogs),
            Shoes("shoes_ruby", "Babouches de rubis", 260, 0, ShoeStyle.RubyPointed),
            Shoes("shoes_croc", "Bottes de Sobek", 300, 0, ShoeStyle.CrocBoots),
            Shoes("shoes_greaves", "Jambières d'argent", 340, 0, ShoeStyle.SilverGreaves),
            Shoes("shoes_winged", "Sandales ailées d'Horus", 400, 0, ShoeStyle.WingedSandals),
        };

        /// <summary>Items every player owns from the start: one per slot.</summary>
        public static readonly string[] Defaults = { DefaultMummyId, DefaultSkinId, DefaultTorchId, NoHatId, NoShoesId };

        public static SkinDef Get(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return PvpSkins.Resolve(id) ?? LegendarySkins.Resolve(id) ?? Monetization.PremiumSkins.Resolve(id) ?? All[0];
        }

        /// <summary>Item of that slot, or the slot's default when the id is unknown or belongs to another slot.</summary>
        public static SkinDef Get(string id, CosmeticSlot slot)
        {
            foreach (var s in All) if (s.Id == id && s.Slot == slot) return s;
            var pvp = PvpSkins.Resolve(id) ?? LegendarySkins.Resolve(id) ?? Monetization.PremiumSkins.Resolve(id);
            if (pvp != null && pvp.Slot == slot) return pvp;
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
