// Mummy Rush PvP — un joueur simulé qui joue comme une personne (fantômes hors ligne, simulateur tools/PvpSim).
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// Joue un tombeau comme un humain : enchaîne vite les cases d'un couloir et marque un temps aux virages et aux
    /// carrefours, se trompe de branche quand le souvenir de l'aperçu flanche (de plus en plus avec les minutes), rate
    /// parfois un swipe, est désorienté par un tombeau qui tourne ou des commandes inversées, puis s'arrête un instant
    /// quand il comprend son erreur. Le niveau (0..1) règle tout cela.
    /// Calé sur de vraies parties (tools/PvpSim analyze) : vers 1100 Elo, ~180 ms entre deux cases d'un même couloir,
    /// ~520 ms à un virage, et ~15 % de pas qui éloignent de la sortie.
    /// </summary>
    public sealed class HumanPlayer
    {
        public readonly double Skill;

        readonly double _memory;      // chance de prendre la bonne branche à un carrefour, juste après l'aperçu
        readonly double _decayPerMin; // le souvenir s'efface, par minute de course
        readonly double _misswipe;    // chance qu'un swipe parte du mauvais côté
        readonly double _straightMs;  // case suivante dans le même couloir
        readonly double _turnMs;      // changer de direction

        public sealed class RunStats
        {
            public int Lapses, Misswipes, Confusions, Bumps, Actions;
            public bool Quit;
        }

        public HumanPlayer(double skill, Random rng)
        {
            Skill = Math.Max(0, Math.Min(1, skill));
            double jitter = (rng.NextDouble() - 0.5) * 0.06;
            _memory = 0.40 + 0.52 * Skill + jitter;
            _decayPerMin = 0.10 - 0.07 * Skill;
            _misswipe = 0.12 - 0.10 * Skill;
            double pace = Math.Exp((rng.NextDouble() - 0.5) * 0.3); // certains sont plus vifs que d'autres
            _straightMs = (210 - 150 * Skill) * pace;
            _turnMs = (440 - 320 * Skill) * pace;
        }

        /// <summary>Niveau d'un joueur de cet Elo (700 → 0, 1700 → 1).</summary>
        public static double SkillForElo(int elo) => Math.Max(0, Math.Min(1, (elo - 700) / 1000.0));

        /// <summary>Joue le tombeau : les actions horodatées que le jeu enverrait, l'issue, et comment ça s'est passé.</summary>
        public (List<RunInput> inputs, RunOutcome outcome, RunStats stats) Play(Level level, Random rng, double quitChance = 0) =>
            Play(level, rng, Rules.Initial(level), 0, PvpConfig.TimeLimitMs, quitChance);

        /// <summary>
        /// Joue à partir de <paramref name="start"/>, libéré à <paramref name="startMs"/> : une étape du relais 2v2, dont
        /// l'objectif est la sortie de <paramref name="level"/>. Faux une fois <paramref name="timeLimitMs"/> passé.
        /// </summary>
        public (List<RunInput> inputs, RunOutcome outcome, RunStats stats) Play(Level level, Random rng, RuleState start, double startMs,
                                                                                 int timeLimitMs, double quitChance = 0)
        {
            var stats = new RunStats();
            var session = new GameSession(level, start);
            var inputs = new List<RunInput>();
            double t = startMs + 150 + rng.NextDouble() * 600 * (1.2 - Skill); // le premier couloir est encore en tête : on part vite
            List<PlayerAction> plan = null;
            int planAt = 0;
            var wrongTried = new HashSet<Cell>();
            Cell? previous = null;
            Dir? lastSwipe = null;
            int confusedSteps = 0;
            // Le début du chemin, tout frais de l'aperçu, est sûr : on le court sans hésiter.
            int openingActions = 8 + rng.Next(5);
            bool opening = true;
            bool quitter = rng.NextDouble() < quitChance;
            int quitAfter = rng.Next(5, 40);

            // Faux une fois le temps écoulé.
            double earliest = 0; // l'action précédente est encore animée : le swipe suivant l'attend (RunTiming)
            bool Apply(PlayerAction action, double thinkMs)
            {
                t = Math.Max(t + thinkMs, earliest);
                if (t > timeLimitMs) return false;
                var before = session.State;
                var r = session.Apply(action);
                if (action.Kind == ActionKind.Move) lastSwipe = action.Dir;
                // Un mur : le jeu ne l'enregistre pas, seul le temps passe.
                if (r.Has(StepFlags.Blocked)) { stats.Bumps++; t += 140; return true; }
                int tick = RunActions.TickOf((int)t);
                if (inputs.Count > 0) tick = Math.Max(tick, inputs[inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
                inputs.Add(new RunInput { Tick = tick, Direction = RunActions.Encode(action) });
                earliest = Math.Max(t, RunActions.MsOf(tick)) + RunTiming.MinGapMs(level, before, action, r); // le vrai instant, pas l'horodatage arrondi
                stats.Actions++;
                if (!before.Position.Equals(session.Position)) previous = before.Position;
                // Une surprise coûte un instant, pas plus : l'animation a déjà laissé le temps de comprendre.
                if (r.Has(StepFlags.Rotated)) { confusedSteps = 6; t += 300 + 600 * (1 - Skill); }
                if (r.Has(StepFlags.Reversed)) t += 200;
                if (r.Has(StepFlags.TrapTriggered) || r.Has(StepFlags.Teleported) || r.Has(StepFlags.Climbed) || r.Has(StepFlags.Fell))
                    t += rng.NextDouble() * 200;
                return true;
            }

            // Le temps avant ce swipe : un réflexe dans un couloir, une décision rapide à un virage. Un joueur préfère
            // deviner et se tromper que s'arrêter pour se souvenir : pas de ralenti dans le noir, à peine aux carrefours.
            double Think(Dir swipe, bool crossing)
            {
                bool straight = lastSwipe.HasValue && lastSwipe.Value == swipe;
                if (opening) return (straight ? _straightMs * 0.85 : _straightMs * 1.3) * Math.Exp((rng.NextDouble() - 0.5) * 0.5);
                double ms = (straight ? _straightMs : _turnMs) * Math.Exp((rng.NextDouble() - 0.5) * 0.7);
                if (crossing) ms += _turnMs * 0.2 * rng.NextDouble();
                if (rng.NextDouble() < 0.015) ms += _turnMs * (0.5 + rng.NextDouble()); // un rare vrai doute
                return ms;
            }

            Dir SwipeFor(Dir world) // le swipe qui avance dans cette direction du tombeau, maintenant
            {
                var s = world.Turn(session.State.Rotation);
                return session.State.Reversed > 0 ? s.Opposite() : s;
            }

            while (session.Status == SessionStatus.Playing)
            {
                if (quitter && stats.Actions >= quitAfter) { stats.Quit = true; return (inputs, RunOutcome.Abandoned, stats); }
                if (plan == null || planAt >= plan.Count)
                {
                    var sol = Solver.Solve(level, session.State, SolverOptions.Default);
                    if (sol == null || sol.Actions.Count == 0) break; // plus rien à faire : le temps s'écoule
                    plan = sol.Actions;
                    planAt = 0;
                }
                var next = plan[planAt];
                var pos = session.Position;

                if (next.Kind != ActionKind.Move)
                {
                    if (!Apply(next, _turnMs * Math.Exp((rng.NextDouble() - 0.5) * 0.7))) return (inputs, RunOutcome.TimedOut, stats);
                    planAt++;
                    continue;
                }

                var intended = session.State.WorldDir(next.Dir);
                var open = new List<Dir>();
                foreach (var d in DirExt.All)
                {
                    var n = pos.Step(d);
                    if (Rules.CanEnter(level, n, session.State) && (!previous.HasValue || !n.Equals(previous.Value))) open.Add(d);
                }
                bool crossing = open.Count >= 2;
                if (stats.Actions >= openingActions) opening = false; // la suite : place à la mémoire
                double recall = Math.Max(0.4, _memory - _decayPerMin * t / 60000.0);

                // Sur sa lancée (surtout dans le noir) : le virage passe, on file tout droit jusqu'au mur.
                if (!opening && lastSwipe.HasValue && next.Dir != lastSwipe.Value)
                {
                    var ahead = session.State.WorldDir(lastSwipe.Value);
                    double overshoot = (session.State.SeesNeighbours ? 0.06 : 0.30) * (1.2 - Skill);
                    if (Rules.CanEnter(level, pos.Step(ahead), session.State) && rng.NextDouble() < overshoot)
                    {
                        stats.Lapses++;
                        var swipe = lastSwipe.Value;
                        for (int k = 0; k < 8 && session.Status == SessionStatus.Playing; k++)
                        {
                            bool wall = !Rules.CanEnter(level, session.Position.Step(ahead), session.State);
                            if (!Apply(PlayerAction.Move(swipe), Think(swipe, false))) return (inputs, RunOutcome.TimedOut, stats);
                            if (wall) break; // le mur : on s'arrête là
                        }
                        plan = null;
                        continue;
                    }
                }

                // Un carrefour : le souvenir de l'aperçu peut indiquer le mauvais couloir.
                if (crossing && !opening)
                {
                    var wrong = open.FindAll(d => d != intended && !wrongTried.Contains(pos.Step(d)));
                    if (wrong.Count > 0 && rng.NextDouble() > recall)
                    {
                        var d = wrong[rng.Next(wrong.Count)];
                        wrongTried.Add(pos.Step(d));
                        stats.Lapses++;
                        // Souvent un pas pour voir, parfois tout un couloir avant de comprendre.
                        int depth = rng.NextDouble() < 0.5 ? 1 : 2 + rng.Next(7);
                        for (int k = 0; k < depth && session.Status == SessionStatus.Playing; k++)
                        {
                            if (!Rules.CanEnter(level, session.Position.Step(d), session.State)) break;
                            var swipe = SwipeFor(d);
                            if (!Apply(PlayerAction.Move(swipe), Think(swipe, k == 0))) return (inputs, RunOutcome.TimedOut, stats);
                        }
                        t += rng.NextDouble() * 200; // « pas par là »
                        plan = null;
                        continue;
                    }
                }

                // Tombeau tourné ou commandes inversées : la main swipe comme avant.
                var move = next.Dir;
                bool confused = (confusedSteps > 0 && rng.NextDouble() < 0.10 + 0.30 * (1 - Skill))
                                || (session.State.Reversed > 0 && rng.NextDouble() < 0.08 + 0.25 * (1 - Skill));
                if (confused)
                {
                    move = session.State.Reversed > 0 ? move.Opposite() : intended;
                    if (move != next.Dir) stats.Confusions++;
                }
                else if (!opening && rng.NextDouble() < _misswipe)
                {
                    move = move.Turn(rng.Next(2) == 0 ? 1 : -1);
                    stats.Misswipes++;
                }
                if (confusedSteps > 0) confusedSteps--;

                var stateBefore = session.State;
                if (!Apply(PlayerAction.Move(move), Think(move, crossing))) return (inputs, RunOutcome.TimedOut, stats);
                if (move != next.Dir)
                {
                    if (!session.State.Equals(stateBefore))
                    {
                        plan = null; // parti de travers : on repart d'ici
                        t += rng.NextDouble() * 250;
                    }
                    continue;
                }
                planAt++;
            }
            var outcome = session.Status == SessionStatus.Won ? RunOutcome.Finished
                        : session.Status == SessionStatus.Dead ? RunOutcome.Died
                        : RunOutcome.TimedOut;
            return (inputs, outcome, stats);
        }
    }
}
