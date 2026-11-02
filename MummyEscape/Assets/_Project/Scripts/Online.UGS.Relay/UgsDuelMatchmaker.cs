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
    /// lobby; the guest connects and the host says "go". Alone for a minute and bots accepted: a bot of the league. After
    /// 30 s the neighbour leagues count too. A guest who gets no code from the host leaves and searches again; so does the
    /// innocent side when the server refuses the duel because of the other player. The app going to the background stops
    /// the search (<see cref="MatchSearches"/>).
    /// </summary>
    public sealed class UgsDuelMatchmaker : IDuelMatchmaker
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => DuelMatchmakerFactory.CreateOnline = () => new UgsDuelMatchmaker();

        const string LobbyName = "mummy_duel"; // noloc
        const string KeyKind = "kind", KeyGen = "gen", KeyLeague = "league", KeyMatch = "match", KeyJoin = "join"; // noloc
        const string KeyName = "name", KeyLook = "look", KeyElo = "elo"; // noloc
        /// <summary>Why the host's duel was refused: "error|playerId" (the player at fault), next to <see cref="Failed"/>.</summary>
        const string KeyRefused = "refused"; // noloc
        const string KindDuel = "duel", Failed = "-"; // noloc
        const int PollMs = 2000;
        const int SeekMs = 4000;
        const int HeartbeatMs = 15_000;
        const int ConnectMs = 20_000;
        /// <summary>A guest without the host's Relay code this long after joining: the host is gone, search again.</summary>
        const int GuestWaitMs = 10_000;
        /// <summary>After this long, the lobbies of the neighbour leagues too.</summary>
        const int WidenMs = 30_000;

        static ILobbyService Lobbies => LobbyService.Instance;

        public async Task<DuelStart> FindAsync(DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            string lobbyId = null;
            bool host = false;
            var caller = cancel;
            var background = MatchSearches.Token;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, background);
            cancel = linked.Token;
            // Sent to the background: out of the lobby at once, nobody waits for a sleeping phone.
            using var leaveNow = background.Register(() => _ = LeaveAsync(lobbyId, host, search.Me));
            // Lobbies left without a duel: not joined again in this search (a dead host's lobby stays listed a while).
            var skip = new HashSet<string>();
            try
            {
                var started = DateTime.UtcNow;
                DateTime lastSeek = DateTime.MinValue, lastBeat = DateTime.UtcNow, joinedAt = DateTime.UtcNow;
                status(Loc.T("Recherche d'un adversaire de ta ligue…"));
                await search.Pvp.StartLiveSearchAsync();
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    bool widen = (DateTime.UtcNow - started).TotalMilliseconds >= WidenMs;
                    if (lobbyId == null)
                    {
                        // An open lobby of the league (and the neighbour ones after a while), else our own.
                        var open = await OpenLobbiesAsync(search, widen);
                        foreach (var candidate in open)
                        {
                            if (skip.Contains(candidate.Id)) continue;
                            if (await TryJoinAsync(candidate.Id, search)) { lobbyId = candidate.Id; host = false; joinedAt = DateTime.UtcNow; break; }
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
                        if (join == Failed)
                        {
                            var (error, culprit) = Refusal(Data(lobby, KeyRefused));
                            if (lobby.HostId == search.Me || culprit == null || culprit == search.Me) { status(ErrorText(error)); return null; }
                            // Refused because of the host: this guest searches on.
                            await LeaveAsync(lobby.Id, false, search.Me);
                            skip.Add(lobby.Id);
                            lobbyId = null;
                            status(Loc.T("Recherche d'un adversaire de ta ligue…"));
                            continue;
                        }
                        if (lobby.HostId != search.Me)
                        {
                            var start = await ConnectAsync(lobby, Data(lobby, KeyMatch), join, search, status, cancel);
                            lobbyId = null; // left
                            return start;
                        }
                    }
                    else if (lobby.HostId != search.Me && (DateTime.UtcNow - joinedAt).TotalMilliseconds >= GuestWaitMs)
                    {
                        // The host never gave his code (gone, or stuck): leave and search again.
                        await LeaveAsync(lobby.Id, false, search.Me);
                        skip.Add(lobby.Id);
                        lobbyId = null;
                        continue;
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
                            var (start, again) = await HostAsync(lobby, rival, search, status, cancel);
                            lobbyId = null; // deleted
                            host = false;
                            if (again)
                            {
                                status(Loc.T("Recherche d'un adversaire de ta ligue…"));
                                continue;
                            }
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
                            if (r?.Error == "SEARCH") // noloc
                            {
                                // The server did not see this search start (its call was lost): a minute more.
                                await search.Pvp.StartLiveSearchAsync();
                                started = DateTime.UtcNow;
                                continue;
                            }
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
                            var older = (await OpenLobbiesAsync(search, widen)).FirstOrDefault(l => l.Id != lobby.Id && !skip.Contains(l.Id) && string.CompareOrdinal(l.Id, lobby.Id) < 0);
                            if (older != null && await TryJoinAsync(older.Id, search))
                            {
                                try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
                                lobbyId = older.Id;
                                host = false;
                                joinedAt = DateTime.UtcNow;
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
                if (!caller.IsCancellationRequested) status(Loc.T("Recherche interrompue."));
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

        /// <summary>The duel, or <c>again</c>: the server refused it because of the rival, and this player searches on.</summary>
        async Task<(DuelStart Start, bool Again)> HostAsync(Lobby lobby, Player rival, DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Adversaire trouvé !"));
            await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { IsLocked = true });
            // The leagues each phone believes in: the server blames the one that is out of date.
            var mine = new LiveDuelist { PlayerId = search.Me, Name = search.Name, Look = search.Look, Elo = search.Elo };
            var theirs = new LiveDuelist
            {
                PlayerId = rival.Id, Name = PlayerData(rival, KeyName), Look = DecodeLook(PlayerData(rival, KeyLook)),
                Elo = int.TryParse(PlayerData(rival, KeyElo), out int elo) ? elo : 0,
            };
            var r = await search.Pvp.StartLiveDuelAsync(lobby.Id, mine, theirs);
            if (r?.Match == null)
            {
                await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        [KeyJoin] = new DataObject(DataObject.VisibilityOptions.Member, Failed),
                        [KeyRefused] = new DataObject(DataObject.VisibilityOptions.Member, (r?.Error ?? "") + "|" + (r?.Refused ?? "")),
                    },
                });
                // Long enough for the guest's poll to read why, before the lobby goes.
                await Task.Delay(PollMs + 1000, cancel);
                try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
                if (r?.Refused != null && r.Refused != search.Me) return (null, true);
                status(ErrorText(r?.Error));
                return (null, false);
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
            return (new DuelStart { Match = r.Match, Me = search.Me, Link = link }, false);
        }

        /// <summary>
        /// The guest connects to the host first, then reads the duel: reading it is what shows him up on the server, so a
        /// connection that never comes leaves the duel void instead of lost.
        /// </summary>
        async Task<DuelStart> ConnectAsync(Lobby lobby, string matchId, string code, DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Connexion des joueurs…"));
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
            LiveDuel match = null;
            for (int attempt = 0; link.Ready && attempt < 4 && match == null; attempt++)
            {
                match = await FetchAsync(search.Pvp, matchId);
                if (match == null) await Task.Delay(1000, cancel);
            }
            if (!link.Ready || match == null)
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

        /// <summary>Open lobbies of the league, oldest first; <paramref name="widen"/>: of the neighbour leagues too.</summary>
        static async Task<List<Lobby>> OpenLobbiesAsync(DuelSearch search, bool widen)
        {
            try
            {
                int league = (int)Leagues.FromElo(search.Elo);
                var filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S1, KindDuel, QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.S2, Gen(), QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.EQ),
                };
                if (widen)
                {
                    filters.Add(new QueryFilter(QueryFilter.FieldOptions.N1, (league - 1).ToString(), QueryFilter.OpOptions.GE));
                    filters.Add(new QueryFilter(QueryFilter.FieldOptions.N1, (league + 1).ToString(), QueryFilter.OpOptions.LE));
                }
                else filters.Add(new QueryFilter(QueryFilter.FieldOptions.N1, League(search), QueryFilter.OpOptions.EQ));
                var r = await Lobbies.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 10,
                    Filters = filters,
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

        /// <summary>The host's "error|playerId" (<see cref="KeyRefused"/>); nobody at fault: null.</summary>
        static (string Error, string Culprit) Refusal(string text)
        {
            var parts = (text ?? "").Split('|');
            return (parts[0].Length == 0 ? null : parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
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
                case "LOCKED": return Loc.F("Évade-toi du tombeau {0} pour débloquer les duels.", MummyEscape.UI.LevelNames.Of(MummyEscape.Core.Unlocks.EndOfAct(1))); // noloc
                case "ENERGY": return Loc.T("Plus d'énergie de combat : un point revient toutes les 2 minutes."); // noloc
                default: return Loc.T("Le match n'a pas pu démarrer.");
            }
        }

        static string Data(Lobby lobby, string key) => lobby.Data != null && lobby.Data.TryGetValue(key, out var d) ? d.Value : null;
        static string PlayerData(Player p, string key) => p.Data != null && p.Data.TryGetValue(key, out var d) ? d.Value : null;
        /// <summary>Only games with the same tombs, the same map draft and the same protocol between phones meet.</summary>
        static string Gen() => "v" + Core.DifficultyTable.GeneratorVersion + "d" + PvpDraft.Version + "p" + LiveDuelConfig.Protocol; // noloc
        static string League(DuelSearch search) => ((int)Leagues.FromElo(search.Elo)).ToString();

        internal static string EncodeLook(PlayerLook look) => look == null ? "" :
            string.Join("|", look.Mummy, look.Color, look.Torch, look.Hat, look.Shoes, look.Title); // noloc

        internal static PlayerLook DecodeLook(string text)
        {
            var parts = (text ?? "").Split('|');
            if (parts.Length != 6) return null;
            string Part(int i) => parts[i].Length == 0 ? null : parts[i];
            return new PlayerLook { Mummy = Part(0), Color = Part(1), Torch = Part(2), Hat = Part(3), Shoes = Part(4), Title = Part(5) };
        }
    }
}
