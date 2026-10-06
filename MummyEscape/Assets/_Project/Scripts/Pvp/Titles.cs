// Mummy Rush — titres affichés sous le nom du joueur (profil, écran VS, duel, replays).
// C# pur, partagé avec le serveur : il vérifie les titres de duel avant de les montrer aux adversaires.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public enum TitleKind
    {
        /// <summary>Étoiles gagnées dans un acte en solo.</summary>
        Solo = 0,
        /// <summary>Victoires en duel, depuis toujours.</summary>
        Wins = 1,
        /// <summary>Meilleure ligue atteinte en duel.</summary>
        League = 2,
        /// <summary>Tombeaux différents terminés en moins de <see cref="Titles.SpeedLimitMs"/> en solo.</summary>
        Speed = 3,
        /// <summary>L'équipe du jeu (<see cref="Developers"/>) : légendaire, animé.</summary>
        Developer = 4,
    }

    public sealed class TitleDef
    {
        public string Id;
        /// <summary>Nom en français (traduit par le jeu).</summary>
        public string Name;
        public TitleKind Kind;
        /// <summary>Étoiles (Solo), victoires (Wins) ou tombeaux (Speed) à atteindre.</summary>
        public int Goal;
        /// <summary>Acte concerné (Solo).</summary>
        public int Act;
        /// <summary>Ligue à atteindre (League).</summary>
        public League League;

        public bool IsDuel => Kind == TitleKind.Wins || Kind == TitleKind.League;
        /// <summary>Légendaire : affiché animé, jamais gagné en jouant.</summary>
        public bool Legendary => Kind == TitleKind.Developer;
    }

    public static class Titles
    {
        /// <summary>Un acte compte 10 niveaux de 3 étoiles : 30, c'est l'acte parfait.</summary>
        public const int SoloStarsPerAct = 30;
        public const int SpeedLimitMs = 5_000;

        public static readonly IReadOnlyList<TitleDef> All = new[]
        {
            Solo("title_act1", "Gardien de l'Antichambre", 1),
            Solo("title_act2", "Plongeur des Galeries", 2),
            Solo("title_act3", "Bâtisseur des Ruines", 3),
            Solo("title_act4", "Juge de la Cité d'Anubis", 4),
            Solo("title_act5", "Maître du Sanctuaire", 5),
            new TitleDef { Id = "title_wins50", Name = "Gladiateur", Kind = TitleKind.Wins, Goal = 50 },
            new TitleDef { Id = "title_wins100", Name = "Champion de l'arène", Kind = TitleKind.Wins, Goal = 100 },
            new TitleDef { Id = "title_argent", Name = "Duelliste d'argent", Kind = TitleKind.League, League = League.Argent },
            new TitleDef { Id = "title_platine", Name = "Seigneur de platine", Kind = TitleKind.League, League = League.Platine },
            new TitleDef { Id = "title_diamant", Name = "Légende de diamant", Kind = TitleKind.League, League = League.Diamant },
            new TitleDef { Id = "title_speed5", Name = "Pieds légers", Kind = TitleKind.Speed, Goal = 5 },
            new TitleDef { Id = "title_speed15", Name = "Éclair du désert", Kind = TitleKind.Speed, Goal = 15 },
            new TitleDef { Id = "title_speed30", Name = "Souffle de Shou", Kind = TitleKind.Speed, Goal = 30 },
            new TitleDef { Id = Developers.TitleId, Name = "Développeur de Mummy Rush", Kind = TitleKind.Developer, Goal = 1 },
        };

        static TitleDef Solo(string id, string name, int act) =>
            new TitleDef { Id = id, Name = name, Kind = TitleKind.Solo, Act = act, Goal = SoloStarsPerAct };

        public static TitleDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }

        /// <summary>Progression vers un titre : (valeur actuelle, but). Le titre est acquis quand valeur ≥ but.</summary>
        public static (int value, int goal) Progress(TitleDef t, Func<int, int> starsInAct, int fastTombs, int wins, League highest)
        {
            switch (t.Kind)
            {
                case TitleKind.Solo: return (Math.Min(starsInAct(t.Act), t.Goal), t.Goal);
                case TitleKind.Wins: return (Math.Min(wins, t.Goal), t.Goal);
                case TitleKind.League: return ((int)highest >= (int)t.League ? 1 : 0, 1);
                case TitleKind.Developer: return (0, 1); // given by the player id, see Developers
                default: return (Math.Min(fastTombs, t.Goal), t.Goal);
            }
        }

        /// <summary>
        /// Le titre qu'un joueur peut montrer aux autres : les titres de duel sont comparés à ses données protégées, les
        /// titres solo (calculés sur l'appareil) passent tels quels. Null si le titre est inconnu ou pas mérité.
        /// </summary>
        public static string Check(string id, PlayerPvpData d)
        {
            var t = Get(id);
            if (t == null) return null;
            if (t.Kind == TitleKind.Wins && (d == null || d.Wins < t.Goal)) return null;
            if (t.Kind == TitleKind.League && (d == null || d.HighestLeague < t.League)) return null;
            return t.Id;
        }
    }
}
