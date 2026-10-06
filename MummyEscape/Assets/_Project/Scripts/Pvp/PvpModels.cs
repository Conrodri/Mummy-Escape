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
        /// <summary>Tenue de la momie (cosmétique, montrée à l'adversaire : écran VS, fantôme, replay).</summary>
        public PlayerLook Look;
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
        public PlayerLook Look;
        /// <summary>Joueur simulé (personne en vue dans la division après une minute) : seul le vrai joueur est mis à jour.</summary>
        public bool Bot;
    }

    /// <summary>Duel en cours pour un joueur : créé par FindDuel, consommé par SubmitRun.</summary>
    [Serializable]
    public class PendingDuel
    {
        public string MatchId;
        public int Seed;
        /// <summary>Combat d'équipe dont c'est une manche (2v2 ou guerre), null pour un duel.</summary>
        public string BattleId;
        public int Slot;
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
        public int Wins, Losses, Draws; // bilan du mois (classement)
        public bool Ranked;            // Elo publié dans le classement du mois
        public bool RewardsClaimed;
    }

    /// <summary>Données PvP d'un joueur, stockées côté serveur (lecture seule pour le client).</summary>
    [Serializable]
    public class PlayerPvpData
    {
        public int Elo = PvpConfig.StartingElo;
        /// <summary>Début de la recherche de duel en cours (0 : aucune) : une minute sans adversaire de la division avant les bots.</summary>
        public long DuelSearchSinceUnixMs;
        public int TotalDuels;          // depuis la création du compte (placement)
        public int Wins;
        public int Losses;
        public int Draws;

        public string Season;           // "2026-10"
        public int SeasonDuels;
        public int SeasonDuelsLastWeek;
        public int SeasonCountedDuels;  // duels comptés pour le skin de participation
        /// <summary>Bilan du mois, affiché dans le classement mensuel.</summary>
        public int SeasonWins;
        public int SeasonLosses;
        public int SeasonDraws;
        public int BestEloThisSeason = PvpConfig.StartingElo;
        /// <summary>Le serveur a publié l'Elo du joueur dans le classement de cette saison (au moins un duel résolu).</summary>
        public bool Ranked;
        public SeasonSummary LastSeason;

        public string Day;              // "2026-10-04" (UTC)
        public int DuelsToday;
        public int CountedDuelsToday;
        public int WinsToday;
        /// <summary>Signalements de triche envoyés aujourd'hui (limités par <see cref="PvpConfig.MaxReportsPerDay"/>).</summary>
        public int ReportsToday;
        public bool DailyChestGranted;
        public Dictionary<string, int> OpponentsToday = new Dictionary<string, int>();

        /// <summary>Sceaux de Maât : monnaie PvP, gagnée seulement en jouant.</summary>
        public int Seals;
        /// <summary>Meilleure ligue jamais atteinte (débloque des articles de la boutique PvP).</summary>
        public League HighestLeague = League.Bronze;
        /// <summary>Identifiants des skins PvP obtenus (classement, participation, boutique PvP).</summary>
        public List<string> UnlockedRewards = new List<string>();

        /// <summary>Duos 2v2 du joueur (<see cref="TeamConfig.MaxDuosPerPlayer"/> au plus).</summary>
        public List<string> Duos = new List<string>();
        /// <summary>Invitations à former un duo, reçues d'amis.</summary>
        public List<DuoInvite> DuoInvites = new List<DuoInvite>();
        /// <summary>Guilde du joueur, null s'il n'en a pas.</summary>
        public string GuildId;
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
        /// <summary>Combat d'équipe dont c'est une manche, null pour un duel.</summary>
        public string BattleId;
        public int Slot;
        /// <summary>Personne de la division pour l'instant : le jeu redemande dans quelques secondes (rien n'est créé).</summary>
        public bool Searching;
        /// <summary>Depuis combien de temps le serveur cherche (la minute avant les bots).</summary>
        public int SearchedMs;
        public string Error;   // "LOCKED" si l'acte 3 n'est pas atteint, "OUTDATED" si le jeu n'a pas la version du serveur, "NOT_YOUR_TURN"
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
        /// <summary>Pour une manche d'un combat d'équipe : le combat, vu par ce joueur.</summary>
        public TeamBattle Battle;
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
        /// <summary>Bilan du joueur sur le mois de ce classement.</summary>
        public int Wins, Losses, Draws;
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

    /// <summary>
    /// La tenue d'une momie, par identifiants du catalogue de skins (momie, couleur, torche, chapeau, chaussures). Purement
    /// cosmétique : le client l'annonce, le serveur se contente de la nettoyer (<see cref="Sanitize"/>).
    /// </summary>
    [Serializable]
    public class PlayerLook
    {
        public string Mummy, Color, Torch, Hat, Shoes;
        /// <summary>Titre choisi par le joueur (<see cref="Titles"/>), affiché sous son nom.</summary>
        public string Title;

        public const int MaxIdLength = 40;

        /// <summary>Une copie aux identifiants sûrs (minuscules, chiffres, « _ » et « - », 40 caractères au plus), null si rien ne reste.</summary>
        public static PlayerLook Sanitize(PlayerLook look)
        {
            if (look == null) return null;
            var clean = new PlayerLook
            {
                Mummy = Id(look.Mummy), Color = Id(look.Color), Torch = Id(look.Torch), Hat = Id(look.Hat), Shoes = Id(look.Shoes),
                Title = Titles.Get(Id(look.Title))?.Id,
            };
            return clean.Mummy == null && clean.Color == null && clean.Torch == null && clean.Hat == null && clean.Shoes == null && clean.Title == null ? null : clean;
        }

        static string Id(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return null;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return null;
            return id;
        }
    }

    /// <summary>Une des deux courses d'un duel, de quoi la rejouer (même graine + mêmes actions = même course).</summary>
    [Serializable]
    public class DuelRun
    {
        public string PlayerId;
        public string PlayerName;
        public PlayerLook Look;
        /// <summary>Elo du joueur au moment de la course.</summary>
        public int Elo;
        public RunOutcome Outcome;
        public int TimeMs;
        public float Progress;
        public List<RunInput> Inputs = new List<RunInput>();

        public static DuelRun Of(GhostRun g) => g == null ? null : new DuelRun
        {
            PlayerId = g.PlayerId, PlayerName = g.PlayerName, Look = g.Look, Elo = g.Elo,
            Outcome = g.Outcome, TimeMs = g.TimeMs, Progress = g.Progress, Inputs = g.Inputs ?? new List<RunInput>(),
        };
    }

    /// <summary>
    /// Un duel joué, vu par un joueur : sa course (<see cref="Me"/>), celle de l'adversaire (<see cref="Rival"/>) et le verdict.
    /// Le premier sur un tombeau n'a pas encore d'adversaire : le duel reste ouvert jusqu'à ce que quelqu'un affronte son
    /// fantôme, et le serveur le complète alors.
    /// </summary>
    [Serializable]
    public class DuelRecord
    {
        /// <summary>Identifiant du duel de ce joueur (pour le premier coureur, aussi celui de son fantôme).</summary>
        public string MatchId;
        public int Seed;
        public int GeneratorVersion;
        public long PlayedAtUnixMs;
        /// <summary>False tant que personne n'a couru contre le fantôme de ce joueur.</summary>
        public bool Resolved;
        public DuelResult Result;
        public int EloBefore;
        public int EloAfter;
        public DuelRun Me;
        /// <summary>Null tant que le duel n'est pas résolu.</summary>
        public DuelRun Rival;
        /// <summary>Ce joueur a signalé son adversaire pour ce duel.</summary>
        public bool Reported;
    }

    /// <summary>Réponse de GetDuelHistory : les derniers duels du joueur, du plus récent au plus ancien.</summary>
    [Serializable]
    public class DuelHistoryResponse
    {
        public List<DuelRecord> Duels = new List<DuelRecord>();
    }

    /// <summary>Un signalement : le duel en entier, pour qu'un humain le revoie.</summary>
    [Serializable]
    public class CheatReport
    {
        public string MatchId;
        public string ReporterId;
        public long ReportedAtUnixMs;
        public int Seed;
        public int GeneratorVersion;
        /// <summary>La course soupçonnée.</summary>
        public DuelRun Suspect;
        /// <summary>La course de celui qui signale.</summary>
        public DuelRun Reporter;
    }

    /// <summary>
    /// Dossier d'un joueur signalé, rangé à part (illisible par les joueurs) et examiné depuis le Dashboard. Plusieurs
    /// joueurs différents qui le signalent le marquent « à vérifier » ; aucune sanction automatique.
    /// </summary>
    [Serializable]
    public class CheatDossier
    {
        public string PlayerId;
        public string PlayerName;
        /// <summary>Joueurs différents qui l'ont signalé.</summary>
        public List<string> Reporters = new List<string>();
        /// <summary>Les derniers signalements (<see cref="PvpConfig.MaxReportsPerDossier"/> au plus).</summary>
        public List<CheatReport> Reports = new List<CheatReport>();
        public int TotalReports;
        /// <summary>À vérifier : <see cref="PvpConfig.ReportersToFlag"/> joueurs différents l'ont signalé.</summary>
        public bool Flagged;
        public long LastReportUnixMs;
    }

    /// <summary>Réponse de ReportCheat.</summary>
    [Serializable]
    public class ReportResponse
    {
        public bool Ok;
        public string Error;   // "UNKNOWN_DUEL", "NO_RIVAL", "ALREADY_REPORTED", "LIMIT"
    }
}
