// Mummy Rush PvP — côté serveur du duel en direct : le duel est créé quand deux joueurs de la même ligue se sont trouvés
// (ou contre un bot de la ligue après une minute), chacun envoie sa course à la fin, le serveur les rejoue et juge.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;

namespace MummyEscape.Pvp
{
    public sealed partial class PvpServer
    {
        public const string LiveCollection = "pvp_live";

        static string LiveId(string matchKey) => "live_" + new string((matchKey ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(60).ToArray());

        /// <summary>
        /// Every name a phone sends goes through here (<see cref="ChatFilter.Name"/>: no rich-text tags, controls, links or
        /// swearing, 24 characters): it shows in the rivals' screens, replays and ladders. Null stays null (callers fall back).
        /// </summary>
        public static string CleanName(string name) => name == null ? null : ChatFilter.Name(name);

        async Task<LiveDuelist> DuelistAsync(LiveDuelist claimed, int elo) => new LiveDuelist
        {
            PlayerId = claimed.PlayerId,
            Name = ChatFilter.Name(claimed.Name),
            Elo = elo,
            Look = await VerifiedLookAsync(claimed.Look, claimed.PlayerId),
        };

        async Task<string> LiveRefusalAsync(string me, int generatorVersion, string matchKey, int draftVersion)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion || draftVersion != PvpDraft.Version) return "OUTDATED";
            if (string.IsNullOrEmpty(matchKey)) return "UNKNOWN";
            if (!await DuelsOpenAsync(me)) return "LOCKED";
            return null;
        }

        /// <summary>
        /// Crée le duel de deux joueurs qui se sont trouvés (l'hôte du salon l'appelle ; <paramref name="matchKey"/> = le salon,
        /// donc relancer l'appel rend le même duel). Deux joueurs de la même ligue, ou de deux ligues voisines (le jeu élargit sa
        /// recherche après 30 s). Le duel commence par le tirage des cartes (<see cref="BanLiveDuelAsync"/>) ;
        /// <paramref name="draftVersion"/> : un jeu qui ne le connaît pas reçoit « OUTDATED ». Seul l'appelant est montré
        /// (<see cref="LiveDuelist.Confirmed"/>) : l'autre l'est à son premier appel pour ce duel, sinon le duel est annulé.
        /// Refusé à cause d'un des deux : <see cref="LiveDuelResponse.Refused"/> le nomme, l'autre peut chercher à nouveau.
        /// </summary>
        public async Task<LiveDuelResponse> StartLiveDuelAsync(string me, int generatorVersion, string matchKey, LiveDuelist a, LiveDuelist b,
                                                               int draftVersion = PvpDraft.Version)
        {
            string refusal = await LiveRefusalAsync(me, generatorVersion, matchKey, draftVersion);
            if (refusal != null) return new LiveDuelResponse { Error = refusal, Refused = me };
            if (string.IsNullOrEmpty(a?.PlayerId) || string.IsNullOrEmpty(b?.PlayerId) || a.PlayerId == b.PlayerId
                || (a.PlayerId != me && b.PlayerId != me) || a.PlayerId.StartsWith("bot_") || b.PlayerId.StartsWith("bot_"))
                return new LiveDuelResponse { Error = "UNKNOWN" };
            string id = LiveId(matchKey);
            var existing = await Shared<LiveDuel>(LiveCollection, id);
            if (existing != null)
                return existing.SideOf(me) != null ? new LiveDuelResponse { Match = await ConfirmLiveAsync(id, me) } : new LiveDuelResponse { Error = "UNKNOWN" };

            string rival = a.PlayerId == me ? b.PlayerId : a.PlayerId;
            if (!await DuelsOpenAsync(rival)) return new LiveDuelResponse { Error = "LOCKED", Refused = rival };
            var dataA = await Update(a.PlayerId);
            var dataB = await Update(b.PlayerId);
            League leagueA = Leagues.FromElo(dataA.Elo), leagueB = Leagues.FromElo(dataB.Elo);
            if (Math.Abs((int)leagueA - (int)leagueB) > 1)
            {
                // The side whose phone believed in another league (an old profile) is at fault; unknown: both stop.
                bool staleA = Leagues.FromElo(a.Elo) != leagueA, staleB = Leagues.FromElo(b.Elo) != leagueB;
                return new LiveDuelResponse { Error = "DIVISION", Refused = staleA == staleB ? null : staleA ? a.PlayerId : b.PlayerId };
            }
            var (spent, broke) = await SpendEnergyForAsync(new[] { a.PlayerId, b.PlayerId });
            if (spent != null) return new LiveDuelResponse { Error = spent, Refused = broke };
            var duelistA = await DuelistAsync(a, dataA.Elo);
            var duelistB = await DuelistAsync(b, dataB.Elo);
            if (a.PlayerId == me) duelistA.Confirmed = true; else duelistB.Confirmed = true;
            var duel = await Shared<LiveDuel>(LiveCollection, id, d => d ?? new LiveDuel
            {
                Id = id,
                GeneratorVersion = DifficultyTable.GeneratorVersion,
                A = duelistA,
                B = duelistB,
                // Two leagues: the maps of the higher one (as a 2v2 plays its best player's division).
                Draft = PvpDraft.Open(_newSeed(), (League)Math.Max((int)leagueA, (int)leagueB), new[] { (a.PlayerId, 0, false), (b.PlayerId, 1, false) }),
                CreatedAtUnixMs = NowMs,
            });
            return new LiveDuelResponse { Match = duel };
        }

        /// <summary>Le joueur s'est montré pour ce duel (rien d'écrit s'il l'était déjà).</summary>
        Task<LiveDuel> ConfirmLiveAsync(string matchId, string me) =>
            Shared<LiveDuel>(LiveCollection, matchId, d =>
            {
                var side = d?.SideOf(me);
                if (side != null) side.Confirmed = true;
                return d;
            });

        /// <summary>Un bot ne se montre pas : il est toujours là.</summary>
        static bool Shown(LiveDuelist side) => side == null || side.Bot || side.Confirmed;

        /// <summary>
        /// Le jeu commence à chercher un duel en direct : l'heure du serveur, pour qu'un bot n'arrive qu'après une vraie minute
        /// de recherche (<see cref="StartLiveBotDuelAsync"/>).
        /// </summary>
        public async Task<TeamActionResponse> StartLiveSearchAsync(string me)
        {
            long now = NowMs;
            await Update(me, d => d.LiveSearchSinceUnixMs = now);
            return new TeamActionResponse { Ok = true };
        }

        /// <summary>
        /// Le duel, pour l'invité du salon (l'hôte l'a créé) et pendant le tirage : seuls ses deux joueurs le lisent. Ferme le
        /// tirage quand son heure est passée.
        /// </summary>
        public async Task<LiveDuelResponse> GetLiveDuelAsync(string me, string matchId)
        {
            var duel = await Shared<LiveDuel>(LiveCollection, matchId ?? "");
            if (duel?.SideOf(me) == null) return new LiveDuelResponse { Error = "UNKNOWN" };
            return new LiveDuelResponse { Match = await DraftLiveAsync(duel.Id, me) };
        }

        /// <summary>
        /// Le joueur bannit une carte du tirage (une fois ; -1 : pas de choix à temps, le hasard bannit pour lui). Le tirage
        /// ferme dès que les deux ont voté ; le duel rendu donne alors le tombeau joué (<see cref="LiveDuel.Seed"/>).
        /// </summary>
        public async Task<LiveDuelResponse> BanLiveDuelAsync(string me, string matchId, int map)
        {
            var duel = await Shared<LiveDuel>(LiveCollection, matchId ?? "");
            if (duel?.SideOf(me) == null) return new LiveDuelResponse { Error = "UNKNOWN" };
            return new LiveDuelResponse { Match = await DraftLiveAsync(duel.Id, me, map, vote: true) };
        }

        /// <summary>
        /// Le joueur se montre, vote s'il y a lieu, puis la fin du tirage si elle est due : le tombeau, et la course du bot sur
        /// lui. Rien d'écrit quand rien n'a changé (les deux téléphones relisent le tirage chaque seconde).
        /// </summary>
        Task<LiveDuel> DraftLiveAsync(string matchId, string me, int map = -1, bool vote = false) =>
            Shared<LiveDuel>(LiveCollection, matchId, d =>
            {
                var side = d?.SideOf(me);
                if (side != null) side.Confirmed = true;
                if (d?.Draft == null || d.Draft.Closed) return d;
                if (vote) PvpDraft.Vote(d.Draft, me, map, NowMs);
                if (!PvpDraft.TryClose(d.Draft, NowMs)) return d;
                d.Seed = d.Draft.Maps[d.Draft.Pick];
                if (d.B.Bot)
                {
                    // Même graine que l'Elo montré au tirage (PvpBots.EloFor) : le même bot, qui court maintenant le tombeau choisi.
                    var bot = PvpBots.Make(new Random(d.Draft.Seed), d.A.Elo, d.Seed, d.B.PlayerId, NowMs);
                    bot.PlayerName = d.B.Name;
                    bot.Look = d.B.Look;
                    d.BotRun = bot;
                    d.B.Run = new RunSubmission { MatchId = d.Id, Outcome = bot.Outcome, TimeMs = bot.TimeMs, Progress = bot.Progress, Inputs = bot.Inputs };
                }
                return d;
            });

        /// <summary>
        /// Personne de la ligue en une minute et le joueur accepte les bots : un bot de sa ligue, qui bannit aussi, puis court
        /// d'avance. <paramref name="protocol"/> ≥ <see cref="LiveDuelConfig.Protocol"/> (jeu 0.1.15 et suivants) : il faut une
        /// recherche commencée (<see cref="StartLiveSearchAsync"/>) depuis presque une minute, sinon « SEARCH » ; chaque bot en
        /// consomme une. Un jeu plus ancien (protocole 0) n'appelle pas StartLiveSearch : il n'est pas vérifié.
        /// </summary>
        public async Task<LiveDuelResponse> StartLiveBotDuelAsync(string me, int generatorVersion, string matchKey, LiveDuelist mine,
                                                                  int draftVersion = PvpDraft.Version, int protocol = 0)
        {
            string refusal = await LiveRefusalAsync(me, generatorVersion, matchKey, draftVersion);
            if (refusal != null) return new LiveDuelResponse { Error = refusal };
            if (mine == null || mine.PlayerId != me) return new LiveDuelResponse { Error = "UNKNOWN" };
            string id = LiveId(matchKey);
            var existing = await Shared<LiveDuel>(LiveCollection, id);
            if (existing != null) return existing.SideOf(me) != null ? new LiveDuelResponse { Match = existing } : new LiveDuelResponse { Error = "UNKNOWN" };

            // shortcut: protocol 0 (0.1.14) skips the search check; require it once those phones are gone (ClientVersion.Minimum).
            if (protocol >= LiveDuelConfig.Protocol)
            {
                long now = NowMs;
                bool searched = false;
                await Update(me, d =>
                {
                    long since = d.LiveSearchSinceUnixMs;
                    searched = since > 0 && since <= now && now - since >= PvpConfig.BotFallbackMs - LiveDuelConfig.BotSearchSlackMs;
                    if (searched) d.LiveSearchSinceUnixMs = 0;
                });
                if (!searched) return new LiveDuelResponse { Error = "SEARCH" };
            }
            string spent = await SpendEnergyAsync(new[] { me });
            if (spent != null) return new LiveDuelResponse { Error = spent };
            var data = await Update(me);
            int seed = _newSeed();
            string botId = "bot_" + seed;
            var duelistMine = await DuelistAsync(mine, data.Elo);
            duelistMine.Confirmed = true;
            var duel = await Shared<LiveDuel>(LiveCollection, id, d => d ?? new LiveDuel
            {
                Id = id,
                GeneratorVersion = DifficultyTable.GeneratorVersion,
                A = duelistMine,
                // Sa course viendra à la fin du tirage (DraftLiveAsync), sur le tombeau choisi.
                B = new LiveDuelist { PlayerId = botId, Name = BotName?.Invoke(botId) ?? PvpBots.NameOf(botId), Elo = PvpBots.EloFor(new Random(seed), data.Elo), Look = PvpBots.LookOf(botId, NowMs), Bot = true },
                Draft = PvpDraft.Open(seed, Leagues.FromElo(data.Elo), new[] { (me, 0, false), (botId, 1, true) }),
                CreatedAtUnixMs = NowMs,
            });
            return new LiveDuelResponse { Match = duel };
        }

        /// <summary>Durée de l'aperçu du tombeau d'un duel (7 s par étage, à répartir entre eux, après 350 ms de mise en place).</summary>
        static long PreviewMs(int seed)
        {
            return Arena(seed).PreviewSeconds * 1000L + 350;
        }

        /// <summary>
        /// Reçoit la course d'un joueur. Le serveur la rejoue sur le tombeau et ne retient que ce qu'il a vu ; une course qui
        /// arrive bien plus tard que son temps ne le permet (actions réhorodatées) est refusée. Dès que les deux courses sont là,
        /// ou une minute après la première, le duel est jugé.
        /// </summary>
        public async Task<SubmitRunResponse> SubmitLiveDuelAsync(string me, string matchId, RunSubmission run, string playerName)
        {
            var duel = await Shared<LiveDuel>(LiveCollection, matchId);
            if (duel == null || duel.SideOf(me) == null || run == null) return new SubmitRunResponse { Error = "UNKNOWN" };
            // Une course envoyée vaut fin du tirage pour ce joueur (s'il quitte pendant le tirage, le hasard bannit pour lui).
            duel = await DraftLiveAsync(matchId, me, -1, vote: true);
            // Encore au tirage : aucun tombeau sur lequel rejouer, seul un abandon a un sens.
            bool drafting = duel.Draft?.Closed == false;
            string error = null;
            RunSubmission verified;
            if (run.Outcome == RunOutcome.Abandoned) verified = new RunSubmission { MatchId = matchId, Outcome = RunOutcome.Abandoned };
            else
            {
                verified = !drafting && RunValidator.IsPlausible(run, out _) ? RunReplay.Verify(Arena(duel.Seed), run) : null;
                if (verified != null && verified.Outcome != RunOutcome.TimedOut)
                {
                    long started = duel.Draft?.ClosedUnixMs ?? duel.CreatedAtUnixMs; // la course part après le tirage
                    long sinceStart = NowMs - started - PreviewMs(duel.Seed) - LiveDuelConfig.VsScreenMs - LiveDuelConfig.ReadyWaitMs;
                    if (verified.TimeMs < sinceStart - LiveDuelConfig.LateSlackMs) verified = null;
                }
                if (verified == null)
                {
                    error = "INVALID_RUN";
                    verified = new RunSubmission { MatchId = matchId, Outcome = RunOutcome.Abandoned };
                }
            }
            verified.Look = await VerifiedLookAsync(run.Look, me);
            if (Titles.Get(verified.Look?.Title)?.IsDuel == true)
            {
                var mine = await _store.ReadPlayersAsync(new[] { me });
                mine.TryGetValue(me, out var data);
                verified.Look.Title = Titles.Check(verified.Look.Title, data);
            }
            await Shared<LiveDuel>(LiveCollection, matchId, d =>
            {
                if (d == null || d.Settled) return d;
                var side = d.SideOf(me);
                if (side.Run == null) side.Run = verified;
                if (verified.Look != null) side.Look = verified.Look;
                if (!string.IsNullOrWhiteSpace(playerName)) side.Name = CleanName(playerName);
                if (d.FirstSubmitUnixMs == 0) d.FirstSubmitUnixMs = NowMs;
                return d;
            });
            var response = await LiveDuelResultAsync(me, matchId);
            response.Error = error ?? response.Error;
            return response;
        }

        /// <summary>
        /// Le verdict pour ce joueur ; <see cref="SubmitRunResponse.Resolved"/> faux tant que l'adversaire n'a rien envoyé (une
        /// minute au plus), et tant que le serveur n'a pas fini d'appliquer le verdict (l'Elo d'après). Un camp jamais montré :
        /// duel annulé (<see cref="SubmitRunResponse.Void"/>), jugé dès <see cref="LiveDuelConfig.ConfirmWindowMs"/>.
        /// </summary>
        public async Task<SubmitRunResponse> LiveDuelResultAsync(string me, string matchId)
        {
            var duel = await ConfirmLiveAsync(matchId ?? "", me);
            if (duel == null || duel.SideOf(me) == null) return new SubmitRunResponse { Error = "UNKNOWN" };
            if (!duel.Settled)
            {
                bool both = duel.A.Run != null && duel.B.Run != null;
                bool late = duel.FirstSubmitUnixMs > 0 && NowMs - duel.FirstSubmitUnixMs >= LiveDuelConfig.SubmitWindowMs;
                bool absent = (!Shown(duel.A) || !Shown(duel.B)) && NowMs - duel.CreatedAtUnixMs >= LiveDuelConfig.ConfirmWindowMs;
                if (both || late || absent) duel = await SettleLiveAsync(matchId) ?? duel;
            }
            var side = duel.SideOf(me);
            // Judged but not applied yet (another call is writing the Elo): no verdict to show.
            if (!duel.Settled || side.EloAfter <= 0)
                return new SubmitRunResponse { Resolved = false, EloBefore = side.Elo, EloAfter = side.Elo, League = Leagues.FromElo(side.Elo) };
            if (duel.Void)
            {
                var players = await _store.ReadPlayersAsync(new[] { me });
                return new SubmitRunResponse
                {
                    Resolved = true, Void = true, Result = DuelResult.Draw, EloBefore = side.Elo, EloAfter = side.Elo,
                    League = Leagues.FromElo(side.Elo), Seals = players.TryGetValue(me, out var held) && held != null ? held.Seals : 0,
                };
            }
            bool isA = duel.A.PlayerId == me;
            var mine = isA ? duel.Result : DuelResolver.Invert(duel.Result);
            var season = BattlePass.At(_utcNow());
            var data = await Update(me, d =>
            {
                // The pass XP of the match, in the write this verdict makes anyway (once, whatever the polls).
                var w = WalletOf(d);
                AddXp(w, season, "match:" + matchId, BattlePass.MatchXp); // noloc
                if (mine == DuelResult.Win) AddXp(w, season, "win:" + matchId, BattlePass.WinBonusXp); // noloc
            });
            return new SubmitRunResponse
            {
                Resolved = true,
                Result = mine,
                EloBefore = side.Elo,
                EloAfter = side.EloAfter,
                League = Leagues.FromElo(side.EloAfter),
                SealsGained = side.SealsGained,
                Seals = data.Seals,
            };
        }

        /// <summary>
        /// Juge le duel une seule fois, puis met à jour les deux joueurs (un bot n'a rien à mettre à jour). Un camp jamais
        /// montré : duel annulé, l'énergie rendue aux deux, rien d'autre de compté.
        /// </summary>
        async Task<LiveDuel> SettleLiveAsync(string matchId)
        {
            bool mine = false;
            var duel = await Shared<LiveDuel>(LiveCollection, matchId, d =>
            {
                mine = false; // run again after a write conflict: another call may have judged it meanwhile
                if (d == null || d.Settled) return d;
                d.Settled = true;
                d.Void = !Shown(d.A) || !Shown(d.B);
                d.Result = d.Void ? DuelResult.Draw : LiveDuelJudge.Resolve(d.A.Run, d.B.Run);
                mine = true;
                return d;
            });
            if (!mine) return duel;
            if (duel.Void)
            {
                await RefundEnergyAsync(new[] { duel.A.PlayerId, duel.B.PlayerId });
                foreach (var (side, other) in new[] { (duel.A, duel.B), (duel.B, duel.A) })
                    if (IsHuman(side.PlayerId))
                        await RecordAsync(side.PlayerId, h => Remember(h, new DuelRecord
                        {
                            MatchId = duel.Id, Seed = duel.Seed, GeneratorVersion = duel.GeneratorVersion, PlayedAtUnixMs = duel.CreatedAtUnixMs,
                            Resolved = true, Void = true, Result = DuelResult.Draw, EloBefore = side.Elo, EloAfter = side.Elo, Live = true,
                            Me = RunOf(side.PlayerId, side.Name, side.Elo, side.Run ?? new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Abandoned }),
                            Rival = RunOf(other.PlayerId, other.Name, other.Elo, other.Run ?? new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Abandoned }),
                        }));
                return await Shared<LiveDuel>(LiveCollection, matchId, d =>
                {
                    if (d == null) return d;
                    d.A.EloAfter = d.A.Elo;
                    d.B.EloAfter = d.B.Elo;
                    return d;
                });
            }
            var utc = _utcNow();
            var a = await SettleDuelistAsync(duel, duel.A, duel.B, duel.Result, utc);
            var b = await SettleDuelistAsync(duel, duel.B, duel.A, DuelResolver.Invert(duel.Result), utc);
            await ForgetBoardIfChangedAsync(Math.Max(a.EloAfter, b.EloAfter), duel.A.PlayerId, duel.B.PlayerId);
            return await Shared<LiveDuel>(LiveCollection, matchId, d =>
            {
                if (d == null) return d;
                d.A.EloAfter = a.EloAfter; d.A.SealsGained = a.SealsGained;
                d.B.EloAfter = b.EloAfter; d.B.SealsGained = b.SealsGained;
                return d;
            });
        }

        async Task<LiveDuelist> SettleDuelistAsync(LiveDuel duel, LiveDuelist side, LiveDuelist other, DuelResult result, DateTime utc)
        {
            var done = new LiveDuelist { PlayerId = side.PlayerId, EloAfter = side.Elo };
            if (side.Bot) return done;
            var run = side.Run ?? new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Abandoned };
            bool abandoned = run.Outcome == RunOutcome.Abandoned;
            int rank = await WorldRankAsync(side.PlayerId);
            bool isTop100 = rank > 0 && rank <= PvpConfig.Top100Size;
            int gained = 0;
            var data = await Update(side.PlayerId, d =>
            {
                gained = abandoned ? 0 : DuelBookkeeping.RecordDuelPlayed(d, run.TimeMs, utc, isTop100);
                gained += DuelBookkeeping.ApplyResult(d, other.PlayerId, other.Elo, result, halfStake: other.Bot);
                CountDuel(d, result);
                d.Ranked = true;
            });
            done.EloAfter = data.Elo;
            done.SealsGained = gained;
            await _store.SubmitEloAsync(side.PlayerId, data.Elo, data.Country);
            if (result == DuelResult.Win) await AddGuildPointsAsync(data.GuildId, side.PlayerId, TeamConfig.PointsPerDuelWin);
            // Les deux courses dans l'historique : de quoi revoir le duel (et le signaler).
            var otherRun = other.Run ?? new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Abandoned };
            await RecordAsync(side.PlayerId, h => Remember(h, new DuelRecord
            {
                MatchId = duel.Id, Seed = duel.Seed, GeneratorVersion = duel.GeneratorVersion, PlayedAtUnixMs = duel.CreatedAtUnixMs,
                Resolved = true, Result = result, EloBefore = side.Elo, EloAfter = data.Elo, Live = true,
                Me = RunOf(side.PlayerId, side.Name, side.Elo, run),
                Rival = RunOf(other.PlayerId, other.Name, other.Elo, otherRun),
            }));
            return done;
        }
    }
}
