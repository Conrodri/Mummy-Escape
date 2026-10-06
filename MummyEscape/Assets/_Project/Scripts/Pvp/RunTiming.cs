// Mummy Rush PvP — le temps qu'une action bloque la suivante : personne ne peut aller plus vite que le jeu n'anime.
using System;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// Pendant qu'une action est animée, le jeu met le swipe suivant en attente (GameController.Play) : l'action suivante
    /// part au plus tôt quand l'animation finit. Ces durées sont celles du jeu (PlayerView les utilise), elles donnent
    /// l'écart minimal entre deux actions enregistrées et le temps minimal d'un tombeau.
    /// </summary>
    public static class RunTiming
    {
        public const int WalkMs = 130;          // une case
        public const int SlideMs = 90;          // chaque case d'un courant
        public const int DisarmMs = 150;
        public const int HurtMs = 360;          // 3 clignotements rouges (pics, flammes)
        public const int TombTurnMs = 450;      // la dalle tournante : le tombeau pivote
        public const int VanishMs = 250;        // un portail
        public const int FallMs = 350;          // un trou vers l'étage du dessous
        public const int ClimbMs = 200;         // une échelle
        public const int AppearMs = 250;        // l'arrivée après un portail, un trou ou une échelle

        /// <summary>Marge pour les arrondis du chronomètre (millisecondes entières, flottants).</summary>
        public const int SlackMs = 5;

        /// <summary>Durée minimale avant l'action suivante, après <paramref name="action"/> jouée depuis <paramref name="before"/>.</summary>
        public static int MinGapMs(Level level, RuleState before, PlayerAction action, StepResult r)
        {
            if (r.Has(StepFlags.Blocked)) return 0; // un mur : le jeu ne l'enregistre pas
            if (action.Kind == ActionKind.Disarm) return DisarmMs;
            int ms = WalkMs;
            if (r.Has(StepFlags.Swept))
            {
                // Emporté case après case, comme le jeu l'anime.
                var c = before.Position.Step(r.Dir);
                for (int guard = 0; guard < 64 && !c.Equals(r.SteppedOn) && level[c].Type == TileType.Current; guard++)
                {
                    c = c.Step((Dir)level[c].Param);
                    ms += SlideMs;
                }
            }
            if (r.Has(StepFlags.Damaged)) ms += HurtMs;
            if (r.Has(StepFlags.Rotated)) ms += TombTurnMs;
            if (r.Has(StepFlags.Teleported)) ms += VanishMs + AppearMs;
            else if (r.Has(StepFlags.Fell)) ms += FallMs + AppearMs;
            else if (r.Has(StepFlags.Climbed)) ms += ClimbMs + AppearMs;
            return ms;
        }

        /// <summary>
        /// Le temps le plus court possible sur ce tombeau, pour un joueur qui connaîtrait le chemin le plus rapide et
        /// swiperait dès que le jeu le laisse faire (le premier swipe à la chute du brouillard). Null si introuvable.
        /// </summary>
        public static int? MinFinishMs(Level level)
        {
            var sol = Solver.SolveFastest(level, (s, a, r) => MinGapMs(level, s, a, r), SolverOptions.Default);
            return sol?.Cost;
        }
    }

    /// <summary>
    /// Suit une course action par action : l'instant le plus tôt où chacune a pu partir, compte tenu de toutes les
    /// animations d'avant. Un horodatage au 1/50 s dit seulement que l'action est partie dans ces 20 ms ; la marge ne
    /// sert qu'une fois, elle ne s'additionne pas d'une action à l'autre (accélérer chaque pas un peu se voit).
    /// </summary>
    public sealed class RunClock
    {
        const int TickMs = 1000 / PvpConfig.TickRate;
        double _earliestMs; // la prochaine action ne peut pas partir avant (0 : la première, dès la chute du brouillard)

        /// <summary>Le plus petit horodatage possible pour la prochaine action.</summary>
        public int MinTick => _earliestMs <= 0 ? 0 : Math.Max(0, (int)Math.Floor((_earliestMs - RunTiming.SlackMs - TickMs) / TickMs) + 1);

        /// <summary>Fin de l'animation de la dernière action : la suivante ne part pas avant (en millisecondes de course).</summary>
        public double EarliestMs => _earliestMs;

        /// <summary>Aucune action avant cet instant (relais 2v2 : le coéquipier vient de libérer la momie).</summary>
        public void NotBefore(double ms) => _earliestMs = Math.Max(_earliestMs, ms);

        /// <summary>Enregistre une action jouée à <paramref name="tick"/> ; faux si elle est partie trop tôt.</summary>
        public bool Accept(int tick, int gapMs)
        {
            bool ok = tick >= MinTick;
            double startedAt = Math.Max(tick * (double)TickMs, _earliestMs); // au plus tôt : l'horodatage, ou la fin de l'animation
            _earliestMs = startedAt + gapMs;
            return ok;
        }
    }
}
