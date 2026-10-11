using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// The two members of a duo meet for the 2v2 campaign over Unity Lobby (a lobby per duo, 2 places), then play over
    /// Unity Relay (<see cref="UgsRelayLink"/>). The first to press "Play" opens the lobby and decides the tomb; the
    /// teammate joins it. Both opened one at the same moment: the newer one moves into the older one. Same game version
    /// only (the tombs are in the game).
    /// </summary>
    public sealed class UgsCoopMatchmaker : ICoopMatchmaker
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => CoopMatchmakerFactory.CreateOnline = () => new UgsCoopMatchmaker();

        const string LobbyName = "mummy_coop"; // noloc
        const string KeyKind = "kind", KeyDuo = "duo", KeyVersion = "ver", KeyTomb = "tomb", KeyJoin = "join"; // noloc
        const string KeyName = "name", KeyLook = "look"; // noloc
        const string KindCoop = "coop"; // noloc
        const int PollMs = 2000, SeekMs = 4000, HeartbeatMs = 15_000, ConnectMs = 20_000;

        static ILobbyService Lobbies => LobbyService.Instance;

        public async Task<CoopStart> FindAsync(CoopSearch search, Action<string> status, CancellationToken cancel)
        {
            string lobbyId = null;
            bool host = false;
            string mateId = search.Duo.Members.Find(m => m != search.Me);
            int mateIndex = search.Duo.Members.IndexOf(mateId);
            string mateName = mateIndex >= 0 && mateIndex < search.Duo.Names.Count ? search.Duo.Names[mateIndex] : "?";
            try
            {
                DateTime lastSeek = DateTime.MinValue, lastBeat = DateTime.UtcNow;
                status(Loc.F("En attente de {0}…", mateName));
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (lobbyId == null)
                    {
                        var open = await OpenLobbiesAsync(search);
                        foreach (var candidate in open)
                            if (await TryJoinAsync(candidate.Id, search)) { lobbyId = candidate.Id; host = false; break; }
                        if (lobbyId == null)
                        {
                            lobbyId = (await Lobbies.CreateLobbyAsync(LobbyName, 2, new CreateLobbyOptions
                            {
                                IsPrivate = false,
                                Player = MePlayer(search),
                                Data = new Dictionary<string, DataObject>
                                {
                                    [KeyKind] = new DataObject(DataObject.VisibilityOptions.Public, KindCoop, DataObject.IndexOptions.S1),
                                    [KeyDuo] = new DataObject(DataObject.VisibilityOptions.Public, search.Duo.Id, DataObject.IndexOptions.S2),
                                    [KeyVersion] = new DataObject(DataObject.VisibilityOptions.Public, Application.version, DataObject.IndexOptions.S3),
                                    [KeyTomb] = new DataObject(DataObject.VisibilityOptions.Member, search.Tomb.ToString()),
                                },
                            })).Id;
                            host = true;
                            lastBeat = DateTime.UtcNow;
                        }
                    }

                    var lobby = await GetAsync(lobbyId, cancel);
                    if (lobby == null) { lobbyId = null; continue; } // the teammate left: search again

                    if (lobby.HostId != search.Me)
                    {
                        if (Data(lobby, KeyJoin) is string join && join.Length > 0)
                        {
                            var start = await ConnectAsync(lobby, join, search, status, cancel);
                            lobbyId = null; // left
                            return start;
                        }
                    }
                    else
                    {
                        if ((DateTime.UtcNow - lastBeat).TotalMilliseconds >= HeartbeatMs)
                        {
                            lastBeat = DateTime.UtcNow;
                            try { await Lobbies.SendHeartbeatPingAsync(lobby.Id); } catch (LobbyServiceException) { }
                        }
                        var mate = lobby.Players.FirstOrDefault(p => p.Id == mateId);
                        if (mate != null)
                        {
                            var start = await HostAsync(lobby, mate, search, status, cancel);
                            lobbyId = null; // deleted
                            return start;
                        }
                        // The teammate opened his own lobby at the same moment: the newer one moves into the older one.
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
                Debug.LogWarning($"[Coop] Search failed: {e}");
                status(Loc.T("Connexion au serveur impossible."));
                await LeaveAsync(lobbyId, host, search.Me);
                return null;
            }
        }

        async Task<CoopStart> HostAsync(Lobby lobby, Player mate, CoopSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Connexion des joueurs…"));
            await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { IsLocked = true });
            var allocation = await RelayService.Instance.CreateAllocationAsync(1);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            var link = UgsRelayLink.Host(allocation.ToRelayServerData("dtls"), null, search.Me, new[] { mate.Id }); // noloc
            await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> { [KeyJoin] = new DataObject(DataObject.VisibilityOptions.Member, code) },
            });
            var until = DateTime.UtcNow.AddMilliseconds(ConnectMs);
            while (!link.Ready && !link.Failed && DateTime.UtcNow < until)
            {
                if (cancel.IsCancellationRequested) { link.Dispose(); cancel.ThrowIfCancellationRequested(); }
                link.Pump();
                await Task.Delay(30);
            }
            try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
            if (!link.Ready)
            {
                link.Dispose();
                status(Loc.T("La connexion avec ton coéquipier a échoué."));
                return null;
            }
            return new CoopStart
            {
                Link = link, Duo = search.Duo, Me = search.Me, Host = true, Tomb = search.Tomb,
                MateName = PlayerData(mate, KeyName), MateLook = UgsDuelMatchmaker.DecodeLook(PlayerData(mate, KeyLook)),
            };
        }

        async Task<CoopStart> ConnectAsync(Lobby lobby, string code, CoopSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Connexion des joueurs…"));
            var hostPlayer = lobby.Players.FirstOrDefault(p => p.Id == lobby.HostId);
            int tomb = int.TryParse(Data(lobby, KeyTomb), out int t) ? t : search.Tomb;
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
                status(Loc.T("La connexion avec ton coéquipier a échoué."));
                return null;
            }
            return new CoopStart
            {
                Link = link, Duo = search.Duo, Me = search.Me, Host = false, Tomb = tomb,
                MateName = hostPlayer == null ? null : PlayerData(hostPlayer, KeyName),
                MateLook = hostPlayer == null ? null : UgsDuelMatchmaker.DecodeLook(PlayerData(hostPlayer, KeyLook)),
            };
        }

        // ------------------------------------------------------------------ Lobby helpers

        static Player MePlayer(CoopSearch search) => new Player(search.Me, data: new Dictionary<string, PlayerDataObject>
        {
            [KeyName] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, search.MyName ?? ""),
            [KeyLook] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, UgsDuelMatchmaker.EncodeLook(search.Look)),
        });

        static async Task<List<Lobby>> OpenLobbiesAsync(CoopSearch search)
        {
            try
            {
                var r = await Lobbies.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 5,
                    Filters = new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.S1, KindCoop, QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.S2, search.Duo.Id, QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.S3, Application.version, QueryFilter.OpOptions.EQ),
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

        static async Task<bool> TryJoinAsync(string lobbyId, CoopSearch search)
        {
            try
            {
                await Lobbies.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions { Player = MePlayer(search) });
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.Log($"[Coop] Could not join {lobbyId}: {e.Reason}");
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

        static string Data(Lobby lobby, string key) => lobby.Data != null && lobby.Data.TryGetValue(key, out var d) ? d.Value : null;
        static string PlayerData(Player p, string key) => p.Data != null && p.Data.TryGetValue(key, out var d) ? d.Value : null;
    }
}
