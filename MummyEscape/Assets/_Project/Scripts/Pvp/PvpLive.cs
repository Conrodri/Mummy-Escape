// Mummy Rush PvP — le duel en direct : deux joueurs de la même ligue, le même tombeau, partis au même moment. Le premier
// sorti gagne ; le premier mort perd. Chaque téléphone envoie ses actions horodatées à l'autre (le rival court à côté, comme
// un fantôme qui avance en même temps) ; à la fin, chacun envoie sa course au serveur, qui la rejoue et juge sur les temps
// qu'il a vérifiés, pas sur ce que les téléphones ont vu.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public static class LiveDuelConfig
    {
        /// <summary>Après la première course reçue, l'autre a ce temps pour envoyer la sienne ; sinon c'est un abandon.</summary>
        public const int SubmitWindowMs = 60_000;
        /// <summary>
        /// Une course qui arrive bien plus tard que son temps ne le permet a été réhorodatée (actions « rembobinées ») :
        /// refusée. Marge : connexion, écran VS, attente de l'adversaire, réseau.
        /// </summary>
        public const int LateSlackMs = 25_000;
        public const int VsScreenMs = 3_000;
        /// <summary>Après l'aperçu, chacun attend l'autre au plus ce temps avant de partir.</summary>
        public const int ReadyWaitMs = 6_000;
        public const int MaxNameLength = 24;
    }

    [Serializable]
    public class LiveDuelist
    {
        public string PlayerId;
        public string Name;
        public int Elo;
        public PlayerLook Look;
        public bool Bot;
        /// <summary>La course vérifiée par le serveur, null tant qu'elle n'est pas arrivée.</summary>
        public RunSubmission Run;
        public int EloAfter;
        public int SealsGained;
    }

    /// <summary>Un duel en direct, gardé par le serveur (clé = le salon où les deux joueurs se sont trouvés).</summary>
    [Serializable]
    public class LiveDuel
    {
        public string Id;
        public int Seed;
        public int GeneratorVersion;
        public LiveDuelist A, B;
        /// <summary>Contre un bot (personne de la ligue en une minute) : sa course, jouée d'avance et rejouée comme un rival.</summary>
        public GhostRun BotRun;
        public long CreatedAtUnixMs;
        public long FirstSubmitUnixMs;
        public bool Settled;
        /// <summary>Du point de vue de A.</summary>
        public DuelResult Result;

        public LiveDuelist SideOf(string playerId) => A?.PlayerId == playerId ? A : B?.PlayerId == playerId ? B : null;
        public LiveDuelist OtherSide(string playerId) => A?.PlayerId == playerId ? B : B?.PlayerId == playerId ? A : null;
        public bool VsBot => BotRun != null;
    }

    [Serializable]
    public class LiveDuelResponse
    {
        public LiveDuel Match;
        public string Error;   // "OUTDATED", "LOCKED", "DIVISION", "UNKNOWN"
    }

    public static class LiveDuelJudge
    {
        /// <summary>
        /// Résultat du point de vue de A. Le premier sorti gagne (deux sorties : la plus rapide, nul sous 0,2 s) ; une sortie
        /// bat tout le reste. Sinon le premier mort perd (deux morts : la plus tardive gagne) ; un vivant bat un mort. Deux
        /// vivants au bout des 3 minutes : le plus avancé. L'abandon (ou rien d'envoyé) perd toujours.
        /// </summary>
        public static DuelResult Resolve(RunSubmission a, RunSubmission b)
        {
            bool quitA = a == null || a.Outcome == RunOutcome.Abandoned;
            bool quitB = b == null || b.Outcome == RunOutcome.Abandoned;
            if (quitA || quitB) return quitA && quitB ? DuelResult.Draw : quitA ? DuelResult.Loss : DuelResult.Win;

            bool outA = a.Outcome == RunOutcome.Finished, outB = b.Outcome == RunOutcome.Finished;
            if (outA && outB) return Earlier(a.TimeMs, b.TimeMs, DuelResult.Win);   // the faster exit wins
            if (outA != outB) return outA ? DuelResult.Win : DuelResult.Loss;

            bool deadA = a.Outcome == RunOutcome.Died, deadB = b.Outcome == RunOutcome.Died;
            if (deadA && deadB) return Earlier(a.TimeMs, b.TimeMs, DuelResult.Loss);  // the first death loses
            if (deadA != deadB) return deadA ? DuelResult.Loss : DuelResult.Win;

            if (Math.Abs(a.Progress - b.Progress) < PvpConfig.DrawProgressThreshold) return DuelResult.Draw;
            return a.Progress > b.Progress ? DuelResult.Win : DuelResult.Loss;
        }

        /// <summary>A's result when A's event came first: <paramref name="ifAFirst"/> (the opposite if B's did, a draw under 0.2 s).</summary>
        static DuelResult Earlier(int timeA, int timeB, DuelResult ifAFirst)
        {
            if (Math.Abs(timeA - timeB) < PvpConfig.DrawTimeThresholdMs) return DuelResult.Draw;
            if (timeA < timeB) return ifAFirst;
            return ifAFirst == DuelResult.Win ? DuelResult.Loss : DuelResult.Win;
        }
    }
}
