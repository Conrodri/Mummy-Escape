using System;
using System.Collections.Generic;

namespace MummyEscape.Visual
{
    /// <summary>
    /// The set a piece belongs to: its act's collection, a Treasure parure, Halloween's, the tower's, the team's or its
    /// pass season's. The shop's collections list them; the zoom puts the whole set on the mummy.
    /// </summary>
    public static class SkinSets
    {
        public static readonly IReadOnlyList<SkinDef> Halloween = new[] { SkinCatalog.Get(Core.Halloween.ShopSkin), SkinCatalog.Get(Core.Halloween.RewardSkin) };

        public static readonly IReadOnlyList<SkinDef> Team = Pieces(Pvp.Developers.Skins);

        public static List<SkinDef> Pieces(IEnumerable<string> ids)
        {
            var list = new List<SkinDef>();
            foreach (var id in ids) list.Add(SkinCatalog.Get(id));
            return list;
        }

        /// <summary>The set of <paramref name="item"/>, null when it belongs to none.</summary>
        public static IReadOnlyList<SkinDef> Of(SkinDef item)
        {
            if (item == null) return null;
            if (item.Theme > 0) return SkinCatalog.ThemeSet(item.Theme);
            foreach (var gold in Monetization.GoldShop.Sets)
                if (Array.IndexOf(gold.SkinIds, item.Id) >= 0) return Pieces(gold.SkinIds);
            foreach (var set in new[] { Halloween, TowerSkins.All, CoopSkins.All, Team })
                foreach (var s in set) if (s.Id == item.Id) return set;
            // A pass piece: every piece of its season ("pass_s3_…").
            if (item.Id.StartsWith("pass_s", StringComparison.Ordinal)) // noloc
            {
                string season = item.Id.Substring(0, item.Id.IndexOf('_', 5) + 1);
                var list = new List<SkinDef>();
                foreach (var s in Monetization.PremiumSkins.All) if (s.Id.StartsWith(season, StringComparison.Ordinal)) list.Add(s);
                return list;
            }
            return null;
        }

        /// <summary>
        /// <paramref name="look"/> wearing the whole set, the first piece of each slot (a season's legendary over its other
        /// colours), and <paramref name="item"/> itself over it all.
        /// </summary>
        public static Loadout Wear(Loadout look, IReadOnlyList<SkinDef> set, SkinDef item)
        {
            for (int i = set.Count - 1; i >= 0; i--) look = look.With(set[i]);
            return look.With(item);
        }
    }
}
