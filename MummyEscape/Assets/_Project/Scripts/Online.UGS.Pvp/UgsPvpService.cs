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

        public Task<FindDuelResponse> FindDuelAsync(bool allowBots) =>
            Call("FindDuel", new Dictionary<string, object> { { "generatorVersion", DifficultyTable.GeneratorVersion }, { "allowBots", allowBots } },
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

        public Task<RelayMatchResponse> StartRelayMatchAsync(string matchKey, RelaySide a, RelaySide b) =>
            Call("StartRelayMatch", Args("generatorVersion", Gen, "matchKey", matchKey, "a", a, "b", b), e => new RelayMatchResponse { Error = e });

        public Task<RelayMatchResponse> StartRelayBotsAsync(string matchKey, RelaySide mine) =>
            Call("StartRelayBots", Args("generatorVersion", Gen, "matchKey", matchKey, "mine", mine), e => new RelayMatchResponse { Error = e });

        public Task<RelayResultResponse> SubmitRelayAsync(string matchId, string starter, List<RelayInput> inputs, List<string> quitters) =>
            Call("SubmitRelay", Args("matchId", matchId, "starter", starter ?? "", "inputs", inputs ?? new List<RelayInput>(), "quitters", quitters ?? new List<string>()),
                 e => new RelayResultResponse { Error = e });

        public Task<RelayResultResponse> GetRelayResultAsync(string matchId) =>
            Call("GetRelayResult", Args("matchId", matchId), e => new RelayResultResponse { Error = e });

        public Task<RelayHistoryResponse> GetRelayHistoryAsync() => Call<RelayHistoryResponse>("GetRelayHistory", null, _ => null);

        public Task<ChatPage> GetChatAsync(string channel, long afterSeq) =>
            Call("GetChat", Args("channel", channel, "afterSeq", afterSeq), e => new ChatPage { Channel = channel, Error = e });

        public Task<ChatSendResponse> SendChatAsync(string channel, string text, string playerName) =>
            Call("SendChat", Args("channel", channel, "text", text ?? "", "playerName", playerName ?? ""), e => new ChatSendResponse { Error = e });

        public Task<ChatInboxResponse> GetChatInboxAsync() => Call("GetChatInbox", null, e => new ChatInboxResponse { Error = e });

        public Task<ReportResponse> BlockChatAsync(string playerId, bool block) =>
            Call("BlockChat", Args("playerId", playerId, "block", block), e => new ReportResponse { Error = e });

        public Task<ReportResponse> SyncChatProfileAsync(List<string> friendIds, bool minor) =>
            Call("SyncChatProfile", Args("contacts", friendIds ?? new List<string>(), "minor", minor), e => new ReportResponse { Error = e });

        public Task<ReportResponse> ReportChatAsync(string channel, long seq) =>
            Call("ReportChat", Args("channel", channel, "seq", seq), e => new ReportResponse { Error = e });

        public Task<ChatSendResponse> ShareReplayAsync(string kind, string matchId, string channel, string text, string playerName) =>
            Call("ShareReplay", Args("kind", kind, "matchId", matchId, "channel", channel, "text", text ?? "", "playerName", playerName ?? ""),
                 e => new ChatSendResponse { Error = e });

        public Task<SharedReplayResponse> GetSharedReplayAsync(string id) =>
            Call("GetSharedReplay", Args("id", id), e => new SharedReplayResponse { Error = e });

        public Task<PvpDataExportResponse> ExportDataAsync() =>
            Call("ExportPvpData", null, e => new PvpDataExportResponse { Error = e });

        public Task<ReportResponse> DeleteDataAsync() =>
            Call("DeletePvpData", null, e => new ReportResponse { Error = e });

        public Task<EnergyResponse> RefillEnergyAsync() =>
            Call("RefillPvpEnergy", null, e => new EnergyResponse { Error = e });

        public Task<WalletResponse> GetWalletAsync() => Call("GetWallet", null, e => new WalletResponse { Error = e });

        public Task<WalletResponse> VerifyPurchaseAsync(string productId, string purchaseToken) =>
            Call("VerifyPurchase", Args("productId", productId, "purchaseToken", purchaseToken), e => new WalletResponse { Error = e });

        public Task<WalletResponse> BuyPassAsync() => Call("BuyPass", null, e => new WalletResponse { Error = e });
        public Task<WalletResponse> BuyTiersAsync() => Call("BuyTiers", null, e => new WalletResponse { Error = e });
        public Task<WalletResponse> BuyScarabsAsync(string offerId) => Call("BuyScarabs", Args("offerId", offerId), e => new WalletResponse { Error = e });
        public Task<WalletResponse> BuyGoldItemAsync(string skinId) => Call("BuyGoldItem", Args("skinId", skinId), e => new WalletResponse { Error = e });

        public Task<WalletResponse> ClaimPassRewardsAsync(string season, List<int> freeTiers, List<int> premiumTiers) =>
            Call("ClaimPassRewards", Args("season", season, "freeTiers", freeTiers ?? new List<int>(), "premiumTiers", premiumTiers ?? new List<int>()),
                 e => new WalletResponse { Error = e });

        public Task<LiveDuelResponse> StartLiveDuelAsync(string matchKey, LiveDuelist a, LiveDuelist b) =>
            Call("StartLiveDuel", Args("generatorVersion", Gen, "matchKey", matchKey, "a", a, "b", b), e => new LiveDuelResponse { Error = e });

        public Task<LiveDuelResponse> StartLiveBotDuelAsync(string matchKey, LiveDuelist mine) =>
            Call("StartLiveBotDuel", Args("generatorVersion", Gen, "matchKey", matchKey, "mine", mine), e => new LiveDuelResponse { Error = e });

        public Task<LiveDuelResponse> GetLiveDuelAsync(string matchId) =>
            Call("GetLiveDuel", Args("matchId", matchId), e => new LiveDuelResponse { Error = e });

        public Task<SubmitRunResponse> SubmitLiveDuelAsync(string matchId, RunSubmission run, string playerName) =>
            Call("SubmitLiveDuel", Args("matchId", matchId, "run", run, "playerName", playerName ?? ""), e => new SubmitRunResponse { Error = e });

        public Task<SubmitRunResponse> GetLiveDuelResultAsync(string matchId) =>
            Call("GetLiveDuelResult", Args("matchId", matchId), e => new SubmitRunResponse { Error = e });

        public Task<ReportResponse> ReportRelayQuitAsync(string matchId, string quitterId) =>
            Call("ReportRelayQuit", Args("matchId", matchId, "quitterId", quitterId), e => new ReportResponse { Error = e });

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
