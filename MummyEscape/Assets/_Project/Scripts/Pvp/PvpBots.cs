// Mummy Escape PvP — adversaires simulés pour les duels hors ligne (et pour tester sans serveur).
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public static class PvpBots
    {
        static readonly string[] Names =
        {
            "Nefertari", "Khépri", "Imhotep", "Sobek", "Hatchepsout", "Ramsès", "Bastet", "Thot", "Meritaton", "Ptah",
            "Horus", "Seth", "Isis", "Osiris", "Neith", "Sekhmet", "Amon", "Hathor", "Khonsou", "Maât",
        };

        /// <summary>
        /// Un fantôme d'Elo proche, qui court sur un nouveau tombeau : il suit le chemin idéal, plus lentement et en
        /// hésitant d'autant plus que son Elo est bas (un bon joueur mémorise mieux et swipe plus vite).
        /// </summary>
        public static GhostRun Make(Random rng, int playerElo, long nowMs)
        {
            int elo = Math.Max(PvpConfig.MinElo, playerElo + rng.Next(-150, 151));
            int seed = rng.Next(1, int.MaxValue);
            var level = PvpArena.Generate(seed);
            var inputs = new List<RunInput>();
            // Rythme moyen par action : ~1,1 s à 700 Elo, ~0,45 s à 1700.
            double pace = Math.Max(380, Math.Min(1300, 1100 - (elo - 700) * 0.65));
            double t = 600 + rng.NextDouble() * 900; // le temps de se repérer quand le brouillard tombe
            foreach (var action in level.Solution.Actions)
            {
                t += pace * (0.6 + rng.NextDouble() * 0.8);
                // De temps en temps, une hésitation (un regard sur la carte, un doute à un carrefour).
                if (rng.NextDouble() < 0.12) t += pace * (1 + rng.NextDouble() * 3);
                int tick = Math.Max(RunActions.TickOf((int)t), inputs.Count == 0 ? 0 : inputs[inputs.Count - 1].Tick + PvpConfig.MinInputGapTicks);
                inputs.Add(new RunInput { Tick = tick, Direction = RunActions.Encode(action) });
            }
            var run = RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.Finished, Inputs = inputs })
                      ?? new RunSubmission { Outcome = RunOutcome.TimedOut, TimeMs = PvpConfig.TimeLimitMs, Inputs = inputs };
            return new GhostRun
            {
                GhostId = "bot_" + seed,
                PlayerId = "bot_" + rng.Next(),
                PlayerName = Names[rng.Next(Names.Length)],
                Elo = elo,
                Seed = seed,
                GeneratorVersion = DifficultyTable.GeneratorVersion,
                Outcome = run.Outcome,
                TimeMs = run.TimeMs,
                Progress = run.Progress,
                Inputs = inputs,
                CreatedAtUnixMs = nowMs,
            };
        }
    }
}
