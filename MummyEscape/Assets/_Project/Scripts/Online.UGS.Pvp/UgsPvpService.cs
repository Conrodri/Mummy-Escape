using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Duels through the "PvpMatchmaking" Cloud Code module (server/PvpMatchmaking): the server picks the rival, replays
    /// the run and keeps the Elo, the seals and the rewards in protected Cloud Save data the game can only read.
    /// The monthly ranking ("pvp_elo" leaderboard) is read through the module too, which checks every score.
    /// </summary>
    public sealed class UgsPvpService : IPvpService
    {
        const string Module = "PvpMatchmaking";
        const string SoloStarsKey = "solo_total_stars";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PvpServiceFactory.CreateOnline = _ => new UgsPvpService();

        public bool IsDemo => false;

        static async Task<T> Call<T>(string function, Dictionary<string, object> args, Func<string, T> onError)
        {
            try
            {
                return await CloudCodeService.Instance.CallModuleEndpointAsync<T>(Module, function, args ?? new Dictionary<string, object>());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Pvp] {function} failed: {e.Message}");
                return onError(PvpServiceFactory.NetworkError);
            }
        }

        public Task<PvpProfileResponse> GetProfileAsync() =>
            Call<PvpProfileResponse>("GetPvpProfile", null, _ => null);

        public Task<FindDuelResponse> FindDuelAsync() =>
            Call("FindDuel", new Dictionary<string, object> { { "generatorVersion", DifficultyTable.GeneratorVersion } },
                 e => new FindDuelResponse { Error = e });

        public Task<SubmitRunResponse> SubmitRunAsync(RunSubmission run, string playerName) =>
            Call("SubmitRun", new Dictionary<string, object> { { "run", run }, { "playerName", playerName ?? "" } },
                 e => new SubmitRunResponse { Error = e });

        public Task<SeasonRewardsResponse> ClaimSeasonRewardsAsync() =>
            Call("ClaimSeasonRewards", null, e => new SeasonRewardsResponse { Error = e });

        public Task<SealPurchaseResponse> BuyWithSealsAsync(string itemId) =>
            Call("BuyWithSeals", new Dictionary<string, object> { { "itemId", itemId } }, e => new SealPurchaseResponse { Error = e });

        public async Task SyncSoloStarsAsync(int stars)
        {
            try { await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { SoloStarsKey, stars } }); }
            catch (Exception e) { Debug.LogWarning("[Pvp] solo stars not synced: " + e.Message); }
        }

        public Task<DuelHistoryResponse> GetHistoryAsync() => Call<DuelHistoryResponse>("GetDuelHistory", null, _ => null);

        public Task<ReportResponse> ReportCheatAsync(string matchId) =>
            Call("ReportCheat", new Dictionary<string, object> { { "matchId", matchId } }, e => new ReportResponse { Error = e });

        public async Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit)
        {
            // Read through the server, which checks every score against the players' protected Elo.
            var page = await Call<PvpBoardPage>("GetPvpBoard",
                new Dictionary<string, object> { { "seasonsAgo", seasonsAgo }, { "limit", limit } }, _ => null);
            return page ?? new PvpBoardPage();
        }
    }
}
