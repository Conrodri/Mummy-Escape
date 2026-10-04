using System;
using System.Collections.Generic;
using System.Globalization;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// Cosmetics won in duels, never sold for scarabs. Their ids come from the server (<see cref="SeasonRewards"/>,
    /// <see cref="SealShop"/>) and are turned into items here:
    /// <list type="bullet">
    /// <item>final league of a month, "pvp_2026-10_rank_or": a colour in the league's metal, eyes in that month's hue;</item>
    /// <item>Top 100, "pvp_2026-10_rank_top100": obsidian bandages with golden eyes;</item>
    /// <item>participation, "pvp_2026-10_participation_25": a torch (sceptre, lantern, plasma, obsidian) lit in the month's hue;</item>
    /// <item>seal shop, "pvp_shop_*": two colours and two mummies found nowhere else.</item>
    /// </list>
    /// </summary>
    public static class PvpSkins
    {
        public const string Prefix = "pvp_";

        static readonly Dictionary<string, SkinDef> Cache = new Dictionary<string, SkinDef>();

        public static bool IsPvp(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The item behind a PvP id, or null when the id is not one.</summary>
        public static SkinDef Resolve(string id)
        {
            if (!IsPvp(id)) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(id, out var cached)) return cached;
                var def = Build(id);
                if (def != null) Cache[id] = def;
                return def;
            }
        }

        /// <summary>The PvP items of a slot the player owns (shown in the shop tab of that slot, ready to wear).</summary>
        public static IEnumerable<SkinDef> Owned(IEnumerable<string> owned, CosmeticSlot slot)
        {
            foreach (var id in owned)
            {
                var def = Resolve(id);
                if (def != null && def.Slot == slot) yield return def;
            }
        }

        /// <summary>The seal shop item for a <see cref="SealShop"/> id.</summary>
        public static SkinDef ShopItem(string id) => Resolve(id);

        static SkinDef Build(string id)
        {
            switch (id)
            {
                case "pvp_shop_scales":
                    return Colour(id, "Balance de Maât", new Color32(236, 222, 186, 255), new Color32(178, 140, 64, 255), new Color32(64, 224, 208, 255), new Color(1f, 0.85f, 0.55f));
                case "pvp_shop_feather":
                    return Colour(id, "Plume de Maât", new Color32(246, 244, 250, 255), new Color32(170, 160, 205, 255), new Color32(64, 224, 208, 255), new Color(0.75f, 0.95f, 1f));
                case "pvp_shop_obsidian":
                    return new SkinDef { Id = id, Name = "Chacal d'obsidienne", Slot = CosmeticSlot.Mummy, Pvp = true, Shape = MummyShape.Jackal, Pattern = BandagePattern.Lava };
                case "pvp_shop_star":
                    return new SkinDef { Id = id, Name = "Bastet des étoiles", Slot = CosmeticSlot.Mummy, Pvp = true, Shape = MummyShape.Cat, Pattern = BandagePattern.Circuit };
                case "pvp_shop_diamond":
                    return Colour(id, "Momie de diamant", new Color32(200, 238, 255, 255), new Color32(104, 160, 214, 255), new Color32(255, 255, 255, 255), new Color(0.7f, 0.9f, 1f));
            }

            // Season rewards: pvp_{yyyy-MM}_rank_{league|top100} and pvp_{yyyy-MM}_participation_{n}.
            var parts = id.Split('_');
            if (parts.Length != 4) return null;
            string season = parts[1];
            Color hue = SeasonHue(season);
            string month = MonthName(season);
            if (parts[2] == "rank")
            {
                if (parts[3] == "top100")
                    return Colour(id, Loc.F("Élu de Maât · {0}", month), new Color32(40, 34, 46, 255), new Color32(16, 12, 20, 255),
                                  new Color32(255, 206, 84, 255), Color.Lerp(new Color(1f, 0.8f, 0.4f), hue, 0.35f), season);
                if (!Enum.TryParse(parts[3], true, out League league)) return null;
                var (bandage, shadow) = LeagueMetal(league);
                return Colour(id, Loc.F("{0} · {1}", Loc.T(LeagueName(league)), month), bandage, shadow, Bright(hue), Color.Lerp(Color.white, hue, 0.6f), season);
            }
            if (parts[2] == "participation" && int.TryParse(parts[3], out int duels))
            {
                var style = duels >= 100 ? TorchStyle.Obsidian : duels >= 50 ? TorchStyle.Plasma : duels >= 25 ? TorchStyle.Lantern : TorchStyle.Scepter;
                string name = duels >= 100 ? "Flambeau du centurion" : duels >= 50 ? "Plasma du duelliste" : duels >= 25 ? "Lanterne de l'arène" : "Sceptre du challenger";
                return new SkinDef
                {
                    Id = id, Name = Loc.T(name) + " · " + month, Slot = CosmeticSlot.Torch, Pvp = true, Season = season,
                    TorchStyle = style, Torch = Color.Lerp(Color.white, hue, 0.75f),
                };
            }
            return null;
        }

        static SkinDef Colour(string id, string name, Color32 bandage, Color32 shadow, Color32 eyes, Color torch, string season = null) =>
            new SkinDef { Id = id, Name = name, Slot = CosmeticSlot.Color, Pvp = true, Season = season, Bandage = bandage, Shadow = shadow, Eyes = eyes, Torch = torch };

        /// <summary>Each month of the year has its hue, so two seasons in the same league still look different.</summary>
        public static Color SeasonHue(string season)
        {
            int month = season != null && season.Length >= 7 && int.TryParse(season.Substring(5, 2), out int m) ? m : 1;
            return Color.HSVToRGB(((month - 1) * 5 % 12) / 12f, 0.75f, 1f);
        }

        static Color32 Bright(Color c) => Color.Lerp(c, Color.white, 0.25f);

        public static (Color32 bandage, Color32 shadow) LeagueMetal(League league)
        {
            switch (league)
            {
                case League.Argent: return (new Color32(204, 210, 220, 255), new Color32(124, 132, 146, 255));
                case League.Or: return (new Color32(242, 201, 76, 255), new Color32(170, 120, 36, 255));
                case League.Platine: return (new Color32(224, 240, 236, 255), new Color32(120, 170, 166, 255));
                case League.Diamant: return (new Color32(170, 226, 255, 255), new Color32(70, 130, 200, 255));
                default: return (new Color32(196, 128, 72, 255), new Color32(120, 70, 36, 255));
            }
        }

        /// <summary>League colour for the UI (badges, names).</summary>
        public static Color LeagueColor(League league) => Color.Lerp(LeagueMetal(league).bandage, Color.white, 0.1f);

        /// <summary>French name of a league (translated by the caller).</summary>
        public static string LeagueName(League league)
        {
            switch (league)
            {
                case League.Argent: return "Argent";
                case League.Or: return "Or";
                case League.Platine: return "Platine";
                case League.Diamant: return "Diamant";
                default: return "Bronze";
            }
        }

        /// <summary>"octobre 2026" / "October 2026", in the game's language.</summary>
        public static string MonthName(string season)
        {
            if (season == null || !DateTime.TryParseExact(season, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return season ?? "";
            var culture = CultureInfo.GetCultureInfo(Loc.Current == Loc.Lang.En ? "en-US" : "fr-FR");
            return date.ToString("MMMM yyyy", culture); // noloc
        }

        /// <summary>Drops the cached names (they are in the current language).</summary>
        /// <summary>What a rival sees of this outfit (VS screen, ghost, replays).</summary>
        public static PlayerLook Look(Loadout l) => new PlayerLook
        {
            Mummy = l.Mummy.Id, Color = l.Color.Id, Torch = l.Torch.Id, Hat = l.Hat.Id, Shoes = l.Shoes.Id,
        };

        /// <summary>The outfit behind a rival's look; unknown pieces (or no look at all) fall back to the classic ones.</summary>
        public static Loadout Loadout(PlayerLook look) => look == null ? SkinCatalog.Classic : new Loadout(
            SkinCatalog.Get(look.Mummy, CosmeticSlot.Mummy), SkinCatalog.Get(look.Color, CosmeticSlot.Color),
            SkinCatalog.Get(look.Torch, CosmeticSlot.Torch), SkinCatalog.Get(look.Hat, CosmeticSlot.Hat),
            SkinCatalog.Get(look.Shoes, CosmeticSlot.Shoes));

        /// <summary>A random outfit from the catalogue, for the simulated rivals of the offline demo.</summary>
        public static PlayerLook RandomLook(System.Random rng)
        {
            string Pick(CosmeticSlot slot)
            {
                var items = new List<SkinDef>(SkinCatalog.InSlot(slot));
                // Half the rivals keep the basic piece: an outfit full of rare items on everyone would look odd.
                return rng.Next(2) == 0 ? items[0].Id : items[rng.Next(items.Count)].Id;
            }
            return new PlayerLook
            {
                Mummy = Pick(CosmeticSlot.Mummy), Color = Pick(CosmeticSlot.Color), Torch = Pick(CosmeticSlot.Torch),
                Hat = Pick(CosmeticSlot.Hat), Shoes = Pick(CosmeticSlot.Shoes),
            };
        }

        public static void ClearCache()
        {
            lock (Cache) Cache.Clear();
        }
    }
}
