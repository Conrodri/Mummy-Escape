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

        public Task<WheelSpinResponse> SpinSealWheelAsync() =>
            Call("SpinSealWheel", null, e => new WheelSpinResponse { Error = e });

        public async Task SyncSoloStarsAsync(int stars)
        {
            try { await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { SoloStarsKey, stars } }); }
            catch (Exception e) { Debug.LogWarning("[Pvp] solo stars not synced: " + e.Message); }
        }

        public Task<DuelHistoryResponse> GetHistoryAsync() => Call<DuelHistoryResponse>("GetDuelHistory", null, _ => null);

        public Task<ReportResponse> ReportCheatAsync(string matchId) =>
            Call("ReportCheat", new Dictionary<string, object> { { "matchId", matchId } }, e => new ReportResponse { Error = e });

        // ------------------------------------------------------------------ teams

        static Dictionary<string, object> Args(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static int Gen => DifficultyTable.GeneratorVersion;

        public Task<TeamsResponse> GetTeamsAsync() => Call("GetTeams", null, e => new TeamsResponse { Error = e });

        public Task<TeamActionResponse> InviteDuoAsync(string friendId, string playerName) =>
            Call("InviteDuo", Args("playerName", playerName ?? "", "friendId", friendId), e => new TeamActionResponse { Error = e });

        public Task<TeamActionResponse> RespondDuoAsync(string inviteId, bool accept, string playerName) =>
            Call("RespondDuo", Args("playerName", playerName ?? "", "inviteId", inviteId, "accept", accept), e => new TeamActionResponse { Error = e });

        public Task<TeamActionResponse> LeaveDuoAsync(string duoId) =>
            Call("LeaveDuo", Args("duoId", duoId), e => new TeamActionResponse { Error = e });

        public Task<TeamActionResponse> FindDuoMatchAsync(string duoId, bool meFirst) =>
            Call("FindDuoMatch", Args("duoId", duoId, "meFirst", meFirst, "generatorVersion", Gen), e => new TeamActionResponse { Error = e });

        public async Task<DuoBoardResponse> GetDuoBoardAsync(int limit) =>
            await Call<DuoBoardResponse>("GetDuoBoard", Args("limit", limit), _ => null) ?? new DuoBoardResponse();

        public Task<FindDuelResponse> StartBattleRunAsync(string battleId) =>
            Call("StartBattleRun", Args("battleId", battleId, "generatorVersion", Gen), e => new FindDuelResponse { Error = e });

        public Task<TeamBattle> GetBattleAsync(string battleId) => Call<TeamBattle>("GetBattle", Args("battleId", battleId), _ => null);

        public Task<GuildResponse> GetGuildAsync() => Call("GetGuild", null, e => new GuildResponse { Error = e });

        public Task<GuildResponse> CreateGuildAsync(string name, string tag, string playerName) =>
            Call("CreateGuild", Args("playerName", playerName ?? "", "name", name, "tag", tag), e => new GuildResponse { Error = e });

        public async Task<GuildSearchResponse> SearchGuildsAsync(string query, int limit) =>
            await Call<GuildSearchResponse>("SearchGuilds", Args("query", query ?? "", "limit", limit), _ => null) ?? new GuildSearchResponse();

        public async Task<GuildSearchResponse> GetGuildBoardAsync(int limit) =>
            await Call<GuildSearchResponse>("GetGuildBoard", Args("limit", limit), _ => null) ?? new GuildSearchResponse();

        public Task<GuildResponse> JoinGuildAsync(string guildId, string playerName) =>
            Call("JoinGuild", Args("playerName", playerName ?? "", "guildId", guildId), e => new GuildResponse { Error = e });

        public Task<GuildResponse> LeaveGuildAsync() => Call("LeaveGuild", null, e => new GuildResponse { Error = e });

        public Task<GuildResponse> SetGuildRoleAsync(string memberId, bool officer) =>
            Call("SetGuildRole", Args("memberId", memberId, "officer", officer), e => new GuildResponse { Error = e });

        public Task<GuildResponse> KickGuildMemberAsync(string memberId) =>
            Call("KickGuildMember", Args("memberId", memberId), e => new GuildResponse { Error = e });

        public Task<GuildResponse> StartWarAsync(int size, List<string> order) =>
            Call("StartWar", Args("size", size, "order", order, "generatorVersion", Gen), e => new GuildResponse { Error = e });

        public async Task<PvpBoardPage> GetBoardAsync(int seasonsAgo, int limit)
        {
            // Read through the server, which checks every score against the players' protected Elo.
            var page = await Call<PvpBoardPage>("GetPvpBoard",
                new Dictionary<string, object> { { "seasonsAgo", seasonsAgo }, { "limit", limit } }, _ => null);
            return page ?? new PvpBoardPage();
        }
    }
}
