// Mummy Rush — combats d'équipe en différé : les duels 2v2 (duo d'amis, 3 manches) et les guerres de guildes (3, 5 ou
// 10 manches). C# pur, partagé entre le serveur (Cloud Code) et le jeu.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public enum BattleKind { Duo = 0, GuildWar = 1 }

    public enum GuildRole { Member = 0, Officer = 1, Leader = 2 }

    /// <summary>Qui entre dans la guilde : sur demande (le chef ou un officier accepte, par défaut), tout le monde, ou personne.</summary>
    public enum GuildJoinPolicy { Request = 0, Open = 1, Closed = 2 }

    /// <summary>Tous les réglages des équipes à un seul endroit.</summary>
    public static class TeamConfig
    {
        // --- 2v2 ---
        public const int DuoSlots = 3;                 // 3 manches, la première équipe à 2 victoires gagne
        public const int DuoWinsNeeded = 2;
        public const int MaxDuosPerPlayer = 3;
        public const int MaxDuoInvites = 10;
        public const int DuoStartingElo = 1000;

        // --- Guildes ---
        public const int GuildMaxMembers = 30;
        public const int GuildCreationScarabs = 500;   // payés dans le jeu (les scarabées sont locaux)
        public const int GuildNameMin = 3, GuildNameMax = 20;
        public const int GuildTagMin = 2, GuildTagMax = 4;
        public static readonly int[] WarSizes = { 3, 5, 10 };
        public const int GuildStartingWarElo = 1000;
        public const int MaxGuildApplicants = 30;
        public const int MaxGuildInvites = 10;
        public const int MaxGuildApplications = 5;

        // --- Combats ---
        public const int BattleHours = 24;             // fenêtre pour courir ses manches, une fois l'adversaire trouvé
        public const int QueueHours = 48;              // un combat sans adversaire disparaît de la file après ce délai

        // --- Points de guilde ---
        public const int PointsPerDuelWin = 1;
        public const int PointsPerDuoWin = 2;
        public const int PointsPerWarRaceWin = 1;
        public const int WarWinBonusPerSlot = 5;       // une guerre gagnée : 5 points par manche (15, 25 ou 50)
        public const int WarWinSealsPerRunner = 20;    // sceaux pour chaque coureur de la guilde gagnante

        /// <summary>Paliers de points de guilde et les skins qu'ils offrent à chaque membre (définitivement).</summary>
        public static readonly (int points, string reward)[] GuildSkinTiers =
        {
            (100, "pvp_guild_banner"),
            (500, "pvp_guild_livery"),
            (1500, "pvp_guild_crown"),
            (5000, "pvp_guild_boots"),
        };
    }

    /// <summary>Une course d'une manche, gardée pour la rejouer (fantôme de l'adversaire, replays des spectateurs).</summary>
    [Serializable]
    public class SlotRun
    {
        public string PlayerId;
        public string PlayerName;
        public PlayerLook Look;
        public RunOutcome Outcome;
        public int TimeMs;
        public float Progress;
        public List<RunInput> Inputs = new List<RunInput>();
        public long RunAtUnixMs;
    }

    /// <summary>Un camp d'un combat : le duo ou la guilde, son ordre de passage et ses courses.</summary>
    [Serializable]
    public class BattleSide
    {
        /// <summary>Identifiant du duo ou de la guilde.</summary>
        public string TeamId;
        public string TeamName;
        /// <summary>Elo 2v2 du duo, ou Elo de guerre de la guilde, au début du combat.</summary>
        public int Elo;
        /// <summary>Coureur de chaque manche (identifiants de joueurs), dans l'ordre de passage.</summary>
        public List<string> Order = new List<string>();
        public List<string> Names = new List<string>();
        /// <summary>Course de chaque manche, null tant qu'elle n'est pas courue.</summary>
        public List<SlotRun> Runs = new List<SlotRun>();
    }

    /// <summary>
    /// Un combat d'équipe. Chaque manche a son propre tombeau ; le premier des deux coureurs d'une manche court seul (sa
    /// course devient le fantôme), le second court contre ce fantôme. Les manches d'un camp se courent dans l'ordre.
    /// </summary>
    [Serializable]
    public class TeamBattle
    {
        public string Id;
        public BattleKind Kind;
        public int Slots;
        public List<int> Seeds = new List<int>();
        public int GeneratorVersion;
        public long CreatedAtUnixMs;
        /// <summary>Instant où le second camp a rejoint (0 = en file d'attente).</summary>
        public long StartedAtUnixMs;
        /// <summary>Après cet instant, les manches non courues comptent comme des abandons.</summary>
        public long DeadlineUnixMs;
        public BattleSide A;
        /// <summary>Null tant que personne n'a rejoint.</summary>
        public BattleSide B;
        /// <summary>Résultat de chaque manche du point de vue du camp A : -1 = pas encore décidé, sinon un <see cref="DuelResult"/>.</summary>
        public List<int> SlotResults = new List<int>();
        public bool Finished;
        /// <summary>Résultat final pour le camp A.</summary>
        public DuelResult Result;
        /// <summary>Elo, points et récompenses appliqués (une seule fois).</summary>
        public bool Applied;
        /// <summary>Variation d'Elo de chaque camp une fois appliquée.</summary>
        public int EloDeltaA, EloDeltaB;
        /// <summary>Dans une vue envoyée à un joueur (<see cref="TeamLogic.ViewFor"/>) : l'équipe de ce joueur.</summary>
        public string ViewerTeam;
    }

    /// <summary>Une place dans la file des combats sans adversaire.</summary>
    [Serializable]
    public class QueuedBattle
    {
        public string BattleId;
        public string TeamId;
        public List<string> Players = new List<string>();
        public int Elo;
        public long CreatedAtUnixMs;
    }

    // ------------------------------------------------------------------ 2v2

    [Serializable]
    public class Duo
    {
        public string Id;
        /// <summary>Le créateur d'abord : c'est lui qui choisit l'ordre de passage.</summary>
        public List<string> Members = new List<string>();
        public List<string> Names = new List<string>();
        public int Elo = TeamConfig.DuoStartingElo;
        public int Wins, Losses, Draws;
        public int Matches;
        public long CreatedAtUnixMs;
        /// <summary>Combat en cours (en file ou commencé), null sinon.</summary>
        public string ActiveBattle;
        /// <summary>Derniers combats terminés (les plus récents d'abord).</summary>
        public List<string> RecentBattles = new List<string>();

        public string Name => Names.Count >= 2 ? Names[0] + " & " + Names[1] : Id;
    }

    [Serializable]
    public class DuoInvite
    {
        public string Id;
        public string FromId;
        public string FromName;
        public long SentAtUnixMs;
    }

    /// <summary>Ligne de l'index des duos (classement 2v2).</summary>
    [Serializable]
    public class DuoSummary
    {
        public string Id;
        public string Name;
        public List<string> Members = new List<string>();
        public int Elo;
        public int Wins, Losses, Draws;
    }

    // ------------------------------------------------------------------ guildes

    [Serializable]
    public class GuildMember
    {
        public string PlayerId;
        public string Name;
        public GuildRole Role;
        public long JoinedAtUnixMs;
        /// <summary>Points rapportés à la guilde par ce membre.</summary>
        public int Points;
    }

    [Serializable]
    public class Guild
    {
        public string Id;
        public string Name;
        public string Tag;
        public long CreatedAtUnixMs;
        public List<GuildMember> Members = new List<GuildMember>();
        public int Points;
        public int WarElo = TeamConfig.GuildStartingWarElo;
        public int WarWins, WarLosses, WarDraws;
        public string ActiveWar;
        public List<string> RecentWars = new List<string>();
        public GuildJoinPolicy JoinPolicy;
        /// <summary>Joueurs qui ont postulé (guilde sur demande), en attente du chef ou d'un officier.</summary>
        public List<GuildApplicant> Applicants = new List<GuildApplicant>();

        public GuildMember Member(string playerId) => Members.Find(m => m.PlayerId == playerId);
    }

    /// <summary>Ligne de l'index des guildes (recherche et classement).</summary>
    [Serializable]
    public class GuildSummary
    {
        public string Id;
        public string Name;
        public string Tag;
        public int MemberCount;
        public int Points;
        public int WarElo;
        public int WarWins, WarLosses;
        public GuildJoinPolicy JoinPolicy;
    }

    [Serializable]
    public class GuildApplicant
    {
        public string PlayerId;
        public string Name;
        public long AtUnixMs;
    }

    /// <summary>Invitation à rejoindre une guilde, envoyée par son chef ou un officier (depuis un tchat).</summary>
    [Serializable]
    public class GuildInvite
    {
        public string GuildId;
        public string GuildName;
        public string GuildTag;
        public string FromName;
        public long AtUnixMs;
    }

    // ------------------------------------------------------------------ réponses

    /// <summary>Réponse de GetTeams : les duos du joueur, ses invitations et leurs combats.</summary>
    [Serializable]
    public class TeamsResponse
    {
        /// <summary>Identifiant du joueur qui demande (pour retrouver ses manches).</summary>
        public string Me;
        public List<Duo> Duos = new List<Duo>();
        public List<DuoInvite> Invites = new List<DuoInvite>();
        /// <summary>Combats en cours et récents des duos, vus par ce joueur (courses adverses non décidées masquées).</summary>
        public List<TeamBattle> Battles = new List<TeamBattle>();
        public string Error;
    }

    /// <summary>Réponse des actions de guilde : la guilde du joueur (null s'il n'en a pas) et ses guerres.</summary>
    [Serializable]
    public class GuildResponse
    {
        /// <summary>Identifiant du joueur qui demande.</summary>
        public string Me;
        public Guild Guild;
        public List<TeamBattle> Wars = new List<TeamBattle>();
        /// <summary>Skins de guilde que le joueur vient de recevoir.</summary>
        public List<string> NewRewards = new List<string>();
        /// <summary>Invitations reçues (joueur sans guilde).</summary>
        public List<GuildInvite> Invites = new List<GuildInvite>();
        /// <summary>Guildes où le joueur a postulé (joueur sans guilde).</summary>
        public List<string> Applied = new List<string>();
        public string Error;   // "NAME", "TAG", "TAKEN", "IN_GUILD", "NO_GUILD", "FULL", "RIGHTS", "ORDER", "BUSY", "REQUESTED", "CLOSED", "UNKNOWN"
    }

    [Serializable]
    public class GuildSearchResponse
    {
        public List<GuildSummary> Guilds = new List<GuildSummary>();
    }

    [Serializable]
    public class DuoBoardResponse
    {
        public List<DuoSummary> Rows = new List<DuoSummary>();
    }

    /// <summary>Réponse générique des actions d'équipe (invitation, recherche de combat…).</summary>
    [Serializable]
    public class TeamActionResponse
    {
        public bool Ok;
        public TeamBattle Battle;
        /// <summary>Invitation en guilde : le joueur est entré tout de suite (il avait postulé).</summary>
        public bool Joined;
        public string Error;   // "LIMIT", "SELF", "EXISTS", "UNKNOWN", "RIGHTS", "ORDER", "BUSY", "NO_GUILD", "MEMBER", "THEIR_GUILD", "FULL"
    }
}
