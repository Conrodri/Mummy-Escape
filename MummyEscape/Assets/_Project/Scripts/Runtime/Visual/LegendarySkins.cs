using System.Collections.Generic;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>
    /// The casino's exclusive legendary colours (<see cref="Casino"/>): animated bandages, never sold. The scarab wheel's
    /// live in the save ("leg_*"), the seal wheel's on the server like every duel reward ("pvp_leg_*").
    /// </summary>
    public static class LegendarySkins
    {
        /// <summary>
        /// The body is first painted in these two marker colours, then <see cref="ArtLibrary"/> repaints every pixel of
        /// them with the effect of the frame (nothing else in the outfit uses them).
        /// </summary>
        public static readonly Color32 MarkBandage = new Color32(201, 77, 203, 255);
        public static readonly Color32 MarkShadow = new Color32(103, 39, 105, 255);

        // (Declared before All: static fields are set in file order, and All reads them.)
        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            // Scarab wheel.
            Def("leg_ra", "Arc-en-ciel de Râ", LegendaryFx.Rainbow, new Color32(255, 255, 255, 255), new Color(1f, 0.9f, 0.7f)),
            Def("leg_sekhmet", "Flammes de Sekhmet", LegendaryFx.Fire, new Color32(255, 250, 200, 255), new Color(1f, 0.55f, 0.2f)),
            Def("leg_nut", "Nébuleuse de Nout", LegendaryFx.Galaxy, new Color32(255, 210, 255, 255), new Color(0.75f, 0.5f, 1f)),
            Def("leg_aurora", "Aurore du Nil", LegendaryFx.Aurora, new Color32(255, 140, 220, 255), new Color(0.45f, 1f, 0.8f)),
            Def("leg_gold", "Or liquide d'Amon", LegendaryFx.Gold, new Color32(90, 220, 255, 255), new Color(1f, 0.85f, 0.4f)),
            // Seal wheel.
            Def("pvp_leg_seth", "Foudre de Seth", LegendaryFx.Storm, new Color32(255, 255, 140, 255), new Color(0.55f, 0.75f, 1f)),
            Def("pvp_leg_anubis", "Spectre d'Anubis", LegendaryFx.Spectre, new Color32(200, 255, 240, 255), new Color(0.4f, 1f, 0.85f)),
            Def("pvp_leg_horus", "Néon d'Horus", LegendaryFx.Neon, new Color32(255, 255, 255, 255), new Color(1f, 0.45f, 0.9f)),
            Def("pvp_leg_prism", "Prisme de diamant", LegendaryFx.Prism, new Color32(120, 200, 255, 255), new Color(0.85f, 0.95f, 1f)),
            Def("pvp_leg_apophis", "Lave d'Apophis", LegendaryFx.Magma, new Color32(255, 230, 80, 255), new Color(1f, 0.4f, 0.15f)),
        };

        static SkinDef Def(string id, string name, LegendaryFx fx, Color32 eyes, Color torch) => new SkinDef
        {
            Id = id, Name = name, Price = 0, Slot = CosmeticSlot.Color, Pvp = PvpSkins.IsPvp(id), Fx = fx,
            Bandage = MarkBandage, Shadow = MarkShadow, Eyes = eyes, Torch = torch,
        };

        public static SkinDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        /// <summary>The scarab wheel's legendaries the player owns (the seal wheel's come with the duel rewards).</summary>
        public static IEnumerable<SkinDef> OwnedScarabLegendaries(ICollection<string> owned)
        {
            foreach (var s in All) if (!s.Pvp && owned.Contains(s.Id)) yield return s;
        }

        /// <summary>A colour that sums up the effect (names, wheel wedges).</summary>
        public static Color Accent(LegendaryFx fx)
        {
            switch (fx)
            {
                case LegendaryFx.Rainbow: return new Color(1f, 0.45f, 0.75f);
                case LegendaryFx.Fire: return new Color(1f, 0.5f, 0.15f);
                case LegendaryFx.Galaxy: return new Color(0.7f, 0.45f, 1f);
                case LegendaryFx.Aurora: return new Color(0.35f, 1f, 0.7f);
                case LegendaryFx.Gold: return new Color(1f, 0.82f, 0.3f);
                case LegendaryFx.Storm: return new Color(0.45f, 0.75f, 1f);
                case LegendaryFx.Spectre: return new Color(0.4f, 1f, 0.9f);
                case LegendaryFx.Neon: return new Color(1f, 0.35f, 0.85f);
                case LegendaryFx.Prism: return new Color(0.8f, 0.92f, 1f);
                case LegendaryFx.Moon: return new Color(0.7f, 0.78f, 1f);
                case LegendaryFx.Lapis: return new Color(1f, 0.82f, 0.35f);
                case LegendaryFx.Nile: return new Color(0.3f, 0.9f, 0.85f);
                case LegendaryFx.Embalm: return new Color(1f, 0.78f, 0.3f);
                case LegendaryFx.Hathor: return new Color(0.35f, 0.95f, 0.85f);
                case LegendaryFx.Sobek: return new Color(0.55f, 0.95f, 0.35f);
                case LegendaryFx.Bastet: return new Color(1f, 0.65f, 0.2f);
                case LegendaryFx.Osiris: return new Color(0.45f, 1f, 0.5f);
                case LegendaryFx.Developer: return new Color(0.35f, 1f, 0.75f);
                default: return new Color(1f, 0.35f, 0.1f);
            }
        }
    }
}
