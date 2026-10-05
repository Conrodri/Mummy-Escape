// Mummy Escape — les temps de référence d'un tombeau solo : celui d'une momie experte (à battre) et le minimum parfait.
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public enum PaceVerdict
    {
        Normal,
        /// <summary>Presque le minimum parfait sur un grand labyrinthe : aucune erreur et chaque swipe au plus tôt. Possible, très suspect.</summary>
        Suspicious,
        /// <summary>Plus vite que les animations du jeu ne le permettent : un jeu modifié.</summary>
        Impossible,
    }

    public sealed class TombPace
    {
        /// <summary>Temps de la momie experte (<see cref="TargetSkill"/>) : le temps à battre. Null si elle n'est pas sortie.</summary>
        public int? TargetMs;
        /// <summary>Aucune erreur, le chemin le plus rapide, chaque swipe dès que le jeu le permet (<see cref="RunTiming"/>).</summary>
        public int? PerfectMs;

        /// <summary>
        /// Niveau de la momie experte (≈ 1400 Elo). Sur les tombeaux solo elle finit en ~1,5× le minimum parfait
        /// (tools/PvpSim solo) ; un joueur moyen en ~2 à 2,5×.
        /// </summary>
        public const double TargetSkill = 0.7;
        const int TargetRuns = 5;

        /// <summary>À moins de 15 % du minimum parfait…</summary>
        public const double SuspiciousRatio = 1.15;
        /// <summary>…sur un tombeau assez long pour qu'un sans-faute au rythme parfait ne soit pas de la chance.</summary>
        public const int SuspiciousMinPar = 25;

        /// <summary>Calcule les deux temps (quelques dizaines de ms à quelques centaines : hors du fil principal).</summary>
        public static TombPace Of(Level level)
        {
            return new TombPace { TargetMs = Target(level), PerfectMs = RunTiming.MinFinishMs(level) };
        }

        /// <summary>
        /// La médiane de quelques courses de la momie experte, toujours la même pour un tombeau donné (mêmes graines sur
        /// tous les appareils) : une course chanceuse ne fixe pas un temps impossible.
        /// </summary>
        static int? Target(Level level)
        {
            var times = new List<int>();
            for (int i = 0; i < TargetRuns; i++)
            {
                var rng = new Random(unchecked(level.Id.Act * 1_000_003 + level.Id.Index * 10_007 + level.Variant * 101 + i));
                var (inputs, outcome, _) = new HumanPlayer(TargetSkill, rng).Play(level, rng);
                if (outcome == RunOutcome.Finished && inputs.Count > 0) times.Add(RunActions.MsOf(inputs[inputs.Count - 1].Tick));
            }
            if (times.Count == 0) return null;
            times.Sort();
            return times[times.Count / 2];
        }

        /// <summary>Ce que vaut un temps sur ce tombeau (<paramref name="par"/> : son chemin idéal, en coups).</summary>
        public PaceVerdict Judge(int timeMs, int par)
        {
            if (!PerfectMs.HasValue) return PaceVerdict.Normal;
            int perfect = PerfectMs.Value;
            // Le chronomètre arrondit à la milliseconde, une image de jeu près : jamais plus de 25 ms sous le minimum.
            if (timeMs < perfect - 25) return PaceVerdict.Impossible;
            if (par >= SuspiciousMinPar && timeMs <= perfect * SuspiciousRatio) return PaceVerdict.Suspicious;
            return PaceVerdict.Normal;
        }
    }
}
