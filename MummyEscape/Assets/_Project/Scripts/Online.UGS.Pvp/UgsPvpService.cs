using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Duels through the "PvpMatchmaking" Cloud Code module (server/PvpMatchmaking): the server picks the rival, replays
    /// the run and keeps the Elo, the seals and the rewards in protected Cloud Save data the game can only read.
    /// The monthly ranking is the "pvp_elo" leaderboard (reset every month, archived versions = past seasons).
    /// </summary>
    public sealed class UgsPvpService : IPvpService
    {
        const string Module = "PvpMatchmaking";
        const string LeaderboardId = "pvp_elo";
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

        public async Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit)
        {
            var page = new PvpBoardPage();
            string me = AuthenticationService.Instance.PlayerId;
            var lb = LeaderboardsService.Instance;
            try
            {
                if (seasonsAgo == 0)
                {
                    page.Season = Seasons.SeasonOf(DateTime.UtcNow);
                    var scores = await lb.GetScoresAsync(LeaderboardId, new GetScoresOptions { Limit = limit });
                    foreach (var e in scores.Results) page.Rows.Add(Row(e.PlayerId, e.PlayerName, e.Score, e.Rank, me));
                    try
                    {
                        var mine = await lb.GetPlayerScoreAsync(LeaderboardId);
                        page.Me = Row(mine.PlayerId, mine.PlayerName, mine.Score, mine.Rank, me);
                    }
                    catch (Exception) { /* no duel this month */ }
                    return page;
                }

                // Past months: the archived versions of the leaderboard, most recent first.
                var versions = await lb.GetVersionsAsync(LeaderboardId, new GetVersionsOptions { Limit = Math.Max(1, seasonsAgo) });
                var list = new List<Unity.Services.Leaderboards.Models.LeaderboardVersion>(versions.Results ?? new List<Unity.Services.Leaderboards.Models.LeaderboardVersion>());
                list.Sort((a, b) => b.End.CompareTo(a.End));
                if (list.Count < seasonsAgo) return page;
                var version = list[seasonsAgo - 1];
                page.Season = Seasons.SeasonOf(version.Start.AddDays(1));
                var past = await lb.GetVersionScoresAsync(LeaderboardId, version.Id, new GetVersionScoresOptions { Limit = limit });
                foreach (var e in past.Results) page.Rows.Add(Row(e.PlayerId, e.PlayerName, e.Score, e.Rank, me));
                try
                {
                    var mine = await lb.GetVersionPlayerScoreAsync(LeaderboardId, version.Id);
                    page.Me = Row(mine.PlayerId, mine.PlayerName, mine.Score, mine.Rank, me);
                }
                catch (Exception) { /* not ranked that month */ }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Pvp] ranking unavailable: " + e.Message);
            }
            return page;
        }

        static PvpBoardRow Row(string playerId, string name, double score, int rank, string me) => new PvpBoardRow
        {
            PlayerId = playerId,
            PlayerName = name,
            Elo = (int)Math.Round(score),
            Rank = rank + 1, // the service counts from 0
            IsMe = playerId == me,
        };
    }
}
