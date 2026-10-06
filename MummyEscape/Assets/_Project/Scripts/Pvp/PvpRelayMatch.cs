// Mummy Escape PvP — un match 2v2 en relais : les deux duos, leurs coureurs, leurs relais, et qui l'emporte.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    /// <summary>Un coureur d'un duo : qui il est et comment il est habillé (écran VS, sa momie quand on le regarde courir).</summary>
    [Serializable]
    public class RelayRunner
    {
        public string PlayerId;
        public string Name;
        public PlayerLook Look;
        public bool Bot;
    }

    /// <summary>Un camp du match : le duo, ses deux coureurs, celui qui part (labyrinthe 0) et ses actions une fois couru.</summary>
    [Serializable]
    public class RelaySide
    {
        public string DuoId;
        public string Name;
        public int Elo;
        public List<RelayRunner> Runners = new List<RelayRunner>();
        public bool Bot;
        /// <summary>Le coureur du labyrinthe 0 (désigné par le vote), null avant le vote.</summary>
        public string Starter;
        /// <summary>Actions du relais (envoyées par le duo à la fin ; écrites d'avance pour un duo de bots).</summary>
        public List<RelayInput> Inputs;
        /// <summary>Joueurs du duo qui ont quitté la partie (selon leur coéquipier ou eux-mêmes).</summary>
        public List<string> Quitters = new List<string>();
        /// <summary>Ce que le serveur a rejoué : étapes faites, issue, temps.</summary>
        public RelaySummary Verified;

        public bool Has(string playerId) => Runners.Exists(r => r.PlayerId == playerId);
        public RelayRunner Runner(string playerId) => Runners.Find(r => r.PlayerId == playerId);
        /// <summary>Le coureur de ce labyrinthe une fois le premier désigné.</summary>
        public RelayRunner RunnerOf(int maze)
        {
            if (Runners.Count < 2) return Runners.Count > 0 ? Runners[0] : null;
            int first = Runners[1].PlayerId == Starter ? 1 : 0;
            return Runners[maze == 0 ? first : 1 - first];
        }
    }

    /// <summary>Le bilan d'un relais : ce qui départage les deux duos.</summary>
    [Serializable]
    public class RelaySummary
    {
        public bool Finished;
        /// <summary>Une momie est morte, un coureur a quitté, ou le relais a été trafiqué.</summary>
        public bool Lost;
        public int TimeMs;
        public int Segments;

        public static RelaySummary Of(RelayRace race, bool quit = false) => race == null
            ? new RelaySummary { Lost = true }
            : new RelaySummary
            {
                Finished = race.Status == RelayStatus.Finished,
                Lost = race.Status == RelayStatus.Lost && race.Defeat != RelayDefeat.TimedOut || quit,
                TimeMs = race.TimeMs,
                Segments = race.Segment,
            };
    }

    /// <summary>Un match 2v2 tel que le serveur le garde.</summary>
    [Serializable]
    public class RelayMatch
    {
        public string Id;
        public int Seed;
        public int GeneratorVersion;
        public RelaySide A, B;
        public long CreatedAtUnixMs;
        /// <summary>Premier relais reçu : l'autre camp a encore un moment pour envoyer le sien.</summary>
        public long FirstSubmitUnixMs;
        public bool Settled;
        /// <summary>Résultat pour le camp A.</summary>
        public DuelResult Result;
        public int EloDeltaA, EloDeltaB;

        public bool VsBots => A?.Bot == true || B?.Bot == true;
        public RelaySide SideOf(string playerId) => A != null && A.Has(playerId) ? A : B != null && B.Has(playerId) ? B : null;
        public RelaySide OtherSide(string playerId) => SideOf(playerId) == A ? B : A;
    }

    /// <summary>Le premier duo arrivé gagne ; une momie morte ou un coureur parti fait perdre le sien.</summary>
    public static class RelayJudge
    {
        public static DuelResult Resolve(RelaySummary a, RelaySummary b)
        {
            a = a ?? new RelaySummary { Lost = true };
            b = b ?? new RelaySummary { Lost = true };
            if (a.Finished && b.Finished)
            {
                if (Math.Abs(a.TimeMs - b.TimeMs) < PvpConfig.DrawTimeThresholdMs) return DuelResult.Draw;
                return a.TimeMs < b.TimeMs ? DuelResult.Win : DuelResult.Loss;
            }
            if (a.Finished != b.Finished) return a.Finished ? DuelResult.Win : DuelResult.Loss;
            if (a.Lost != b.Lost) return a.Lost ? DuelResult.Loss : DuelResult.Win;
            if (a.Segments != b.Segments) return a.Segments > b.Segments ? DuelResult.Win : DuelResult.Loss;
            return DuelResult.Draw;
        }

    }

    // ------------------------------------------------------------------ server messages

    [Serializable]
    public class RelayMatchResponse
    {
        public RelayMatch Match;
        public string Error;   // "UNKNOWN", "OUTDATED", "LOCKED", "NETWORK"
    }

    [Serializable]
    public class RelayResultResponse
    {
        /// <summary>Le serveur attend encore le relais de l'autre duo (quelques secondes au plus).</summary>
        public bool Pending;
        /// <summary>Résultat pour le duo du joueur.</summary>
        public DuelResult Result;
        public int EloDelta;
        public int NewElo;
        public RelayMatch Match;
        public string Error;
    }

    public static class RelayServerConfig
    {
        /// <summary>Après le premier relais reçu, l'autre duo a ce temps pour envoyer le sien ; ensuite il compte comme parti.</summary>
        public const int SubmitWindowMs = 60_000;
        /// <summary>Un match jamais envoyé est oublié après ce délai.</summary>
        public const int MatchLifetimeMs = 30 * 60_000;
        /// <summary>Signalements « a quitté » par joueur et par jour.</summary>
        public const int MaxQuitReportsPerDay = 10;
    }

    /// <summary>Signalements d'un joueur qui quitte les matchs 2v2 (le duo perd à cause de lui).</summary>
    [Serializable]
    public class QuitDossier
    {
        public string PlayerId;
        public List<string> Reporters = new List<string>();
        public int TotalReports;
        public long LastReportUnixMs;
    }
}
