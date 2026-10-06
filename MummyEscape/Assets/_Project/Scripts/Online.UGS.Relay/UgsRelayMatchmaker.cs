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
    /// The 2v2 search over Unity Lobby, then the match over Unity Relay (<see cref="UgsRelayLink"/>).
    ///
    /// Each duo has its own lobby (4 places): its first member creates it, his teammate finds it by the duo id. Once both are
    /// in, the lobby is "open" and shows the duo's division (the league of its best duel player). The two duos of the same
    /// division meet in the older lobby (the smaller id): the other duo's leader joins it, then his teammate. The host of a
    /// full lobby (two whole duos) creates the match on the server, opens a Relay allocation and gives its join code in the
    /// lobby; everyone connects, and the host says "go" when all four are there. Nobody of the division after a minute and
    /// both teammates accept bots: the duo plays a duo of bots (two phones on the same Relay).
    /// </summary>
    public sealed class UgsRelayMatchmaker : IRelayMatchmaker
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => RelayMatchmakerFactory.CreateOnline = () => new UgsRelayMatchmaker();

        public async Task<IRelayLink> FindAsync(RelaySearch search, Action<string> status, CancellationToken cancel)
        {
            var run = new Run(search, status, cancel);
            try
            {
                return await run.FindAsync();
            }
            catch (OperationCanceledException)
            {
                await run.LeaveAsync();
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Relay] Search failed: {e}");
                status(Loc.T("Connexion au 2v2 impossible."));
                await run.LeaveAsync();
                return null;
            }
        }

        /// <summary>One search, from the lobby of the duo to the connected match.</summary>
        sealed class Run
        {
            const string LobbyName = "mummy_2v2"; // noloc
            // Lobby data (S/N = indexed, searchable)
            const string KeyDuo = "duo", KeyGen = "gen", KeyState = "state", KeyDivision = "div", KeyMove = "move", KeyMatch = "match", KeyJoin = "join"; // noloc
            // Player data (visible to the lobby's members)
            const string KeyName = "name", KeyLook = "look", KeyBots = "bots", KeyElo = "elo"; // noloc
            const string StateParty = "party", StateOpen = "open", StateClosed = "closed", Failed = "-"; // noloc
            const string Outdated = "OUTDATED"; // noloc

            const int PollMs = 2000;
            const int HeartbeatMs = 15_000;
            const int SeekMs = 4000;
            /// <summary>A duo that came half (the teammate could not follow) is sent away after this.</summary>
            const int HalfDuoMs = 15_000;
            const int ConnectMs = 20_000;

            static ILobbyService Lobbies => LobbyService.Instance;

            readonly RelaySearch _search;
            readonly Action<string> _status;
            readonly CancellationToken _cancel;
            readonly bool _leader;
            readonly string _mateId, _mateName;
            readonly int _division;

            Lobby _home;          // the duo's lobby
            string _away;         // the other duo's lobby, once the duo moves there
            DateTime _openedAt, _lastBeat, _lastSeek, _moveAt;
            readonly Dictionary<string, DateTime> _halfSince = new Dictionary<string, DateTime>();

            public Run(RelaySearch search, Action<string> status, CancellationToken cancel)
            {
                _search = search;
                _status = status;
                _cancel = cancel;
                var members = search.Duo.Members;
                _leader = members.Count > 0 && members[0] == search.Me;
                int mate = members.FindIndex(m => m != search.Me);
                _mateId = mate >= 0 ? members[mate] : null;
                _mateName = mate >= 0 && mate < search.Duo.Names.Count ? search.Duo.Names[mate] : "?";
                _division = (int)Leagues.FromElo(search.MyDuelElo);
            }

            DateTime Now => DateTime.UtcNow;
            string Me => _search.Me;
            string Current => _away ?? _home?.Id;

            public async Task<IRelayLink> FindAsync()
            {
                _status(Loc.F("En attente de {0}…", _mateName));
                if (_leader) await CreateHomeAsync();
                else await JoinHomeAsync();

                while (true)
                {
                    _cancel.ThrowIfCancellationRequested();
                    var lobby = await GetAsync(Current);
                    if (lobby == null)
                    {
                        if (_away != null) { await BackHomeAsync(); continue; }
                        _status(Loc.F("{0} a quitté la recherche.", _mateName));
                        _home = null;
                        return null;
                    }
                    await HeartbeatAsync();

                    // The match is ready: connect.
                    if (Data(lobby, KeyJoin) is string join && join.Length > 0)
                    {
                        if (join == Failed)
                        {
                            _status(Loc.T("Le match n'a pas pu démarrer."));
                            return null;
                        }
                        return await ConnectAsync(lobby, Data(lobby, KeyMatch), join);
                    }

                    if (lobby.HostId == Me)
                    {
                        var link = await HostStepAsync(lobby);
                        if (link != null) return link;
                    }
                    else if (_away == null && !_leader && Data(lobby, KeyMove) is string target && target.Length > 0)
                    {
                        await FollowAsync(target);
                    }
                    else if (_away != null && _leader)
                    {
                        await CheckFollowedAsync(lobby);
                    }
                    await Task.Delay(PollMs, _cancel);
                }
            }

            // ------------------------------------------------------------------ the duo's lobby

            Player MePlayer() => new Player(Me, data: new Dictionary<string, PlayerDataObject>
            {
                [KeyName] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _search.MeRunner.Name),
                [KeyLook] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, EncodeLook(_search.MeRunner.Look)),
                [KeyBots] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _search.AllowBots ? "1" : "0"),
                [KeyElo] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _search.MyDuelElo.ToString()),
                [KeyDuo] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _search.Duo.Id),
            });

            async Task CreateHomeAsync()
            {
                _home = await Lobbies.CreateLobbyAsync(LobbyName, 4, new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Player = MePlayer(),
                    Data = new Dictionary<string, DataObject>
                    {
                        [KeyDuo] = new DataObject(DataObject.VisibilityOptions.Public, _search.Duo.Id, DataObject.IndexOptions.S1),
                        [KeyGen] = new DataObject(DataObject.VisibilityOptions.Public, DifficultyGen(), DataObject.IndexOptions.S2),
                        [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateParty, DataObject.IndexOptions.S3),
                        [KeyDivision] = new DataObject(DataObject.VisibilityOptions.Public, _division.ToString(), DataObject.IndexOptions.N1),
                    },
                });
                _lastBeat = Now;
            }

            async Task JoinHomeAsync()
            {
                while (_home == null)
                {
                    _cancel.ThrowIfCancellationRequested();
                    var found = await QueryAsync(new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.S1, _search.Duo.Id, QueryFilter.OpOptions.EQ),
                        new QueryFilter(QueryFilter.FieldOptions.S3, StateParty, QueryFilter.OpOptions.EQ),
                    });
                    var lobby = found.FirstOrDefault();
                    if (lobby != null)
                    {
                        try { _home = await Lobbies.JoinLobbyByIdAsync(lobby.Id, new JoinLobbyByIdOptions { Player = MePlayer() }); }
                        catch (LobbyServiceException e) { Debug.LogWarning($"[Relay] Could not join the duo's lobby: {e.Reason}"); }
                    }
                    if (_home == null) await Task.Delay(PollMs, _cancel);
                }
            }

            /// <summary>The host's turn: open the lobby, send the half duos away, start the match, or look for the other duo.</summary>
            async Task<IRelayLink> HostStepAsync(Lobby lobby)
            {
                var duos = lobby.Players.GroupBy(p => PlayerData(p, KeyDuo) ?? "").ToList();
                var mine = duos.FirstOrDefault(g => g.Key == _search.Duo.Id)?.ToList() ?? new List<Player>();
                bool mineWhole = mine.Count == 2;
                // The first other duo that came is the rival; anyone else is sent away.
                var others = duos.Where(g => g.Key != _search.Duo.Id).OrderBy(g => g.Min(p => p.Joined)).ToList();
                foreach (var extra in others.Skip(1).SelectMany(g => g))
                    await KickAsync(lobby, extra.Id);
                var rival = others.FirstOrDefault()?.ToList();
                if (rival != null && rival.Count < 2)
                {
                    if (!_halfSince.ContainsKey(rival[0].Id)) _halfSince[rival[0].Id] = Now;
                    else if ((Now - _halfSince[rival[0].Id]).TotalMilliseconds > HalfDuoMs) await KickAsync(lobby, rival[0].Id);
                }

                if (mineWhole && rival != null && rival.Count == 2) return await StartMatchAsync(lobby, mine, rival);
                if (!mineWhole || rival != null) return null;

                // Only the duo: open to the others of the division.
                if (Data(lobby, KeyState) == StateParty)
                {
                    int division = Math.Max(_division, mine.Max(p => (int)Leagues.FromElo(ParseInt(PlayerData(p, KeyElo)))));
                    await UpdateAsync(lobby, new Dictionary<string, DataObject>
                    {
                        [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateOpen, DataObject.IndexOptions.S3),
                        [KeyDivision] = new DataObject(DataObject.VisibilityOptions.Public, division.ToString(), DataObject.IndexOptions.N1),
                    });
                    _openedAt = Now;
                    _status(Loc.T("Recherche d'un duo adverse…"));
                    return null;
                }

                double waited = (Now - _openedAt).TotalSeconds;
                if (waited >= RelayConfig.BotFallbackSeconds && mine.All(p => PlayerData(p, KeyBots) == "1"))
                    return await StartBotsAsync(lobby, mine);

                if ((Now - _lastSeek).TotalMilliseconds >= SeekMs)
                {
                    _lastSeek = Now;
                    await SeekAsync(lobby);
                }
                return null;
            }

            /// <summary>Another open duo of the division with an older lobby: the duo moves there.</summary>
            async Task SeekAsync(Lobby lobby)
            {
                var found = await QueryAsync(new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S2, DifficultyGen(), QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.S3, StateOpen, QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.N1, Data(lobby, KeyDivision), QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "2", QueryFilter.OpOptions.EQ),
                });
                var target = found.Where(l => l.Id != lobby.Id && Data(l, KeyDuo) != _search.Duo.Id && string.CompareOrdinal(l.Id, lobby.Id) < 0)
                                  .OrderBy(l => l.Created).FirstOrDefault();
                if (target == null) return;
                // Closed while moving: nobody comes here meanwhile.
                await UpdateAsync(lobby, new Dictionary<string, DataObject>
                {
                    [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateClosed, DataObject.IndexOptions.S3),
                    [KeyMove] = new DataObject(DataObject.VisibilityOptions.Member, target.Id),
                });
                try
                {
                    await Lobbies.JoinLobbyByIdAsync(target.Id, new JoinLobbyByIdOptions { Player = MePlayer() });
                    _away = target.Id;
                    _moveAt = Now;
                    _status(Loc.T("Duo adverse trouvé !"));
                }
                catch (LobbyServiceException e)
                {
                    Debug.Log($"[Relay] Could not join {target.Id}: {e.Reason}");
                    await ReopenAsync(lobby);
                }
            }

            /// <summary>The teammate follows the leader into the other duo's lobby.</summary>
            async Task FollowAsync(string target)
            {
                try
                {
                    await Lobbies.JoinLobbyByIdAsync(target, new JoinLobbyByIdOptions { Player = MePlayer() });
                    _away = target;
                    _status(Loc.T("Duo adverse trouvé !"));
                    try { await Lobbies.RemovePlayerAsync(_home.Id, Me); } catch (LobbyServiceException) { }
                }
                catch (LobbyServiceException e)
                {
                    // The leader comes back when the teammate does not arrive.
                    Debug.Log($"[Relay] Could not follow into {target}: {e.Reason}");
                }
            }

            /// <summary>The leader, in the other lobby: his teammate came (the duo's lobby is closed) or did not (back home).</summary>
            async Task CheckFollowedAsync(Lobby away)
            {
                if (away.Players.Any(p => p.Id == _mateId))
                {
                    if (_home != null)
                    {
                        try { await Lobbies.DeleteLobbyAsync(_home.Id); } catch (LobbyServiceException) { }
                        _home = null;
                    }
                    return;
                }
                if (_home != null && (Now - _moveAt).TotalMilliseconds > HalfDuoMs) await BackHomeAsync();
            }

            async Task BackHomeAsync()
            {
                if (_away != null)
                {
                    try { await Lobbies.RemovePlayerAsync(_away, Me); } catch (LobbyServiceException) { }
                    _away = null;
                }
                var home = _home == null ? null : await GetAsync(_home.Id);
                if (home == null)
                {
                    // The duo's lobby was closed when the teammate followed: the duo starts over in a new one.
                    _home = null;
                    _status(Loc.F("En attente de {0}…", _mateName));
                    if (_leader) await CreateHomeAsync();
                    else await JoinHomeAsync();
                    return;
                }
                if (home.HostId == Me) await ReopenAsync(home);
                _status(Loc.T("Recherche d'un duo adverse…"));
            }

            Task ReopenAsync(Lobby lobby) => UpdateAsync(lobby, new Dictionary<string, DataObject>
            {
                [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateOpen, DataObject.IndexOptions.S3),
                [KeyMove] = new DataObject(DataObject.VisibilityOptions.Member, ""),
            });

            // ------------------------------------------------------------------ start

            async Task<IRelayLink> StartMatchAsync(Lobby lobby, List<Player> mine, List<Player> rival)
            {
                _status(Loc.T("Duo adverse trouvé !"));
                await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = true,
                    Data = new Dictionary<string, DataObject> { [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateClosed, DataObject.IndexOptions.S3) },
                });
                var r = await _search.Pvp.StartRelayMatchAsync(lobby.Id, SideOf(mine), SideOf(rival));
                if (r?.Match == null) return await FailAsync(lobby, r?.Error);
                return await HostAsync(lobby, r.Match, mine.Concat(rival).Select(p => p.Id).Where(id => id != Me).ToList());
            }

            async Task<IRelayLink> StartBotsAsync(Lobby lobby, List<Player> mine)
            {
                _status(Loc.T("Aucun duo en vue : des bots prennent le relais."));
                await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = true,
                    Data = new Dictionary<string, DataObject> { [KeyState] = new DataObject(DataObject.VisibilityOptions.Public, StateClosed, DataObject.IndexOptions.S3) },
                });
                var r = await _search.Pvp.StartRelayBotsAsync(lobby.Id, SideOf(mine));
                if (r?.Match == null) return await FailAsync(lobby, r?.Error);
                return await HostAsync(lobby, r.Match, mine.Select(p => p.Id).Where(id => id != Me).ToList());
            }

            async Task<IRelayLink> FailAsync(Lobby lobby, string error)
            {
                Debug.LogWarning($"[Relay] The server did not create the match: {error}");
                await UpdateAsync(lobby, new Dictionary<string, DataObject> { [KeyJoin] = new DataObject(DataObject.VisibilityOptions.Member, Failed) });
                bool outdated = error == Outdated;
                _status(outdated ? Loc.T("Mets le jeu à jour pour affronter les autres joueurs.") : Loc.T("Le match n'a pas pu démarrer."));
                return null;
            }

            /// <summary>Opens the Relay allocation, gives its code in the lobby and waits for the others (they count as gone after a while).</summary>
            async Task<IRelayLink> HostAsync(Lobby lobby, RelayMatch match, List<string> others)
            {
                var allocation = await RelayService.Instance.CreateAllocationAsync(others.Count);
                string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                var link = UgsRelayLink.Host(allocation.ToRelayServerData("dtls"), match, Me, others); // noloc
                if (link.Failed) { link.Dispose(); return await FailAsync(lobby, "BIND"); } // noloc
                await UpdateAsync(lobby, new Dictionary<string, DataObject>
                {
                    [KeyMatch] = new DataObject(DataObject.VisibilityOptions.Member, match.Id),
                    [KeyJoin] = new DataObject(DataObject.VisibilityOptions.Member, code),
                });
                _status(Loc.T("Connexion des joueurs…"));
                var until = Now.AddMilliseconds(ConnectMs);
                while (!link.Ready && Now < until)
                {
                    if (_cancel.IsCancellationRequested) { link.Dispose(); _cancel.ThrowIfCancellationRequested(); }
                    link.Pump();
                    await Task.Delay(30);
                }
                if (!link.Ready) link.StartNow();
                link.Pump();
                // The lobby has done its job.
                try { await Lobbies.DeleteLobbyAsync(lobby.Id); } catch (LobbyServiceException) { }
                if (_home != null && _home.Id != lobby.Id) { try { await Lobbies.DeleteLobbyAsync(_home.Id); } catch (LobbyServiceException) { } }
                _home = null;
                _away = null;
                return link;
            }

            async Task<IRelayLink> ConnectAsync(Lobby lobby, string matchId, string code)
            {
                _status(Loc.T("Connexion des joueurs…"));
                RelayMatch match = null;
                for (int attempt = 0; attempt < 4 && match == null; attempt++)
                {
                    var r = await _search.Pvp.GetRelayResultAsync(matchId);
                    match = r?.Match;
                    if (match == null) await Task.Delay(1000, _cancel);
                }
                if (match == null) { _status(Loc.T("Le match n'a pas pu démarrer.")); return null; }
                var join = await RelayService.Instance.JoinAllocationAsync(code);
                var link = UgsRelayLink.Client(join.ToRelayServerData("dtls"), match, Me, lobby.HostId); // noloc
                var until = Now.AddMilliseconds(ConnectMs);
                while (!link.Ready && !link.Failed && Now < until)
                {
                    if (_cancel.IsCancellationRequested) { link.Dispose(); _cancel.ThrowIfCancellationRequested(); }
                    link.Pump();
                    await Task.Delay(30);
                }
                if (!link.Ready)
                {
                    link.Dispose();
                    _status(Loc.T("Le match n'a pas pu démarrer."));
                    return null;
                }
                // The host deletes the lobby; leaving is only tidier.
                try { await Lobbies.RemovePlayerAsync(lobby.Id, Me); } catch (LobbyServiceException) { }
                _home = null;
                _away = null;
                return link;
            }

            RelaySide SideOf(List<Player> players)
            {
                var side = new RelaySide { DuoId = PlayerData(players[0], KeyDuo) };
                foreach (var p in players)
                    side.Runners.Add(new RelayRunner { PlayerId = p.Id, Name = PlayerData(p, KeyName), Look = DecodeLook(PlayerData(p, KeyLook)) });
                return side;
            }

            // ------------------------------------------------------------------ cleanup

            /// <summary>Cancelled or failed: out of every lobby (the duo's lobby is deleted by its host).</summary>
            public async Task LeaveAsync()
            {
                try
                {
                    if (_away != null) await Lobbies.RemovePlayerAsync(_away, Me);
                }
                catch (Exception) { }
                try
                {
                    if (_home != null)
                    {
                        if (_leader) await Lobbies.DeleteLobbyAsync(_home.Id);
                        else await Lobbies.RemovePlayerAsync(_home.Id, Me);
                    }
                }
                catch (Exception) { }
                _away = null;
                _home = null;
            }

            // ------------------------------------------------------------------ Lobby helpers

            async Task<Lobby> GetAsync(string id)
            {
                if (id == null) return null;
                try { return await Lobbies.GetLobbyAsync(id); }
                catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound || e.Reason == LobbyExceptionReason.Forbidden)
                {
                    return null;
                }
                catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.RateLimited)
                {
                    await Task.Delay(PollMs, _cancel);
                    return await GetAsync(id);
                }
            }

            async Task<List<Lobby>> QueryAsync(List<QueryFilter> filters)
            {
                try
                {
                    var r = await Lobbies.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 20, Filters = filters });
                    return r?.Results ?? new List<Lobby>();
                }
                catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.RateLimited)
                {
                    return new List<Lobby>();
                }
            }

            async Task UpdateAsync(Lobby lobby, Dictionary<string, DataObject> data)
            {
                try { await Lobbies.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { Data = data }); }
                catch (LobbyServiceException e) { Debug.LogWarning($"[Relay] Lobby update refused: {e.Reason}"); }
            }

            async Task KickAsync(Lobby lobby, string playerId)
            {
                _halfSince.Remove(playerId);
                try { await Lobbies.RemovePlayerAsync(lobby.Id, playerId); } catch (LobbyServiceException) { }
            }

            async Task HeartbeatAsync()
            {
                if (!_leader || _home == null || (Now - _lastBeat).TotalMilliseconds < HeartbeatMs) return;
                _lastBeat = Now;
                try { await Lobbies.SendHeartbeatPingAsync(_home.Id); } catch (LobbyServiceException) { }
            }

            static string Data(Lobby lobby, string key) =>
                lobby.Data != null && lobby.Data.TryGetValue(key, out var d) ? d.Value : null;

            static string PlayerData(Player p, string key) =>
                p.Data != null && p.Data.TryGetValue(key, out var d) ? d.Value : null;

            static int ParseInt(string s) => int.TryParse(s, out int v) ? v : PvpConfig.StartingElo;

            static string DifficultyGen() => "v" + Core.DifficultyTable.GeneratorVersion; // noloc

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
}
