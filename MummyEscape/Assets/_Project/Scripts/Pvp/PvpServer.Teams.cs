// Mummy Escape — le serveur des équipes : duos 2v2 (avec un ami, Elo 2v2 et classement), guildes (points, skins,
// rôles) et combats d'équipe en différé (2v2 en 3 manches, guerres de guildes en 3, 5 ou 10 manches).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public sealed partial class PvpServer
    {
        public const string BattlesCollection = "pvp_battles";
        public const string DuosCollection = "pvp_duos";
        public const string GuildsCollection = "pvp_guilds";
        public const string IndexCollection = "pvp_team_index";
        public const string QueueCollection = "pvp_battle_queue";
        public const string DuoIndexKey = "duos";
        public const string GuildIndexKey = "guilds";
        const int RecentKept = 5;

        // --- Hors ligne seulement (LocalPvpService) : coéquipiers et adversaires simulés. ---

        /// <summary>Ce joueur est simulé : il accepte les invitations et court ses manches tout seul.</summary>
        public Func<string, bool> IsBot;
        /// <summary>La course d'un joueur simulé sur un tombeau (playerId, graine, Elo).</summary>
        public Func<string, int, int, SlotRun> BotRun;
        /// <summary>Un camp adverse simulé quand personne n'attend (genre, manches, Elo), ou null.</summary>
        public Func<BattleKind, int, int, BattleSide> BotSide;
        /// <summary>Le nom d'un joueur simulé (par défaut celui de <see cref="PvpBots.NameOf"/>).</summary>
        public Func<string, string> BotName;

        Task<T> Shared<T>(string collection, string key, Func<T, T> mutate = null) where T : class =>
            _store.UpdateSharedAsync(collection, key, mutate);

        static string NewId() => Guid.NewGuid().ToString("N");

        // ------------------------------------------------------------------ duos

        /// <summary>Les duos du joueur, ses invitations et les combats en cours ou récents de ses duos.</summary>
        public async Task<TeamsResponse> GetTeamsAsync(string me)
        {
            var d = await Update(me);
            var response = new TeamsResponse { Me = me, Invites = d.DuoInvites ?? new List<DuoInvite>() };
            foreach (var id in d.Duos ?? new List<string>())
            {
                var duo = await Shared<Duo>(DuosCollection, id);
                if (duo == null) continue;
                if (duo.ActiveBattle != null)
                {
                    var b = await RefreshBattleAsync(duo.ActiveBattle);
                    if (b != null) response.Battles.Add(TeamLogic.ViewFor(b, duo.Id));
                    duo = await Shared<Duo>(DuosCollection, id) ?? duo;
                }
                foreach (var past in duo.RecentBattles.Take(3))
                {
                    var b = await Shared<TeamBattle>(BattlesCollection, past);
                    if (b != null && response.Battles.All(x => x.Id != b.Id)) response.Battles.Add(TeamLogic.ViewFor(b, duo.Id));
                }
                response.Duos.Add(duo);
            }
            return response;
        }

        /// <summary>Invite un ami à former un duo.</summary>
        public async Task<TeamActionResponse> InviteDuoAsync(string me, string myName, string friendId)
        {
            if (string.IsNullOrEmpty(friendId) || friendId == me) return new TeamActionResponse { Error = "SELF" };
            var mine = await Update(me);
            if (mine.Duos.Count >= TeamConfig.MaxDuosPerPlayer) return new TeamActionResponse { Error = "LIMIT" };
            foreach (var id in mine.Duos)
            {
                var duo = await Shared<Duo>(DuosCollection, id);
                if (duo != null && duo.Members.Contains(friendId)) return new TeamActionResponse { Error = "EXISTS" };
            }
            var invite = new DuoInvite { Id = NewId(), FromId = me, FromName = myName, SentAtUnixMs = NowMs };
            if (IsBot?.Invoke(friendId) == true)
            {
                // Offline rival: says yes at once.
                await CreateDuoAsync(invite, friendId, BotName?.Invoke(friendId) ?? PvpBots.NameOf(friendId));
                return new TeamActionResponse { Ok = true };
            }
            string error = null;
            await Update(friendId, d =>
            {
                error = null;
                d.DuoInvites ??= new List<DuoInvite>();
                if (d.DuoInvites.Any(i => i.FromId == me)) error = "EXISTS";
                else if (d.DuoInvites.Count >= TeamConfig.MaxDuoInvites) error = "LIMIT";
                else d.DuoInvites.Add(invite);
            });
            return new TeamActionResponse { Ok = error == null, Error = error };
        }

        /// <summary>Accepte (le duo est créé) ou refuse une invitation.</summary>
        public async Task<TeamActionResponse> RespondDuoAsync(string me, string myName, string inviteId, bool accept)
        {
            DuoInvite invite = null;
            var mine = await Update(me, d =>
            {
                invite = d.DuoInvites?.Find(i => i.Id == inviteId);
                if (invite != null) d.DuoInvites.Remove(invite);
            });
            if (invite == null) return new TeamActionResponse { Error = "UNKNOWN" };
            if (!accept) return new TeamActionResponse { Ok = true };
            if (mine.Duos.Count >= TeamConfig.MaxDuosPerPlayer) return new TeamActionResponse { Error = "LIMIT" };
            var theirs = await Update(invite.FromId);
            if (theirs.Duos.Count >= TeamConfig.MaxDuosPerPlayer) return new TeamActionResponse { Error = "LIMIT" };
            await CreateDuoAsync(invite, me, myName);
            return new TeamActionResponse { Ok = true };
        }

        async Task CreateDuoAsync(DuoInvite invite, string partnerId, string partnerName)
        {
            var duo = new Duo
            {
                Id = NewId(), CreatedAtUnixMs = NowMs,
                Members = new List<string> { invite.FromId, partnerId },
                Names = new List<string> { invite.FromName ?? "", partnerName ?? "" },
            };
            await Shared<Duo>(DuosCollection, duo.Id, _ => duo);
            foreach (var id in duo.Members)
                if (IsBot?.Invoke(id) != true)
                    await Update(id, d => { if (!d.Duos.Contains(duo.Id)) d.Duos.Add(duo.Id); });
            await IndexDuoAsync(duo);
        }

        /// <summary>Quitte un duo (impossible pendant un combat) : il disparaît pour les deux.</summary>
        public async Task<TeamActionResponse> LeaveDuoAsync(string me, string duoId)
        {
            var duo = await Shared<Duo>(DuosCollection, duoId);
            if (duo == null || !duo.Members.Contains(me)) return new TeamActionResponse { Error = "UNKNOWN" };
            if (duo.ActiveBattle != null)
            {
                var b = await RefreshBattleAsync(duo.ActiveBattle);
                if (b != null && !b.Finished) return new TeamActionResponse { Error = "BUSY" };
            }
            foreach (var id in duo.Members)
                if (IsBot?.Invoke(id) != true)
                    await Update(id, d => d.Duos.Remove(duoId));
            await Shared<List<DuoSummary>>(IndexCollection, DuoIndexKey, list =>
            {
                list ??= new List<DuoSummary>();
                list.RemoveAll(s => s.Id == duoId);
                return list;
            });
            return new TeamActionResponse { Ok = true };
        }

        Task IndexDuoAsync(Duo duo) =>
            Shared<List<DuoSummary>>(IndexCollection, DuoIndexKey, list =>
            {
                list ??= new List<DuoSummary>();
                list.RemoveAll(s => s.Id == duo.Id);
                list.Add(new DuoSummary
                {
                    Id = duo.Id, Name = duo.Name, Members = duo.Members, Elo = duo.Elo,
                    Wins = duo.Wins, Losses = duo.Losses, Draws = duo.Draws,
                });
                return list;
            });

        /// <summary>
        /// Lance un combat 2v2 pour ce duo. <paramref name="meFirst"/> : le joueur court les manches 1 et 3, son partenaire la
        /// 2 (sinon l'inverse). Contre le duo d'Elo le plus proche qui attend, sinon le combat attend un adversaire.
        /// </summary>
        public async Task<TeamActionResponse> FindDuoMatchAsync(string me, string duoId, bool meFirst, int generatorVersion)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new TeamActionResponse { Error = "OUTDATED" };
            var duo = await Shared<Duo>(DuosCollection, duoId);
            if (duo == null || !duo.Members.Contains(me)) return new TeamActionResponse { Error = "UNKNOWN" };
            if (duo.ActiveBattle != null)
            {
                var current = await RefreshBattleAsync(duo.ActiveBattle);
                if (current != null && !current.Finished) return new TeamActionResponse { Ok = true, Battle = TeamLogic.ViewFor(current, duo.Id) };
            }
            bool creatorFirst = (duo.Members[0] == me) == meFirst;
            var order = TeamLogic.DuoOrder(duo, creatorFirst);
            var side = TeamLogic.NewSide(duo.Id, duo.Name, duo.Elo, order, id => duo.Names[duo.Members.IndexOf(id)]);
            var battle = await MatchAsync(BattleKind.Duo, TeamConfig.DuoSlots, "duo", side, duo.Members);
            await Shared<Duo>(DuosCollection, duo.Id, x => { x.ActiveBattle = battle.Id; return x; });
            return new TeamActionResponse { Ok = true, Battle = TeamLogic.ViewFor(battle, duo.Id) };
        }

        public async Task<DuoBoardResponse> GetDuoBoardAsync(int limit)
        {
            var list = await Shared<List<DuoSummary>>(IndexCollection, DuoIndexKey) ?? new List<DuoSummary>();
            return new DuoBoardResponse
            {
                Rows = list.Where(s => s.Wins + s.Losses + s.Draws > 0)
                           .OrderByDescending(s => s.Elo).ThenBy(s => s.Id, StringComparer.Ordinal).Take(limit).ToList(),
            };
        }

        // ------------------------------------------------------------------ matchmaking of battles

        /// <summary>
        /// Rejoint le combat en file d'Elo le plus proche (sans joueur commun), ou en crée un nouveau qui attend. Hors ligne,
        /// un camp simulé le rejoint aussitôt.
        /// </summary>
        async Task<TeamBattle> MatchAsync(BattleKind kind, int slots, string queueKey, BattleSide side, IList<string> players)
        {
            long now = NowMs;
            QueuedBattle picked = null;
            await Shared<List<QueuedBattle>>(QueueCollection, queueKey, queue =>
            {
                queue ??= new List<QueuedBattle>();
                queue.RemoveAll(q => now - q.CreatedAtUnixMs > TeamConfig.QueueHours * 3_600_000L);
                picked = queue.Where(q => q.TeamId != side.TeamId && !q.Players.Intersect(players).Any())
                              .OrderBy(q => Math.Abs(q.Elo - side.Elo)).FirstOrDefault();
                if (picked != null) queue.Remove(picked);
                return queue;
            });

            TeamBattle battle = null;
            if (picked != null)
                battle = await Shared<TeamBattle>(BattlesCollection, picked.BattleId, b =>
                {
                    if (b == null || b.B != null || b.Finished) return b;
                    TeamLogic.Join(b, side, now);
                    return b;
                });
            if (battle == null || battle.B != side)
            {
                battle = TeamLogic.NewBattle(NewId(), kind, slots, _newSeed, DifficultyTable.GeneratorVersion, now, side);
                var bot = BotSide?.Invoke(kind, slots, side.Elo);
                if (bot != null) TeamLogic.Join(battle, bot, now);
                await Shared<TeamBattle>(BattlesCollection, battle.Id, _ => battle);
                if (bot == null)
                    await Shared<List<QueuedBattle>>(QueueCollection, queueKey, queue =>
                    {
                        queue ??= new List<QueuedBattle>();
                        queue.Add(new QueuedBattle { BattleId = battle.Id, TeamId = side.TeamId, Players = players.ToList(), Elo = side.Elo, CreatedAtUnixMs = now });
                        return queue;
                    });
            }
            return await RunBotsAsync(battle.Id);
        }

        /// <summary>Hors ligne : les coureurs simulés courent toutes leurs manches tout de suite.</summary>
        async Task<TeamBattle> RunBotsAsync(string battleId)
        {
            if (BotRun == null) return await RefreshBattleAsync(battleId);
            var battle = await Shared<TeamBattle>(BattlesCollection, battleId);
            if (battle?.B == null) return battle;
            var runs = new List<(bool sideA, int slot, SlotRun run)>();
            foreach (bool sideA in new[] { true, false })
            {
                var side = sideA ? battle.A : battle.B;
                for (int k = 0; k < battle.Slots; k++)
                    if (side.Runs[k] == null && IsBot?.Invoke(side.Order[k]) == true)
                    {
                        var run = BotRun(side.Order[k], battle.Seeds[k], side.Elo);
                        if (run != null) runs.Add((sideA, k, run));
                    }
            }
            if (runs.Count > 0)
                await Shared<TeamBattle>(BattlesCollection, battleId, b =>
                {
                    foreach (var (sideA, slot, run) in runs)
                        if ((sideA ? b.A : b.B).Runs[slot] == null && b.SlotResults[slot] < 0) TeamLogic.Record(b, sideA, slot, run, NowMs);
                    return b;
                });
            return await RefreshBattleAsync(battleId);
        }

        /// <summary>Relit un combat : le termine si c'est joué (ou si le temps est écoulé) et applique son issue une fois.</summary>
        async Task<TeamBattle> RefreshBattleAsync(string battleId)
        {
            long now = NowMs;
            bool apply = false;
            var battle = await Shared<TeamBattle>(BattlesCollection, battleId, b =>
            {
                apply = false;
                if (b == null) return null;
                if (b.B == null && !b.Finished && now - b.CreatedAtUnixMs > TeamConfig.QueueHours * 3_600_000L) b.Finished = true; // nobody came
                TeamLogic.CheckFinished(b, now);
                if (b.Finished && !b.Applied)
                {
                    b.Applied = true;
                    apply = true;
                }
                return b;
            });
            if (apply) await ApplyAsync(battle);
            return battle;
        }

        /// <summary>L'issue d'un combat terminé : Elo des deux équipes, bilans, points de guilde, sceaux de guerre.</summary>
        async Task ApplyAsync(TeamBattle b)
        {
            if (b.Kind == BattleKind.Duo) await ApplyDuoAsync(b);
            else await ApplyWarAsync(b);
        }

        async Task ApplyDuoAsync(TeamBattle b)
        {
            if (b.B == null)
            {
                await Shared<Duo>(DuosCollection, b.A.TeamId, d => { if (d != null && d.ActiveBattle == b.Id) d.ActiveBattle = null; return d; });
                return;
            }
            var duoA = await Shared<Duo>(DuosCollection, b.A.TeamId);
            var duoB = await Shared<Duo>(DuosCollection, b.B.TeamId);
            int eloA = duoA?.Elo ?? b.A.Elo, eloB = duoB?.Elo ?? b.B.Elo;
            int newA = Elo.NewRating(eloA, eloB, b.Result, duoA?.Matches ?? 50);
            int newB = Elo.NewRating(eloB, eloA, DuelResolver.Invert(b.Result), duoB?.Matches ?? 50);
            await Shared<TeamBattle>(BattlesCollection, b.Id, x => { x.EloDeltaA = newA - eloA; x.EloDeltaB = newB - eloB; return x; });
            await SettleDuoAsync(b.A.TeamId, b.Id, newA, b.Result);
            await SettleDuoAsync(b.B.TeamId, b.Id, newB, DuelResolver.Invert(b.Result));
            var winners = b.Result == DuelResult.Win ? b.A : b.Result == DuelResult.Loss ? b.B : null;
            if (winners != null)
                foreach (var id in winners.Order.Distinct())
                    await AddGuildPointsForAsync(id, TeamConfig.PointsPerDuoWin);
        }

        async Task SettleDuoAsync(string duoId, string battleId, int elo, DuelResult result)
        {
            var duo = await Shared<Duo>(DuosCollection, duoId, d =>
            {
                if (d == null) return null;
                d.Elo = elo;
                d.Matches++;
                if (result == DuelResult.Win) d.Wins++;
                else if (result == DuelResult.Loss) d.Losses++;
                else d.Draws++;
                if (d.ActiveBattle == battleId) d.ActiveBattle = null;
                d.RecentBattles.Remove(battleId);
                d.RecentBattles.Insert(0, battleId);
                if (d.RecentBattles.Count > RecentKept) d.RecentBattles.RemoveRange(RecentKept, d.RecentBattles.Count - RecentKept);
                return d;
            });
            if (duo != null) await IndexDuoAsync(duo);
        }

        async Task ApplyWarAsync(TeamBattle b)
        {
            if (b.B == null)
            {
                await Shared<Guild>(GuildsCollection, b.A.TeamId, g => { if (g != null && g.ActiveWar == b.Id) g.ActiveWar = null; return g; });
                return;
            }
            var ga = await Shared<Guild>(GuildsCollection, b.A.TeamId);
            var gb = await Shared<Guild>(GuildsCollection, b.B.TeamId);
            int eloA = ga?.WarElo ?? b.A.Elo, eloB = gb?.WarElo ?? b.B.Elo;
            int newA = Elo.NewRating(eloA, eloB, b.Result, 50);
            int newB = Elo.NewRating(eloB, eloA, DuelResolver.Invert(b.Result), 50);
            await Shared<TeamBattle>(BattlesCollection, b.Id, x => { x.EloDeltaA = newA - eloA; x.EloDeltaB = newB - eloB; return x; });
            await SettleWarAsync(b, true, newA);
            await SettleWarAsync(b, false, newB);
        }

        async Task SettleWarAsync(TeamBattle b, bool sideA, int elo)
        {
            var side = sideA ? b.A : b.B;
            var result = sideA ? b.Result : DuelResolver.Invert(b.Result);
            // Race wins, runner by runner: points for the runner and the guild.
            var raceWins = new Dictionary<string, int>();
            for (int k = 0; k < b.Slots; k++)
            {
                int r = b.SlotResults[k];
                bool won = sideA ? r == (int)DuelResult.Win : r == (int)DuelResult.Loss;
                if (!won) continue;
                raceWins.TryGetValue(side.Order[k], out int n);
                raceWins[side.Order[k]] = n + 1;
            }
            int bonus = result == DuelResult.Win ? TeamConfig.WarWinBonusPerSlot * b.Slots : 0;
            var guild = await Shared<Guild>(GuildsCollection, side.TeamId, g =>
            {
                if (g == null) return null;
                g.WarElo = elo;
                if (result == DuelResult.Win) g.WarWins++;
                else if (result == DuelResult.Loss) g.WarLosses++;
                else g.WarDraws++;
                if (g.ActiveWar == b.Id) g.ActiveWar = null;
                g.RecentWars.Remove(b.Id);
                g.RecentWars.Insert(0, b.Id);
                if (g.RecentWars.Count > RecentKept) g.RecentWars.RemoveRange(RecentKept, g.RecentWars.Count - RecentKept);
                foreach (var kv in raceWins)
                {
                    var m = g.Member(kv.Key);
                    if (m != null) m.Points += kv.Value * TeamConfig.PointsPerWarRaceWin;
                    g.Points += kv.Value * TeamConfig.PointsPerWarRaceWin;
                }
                g.Points += bonus;
                return g;
            });
            if (guild != null) await IndexGuildAsync(guild);
            if (result == DuelResult.Win)
                foreach (var id in side.Order.Distinct())
                    if (IsBot?.Invoke(id) != true)
                        await Update(id, d => d.Seals += TeamConfig.WarWinSealsPerRunner);
        }

        // ------------------------------------------------------------------ battle runs

        /// <summary>
        /// Démarre la manche de ce joueur dans un combat : son tombeau, et le fantôme adverse si son vis-à-vis a déjà couru.
        /// La course est ensuite envoyée par SubmitRun, comme un duel.
        /// </summary>
        public async Task<FindDuelResponse> StartBattleRunAsync(string me, string battleId, int generatorVersion)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new FindDuelResponse { Error = "OUTDATED" };
            var battle = await RefreshBattleAsync(battleId);
            if (battle == null) return new FindDuelResponse { Error = "UNKNOWN" };
            if (battle.GeneratorVersion != generatorVersion) return new FindDuelResponse { Error = "OUTDATED" };

            var pending = await _store.GetPendingAsync(me);
            if (pending != null && pending.BattleId == battleId && NowMs - pending.CreatedAtUnixMs < PvpConfig.PendingDuelLifetimeMinutes * 60_000L)
                return new FindDuelResponse { MatchId = pending.MatchId, Seed = pending.Seed, Ghost = pending.Ghost, BattleId = battleId, Slot = pending.Slot,
                                              MyElo = MySide(battle, me)?.Elo ?? 0 };
            if (pending != null) await DropPendingAsync(me, pending);

            battle = await Shared<TeamBattle>(BattlesCollection, battleId);
            int slot = TeamLogic.NextSlot(battle, me);
            if (slot < 0) return new FindDuelResponse { Error = "NOT_YOUR_TURN" };
            bool sideA = TeamLogic.SideOf(battle, me) == true;
            var mySide = sideA ? battle.A : battle.B;
            var other = sideA ? battle.B : battle.A;
            var theirRun = other.Runs[slot];
            GhostRun ghost = theirRun == null ? null : new GhostRun
            {
                GhostId = battleId + "_" + slot, PlayerId = theirRun.PlayerId, PlayerName = theirRun.PlayerName, Elo = other.Elo,
                Seed = battle.Seeds[slot], GeneratorVersion = battle.GeneratorVersion, Outcome = theirRun.Outcome, TimeMs = theirRun.TimeMs,
                Progress = theirRun.Progress, Inputs = theirRun.Inputs ?? new List<RunInput>(), CreatedAtUnixMs = theirRun.RunAtUnixMs, Look = theirRun.Look,
            };
            var duel = new PendingDuel { MatchId = NewId(), Seed = battle.Seeds[slot], Ghost = ghost, CreatedAtUnixMs = NowMs, BattleId = battleId, Slot = slot };
            await _store.SetPendingAsync(me, duel);
            return new FindDuelResponse { MatchId = duel.MatchId, Seed = duel.Seed, Ghost = ghost, MyElo = mySide.Elo, BattleId = battleId, Slot = slot };
        }

        static BattleSide MySide(TeamBattle b, string me)
        {
            var a = TeamLogic.SideOf(b, me);
            return a == null ? null : a.Value ? b.A : b.B;
        }

        /// <summary>Une course commencée puis délaissée : un duel compte comme un abandon, une manche aussi.</summary>
        async Task DropPendingAsync(string me, PendingDuel pending)
        {
            var forfeit = new RunSubmission { MatchId = pending.MatchId, Outcome = RunOutcome.Abandoned };
            if (pending.BattleId != null) await ResolveBattleRunAsync(me, pending, forfeit, null);
            else await ResolveAsync(me, pending, forfeit, null);
            await _store.SetPendingAsync(me, null);
        }

        /// <summary>La course d'une manche est arrivée : rangée dans le combat, la manche (et peut-être le combat) décidée.</summary>
        async Task<SubmitRunResponse> ResolveBattleRunAsync(string me, PendingDuel pending, RunSubmission run, string playerName)
        {
            bool recorded = false;
            await Shared<TeamBattle>(BattlesCollection, pending.BattleId, b =>
            {
                recorded = false;
                if (b == null || b.Finished) return b;
                var sideA = TeamLogic.SideOf(b, me);
                if (sideA == null) return b;
                var side = sideA.Value ? b.A : b.B;
                if (pending.Slot < 0 || pending.Slot >= b.Slots || side.Runs[pending.Slot] != null || b.SlotResults[pending.Slot] >= 0) return b;
                TeamLogic.Record(b, sideA.Value, pending.Slot, new SlotRun
                {
                    PlayerId = me, PlayerName = playerName ?? side.Names[pending.Slot], Look = run.Look, Outcome = run.Outcome,
                    TimeMs = run.TimeMs, Progress = run.Progress, Inputs = run.Inputs ?? new List<RunInput>(), RunAtUnixMs = NowMs,
                }, NowMs);
                recorded = true;
                return b;
            });
            var battle = await RunBotsAsync(pending.BattleId);
            if (battle == null) return new SubmitRunResponse { Error = "NO_PENDING_DUEL" };
            bool mineA = TeamLogic.SideOf(battle, me) == true;
            var mySide = mineA ? battle.A : battle.B;
            int slotResult = battle.SlotResults[pending.Slot];
            int delta = battle.Applied ? (mineA ? battle.EloDeltaA : battle.EloDeltaB) : 0;
            var data = await Update(me);
            return new SubmitRunResponse
            {
                Resolved = recorded && slotResult >= 0,
                Result = slotResult < 0 ? DuelResult.Draw : mineA ? (DuelResult)slotResult : DuelResolver.Invert((DuelResult)slotResult),
                EloBefore = mySide.Elo, EloAfter = mySide.Elo + delta, League = Leagues.FromElo(data.Elo), Seals = data.Seals,
                Battle = TeamLogic.ViewFor(battle, mySide.TeamId),
            };
        }

        /// <summary>Un combat vu par ce joueur (pour revoir les manches décidées), null s'il n'y court pas et n'est pas de l'équipe.</summary>
        public async Task<TeamBattle> GetBattleAsync(string me, string battleId)
        {
            var b = await RefreshBattleAsync(battleId);
            if (b == null) return null;
            var d = await Update(me);
            string team = TeamLogic.SideOf(b, me) is bool a ? (a ? b.A.TeamId : b.B.TeamId)
                        : d.GuildId != null && TeamLogic.SideOfTeam(b, d.GuildId) != null ? d.GuildId
                        : d.Duos.FirstOrDefault(id => TeamLogic.SideOfTeam(b, id) != null);
            return team == null ? null : TeamLogic.ViewFor(b, team);
        }

        // ------------------------------------------------------------------ guilds

        public async Task<GuildResponse> CreateGuildAsync(string me, string myName, string name, string tag)
        {
            string error = TeamLogic.CheckGuildName(name, tag);
            if (error != null) return new GuildResponse { Error = error };
            name = name.Trim();
            tag = tag.Trim().ToUpperInvariant();
            var d = await Update(me);
            if (d.GuildId != null) return new GuildResponse { Error = "IN_GUILD" };
            var index = await Shared<List<GuildSummary>>(IndexCollection, GuildIndexKey) ?? new List<GuildSummary>();
            if (index.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(g.Tag, tag, StringComparison.OrdinalIgnoreCase)))
                return new GuildResponse { Error = "TAKEN" };

            var guild = new Guild { Id = NewId(), Name = name, Tag = tag, CreatedAtUnixMs = NowMs };
            guild.Members.Add(new GuildMember { PlayerId = me, Name = myName ?? "", Role = GuildRole.Leader, JoinedAtUnixMs = NowMs });
            await Shared<Guild>(GuildsCollection, guild.Id, _ => guild);
            await Update(me, x => x.GuildId = guild.Id);
            await IndexGuildAsync(guild);
            return await GetGuildAsync(me);
        }

        public async Task<GuildSearchResponse> SearchGuildsAsync(string query, int limit)
        {
            var index = await Shared<List<GuildSummary>>(IndexCollection, GuildIndexKey) ?? new List<GuildSummary>();
            query = query?.Trim() ?? "";
            return new GuildSearchResponse
            {
                Guilds = index.Where(g => query.Length == 0 || g.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                                                            || g.Tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                              .OrderByDescending(g => g.Points).ThenBy(g => g.Name, StringComparer.Ordinal).Take(limit).ToList(),
            };
        }

        public async Task<GuildSearchResponse> GetGuildBoardAsync(int limit) => await SearchGuildsAsync("", limit);

        public async Task<GuildResponse> JoinGuildAsync(string me, string myName, string guildId)
        {
            var d = await Update(me);
            if (d.GuildId != null) return new GuildResponse { Error = "IN_GUILD" };
            string error = null;
            var guild = await Shared<Guild>(GuildsCollection, guildId, g =>
            {
                error = null;
                if (g == null || g.Members.Count == 0) { error = "UNKNOWN"; return g; }
                if (g.Members.Count >= TeamConfig.GuildMaxMembers) { error = "FULL"; return g; }
                if (g.Member(me) == null) g.Members.Add(new GuildMember { PlayerId = me, Name = myName ?? "", Role = GuildRole.Member, JoinedAtUnixMs = NowMs });
                return g;
            });
            if (error != null) return new GuildResponse { Error = error };
            await Update(me, x => x.GuildId = guildId);
            await IndexGuildAsync(guild);
            return await GetGuildAsync(me);
        }

        public async Task<GuildResponse> LeaveGuildAsync(string me)
        {
            var d = await Update(me);
            if (d.GuildId == null) return new GuildResponse { Error = "NO_GUILD" };
            string guildId = d.GuildId;
            var guild = await Shared<Guild>(GuildsCollection, guildId, g =>
            {
                if (g == null) return null;
                var member = g.Member(me);
                if (member == null) return g;
                g.Members.Remove(member);
                // A guild always has a leader: the oldest officer, else the oldest member.
                if (member.Role == GuildRole.Leader && g.Members.Count > 0)
                    g.Members.OrderByDescending(m => m.Role).ThenBy(m => m.JoinedAtUnixMs).First().Role = GuildRole.Leader;
                return g;
            });
            await Update(me, x => x.GuildId = null);
            if (guild == null || guild.Members.Count == 0)
                await Shared<List<GuildSummary>>(IndexCollection, GuildIndexKey, list =>
                {
                    list ??= new List<GuildSummary>();
                    list.RemoveAll(s => s.Id == guildId);
                    return list;
                });
            else await IndexGuildAsync(guild);
            return new GuildResponse { Me = me };
        }

        /// <summary>La guilde du joueur, sa guerre en cours et les récentes ; donne au passage les skins des paliers atteints.</summary>
        public async Task<GuildResponse> GetGuildAsync(string me)
        {
            var d = await Update(me);
            if (d.GuildId == null) return new GuildResponse { Me = me };
            var guild = await Shared<Guild>(GuildsCollection, d.GuildId);
            if (guild == null || guild.Member(me) == null)
            {
                await Update(me, x => x.GuildId = null);
                return new GuildResponse { Me = me };
            }
            var response = new GuildResponse { Me = me };
            if (guild.ActiveWar != null)
            {
                var war = await RefreshBattleAsync(guild.ActiveWar);
                if (war != null) response.Wars.Add(TeamLogic.ViewFor(war, guild.Id));
                guild = await Shared<Guild>(GuildsCollection, guild.Id) ?? guild;
            }
            foreach (var id in guild.RecentWars.Take(3))
            {
                var war = await Shared<TeamBattle>(BattlesCollection, id);
                if (war != null && response.Wars.All(w => w.Id != war.Id)) response.Wars.Add(TeamLogic.ViewFor(war, guild.Id));
            }
            var rewards = TeamLogic.GuildRewards(guild.Points);
            await Update(me, x =>
            {
                response.NewRewards.Clear();
                foreach (var r in rewards)
                    if (!x.UnlockedRewards.Contains(r))
                    {
                        x.UnlockedRewards.Add(r);
                        response.NewRewards.Add(r);
                    }
            });
            response.Guild = guild;
            return response;
        }

        /// <summary>Le chef nomme (ou retire) un officier.</summary>
        public async Task<GuildResponse> SetGuildRoleAsync(string me, string memberId, bool officer)
        {
            var d = await Update(me);
            if (d.GuildId == null) return new GuildResponse { Error = "NO_GUILD" };
            string error = null;
            await Shared<Guild>(GuildsCollection, d.GuildId, g =>
            {
                error = null;
                var boss = g?.Member(me);
                var target = g?.Member(memberId);
                if (boss == null || target == null) error = "UNKNOWN";
                else if (boss.Role != GuildRole.Leader || target.Role == GuildRole.Leader) error = "RIGHTS";
                else target.Role = officer ? GuildRole.Officer : GuildRole.Member;
                return g;
            });
            return error != null ? new GuildResponse { Error = error } : await GetGuildAsync(me);
        }

        /// <summary>Le chef ou un officier exclut un membre de rang inférieur.</summary>
        public async Task<GuildResponse> KickGuildMemberAsync(string me, string memberId)
        {
            var d = await Update(me);
            if (d.GuildId == null) return new GuildResponse { Error = "NO_GUILD" };
            string error = null;
            var guild = await Shared<Guild>(GuildsCollection, d.GuildId, g =>
            {
                error = null;
                var boss = g?.Member(me);
                var target = g?.Member(memberId);
                if (boss == null || target == null) error = "UNKNOWN";
                else if (boss.Role < GuildRole.Officer || target.Role >= boss.Role) error = "RIGHTS";
                else g.Members.Remove(target);
                return g;
            });
            if (error != null) return new GuildResponse { Error = error };
            await Update(memberId, x => { if (x.GuildId == guild.Id) x.GuildId = null; });
            await IndexGuildAsync(guild);
            return await GetGuildAsync(me);
        }

        /// <summary>
        /// Le chef ou un officier lance une guerre de <paramref name="size"/> manches avec cet ordre de passage (un membre
        /// par manche), contre la guilde d'Elo de guerre le plus proche qui attend.
        /// </summary>
        public async Task<GuildResponse> StartWarAsync(string me, int size, List<string> order, int generatorVersion)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new GuildResponse { Error = "OUTDATED" };
            var d = await Update(me);
            if (d.GuildId == null) return new GuildResponse { Error = "NO_GUILD" };
            var guild = await Shared<Guild>(GuildsCollection, d.GuildId);
            var boss = guild?.Member(me);
            if (boss == null) return new GuildResponse { Error = "NO_GUILD" };
            if (boss.Role < GuildRole.Officer) return new GuildResponse { Error = "RIGHTS" };
            if (guild.ActiveWar != null)
            {
                var current = await RefreshBattleAsync(guild.ActiveWar);
                if (current != null && !current.Finished) return new GuildResponse { Error = "BUSY" };
            }
            string error = TeamLogic.CheckWarOrder(guild, order, size);
            if (error != null) return new GuildResponse { Error = error };
            var side = TeamLogic.NewSide(guild.Id, $"[{guild.Tag}] {guild.Name}", guild.WarElo, order, id => guild.Member(id)?.Name);
            var war = await MatchAsync(BattleKind.GuildWar, size, "war" + size, side, guild.Members.Select(m => m.PlayerId).ToList());
            await Shared<Guild>(GuildsCollection, guild.Id, g => { g.ActiveWar = war.Id; return g; });
            return await GetGuildAsync(me);
        }

        Task IndexGuildAsync(Guild g) =>
            Shared<List<GuildSummary>>(IndexCollection, GuildIndexKey, list =>
            {
                list ??= new List<GuildSummary>();
                list.RemoveAll(s => s.Id == g.Id);
                if (g.Members.Count > 0)
                    list.Add(new GuildSummary
                    {
                        Id = g.Id, Name = g.Name, Tag = g.Tag, MemberCount = g.Members.Count, Points = g.Points,
                        WarElo = g.WarElo, WarWins = g.WarWins, WarLosses = g.WarLosses,
                    });
                return list;
            });

        /// <summary>Points de guilde gagnés par un joueur (sa victoire en duel, en 2v2…), s'il est dans une guilde.</summary>
        async Task AddGuildPointsForAsync(string playerId, int points)
        {
            if (IsBot?.Invoke(playerId) == true) return;
            var all = await _store.ReadPlayersAsync(new[] { playerId });
            if (all.TryGetValue(playerId, out var d)) await AddGuildPointsAsync(d.GuildId, playerId, points);
        }

        async Task AddGuildPointsAsync(string guildId, string playerId, int points)
        {
            if (string.IsNullOrEmpty(guildId) || points <= 0) return;
            var guild = await Shared<Guild>(GuildsCollection, guildId, g =>
            {
                var m = g?.Member(playerId);
                if (m == null) return g;
                m.Points += points;
                g.Points += points;
                return g;
            });
            if (guild != null) await IndexGuildAsync(guild);
        }
    }
}
