// Mummy Rush — la campagne 2v2 : deux momies dans le même tombeau, en direct. Modèles partagés avec le serveur.
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public static class CoopConfig
    {
        /// <summary>Scarabées (ordinaires, gardés par l'appareil) du premier passage d'un tombeau, pour chaque joueur.</summary>
        public const int FirstClearScarabs = 30;
        /// <summary>Marge sur le temps minimal d'un passage (deux téléphones, deux horloges qui partent presque ensemble).</summary>
        public const int TimeSlackMs = 500;
    }

    /// <summary>Une action d'une momie, dans l'ordre où l'hôte de la partie l'a acceptée.</summary>
    [Serializable]
    public class CoopRunInput
    {
        /// <summary>0 ou 1 : le membre du duo (<see cref="Duo.Members"/>) qui joue cette momie.</summary>
        public int Mummy;
        /// <summary><see cref="RunActions.Encode"/>.</summary>
        public int Direction;
    }

    /// <summary>Le meilleur passage d'un tombeau par un duo.</summary>
    [Serializable]
    public class CoopRecord
    {
        public int Tomb;
        public int Stars;
        public int Moves;
        public int TimeMs;
    }

    /// <summary>Les étoiles 2v2 d'un joueur sur un tombeau (elles lui appartiennent, quel que soit le duo).</summary>
    [Serializable]
    public class CoopStar
    {
        public int Tomb;
        public int Stars;
    }

    [Serializable]
    public class CoopClearResponse
    {
        /// <summary>Étoiles de ce passage.</summary>
        public int Stars;
        /// <summary>Premier passage de ce tombeau pour ce joueur : la récompense est pour lui.</summary>
        public bool FirstClear;
        /// <summary>Étoiles 2v2 gagnées par ce joueur avec ce passage (au-delà de son meilleur).</summary>
        public int NewStars;
        /// <summary>Total des étoiles 2v2 du joueur.</summary>
        public int TotalStars;
        public Duo Duo;
        /// <summary>Le portefeuille après ce passage (les scarabées du premier passage, une fois tenus par le serveur).</summary>
        public Wallet Wallet;
        public string Error;   // "UNKNOWN", "LOCKED", "INVALID", "OUTDATED"
    }

    public static class CoopLogic
    {
        /// <summary>Total des étoiles 2v2 d'un joueur.</summary>
        public static int TotalStars(IEnumerable<CoopStar> stars)
        {
            int total = 0;
            if (stars != null) foreach (var s in stars) total += s.Stars;
            return total;
        }

        public static CoopRecord Record(Duo duo, int tomb) => duo?.Coop?.Find(r => r.Tomb == tomb);

        /// <summary>Le tombeau 1 est ouvert ; chacun des suivants une fois le précédent passé par le duo.</summary>
        public static bool IsOpen(Duo duo, int tomb) => tomb == 1 || (tomb > 1 && tomb <= CoopCampaign.Count && Record(duo, tomb - 1) != null);

        /// <summary>Tombeaux passés par le duo.</summary>
        public static int Cleared(Duo duo) => duo?.Coop?.Count ?? 0;

        /// <summary>
        /// Rejoue un passage avec les règles du jeu : null s'il ne sort pas les deux momies (action impossible, momie morte,
        /// arrêt avant la sortie), sinon la session finie. <paramref name="minMs"/> : le temps que les animations imposent au
        /// moins (la momie la plus occupée, chacune ne pouvant agir plus vite que <see cref="RunTiming"/>).
        /// </summary>
        public static CoopSession Replay(CoopTomb tomb, IList<CoopRunInput> inputs, out int minMs)
        {
            minMs = 0;
            if (tomb == null || inputs == null) return null;
            var session = new CoopSession(tomb);
            var busy = new int[2];
            foreach (var input in inputs)
            {
                if (session.Status != SessionStatus.Playing || input == null || input.Mummy < 0 || input.Mummy > 1) return null;
                if (!RunActions.TryDecode(input.Direction, out var action)) return null;
                var before = session.Mummy(input.Mummy).State;
                var r = session.Apply(input.Mummy, action);
                if (r.Has(StepFlags.Blocked)) return null;
                busy[input.Mummy] += RunTiming.MinGapMs(tomb.Level, before, action, r);
            }
            minMs = Math.Max(busy[0], busy[1]);
            return session.Status == SessionStatus.Won ? session : null;
        }
    }
}
