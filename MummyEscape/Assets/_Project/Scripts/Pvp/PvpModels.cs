// Mummy Escape PvP — modèles partagés entre le serveur (Cloud Code) et le client Unity.
// C# pur, sans dépendance Unity ni NuGet : le module serveur lie ces fichiers tels quels (server/PvpMatchmaking).
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// Une action jouée pendant une course : le jeu avance case par case, donc on enregistre chaque action acceptée
    /// (pas une direction par image) et l'instant où elle a été jouée.
    /// </summary>
    [Serializable]
    public class RunInput
    {
        /// <summary>Instant de l'action, en pas de 1/<see cref="PvpConfig.TickRate"/> s depuis la chute du brouillard.</summary>
        public int Tick;
        /// <summary>1 = haut, 2 = droite, 3 = bas, 4 = gauche (déplacement) ; 5 à 8 = désamorcer dans ces directions.</summary>
        public int Direction;
    }

    public enum RunOutcome
    {
        /// <summary>La momie est sortie de l'étage.</summary>
        Finished = 0,
        /// <summary>La momie est morte (piège) : défaite, sauf si l'adversaire meurt aussi.</summary>
        Died = 1,
        /// <summary>Limite de 3 minutes atteinte sans sortir.</summary>
        TimedOut = 2,
        /// <summary>Abandon, déconnexion ou course jamais envoyée.</summary>
        Abandoned = 3
    }

    /// <summary>Une course envoyée par le client.</summary>
    [Serializable]
    public class RunSubmission
    {
        public string MatchId;
        public RunOutcome Outcome;
        /// <summary>Temps de course en millisecondes (fin, mort ou limite).</summary>
        public int TimeMs;
        /// <summary>Progression de 0 à 1 : 1 - distance restante / distance de départ jusqu'à la sortie.</summary>
        public float Progress;
        public List<RunInput> Inputs = new List<RunInput>();
    }

    /// <summary>Une course stockée, rejouée comme fantôme par l'adversaire.</summary>
    [Serializable]
    public class GhostRun
    {
        public string GhostId;
        public string PlayerId;
        public string PlayerName;
        public int Elo;
        public int Seed;
        /// <summary>Version du générateur qui a tiré le tombeau : un fantôme ne sert qu'aux joueurs de la même version.</summary>
        public int GeneratorVersion;
        public RunOutcome Outcome;
        public int TimeMs;
        public float Progress;
        public List<RunInput> Inputs = new List<RunInput>();
        public long CreatedAtUnixMs;
    }

    /// <summary>Duel en cours pour un joueur : créé par FindDuel, consommé par SubmitRun.</summary>
    [Serializable]
    public class PendingDuel
    {
        public string MatchId;
        public int Seed;
        /// <summary>Null si aucun fantôme n'était disponible : la course du joueur devient un fantôme.</summary>
        public GhostRun Ghost;
        public long CreatedAtUnixMs;
    }

    /// <summary>Résumé de la saison précédente, conservé pour distribuer les skins de fin de mois.</summary>
    [Serializable]
    public class SeasonSummary
    {
        public string Season;          // "2026-10"
        public int FinalElo;
        public int Duels;
        public int DuelsLastWeek;
        public int CountedDuels;       // pour le skin de participation
        public bool Ranked;            // Elo publié dans le classement du mois
        public bool RewardsClaimed;
    }

    /// <summary>Données PvP d'un joueur, stockées côté serveur (lecture seule pour le client).</summary>
    [Serializable]
    public class PlayerPvpData
    {
        public int Elo = PvpConfig.StartingElo;
        public int TotalDuels;          // depuis la création du compte (placement)
        public int Wins;
        public int Losses;
        public int Draws;

        public string Season;           // "2026-10"
        public int SeasonDuels;
        public int SeasonDuelsLastWeek;
        public int SeasonCountedDuels;  // duels comptés pour le skin de participation
        public int BestEloThisSeason = PvpConfig.StartingElo;
        /// <summary>Le serveur a publié l'Elo du joueur dans le classement de cette saison (au moins un duel résolu).</summary>
        public bool Ranked;
        public SeasonSummary LastSeason;

        public string Day;              // "2026-10-04" (UTC)
        public int DuelsToday;
        public int CountedDuelsToday;
        public int WinsToday;
        public bool DailyChestGranted;
        public Dictionary<string, int> OpponentsToday = new Dictionary<string, int>();

        /// <summary>Sceaux de Maât : monnaie PvP, gagnée seulement en jouant.</summary>
        public int Seals;
        /// <summary>Meilleure ligue jamais atteinte (débloque des articles de la boutique PvP).</summary>
        public League HighestLeague = League.Bronze;
        /// <summary>Identifiants des skins PvP obtenus (classement, participation, boutique PvP).</summary>
        public List<string> UnlockedRewards = new List<string>();
    }

    /// <summary>Profil renvoyé au client.</summary>
    [Serializable]
    public class PvpProfileResponse
    {
        public PlayerPvpData Data;
        /// <summary>Rang mondial (1 = premier), ou 0 si le joueur n'est pas encore classé ce mois-ci.</summary>
        public int WorldRank;
        public string LeagueName;
    }

    /// <summary>Réponse de ClaimSeasonRewards.</summary>
    [Serializable]
    public class SeasonRewardsResponse
    {
        public string Season;
        public List<string> NewRewards = new List<string>();
        public string Error;   // "NOTHING_TO_CLAIM", "NOT_ELIGIBLE"
    }

    public enum DuelResult { Win = 0, Draw = 1, Loss = 2 }

    /// <summary>Réponse de FindDuel.</summary>
    [Serializable]
    public class FindDuelResponse
    {
        public string MatchId;
        public int Seed;
        /// <summary>Fantôme à affronter, ou null si le joueur court en premier.</summary>
        public GhostRun Ghost;
        public int MyElo;
        public string Error;   // "LOCKED" si l'acte 3 n'est pas atteint, "OUTDATED" si le jeu n'a pas la version du serveur
    }

    /// <summary>Réponse de SubmitRun.</summary>
    [Serializable]
    public class SubmitRunResponse
    {
        /// <summary>False si la course a été mise en attente comme fantôme (pas encore d'adversaire).</summary>
        public bool Resolved;
        public DuelResult Result;
        public int EloBefore;
        public int EloAfter;
        public League League;
        public int SealsGained;
        /// <summary>Solde de sceaux après le duel.</summary>
        public int Seals;
        public string Error;   // "INVALID_RUN", "NO_PENDING_DUEL"
    }

    /// <summary>Réponse de BuyWithSeals (boutique PvP).</summary>
    [Serializable]
    public class SealPurchaseResponse
    {
        public bool Ok;
        /// <summary>Solde après l'achat.</summary>
        public int Seals;
        public List<string> UnlockedRewards = new List<string>();
        public string Error;   // "UNKNOWN_ITEM", "OWNED", "LEAGUE", "SEALS"
    }

    /// <summary>Une ligne du classement mensuel.</summary>
    [Serializable]
    public sealed class PvpBoardRow
    {
        public int Rank;          // 1 = premier
        public string PlayerId;
        public string PlayerName;
        public int Elo;
        public bool IsMe;
    }

    /// <summary>Classement d'un mois, vérifié par le serveur.</summary>
    [Serializable]
    public sealed class PvpBoardPage
    {
        /// <summary>Mois du classement ("2026-10"), vide si inconnu.</summary>
        public string Season = "";
        public List<PvpBoardRow> Rows = new List<PvpBoardRow>();
        /// <summary>L'entrée du joueur, null s'il n'est pas classé.</summary>
        public PvpBoardRow Me;
        /// <summary>Instant de la vérification (cache du serveur).</summary>
        public long BuiltAtUnixMs;
    }

    /// <summary>Une entrée du service de classement, telle quelle : le score peut venir d'un tricheur.</summary>
    public sealed class BoardEntry
    {
        public string PlayerId;
        public string PlayerName;
        public int Score;
        public int Rank;          // 1 = premier
    }
}
