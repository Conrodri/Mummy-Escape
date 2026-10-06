// Module Cloud Code « PvpMatchmaking » : points d'entrée appelés par le jeu. Toute la logique est dans PvpServer
// (Assets/_Project/Scripts/Pvp), partagée avec le jeu ; ce module ne fait que la brancher sur Cloud Save et Leaderboards.
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace MummyEscape.Pvp.Server
{
    public class ModuleConfig : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.AddGameApiClient();
        }
    }

    public class PvpModule
    {
        readonly IGameApiClient _api;
        readonly ILogger<PvpModule> _logger;

        public PvpModule(IGameApiClient api, ILogger<PvpModule> logger)
        {
            _api = api;
            _logger = logger;
        }

        PvpServer Server(IExecutionContext ctx) => new PvpServer(new CloudSavePvpStore(_api, ctx, _logger));

        /// <summary>Trouve un adversaire de la ligue du joueur (fantôme proche en Elo), cherche encore (une minute), donne un
        /// bot de la ligue si le joueur l'accepte, ou fait courir le joueur en premier.</summary>
        [CloudCodeFunction("FindDuel")]
        public Task<FindDuelResponse> FindDuel(IExecutionContext ctx, int generatorVersion, bool allowBots) =>
            Server(ctx).FindDuelAsync(ctx.PlayerId, generatorVersion, allowBots);

        /// <summary>Reçoit la course, la rejoue, résout le duel ou met la course en file comme fantôme.</summary>
        [CloudCodeFunction("SubmitRun")]
        public Task<SubmitRunResponse> SubmitRun(IExecutionContext ctx, RunSubmission run, string playerName) =>
            Server(ctx).SubmitRunAsync(ctx.PlayerId, run, playerName);

        [CloudCodeFunction("GetPvpProfile")]
        public Task<PvpProfileResponse> GetPvpProfile(IExecutionContext ctx) => Server(ctx).GetProfileAsync(ctx.PlayerId);

        [CloudCodeFunction("ClaimSeasonRewards")]
        public Task<SeasonRewardsResponse> ClaimSeasonRewards(IExecutionContext ctx) => Server(ctx).ClaimSeasonRewardsAsync(ctx.PlayerId);

        [CloudCodeFunction("BuyWithSeals")]
        public Task<SealPurchaseResponse> BuyWithSeals(IExecutionContext ctx, string itemId) => Server(ctx).BuyWithSealsAsync(ctx.PlayerId, itemId);

        /// <summary>Un tour de la roue des sceaux du casino (0,5 % de chance d'un skin légendaire).</summary>
        [CloudCodeFunction("SpinSealWheel")]
        public Task<WheelSpinResponse> SpinSealWheel(IExecutionContext ctx) => Server(ctx).SpinSealWheelAsync(ctx.PlayerId);

        /// <summary>Classement mensuel vérifié : chaque score est comparé à l'Elo protégé du joueur (0 = ce mois, 1 = le précédent).</summary>
        [CloudCodeFunction("GetPvpBoard")]
        public Task<PvpBoardPage> GetPvpBoard(IExecutionContext ctx, int seasonsAgo, int limit) =>
            Server(ctx).GetBoardAsync(ctx.PlayerId, seasonsAgo, limit);

        /// <summary>Les 10 derniers duels du joueur, avec les deux courses de chacun (replays) ; complète ceux où il courait en premier.</summary>
        [CloudCodeFunction("GetDuelHistory")]
        public Task<DuelHistoryResponse> GetDuelHistory(IExecutionContext ctx) => Server(ctx).GetHistoryAsync(ctx.PlayerId);

        /// <summary>Signale l'adversaire d'un duel de l'historique : le duel est copié dans son dossier (Cloud Save › pvp_reports).</summary>
        [CloudCodeFunction("ReportCheat")]
        public Task<ReportResponse> ReportCheat(IExecutionContext ctx, string matchId) => Server(ctx).ReportCheatAsync(ctx.PlayerId, matchId);

        // ------------------------------------------------------------------ 2v2

        /// <summary>Les duos du joueur, ses invitations et leurs combats (courses adverses non décidées masquées).</summary>
        [CloudCodeFunction("GetTeams")]
        public Task<TeamsResponse> GetTeams(IExecutionContext ctx) => Server(ctx).GetTeamsAsync(ctx.PlayerId);

        [CloudCodeFunction("InviteDuo")]
        public Task<TeamActionResponse> InviteDuo(IExecutionContext ctx, string playerName, string friendId) =>
            Server(ctx).InviteDuoAsync(ctx.PlayerId, playerName, friendId);

        [CloudCodeFunction("RespondDuo")]
        public Task<TeamActionResponse> RespondDuo(IExecutionContext ctx, string playerName, string inviteId, bool accept) =>
            Server(ctx).RespondDuoAsync(ctx.PlayerId, playerName, inviteId, accept);

        [CloudCodeFunction("LeaveDuo")]
        public Task<TeamActionResponse> LeaveDuo(IExecutionContext ctx, string duoId) => Server(ctx).LeaveDuoAsync(ctx.PlayerId, duoId);

        /// <summary>Lance un combat 2v2 (meFirst : le joueur court les manches 1 et 3).</summary>
        [CloudCodeFunction("FindDuoMatch")]
        public Task<TeamActionResponse> FindDuoMatch(IExecutionContext ctx, string duoId, bool meFirst, int generatorVersion) =>
            Server(ctx).FindDuoMatchAsync(ctx.PlayerId, duoId, meFirst, generatorVersion);

        [CloudCodeFunction("GetDuoBoard")]
        public Task<DuoBoardResponse> GetDuoBoard(IExecutionContext ctx, int limit) => Server(ctx).GetDuoBoardAsync(limit);

        /// <summary>Démarre la manche du joueur dans un combat d'équipe ; la course revient par SubmitRun.</summary>
        [CloudCodeFunction("StartBattleRun")]
        public Task<FindDuelResponse> StartBattleRun(IExecutionContext ctx, string battleId, int generatorVersion) =>
            Server(ctx).StartBattleRunAsync(ctx.PlayerId, battleId, generatorVersion);

        [CloudCodeFunction("GetBattle")]
        public Task<TeamBattle> GetBattle(IExecutionContext ctx, string battleId) => Server(ctx).GetBattleAsync(ctx.PlayerId, battleId);

        // ------------------------------------------------------------------ guildes

        [CloudCodeFunction("GetGuild")]
        public Task<GuildResponse> GetGuild(IExecutionContext ctx) => Server(ctx).GetGuildAsync(ctx.PlayerId);

        /// <summary>Crée une guilde (les 500 scarabées sont payés dans le jeu).</summary>
        [CloudCodeFunction("CreateGuild")]
        public Task<GuildResponse> CreateGuild(IExecutionContext ctx, string playerName, string name, string tag) =>
            Server(ctx).CreateGuildAsync(ctx.PlayerId, playerName, name, tag);

        [CloudCodeFunction("SearchGuilds")]
        public Task<GuildSearchResponse> SearchGuilds(IExecutionContext ctx, string query, int limit) => Server(ctx).SearchGuildsAsync(query, limit);

        [CloudCodeFunction("GetGuildBoard")]
        public Task<GuildSearchResponse> GetGuildBoard(IExecutionContext ctx, int limit) => Server(ctx).GetGuildBoardAsync(limit);

        [CloudCodeFunction("JoinGuild")]
        public Task<GuildResponse> JoinGuild(IExecutionContext ctx, string playerName, string guildId) =>
            Server(ctx).JoinGuildAsync(ctx.PlayerId, playerName, guildId);

        [CloudCodeFunction("LeaveGuild")]
        public Task<GuildResponse> LeaveGuild(IExecutionContext ctx) => Server(ctx).LeaveGuildAsync(ctx.PlayerId);

        [CloudCodeFunction("SetGuildRole")]
        public Task<GuildResponse> SetGuildRole(IExecutionContext ctx, string memberId, bool officer) =>
            Server(ctx).SetGuildRoleAsync(ctx.PlayerId, memberId, officer);

        [CloudCodeFunction("KickGuildMember")]
        public Task<GuildResponse> KickGuildMember(IExecutionContext ctx, string memberId) => Server(ctx).KickGuildMemberAsync(ctx.PlayerId, memberId);

        /// <summary>Le chef ou un officier lance une guerre (3, 5 ou 10 manches) avec l'ordre de passage choisi.</summary>
        [CloudCodeFunction("StartWar")]
        public Task<GuildResponse> StartWar(IExecutionContext ctx, int size, System.Collections.Generic.List<string> order, int generatorVersion) =>
            Server(ctx).StartWarAsync(ctx.PlayerId, size, order, generatorVersion);

        // ------------------------------------------------------------------ 2v2 relay (live)

        /// <summary>L'hôte du salon crée le match des deux duos qui se sont trouvés (même salon = même match).</summary>
        [CloudCodeFunction("StartRelayMatch")]
        public Task<RelayMatchResponse> StartRelayMatch(IExecutionContext ctx, int generatorVersion, string matchKey, RelaySide a, RelaySide b) =>
            Server(ctx).StartRelayMatchAsync(ctx.PlayerId, generatorVersion, matchKey, a, b);

        /// <summary>Aucun duo en vue : un match contre un duo de bots, couru d'avance par le serveur.</summary>
        [CloudCodeFunction("StartRelayBots")]
        public Task<RelayMatchResponse> StartRelayBots(IExecutionContext ctx, int generatorVersion, string matchKey, RelaySide mine) =>
            Server(ctx).StartRelayBotsAsync(ctx.PlayerId, generatorVersion, matchKey, mine);

        /// <summary>Le relais d'un duo à la fin du match : rejoué, puis le match est jugé (Elo 2v2) quand les deux sont là.</summary>
        [CloudCodeFunction("SubmitRelay")]
        public Task<RelayResultResponse> SubmitRelay(IExecutionContext ctx, string matchId, string starter,
                                                     System.Collections.Generic.List<RelayInput> inputs, System.Collections.Generic.List<string> quitters) =>
            Server(ctx).SubmitRelayAsync(ctx.PlayerId, matchId, starter, inputs, quitters);

        [CloudCodeFunction("GetRelayResult")]
        public Task<RelayResultResponse> GetRelayResult(IExecutionContext ctx, string matchId) => Server(ctx).RelayResultAsync(ctx.PlayerId, matchId);

        /// <summary>« Quitter et signaler » : le coéquipier qui a quitté la partie est noté (Cloud Save › pvp_quit_reports).</summary>
        [CloudCodeFunction("ReportRelayQuit")]
        public Task<ReportResponse> ReportRelayQuit(IExecutionContext ctx, string matchId, string quitterId) =>
            Server(ctx).ReportRelayQuitAsync(ctx.PlayerId, matchId, quitterId);

        /// <summary>Les 10 derniers matchs 2v2 du joueur, les deux relais de chacun (replays).</summary>
        [CloudCodeFunction("GetRelayHistory")]
        public Task<RelayHistoryResponse> GetRelayHistory(IExecutionContext ctx) => Server(ctx).GetRelayHistoryAsync(ctx.PlayerId);

        // ------------------------------------------------------------------ tchat

        /// <summary>Les messages d'un canal ("global", "guild", "dm:" + joueur) après un numéro.</summary>
        [CloudCodeFunction("GetChat")]
        public Task<ChatPage> GetChat(IExecutionContext ctx, string channel, long afterSeq) => Server(ctx).GetChatAsync(ctx.PlayerId, channel, afterSeq);

        [CloudCodeFunction("SendChat")]
        public Task<ChatSendResponse> SendChat(IExecutionContext ctx, string channel, string text, string playerName) =>
            Server(ctx).SendChatAsync(ctx.PlayerId, playerName, channel, text);

        [CloudCodeFunction("GetChatInbox")]
        public Task<ChatInboxResponse> GetChatInbox(IExecutionContext ctx) => Server(ctx).GetChatInboxAsync(ctx.PlayerId);

        [CloudCodeFunction("BlockChat")]
        public Task<ReportResponse> BlockChat(IExecutionContext ctx, string playerId, bool block) => Server(ctx).BlockChatAsync(ctx.PlayerId, playerId, block);

        [CloudCodeFunction("SyncChatProfile")]
        public Task<ReportResponse> SyncChatProfile(IExecutionContext ctx, List<string> contacts, bool minor) => Server(ctx).SyncChatProfileAsync(ctx.PlayerId, contacts, minor);

        /// <summary>Signale un message : copié dans le dossier de son auteur (Cloud Save › pvp_chat_reports), masqué à 3 signalements.</summary>
        [CloudCodeFunction("ReportChat")]
        public Task<ReportResponse> ReportChat(IExecutionContext ctx, string channel, long seq) => Server(ctx).ReportChatAsync(ctx.PlayerId, channel, seq);

        /// <summary>Partage un replay du joueur (duel ou 2v2) dans un canal : le serveur le copie depuis ses propres données.</summary>
        [CloudCodeFunction("ShareReplay")]
        public Task<ChatSendResponse> ShareReplay(IExecutionContext ctx, string kind, string matchId, string channel, string text, string playerName) =>
            Server(ctx).ShareReplayAsync(ctx.PlayerId, playerName, kind, matchId, channel, text);

        [CloudCodeFunction("GetSharedReplay")]
        public Task<SharedReplayResponse> GetSharedReplay(IExecutionContext ctx, string id) => Server(ctx).GetSharedReplayAsync(ctx.PlayerId, id);

        [CloudCodeFunction("ExportPvpData")]
        public Task<PvpDataExportResponse> ExportPvpData(IExecutionContext ctx) => Server(ctx).ExportPlayerDataAsync(ctx.PlayerId);

        [CloudCodeFunction("DeletePvpData")]
        public Task<ReportResponse> DeletePvpData(IExecutionContext ctx) => Server(ctx).DeletePlayerDataAsync(ctx.PlayerId);

        [CloudCodeFunction("RefillPvpEnergy")]
        public Task<EnergyResponse> RefillPvpEnergy(IExecutionContext ctx) => Server(ctx).RefillEnergyAsync(ctx.PlayerId);

        // --- Duel en direct

        /// <summary>Crée le duel de deux joueurs de la même ligue qui se sont trouvés dans un salon (l'hôte l'appelle).</summary>
        [CloudCodeFunction("StartLiveDuel")]
        public Task<LiveDuelResponse> StartLiveDuel(IExecutionContext ctx, int generatorVersion, string matchKey, LiveDuelist a, LiveDuelist b) =>
            Server(ctx).StartLiveDuelAsync(ctx.PlayerId, generatorVersion, matchKey, a, b);

        [CloudCodeFunction("GetLiveDuel")]
        public Task<LiveDuelResponse> GetLiveDuel(IExecutionContext ctx, string matchId) => Server(ctx).GetLiveDuelAsync(ctx.PlayerId, matchId);

        /// <summary>Personne de la ligue en une minute : un bot de la ligue, sa course jouée d'avance.</summary>
        [CloudCodeFunction("StartLiveBotDuel")]
        public Task<LiveDuelResponse> StartLiveBotDuel(IExecutionContext ctx, int generatorVersion, string matchKey, LiveDuelist mine) =>
            Server(ctx).StartLiveBotDuelAsync(ctx.PlayerId, generatorVersion, matchKey, mine);

        /// <summary>La course d'un joueur à la fin du duel : rejouée, puis le duel est jugé quand les deux sont là.</summary>
        [CloudCodeFunction("SubmitLiveDuel")]
        public Task<SubmitRunResponse> SubmitLiveDuel(IExecutionContext ctx, string matchId, RunSubmission run, string playerName) =>
            Server(ctx).SubmitLiveDuelAsync(ctx.PlayerId, matchId, run, playerName);

        [CloudCodeFunction("GetLiveDuelResult")]
        public Task<SubmitRunResponse> GetLiveDuelResult(IExecutionContext ctx, string matchId) => Server(ctx).LiveDuelResultAsync(ctx.PlayerId, matchId);
    }
}
