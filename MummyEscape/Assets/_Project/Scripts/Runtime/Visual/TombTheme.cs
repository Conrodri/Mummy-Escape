using UnityEngine;

namespace MummyEscape.Visual
{
    /// <summary>Art direction of one act: palette, how floors and walls are drawn, lighting and ambient particles.</summary>
    public sealed class TombTheme
    {
        public enum Style { Sandstone, Flooded, Ruins, Tech, Inferno }

        public int Act;
        public Style Kind;
        public string Mood;

        // Palette (floor = slabs, wall = blocks, accent = carvings / inlays / neon / lava).
        public Color32 Floor, FloorDark, FloorLight;
        public Color32 Wall, WallDark, WallLight;
        public Color32 Accent, AccentDark;

        /// <summary>Cold ambient of the dark tomb (what the player remembers) and its strength.</summary>
        public Color Ambient;
        public float AmbientIntensity = 0.5f;
        /// <summary>Colour of the wall sconces (fire, neon...).</summary>
        public Color SconceLight;
        /// <summary>Particles drifting in the torch light.</summary>
        public Color Motes;
        public bool MotesGlow;
        public float MotesFall;

        public static TombTheme ForAct(int act) => All[Mathf.Clamp(act, 1, All.Length) - 1];

        static readonly TombTheme[] All =
        {
            new TombTheme
            {
                Act = 1, Kind = Style.Sandstone, Mood = "Un tombeau intact, scellé depuis trois mille ans.",
                Floor = new Color32(201, 166, 107, 255), FloorDark = new Color32(156, 122, 69, 255), FloorLight = new Color32(222, 192, 138, 255),
                Wall = new Color32(120, 92, 56, 255), WallDark = new Color32(82, 60, 34, 255), WallLight = new Color32(168, 132, 84, 255),
                Accent = new Color32(232, 195, 90, 255), AccentDark = new Color32(38, 64, 140, 255),
                Ambient = new Color(0.5f, 0.58f, 0.85f), AmbientIntensity = 0.5f, SconceLight = new Color(1f, 0.62f, 0.25f),
                Motes = new Color(1f, 0.9f, 0.7f, 0.5f), MotesFall = 0f,
            },
            new TombTheme
            {
                Act = 2, Kind = Style.Flooded, Mood = "Le Nil a envahi les galeries : les courants emportent tout.",
                Floor = new Color32(104, 120, 118, 255), FloorDark = new Color32(62, 76, 78, 255), FloorLight = new Color32(150, 176, 170, 255),
                Wall = new Color32(70, 86, 84, 255), WallDark = new Color32(38, 50, 52, 255), WallLight = new Color32(112, 134, 124, 255),
                Accent = new Color32(86, 150, 92, 255), AccentDark = new Color32(30, 70, 90, 255),
                Ambient = new Color(0.32f, 0.55f, 0.75f), AmbientIntensity = 0.55f, SconceLight = new Color(1f, 0.66f, 0.32f),
                Motes = new Color(0.7f, 0.9f, 1f, 0.45f), MotesFall = 0.25f,
            },
            new TombTheme
            {
                Act = 3, Kind = Style.Ruins, Mood = "Les voûtes s'effondrent : chaque dalle peut céder.",
                Floor = new Color32(150, 128, 104, 255), FloorDark = new Color32(98, 82, 66, 255), FloorLight = new Color32(186, 164, 136, 255),
                Wall = new Color32(104, 90, 78, 255), WallDark = new Color32(60, 50, 44, 255), WallLight = new Color32(146, 128, 110, 255),
                Accent = new Color32(120, 140, 70, 255), AccentDark = new Color32(40, 32, 28, 255),
                Ambient = new Color(0.55f, 0.52f, 0.62f), AmbientIntensity = 0.48f, SconceLight = new Color(1f, 0.58f, 0.25f),
                Motes = new Color(0.85f, 0.8f, 0.72f, 0.55f), MotesFall = 0.12f,
            },
            new TombTheme
            {
                Act = 4, Kind = Style.Tech, Mood = "Une cité d'Anubis venue d'un autre âge : lasers et portails.",
                Floor = new Color32(70, 76, 92, 255), FloorDark = new Color32(36, 40, 52, 255), FloorLight = new Color32(112, 122, 144, 255),
                Wall = new Color32(46, 50, 66, 255), WallDark = new Color32(20, 22, 32, 255), WallLight = new Color32(92, 100, 126, 255),
                Accent = new Color32(70, 230, 255, 255), AccentDark = new Color32(232, 195, 90, 255),
                Ambient = new Color(0.38f, 0.42f, 0.9f), AmbientIntensity = 0.5f, SconceLight = new Color(0.35f, 0.9f, 1f),
                Motes = new Color(0.5f, 0.95f, 1f, 0.6f), MotesGlow = true, MotesFall = -0.05f,
            },
            new TombTheme
            {
                Act = 5, Kind = Style.Inferno, Mood = "Le sanctuaire brûle : les flammes jaillissent en rythme.",
                Floor = new Color32(70, 52, 50, 255), FloorDark = new Color32(36, 24, 24, 255), FloorLight = new Color32(112, 84, 72, 255),
                Wall = new Color32(48, 34, 34, 255), WallDark = new Color32(22, 14, 14, 255), WallLight = new Color32(96, 64, 56, 255),
                Accent = new Color32(255, 120, 30, 255), AccentDark = new Color32(232, 195, 90, 255),
                Ambient = new Color(0.75f, 0.35f, 0.3f), AmbientIntensity = 0.5f, SconceLight = new Color(1f, 0.5f, 0.15f),
                Motes = new Color(1f, 0.55f, 0.15f, 0.8f), MotesGlow = true, MotesFall = -0.35f,
            },
        };
    }
}
