using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Pvp;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// The live duel search over Unity Lobby, then the duel over Unity Relay (<see cref="UgsRelayLink"/>).
    ///
    /// A player joins the oldest open duel lobby of his league, or opens his own (2 places) and waits. Two players who
    /// opened a lobby each at the same moment meet in the older one (the smaller id): the other leaves his own and joins
    /// it. The host of a full lobby creates the duel on the server, opens a Relay allocation and gives its join code in the
    /// lobby; the guest connects and the host says "go". Alone for a minute and bots accepted: a bot of the league.
    /// </summary>
    public sealed class UgsDuelMatchmaker : IDuelMatchmaker
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => DuelMatchmakerFactory.CreateOnline = () => new UgsDuelMatchmaker();

        const string LobbyName = "mummy_duel"; // noloc
        const string KeyKind = "kind", KeyGen = "gen", KeyLeague = "league", KeyMatch = "match", KeyJoin = "join"; // noloc
        const string KeyName = "name", KeyLook = "look", KeyElo = "elo"; // noloc
        const string KindDuel = "duel", Failed = "-"; // noloc
        const int PollMs = 2000;
        const int SeekMs = 4000;
        const int HeartbeatMs = 15_000;
        const int ConnectMs = 20_000;

        static ILobbyService Lobbies => LobbyService.Instance;

        public async Task<DuelStart> FindAsync(DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            string lobbyId = null;
            bool host = false;
            try
            {
                var started = DateTime.UtcNow;
                DateTime lastSeek = DateTime.MinValue, lastBeat = DateTime.UtcNow;
                status(Loc.T("Recherche d'un adversaire de ta ligue…"));
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (lobbyId == null)
                    {
                        // An open lobby of the league, else our own.
                        var open = await OpenLobbiesAsync(search);
                        foreach (var candidate in open)
                        {
                            if (await TryJoinAsync(candidate.Id, search)) { lobbyId = candidate.Id; host = false; break; }
                        }
                        if (lobbyId == null)
                        {
                            lobbyId = (await Lobbies.CreateLobbyAsync(LobbyName, 2, new CreateLobbyOptions
                            {
                                IsPrivate = false,
                                Player = MePlayer(search),
                                Data = new Dictionary<string, DataObject>
                                {
                                    [KeyKind] = new DataObject(DataObject.VisibilityOptions.Public, KindDuel, DataObject.IndexOptions.S1),
                                    [KeyGen] = new DataObject(DataObject.VisibilityOptions.Public, Gen(), DataObject.IndexOptions.S2),
                                    [KeyLeague] = new DataObject(DataObject.VisibilityOptions.Public, League(search), DataObject.IndexOptions.N1),
                                },
                            })).Id;
                            host = true;
                            lastBeat = DateTime.UtcNow;
                        }
                    }

                    var lobby = await GetAsync(lobbyId, cancel);
                    if (lobby == null)
                    {
                        // The other player left (or the lobby closed): search again.
                        lobbyId = null;
                        continue;
                    }

                    if (Data(lobby, KeyJoin) is string join && join.Length > 0)
                    {
                        if (join == Failed) { status(Loc.T("Le match n'a pas pu démarrer.")); return null; }
                        if (lobby.HostId != search.Me)
                        {
                            var start = await ConnectAsync(lobby, Data(lobby, KeyMatch), join, search, status, cancel);
                            lobbyId = null; // left
                            return start;
                        }
                    }

                    if (lobby.HostId == search.Me)
                    {
                        if ((DateTime.UtcNow - lastBeat).TotalMilliseconds >= HeartbeatMs)
                        {
                            lastBeat = DateTime.UtcNow;
                            try { await Lobbies.SendHeartbeatPingAsync(lobby.Id); } catch (LobbyServiceException) { }
                        }
                        var rival = lobby.Players.FirstOrDefault(p => p.Id != search.Me);
                        if (rival != null)
                        {
                            var start = await HostAsync(lobby, rival, search, status, cancel);
                            lobbyId = null; // deleted
                            return start;
                        }
                        double waited = (DateTime.UtcNow - started).TotalMilliseconds;
                        if (search.AllowBots && waited >= PvpConfig.BotFallbackMs)
                        {
                            status(Loc.T("Personne de ta ligue en vue : un bot prend le relais."));
                            await Lobbies.DeleteLobbyAsync(lobby.Id);
                            lobbyId = null;
                            var mine = new LiveDuelist { PlayerId = search.Me, Name = search.Name, Look = search.Look };
                            var r = await search.Pvp.StartLiveBotDuelAsync(lobby.Id, mine);
                            if (r?.Match == null) { status(ErrorText(r?.Error)); return null; }
                            return new DuelStart { Match = r.Match, Me = search.Me };
                        }
                        int left = (int)Math.Max(0, (PvpConfig.BotFallbackMs - waited) / 1000);
                        status(search.AllowBots ? Loc.F("Recherche d'un adversaire de ta ligue… sinon un bot dans {0} s", left)
                                                : Loc.T("Recherche d'un adversaire de ta ligue…"));
                        // Someone else opened a lobby at the same moment: the newer one moves into the older one.
                        if ((DateTime.UtcNow - lastSeek).TotalMilliseconds >= SeekMs)
                        {
                            lastSeek = DateTime.UtcNow;
                            var older = (await OpenLobbiesAsync(search)).FirstOrDefault(l => l.Id != lobby.Id && string.CompareOrdinal(l.Id, lobby.Id) < 0);
                            if (older != null && await TryJoinAsync(older.Id, search))
                            {
                                try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
                                lobbyId = older.Id;
                                host = false;
                                continue;
                            }
                        }
                    }
                    await Task.Delay(PollMs, cancel);
                }
            }
            catch (OperationCanceledException)
            {
                await LeaveAsync(lobbyId, host, search.Me);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Duel] Search failed: {e}");
                status(Loc.T("Connexion au serveur des duels impossible."));
                await LeaveAsync(lobbyId, host, search.Me);
                return null;
            }
        }

        // ------------------------------------------------------------------ start

        async Task<DuelStart> HostAsync(Lobby lobby, Player rival, DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Adversaire trouvé !"));
            await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { IsLocked = true });
            var mine = new LiveDuelist { PlayerId = search.Me, Name = search.Name, Look = search.Look };
            var theirs = new LiveDuelist { PlayerId = rival.Id, Name = PlayerData(rival, KeyName), Look = DecodeLook(PlayerData(rival, KeyLook)) };
            var r = await search.Pvp.StartLiveDuelAsync(lobby.Id, mine, theirs);
            if (r?.Match == null)
            {
                await SetDataAsync(lobby, KeyJoin, Failed);
                try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
                status(ErrorText(r?.Error));
                return null;
            }
            var allocation = await RelayService.Instance.CreateAllocationAsync(1);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            var link = UgsRelayLink.Host(allocation.ToRelayServerData("dtls"), null, search.Me, new[] { rival.Id }); // noloc
            await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    [KeyMatch] = new DataObject(DataObject.VisibilityOptions.Member, r.Match.Id),
                    [KeyJoin] = new DataObject(DataObject.VisibilityOptions.Member, code),
                },
            });
            status(Loc.T("Connexion des joueurs…"));
            var until = DateTime.UtcNow.AddMilliseconds(ConnectMs);
            while (!link.Ready && !link.Failed && DateTime.UtcNow < until)
            {
                if (cancel.IsCancellationRequested) { link.Dispose(); cancel.ThrowIfCancellationRequested(); }
                link.Pump();
                await Task.Delay(30);
            }
            // The rival never came: he counts as gone (the duel is his loss).
            if (!link.Ready) link.StartNow();
            link.Pump();
            try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
            return new DuelStart { Match = r.Match, Me = search.Me, Link = link };
        }

        async Task<DuelStart> ConnectAsync(Lobby lobby, string matchId, string code, DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Connexion des joueurs…"));
            LiveDuel match = null;
            for (int attempt = 0; attempt < 4 && match == null; attempt++)
            {
                match = await FetchAsync(search.Pvp, matchId);
                if (match == null) await Task.Delay(1000, cancel);
            }
            if (match == null) { status(Loc.T("Le match n'a pas pu démarrer.")); return null; }
            var join = await RelayService.Instance.JoinAllocationAsync(code);
            var link = UgsRelayLink.Client(join.ToRelayServerData("dtls"), null, search.Me, lobby.HostId); // noloc
            var until = DateTime.UtcNow.AddMilliseconds(ConnectMs);
            while (!link.Ready && !link.Failed && DateTime.UtcNow < until)
            {
                if (cancel.IsCancellationRequested) { link.Dispose(); cancel.ThrowIfCancellationRequested(); }
                link.Pump();
                await Task.Delay(30);
            }
            try { await Lobbies.RemovePlayerAsync(lobby.Id, search.Me); } catch (LobbyServiceException) { }
            if (!link.Ready)
            {
                link.Dispose();
                status(Loc.T("Le match n'a pas pu démarrer."));
                return null;
            }
            return new DuelStart { Match = match, Me = search.Me, Link = link };
        }

        /// <summary>The guest learns the duel from the server.</summary>
        static async Task<LiveDuel> FetchAsync(IPvpService pvp, string matchId) => (await pvp.GetLiveDuelAsync(matchId))?.Match;

        // ------------------------------------------------------------------ Lobby helpers

        static Player MePlayer(DuelSearch search) => new Player(search.Me, data: new Dictionary<string, PlayerDataObject>
        {
            [KeyName] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, search.Name ?? ""),
            [KeyLook] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, EncodeLook(search.Look)),
            [KeyElo] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, search.Elo.ToString()),
        });

        static async Task<List<Lobby>> OpenLobbiesAsync(DuelSearch search)
        {
            try
            {
                var r = await Lobbies.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 10,
                    Filters = new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.S1, KindDuel, QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.S2, Gen(), QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.N1, League(search), QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.EQ),
                    },
                    Order = new List<QueryOrder> { new QueryOrder(true, QueryOrder.FieldOptions.Created) },
                });
                return (r?.Results ?? new List<Lobby>()).Where(l => l.HostId != search.Me).ToList();
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.RateLimited)
            {
                return new List<Lobby>();
            }
        }

        static async Task<bool> TryJoinAsync(string lobbyId, DuelSearch search)
        {
            try
            {
                await Lobbies.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions { Player = MePlayer(search) });
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.Log($"[Duel] Could not join {lobbyId}: {e.Reason}");
                return false;
            }
        }

        static async Task<Lobby> GetAsync(string id, CancellationToken cancel)
        {
            try { return await Lobbies.GetLobbyAsync(id); }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound || e.Reason == LobbyExceptionReason.Forbidden)
            {
                return null;
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.RateLimited)
            {
                await Task.Delay(PollMs, cancel);
                return await GetAsync(id, cancel);
            }
        }

        static async Task SetDataAsync(Lobby lobby, string key, string value)
        {
            try
            {
                await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject> { [key] = new DataObject(DataObject.VisibilityOptions.Member, value) },
                });
            }
            catch (LobbyServiceException) { }
        }

        static async Task LeaveAsync(string lobbyId, bool host, string me)
        {
            if (lobbyId == null) return;
            try
            {
                if (host) await Lobbies.DeleteLobbyAsync(lobbyId);
                else await Lobbies.RemovePlayerAsync(lobbyId, me);
            }
            catch (Exception) { }
        }

        static string ErrorText(string error)
        {
            switch (error)
            {
                case "OUTDATED": return Loc.T("Mets le jeu à jour pour affronter les autres joueurs."); // noloc
                case "LOCKED": return Loc.F("Gagne {0} étoiles en solo pour débloquer les duels.", PvpConfig.RequiredSoloStars); // noloc
                default: return Loc.T("Le match n'a pas pu démarrer.");
            }
        }

        static string Data(Lobby lobby, string key) => lobby.Data != null && lobby.Data.TryGetValue(key, out var d) ? d.Value : null;
        static string PlayerData(Player p, string key) => p.Data != null && p.Data.TryGetValue(key, out var d) ? d.Value : null;
        static string Gen() => "v" + Core.DifficultyTable.GeneratorVersion; // noloc
        static string League(DuelSearch search) => ((int)Leagues.FromElo(search.Elo)).ToString();

        static string EncodeLook(PlayerLook look) => look == null ? "" :
            string.Join("|", look.Mummy, look.Color, look.Torch, look.Hat, look.Shoes, look.Title); // noloc

        static PlayerLook DecodeLook(string text)
        {
            var parts = (text ?? "").Split('|');
            if (parts.Length != 6) return null;
            string Part(int i) => parts[i].Length == 0 ? null : parts[i];
            return new PlayerLook { Mummy = Part(0), Color = Part(1), Torch = Part(2), Hat = Part(3), Shoes = Part(4), Title = Part(5) };
        }
    }
}
