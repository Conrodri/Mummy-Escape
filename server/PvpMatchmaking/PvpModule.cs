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

        static string ReadEndlessArchive()
        {
            using var stream = typeof(PvpModule).Assembly.GetManifestResourceStream("endless.txt");
            if (stream == null) return null;
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }

        PvpServer Server(IExecutionContext ctx) => new PvpServer(new CloudSavePvpStore(_api, ctx, _logger))
        {
            Purchases = new GooglePlayVerifier(_api, ctx, _logger),
            EndlessArchive = ReadEndlessArchive,
        };

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

        /// <summary>Message de maintenance et pause des duels, lus par le jeu à sa connexion.</summary>
        [CloudCodeFunction("GetStatus")]
        public Task<ServiceStatus> GetStatus(IExecutionContext ctx) => Server(ctx).GetStatusAsync();

        /// <summary>Pose l'état du service (équipe seulement).</summary>
        [CloudCodeFunction("SetStatus")]
        public Task<ServiceStatus> SetStatus(IExecutionContext ctx, ServiceStatus status) => Server(ctx).SetStatusAsync(ctx.PlayerId, status);

        /// <summary>Achat direct d'un légendaire de la roue des sceaux.</summary>
        [CloudCodeFunction("BuySealLegendary")]
        public Task<SealPurchaseResponse> BuySealLegendary(IExecutionContext ctx, string id) => Server(ctx).BuySealLegendaryAsync(ctx.PlayerId, id);

        /// <summary>Un tour de la roue des sceaux du casino (0,5 % de chance d'un skin légendaire).</summary>
        [CloudCodeFunction("SpinSealWheel")]
        public Task<WheelSpinResponse> SpinSealWheel(IExecutionContext ctx) => Server(ctx).SpinSealWheelAsync(ctx.PlayerId);

        /// <summary>Plusieurs tours de la roue des sceaux d'un coup (10 pour le prix de 9).</summary>
        [CloudCodeFunction("SpinSealWheelMany")]
        public Task<WheelSpinResponse> SpinSealWheelMany(IExecutionContext ctx, int count) => Server(ctx).SpinSealWheelAsync(ctx.PlayerId, count);

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

        /// <summary>Le groupe : le duo à jour (avec ma tenue notée dedans), prêt / plus prêt, et le lancement par le chef.</summary>
        [CloudCodeFunction("GetDuo")]
        public Task<DuoResponse> GetDuo(IExecutionContext ctx, string duoId, PlayerLook look) => Server(ctx).GetDuoAsync(ctx.PlayerId, duoId, look);

        [CloudCodeFunction("SetGroupReady")]
        public Task<DuoResponse> SetGroupReady(IExecutionContext ctx, string duoId, bool ready, PlayerLook look) =>
            Server(ctx).SetGroupReadyAsync(ctx.PlayerId, duoId, ready, look);

        [CloudCodeFunction("LaunchGroup")]
        public Task<DuoResponse> LaunchGroup(IExecutionContext ctx, string duoId, string mode) => Server(ctx).LaunchGroupAsync(ctx.PlayerId, duoId, mode);

        [CloudCodeFunction("LeaveDuo")]
        public Task<TeamActionResponse> LeaveDuo(IExecutionContext ctx, string duoId) => Server(ctx).LeaveDuoAsync(ctx.PlayerId, duoId);

        /// <summary>Lance un combat 2v2 (meFirst : le joueur court les manches 1 et 3).</summary>
        [CloudCodeFunction("FindDuoMatch")]
        public Task<TeamActionResponse> FindDuoMatch(IExecutionContext ctx, string duoId, bool meFirst, int generatorVersion) =>
            Server(ctx).FindDuoMatchAsync(ctx.PlayerId, duoId, meFirst, generatorVersion);

        [CloudCodeFunction("GetDuoBoard")]
        public Task<DuoBoardResponse> GetDuoBoard(IExecutionContext ctx, int limit) => Server(ctx).GetDuoBoardAsync(limit);

        /// <summary>Campagne 2v2 : un passage du duo, rejoué ; progression du duo et étoiles 2v2 du joueur.</summary>
        [CloudCodeFunction("SubmitCoop")]
        public Task<CoopClearResponse> SubmitCoop(IExecutionContext ctx, string duoId, int tomb,
                                                  System.Collections.Generic.List<CoopRunInput> inputs, int timeMs) =>
            Server(ctx).SubmitCoopAsync(ctx.PlayerId, duoId, tomb, inputs, timeMs);

        /// <summary>Classement de la campagne 2v2 (les duos, par étoiles puis temps cumulé).</summary>
        [CloudCodeFunction("GetCoopBoard")]
        public Task<DuoBoardResponse> GetCoopBoard(IExecutionContext ctx, int limit) => Server(ctx).GetCoopBoardAsync(limit);

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

        [CloudCodeFunction("SetGuildPolicy")]
        public Task<GuildResponse> SetGuildPolicy(IExecutionContext ctx, int policy) => Server(ctx).SetGuildPolicyAsync(ctx.PlayerId, (GuildJoinPolicy)policy);

        [CloudCodeFunction("AnswerGuildRequest")]
        public Task<GuildResponse> AnswerGuildRequest(IExecutionContext ctx, string playerId, bool accept) =>
            Server(ctx).AnswerGuildRequestAsync(ctx.PlayerId, playerId, accept);

        [CloudCodeFunction("InviteToGuild")]
        public Task<TeamActionResponse> InviteToGuild(IExecutionContext ctx, string playerName, string playerId) =>
            Server(ctx).InviteToGuildAsync(ctx.PlayerId, playerName, playerId);

        [CloudCodeFunction("RespondGuildInvite")]
        public Task<GuildResponse> RespondGuildInvite(IExecutionContext ctx, string playerName, string guildId, bool accept) =>
            Server(ctx).RespondGuildInviteAsync(ctx.PlayerId, playerName, guildId, accept);

        /// <summary>Le chef ou un officier lance une guerre (3, 5 ou 10 manches) avec l'ordre de passage choisi.</summary>
        [CloudCodeFunction("StartWar")]
        public Task<GuildResponse> StartWar(IExecutionContext ctx, int size, System.Collections.Generic.List<string> order, int generatorVersion) =>
            Server(ctx).StartWarAsync(ctx.PlayerId, size, order, generatorVersion);

        // ------------------------------------------------------------------ 2v2 relay (live)

        /// <summary>L'hôte du salon crée le match des deux duos qui se sont trouvés (même salon = même match).</summary>
        /// <remarks><c>draftVersion</c> : absent chez un jeu sans tirage des cartes (0), qui reçoit « OUTDATED ».</remarks>
        [CloudCodeFunction("StartRelayMatch")]
        public Task<RelayMatchResponse> StartRelayMatch(IExecutionContext ctx, int generatorVersion, string matchKey, RelaySide a, RelaySide b, int draftVersion) =>
            Server(ctx).StartRelayMatchAsync(ctx.PlayerId, generatorVersion, matchKey, a, b, draftVersion);

        /// <summary>Aucun duo en vue : un match contre un duo de bots, qui votent au tirage puis courent d'avance.</summary>
        [CloudCodeFunction("StartRelayBots")]
        public Task<RelayMatchResponse> StartRelayBots(IExecutionContext ctx, int generatorVersion, string matchKey, RelaySide mine, int draftVersion) =>
            Server(ctx).StartRelayBotsAsync(ctx.PlayerId, generatorVersion, matchKey, mine, draftVersion);

        /// <summary>Le vote d'un joueur au tirage des cartes (la carte que son duo bannit ; -1 : pas de choix à temps).</summary>
        [CloudCodeFunction("BanRelay")]
        public Task<RelayMatchResponse> BanRelay(IExecutionContext ctx, string matchId, int map) => Server(ctx).BanRelayAsync(ctx.PlayerId, matchId, map);

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

        [CloudCodeFunction("RegisterName")]
        public Task<ReportResponse> RegisterName(IExecutionContext ctx, string name) => Server(ctx).RegisterNameAsync(ctx.PlayerId, name);

        [CloudCodeFunction("FindPlayer")]
        public Task<FindPlayerResponse> FindPlayer(IExecutionContext ctx, string code) => Server(ctx).FindPlayerAsync(ctx.PlayerId, code);

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

        // ------------------------------------------------------------------ golden wallet (kept by the server)

        [CloudCodeFunction("GetWallet")]
        public Task<WalletResponse> GetWallet(IExecutionContext ctx) => Server(ctx).GetWalletAsync(ctx.PlayerId);

        /// <summary>Crédite un achat du Play Store après l'avoir vérifié auprès de Google (le jeton d'achat de Google Play Billing).</summary>
        [CloudCodeFunction("VerifyPurchase")]
        public Task<WalletResponse> VerifyPurchase(IExecutionContext ctx, string productId, string purchaseToken) =>
            Server(ctx).VerifyPurchaseAsync(ctx.PlayerId, productId, purchaseToken);

        [CloudCodeFunction("BuyPass")]
        public Task<WalletResponse> BuyPass(IExecutionContext ctx) => Server(ctx).BuyPassAsync(ctx.PlayerId);

        [CloudCodeFunction("BuyTiers")]
        public Task<WalletResponse> BuyTiers(IExecutionContext ctx, string requestId = null) => Server(ctx).BuyTiersAsync(ctx.PlayerId, requestId);

        /// <summary><paramref name="requestId"/> : rejouer la même demande ne débite pas deux fois (absent chez les anciens clients).</summary>
        [CloudCodeFunction("BuyScarabs")]
        public Task<WalletResponse> BuyScarabs(IExecutionContext ctx, string offerId, string requestId = null) =>
            Server(ctx).BuyScarabsAsync(ctx.PlayerId, offerId, requestId);

        [CloudCodeFunction("BuyGoldItem")]
        public Task<WalletResponse> BuyGoldItem(IExecutionContext ctx, string skinId) => Server(ctx).BuyGoldItemAsync(ctx.PlayerId, skinId);

        [CloudCodeFunction("OpenScarabs")]
        public Task<WalletResponse> OpenScarabs(IExecutionContext ctx, ScarabSeed seed) => Server(ctx).OpenScarabsAsync(ctx.PlayerId, seed);

        [CloudCodeFunction("BuyScarabPiece")]
        public Task<WalletResponse> BuyScarabPiece(IExecutionContext ctx, string id) => Server(ctx).BuyScarabPieceAsync(ctx.PlayerId, id);

        [CloudCodeFunction("BuyScarabSet")]
        public Task<WalletResponse> BuyScarabSet(IExecutionContext ctx, int act) => Server(ctx).BuyScarabSetAsync(ctx.PlayerId, act);

        [CloudCodeFunction("BuyWheelLegendary")]
        public Task<WalletResponse> BuyWheelLegendary(IExecutionContext ctx, string id) => Server(ctx).BuyWheelLegendaryAsync(ctx.PlayerId, id);

        [CloudCodeFunction("SpinScarabWheel")]
        public Task<WheelSpinResponse> SpinScarabWheel(IExecutionContext ctx, int count) => Server(ctx).SpinScarabWheelAsync(ctx.PlayerId, count);

        [CloudCodeFunction("ClaimPassRewards")]
        public Task<WalletResponse> ClaimPassRewards(IExecutionContext ctx, string season, List<int> freeTiers, List<int> premiumTiers) =>
            Server(ctx).ClaimPassRewardsAsync(ctx.PlayerId, season, freeTiers, premiumTiers);

        [CloudCodeFunction("SubmitSoloRuns")]
        public Task<WalletResponse> SubmitSoloRuns(IExecutionContext ctx, List<SoloRunReport> runs) =>
            Server(ctx).SubmitSoloRunsAsync(ctx.PlayerId, runs);

        // --- Daily and weekly quests

        [CloudCodeFunction("GetQuests")]
        public Task<QuestResponse> GetQuests(IExecutionContext ctx) => Server(ctx).GetQuestsAsync(ctx.PlayerId);

        [CloudCodeFunction("VisitQuest")]
        public Task<QuestResponse> VisitQuest(IExecutionContext ctx, string id) => Server(ctx).VisitQuestAsync(ctx.PlayerId, id);

        [CloudCodeFunction("ClaimQuest")]
        public Task<QuestResponse> ClaimQuest(IExecutionContext ctx, string id) => Server(ctx).ClaimQuestAsync(ctx.PlayerId, id);

        // --- Duel en direct

        /// <summary>Crée le duel de deux joueurs de la même ligue qui se sont trouvés dans un salon (l'hôte l'appelle).</summary>
        /// <remarks><c>draftVersion</c> : absent chez un jeu sans tirage des cartes (0), qui reçoit « OUTDATED ».</remarks>
        [CloudCodeFunction("StartLiveDuel")]
        public Task<LiveDuelResponse> StartLiveDuel(IExecutionContext ctx, int generatorVersion, string matchKey, LiveDuelist a, LiveDuelist b, int draftVersion) =>
            Server(ctx).StartLiveDuelAsync(ctx.PlayerId, generatorVersion, matchKey, a, b, draftVersion);

        /// <summary>Le duel (pour l'invité, et pendant le tirage : ferme le tirage quand son heure est passée).</summary>
        [CloudCodeFunction("GetLiveDuel")]
        public Task<LiveDuelResponse> GetLiveDuel(IExecutionContext ctx, string matchId) => Server(ctx).GetLiveDuelAsync(ctx.PlayerId, matchId);

        /// <summary>Le joueur bannit une carte du tirage (-1 : pas de choix à temps, le hasard bannit pour lui).</summary>
        [CloudCodeFunction("BanLiveDuel")]
        public Task<LiveDuelResponse> BanLiveDuel(IExecutionContext ctx, string matchId, int map) => Server(ctx).BanLiveDuelAsync(ctx.PlayerId, matchId, map);

        /// <summary>Personne de la ligue en une minute : un bot de la ligue, qui bannit aussi, puis court d'avance.</summary>
        [CloudCodeFunction("StartLiveBotDuel")]
        public Task<LiveDuelResponse> StartLiveBotDuel(IExecutionContext ctx, int generatorVersion, string matchKey, LiveDuelist mine, int draftVersion) =>
            Server(ctx).StartLiveBotDuelAsync(ctx.PlayerId, generatorVersion, matchKey, mine, draftVersion);

        /// <summary>La course d'un joueur à la fin du duel : rejouée, puis le duel est jugé quand les deux sont là.</summary>
        [CloudCodeFunction("SubmitLiveDuel")]
        public Task<SubmitRunResponse> SubmitLiveDuel(IExecutionContext ctx, string matchId, RunSubmission run, string playerName) =>
            Server(ctx).SubmitLiveDuelAsync(ctx.PlayerId, matchId, run, playerName);

        [CloudCodeFunction("GetLiveDuelResult")]
        public Task<SubmitRunResponse> GetLiveDuelResult(IExecutionContext ctx, string matchId) => Server(ctx).LiveDuelResultAsync(ctx.PlayerId, matchId);
    }
}
