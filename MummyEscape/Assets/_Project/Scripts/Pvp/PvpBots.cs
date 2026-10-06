// Mummy Rush PvP — adversaires simulés pour les duels hors ligne (et pour tester sans serveur).
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
        /// Un fantôme d'Elo proche, qui court sur un nouveau tombeau en jouant comme une personne (<see cref="HumanPlayer"/>) :
        /// rapide dans les couloirs, plus lent aux virages, avec des erreurs de mémoire et des swipes ratés, d'autant moins
        /// que son Elo est haut.
        /// </summary>
        public static GhostRun Make(Random rng, int playerElo, long nowMs) =>
            Make(rng, playerElo, rng.Next(1, int.MaxValue), "bot_" + rng.Next(), nowMs);

        /// <summary>Le nom affiché d'un joueur simulé, tiré de son identifiant (toujours le même).</summary>
        public static string NameOf(string botId)
        {
            int h = 17;
            foreach (char c in botId ?? "") h = unchecked(h * 31 + c);
            return Names[(h & int.MaxValue) % Names.Length];
        }

        /// <summary>La course d'un joueur simulé donné, sur un tombeau donné (manches des combats d'équipe hors ligne).</summary>
        public static GhostRun Make(Random rng, int playerElo, int seed, string botId, long nowMs)
        {
            // Toujours de la ligue du joueur.
            int elo = Leagues.ClampTo(Math.Max(PvpConfig.MinElo, playerElo + rng.Next(-150, 151)), Leagues.FromElo(playerElo));
            var level = PvpArena.Generate(seed);
            var (inputs, outcome, _) = new HumanPlayer(HumanPlayer.SkillForElo(elo), rng).Play(level, rng);
            // Un tombeau très court couru par un très bon bot : pas plus vite que ce que le serveur croit possible.
            int minTick = RunActions.TickOf(PvpConfig.MinPlausibleTimeMs + 200);
            int lastTick = inputs.Count > 0 ? inputs[inputs.Count - 1].Tick : 0;
            if (outcome == RunOutcome.Finished && lastTick > 0 && lastTick < minTick)
                foreach (var input in inputs) input.Tick = (int)((long)input.Tick * minTick / lastTick);
            var run = RunReplay.Verify(level, new RunSubmission { Outcome = outcome, Inputs = inputs })
                      ?? new RunSubmission { Outcome = RunOutcome.TimedOut, TimeMs = PvpConfig.TimeLimitMs, Inputs = inputs };
            return new GhostRun
            {
                GhostId = "bot_" + seed,
                PlayerId = botId,
                PlayerName = NameOf(botId),
                Elo = elo,
                Seed = seed,
                GeneratorVersion = DifficultyTable.GeneratorVersion,
                Outcome = run.Outcome,
                TimeMs = run.TimeMs,
                Progress = run.Progress,
                Inputs = inputs,
                CreatedAtUnixMs = nowMs,
                Bot = true,
            };
        }
    }
}
