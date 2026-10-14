using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Monetization
{
    /// <summary>A piece sold for scarabs: its price and the act whose collection it belongs to (0: a classic).</summary>
    public sealed class ScarabItem
    {
        public string Id;
        public int Price;
        public int Theme;

        /// <summary>Stars the player must have won before buying it: <see cref="ScarabShop.StarsPerTheme"/> per act of its theme.</summary>
        public int MinStars => Theme * ScarabShop.StarsPerTheme;
    }

    /// <summary>
    /// The scarab shop's prices, the same on the phone (the skin catalogue reads them) and on the server, which takes the
    /// scarabs: each piece's price and act, today's sale (3 classics and 2 act pieces at -25 %), the act collections at
    /// -20 %, and Halloween's piece while the event is open.
    /// </summary>
    public static class ScarabShop
    {
        /// <summary>An act's collection unlocks with 50 stars per act: 50 for the first, 250 for the fifth.</summary>
        public const int StarsPerTheme = 50;

        /// <summary>Id, price, act: in the catalogue's order (the sale deals from it).</summary>
        const string Table =
            "mummy_cat 150 0|mummy_jackal 200 0|mummy_glyphs 120 1|mummy_moss 160 2|mummy_stone 200 3|mummy_circuit 240 4|mummy_lava 280 5|" + // noloc
            "mummy_cat_glyphs 220 0|mummy_jackal_glyphs 240 0|mummy_cat_moss 260 0|mummy_jackal_moss 280 0|mummy_cat_stone 300 0|" + // noloc
            "mummy_jackal_stone 320 0|mummy_jackal_circuit 360 0|mummy_cat_lava 400 0|" + // noloc
            "gold 120 0|anubis 180 0|jade 240 0|lapis 300 0|scarab 400 0|sand 80 0|rose 140 0|copper 160 0|ivory 180 0|amethyst 200 0|" + // noloc
            "turquoise 220 0|crimson 260 0|frost 280 0|silver 320 0|ebony 450 0|" + // noloc
            "torch_scepter 100 1|torch_lantern 150 2|torch_bone 200 3|torch_plasma 250 4|torch_obsidian 300 5|torch_lamp 120 0|" + // noloc
            "torch_papyrus 150 0|torch_crook 180 0|torch_ankh 200 0|torch_was 220 0|torch_feather 240 0|torch_sistrum 260 0|" + // noloc
            "torch_cobra 300 0|torch_moon 340 0|torch_crystal 400 0|" + // noloc
            "hat_explorer 120 0|hat_nemes 100 1|hat_lotus 150 2|hat_helm 200 3|hat_cyber 250 4|hat_sun 300 5|hat_feather 120 0|" + // noloc
            "hat_turban 140 0|hat_scarab 180 0|hat_horns 220 0|hat_khepresh 260 0|hat_vulture 300 0|hat_atef 340 0|hat_nefertiti 380 0|" + // noloc
            "hat_pschent 450 0|" + // noloc
            "shoes_slippers 80 0|shoes_sandals 100 1|shoes_mud 150 2|shoes_stone 200 3|shoes_jet 250 4|shoes_hooves 300 5|" + // noloc
            "shoes_papyrus 90 0|shoes_desert 120 0|shoes_lotus 150 0|shoes_lapis 180 0|shoes_scarab 220 0|shoes_ruby 260 0|" + // noloc
            "shoes_croc 300 0|shoes_greaves 340 0|shoes_winged 400 0"; // noloc

        /// <summary>The catalogue's pieces sold all year.</summary>
        public static readonly IReadOnlyList<ScarabItem> Items = Parse();

        /// <summary>Halloween's scarab piece, sold only while the event is open.</summary>
        public static readonly ScarabItem HalloweenItem = new ScarabItem { Id = Halloween.ShopSkin, Price = 350 };

        static List<ScarabItem> Parse()
        {
            var list = new List<ScarabItem>();
            foreach (var entry in Table.Split('|'))
            {
                var f = entry.Split(' ');
                list.Add(new ScarabItem { Id = f[0], Price = int.Parse(f[1]), Theme = int.Parse(f[2]) });
            }
            return list;
        }

        /// <summary>The piece of that id (Halloween's included), null when it is not sold for scarabs.</summary>
        public static ScarabItem Get(string id)
        {
            foreach (var i in Items) if (i.Id == id) return i;
            return id == HalloweenItem.Id ? HalloweenItem : null;
        }

        public static int PriceOf(string id) => Get(id)?.Price ?? 0;
        public static int ThemeOf(string id) => Get(id)?.Theme ?? 0;

        /// <summary>Halloween's piece is only sold during the event; the others always.</summary>
        public static bool OnSale(ScarabItem item, DateTime utc) => item != HalloweenItem || Halloween.IsOpen(utc);

        static readonly List<ScarabItem> Commons = Pool(false), Rares = Pool(true);

        static List<ScarabItem> Pool(bool acts)
        {
            var list = new List<ScarabItem>();
            foreach (var i in Items) if ((i.Theme > 0) == acts) list.Add(i);
            return list;
        }

        /// <summary>This week's scarab pieces at -25 %: the classics then the act pieces.</summary>
        public static List<ScarabItem> Sale(DateTime utc)
        {
            int day = ShopSale.Day(utc);
            var list = ShopSale.Deal(Commons, ShopSale.Commons, day, ShopSale.CommonSalt);
            list.AddRange(ShopSale.Deal(Rares, ShopSale.Rares, day, ShopSale.RareSalt));
            return list;
        }

        /// <summary>What a piece costs now: -25 % while on sale (or in the grace minutes after it).</summary>
        public static int PriceNow(ScarabItem item, DateTime utc) =>
            Sale(utc).Contains(item) || Sale(utc - ShopSale.Grace).Contains(item) ? ShopSale.Price(item.Price) : item.Price;

        /// <summary>An act's collection: its pieces (a mummy, a torch, a hat, shoes).</summary>
        public static List<ScarabItem> ThemeSet(int act)
        {
            var list = new List<ScarabItem>();
            foreach (var i in Items) if (i.Theme == act && act > 0) list.Add(i);
            return list;
        }

        /// <summary>A whole collection costs a fifth less than its pieces bought one by one.</summary>
        public static int SetPrice(int piecesPrice) => piecesPrice * 4 / 5 / 10 * 10;

        /// <summary>What the act's collection costs <paramref name="owned"/>: its missing pieces, 20 % off (0: complete).</summary>
        public static int SetPrice(int act, ICollection<string> owned)
        {
            int missing = 0;
            foreach (var i in ThemeSet(act)) if (!owned.Contains(i.Id)) missing += i.Price;
            return SetPrice(missing);
        }
    }
}
