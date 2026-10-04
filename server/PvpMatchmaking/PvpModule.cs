// Module Cloud Code « PvpMatchmaking » : points d'entrée appelés par le jeu. Toute la logique est dans PvpServer
// (Assets/_Project/Scripts/Pvp), partagée avec le jeu ; ce module ne fait que la brancher sur Cloud Save et Leaderboards.
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace MummyEscape.Pvp.Server
{
    public class ModuleConfig : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.Dependencies.AddSingleton(GameApiClient.Create());
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

        /// <summary>Trouve un adversaire (fantôme proche en Elo) ou fait courir le joueur en premier.</summary>
        [CloudCodeFunction("FindDuel")]
        public Task<FindDuelResponse> FindDuel(IExecutionContext ctx, int generatorVersion) =>
            Server(ctx).FindDuelAsync(ctx.PlayerId, generatorVersion);

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
    }
}
