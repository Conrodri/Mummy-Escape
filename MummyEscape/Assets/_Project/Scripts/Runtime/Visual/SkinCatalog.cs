using System.Collections.Generic;
using UnityEngine;

namespace MummyEscape.Visual
{
    public sealed class SkinDef
    {
        public string Id;
        public string Name;
        public int Price;
        public Color32 Bandage;
        public Color32 Shadow;
        public Color32 Eyes;
        /// <summary>Tint of the mummy's torch light.</summary>
        public Color Torch;
    }

    /// <summary>Shop catalogue. Prices are in scarabs, earned by collecting new stars.</summary>
    public static class SkinCatalog
    {
        public const string DefaultSkinId = "classic";

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
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
        };

        public static SkinDef Get(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return All[0];
        }
    }
}
