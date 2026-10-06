// Mummy Rush PvP — côté serveur du 2v2 en relais : le match est créé quand quatre joueurs se sont trouvés (ou contre un
// duo de bots), chaque duo envoie son relais à la fin, le serveur les rejoue et met à jour l'Elo 2v2 des deux duos.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public sealed partial class PvpServer
    {
        public const string RelaysCollection = "pvp_relays";
        public const string QuitCollection = "pvp_quit_reports";

        static readonly Dictionary<int, RelayMap> RelayMaps = new Dictionary<int, RelayMap>();

        /// <summary>Les labyrinthes d'un relais, gardés en mémoire : les deux duos envoient leur relais l'un après l'autre.</summary>
        public static RelayMap RelayArenaFor(int seed)
        {
            lock (RelayMaps)
                if (RelayMaps.TryGetValue(seed, out var cached)) return cached;
            var map = RelayArena.Generate(seed);
            lock (RelayMaps)
            {
                if (RelayMaps.Count > 32) RelayMaps.Clear();
                RelayMaps[seed] = map;
            }
            return map;
        }

        /// <summary>
        /// Un camp tel que le serveur le croit : le duo enregistré, ses deux membres (les tenues envoyées, nettoyées), son
        /// Elo. Null si le duo n'existe pas ou si les coureurs annoncés n'en sont pas les membres.
        /// </summary>
        async Task<RelaySide> CheckedSideAsync(RelaySide claimed)
        {
            if (claimed == null || string.IsNullOrEmpty(claimed.DuoId)) return null;
            var duo = await Shared<Duo>(DuosCollection, claimed.DuoId);
            if (duo == null || duo.Members.Count != 2) return null;
            var side = new RelaySide { DuoId = duo.Id, Name = duo.Name, Elo = duo.Elo };
            foreach (var id in duo.Members)
            {
                var sent = claimed.Runners?.Find(r => r != null && r.PlayerId == id);
                if (sent == null) return null;
                side.Runners.Add(new RelayRunner { PlayerId = id, Name = duo.Names[duo.Members.IndexOf(id)], Look = Developers.Restrict(PlayerLook.Sanitize(sent.Look), id) });
            }
            return side;
        }

        /// <summary>L'Elo de duel du meilleur joueur d'un duo, et sa ligue : la division du duo.</summary>
        async Task<(int Elo, League League)> BestDuelEloAsync(RelaySide side)
        {
            var data = await _store.ReadPlayersAsync(side.Runners.Select(r => r.PlayerId).ToList());
            int best = PvpConfig.MinElo;
            foreach (var r in side.Runners)
                best = Math.Max(best, data.TryGetValue(r.PlayerId, out var d) && d != null ? d.Elo : PvpConfig.StartingElo);
            return (best, Leagues.FromElo(best));
        }

        static string RelayId(string matchKey) => "relay_" + new string((matchKey ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(60).ToArray());

        /// <summary>
        /// Crée le match de deux duos qui se sont trouvés (l'hôte du salon l'appelle ; <paramref name="matchKey"/> = le salon,
        /// donc relancer l'appel rend le même match). Les quatre joueurs reçoivent la graine par le salon.
        /// </summary>
        public async Task<RelayMatchResponse> StartRelayMatchAsync(string me, int generatorVersion, string matchKey, RelaySide a, RelaySide b)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new RelayMatchResponse { Error = "OUTDATED" };
            if (string.IsNullOrEmpty(matchKey)) return new RelayMatchResponse { Error = "UNKNOWN" };
            var sideA = await CheckedSideAsync(a);
            var sideB = await CheckedSideAsync(b);
            if (sideA == null || sideB == null || sideA.DuoId == sideB.DuoId) return new RelayMatchResponse { Error = "UNKNOWN" };
            if (sideA.Runners.Any(r => sideB.Has(r.PlayerId))) return new RelayMatchResponse { Error = "UNKNOWN" };
            if (!sideA.Has(me) && !sideB.Has(me)) return new RelayMatchResponse { Error = "UNKNOWN" };
            // Toujours la même division : celle du meilleur joueur de chaque duo (un diamant et un bronze affrontent au
            // moins un diamant). Une partie déjà créée pour ce salon est rendue telle quelle.
            var existing = await Shared<RelayMatch>(RelaysCollection, RelayId(matchKey));
            if (existing == null && (await BestDuelEloAsync(sideA)).League != (await BestDuelEloAsync(sideB)).League)
                return new RelayMatchResponse { Error = "DIVISION" };
            if (existing != null) return new RelayMatchResponse { Match = existing };
            string spent = await SpendEnergyAsync(sideA.Runners.Concat(sideB.Runners).Select(r => r.PlayerId));
            if (spent != null) return new RelayMatchResponse { Error = spent };
            return await CreateRelayAsync(RelayId(matchKey), sideA, sideB);
        }

        /// <summary>
        /// Personne en vue et le duo accepte les bots : un match contre deux joueurs simulés d'Elo proche, dont le relais est
        /// couru d'avance (les téléphones le rejouent sur leur horloge, comme un fantôme).
        /// </summary>
        public async Task<RelayMatchResponse> StartRelayBotsAsync(string me, int generatorVersion, string matchKey, RelaySide mine)
        {
            if (generatorVersion != DifficultyTable.GeneratorVersion) return new RelayMatchResponse { Error = "OUTDATED" };
            if (string.IsNullOrEmpty(matchKey)) return new RelayMatchResponse { Error = "UNKNOWN" };
            var side = await CheckedSideAsync(mine);
            if (side == null || !side.Has(me)) return new RelayMatchResponse { Error = "UNKNOWN" };
            string id = RelayId(matchKey);
            var existing = await Shared<RelayMatch>(RelaysCollection, id);
            if (existing != null) return new RelayMatchResponse { Match = existing };
            string spent = await SpendEnergyAsync(side.Runners.Select(r => r.PlayerId));
            if (spent != null) return new RelayMatchResponse { Error = spent };

            int seed = _newSeed();
            var rng = new Random(seed);
            int elo = Math.Max(PvpConfig.MinElo, side.Elo + rng.Next(-120, 121));
            // Des bots de la division du duo : ils jouent au niveau de son meilleur joueur (l'Elo 2v2 sert au calcul).
            var (best, division) = await BestDuelEloAsync(side);
            int skill = Leagues.ClampTo(best + rng.Next(-100, 101), division);
            var bots = new RelaySide { DuoId = "bot_" + seed, Elo = elo, Bot = true };
            for (int k = 0; k < 2; k++)
            {
                string botId = "bot_" + rng.Next(1, int.MaxValue);
                bots.Runners.Add(new RelayRunner { PlayerId = botId, Name = BotName?.Invoke(botId) ?? PvpBots.NameOf(botId), Bot = true });
            }
            bots.Name = bots.Runners[0].Name + " & " + bots.Runners[1].Name;
            bots.Starter = bots.Runners[rng.Next(2)].PlayerId;
            var race = RelayBots.Play(RelayArenaFor(seed), rng, skill);
            bots.Inputs = race.Inputs.Select(i => new RelayInput { Tick = i.Tick, Maze = i.Maze, Direction = i.Direction }).ToList();
            bots.Verified = RelaySummary.Of(race);
            return await CreateRelayAsync(id, side, bots, seed);
        }

        async Task<RelayMatchResponse> CreateRelayAsync(string id, RelaySide a, RelaySide b, int? seed = null)
        {
            var match = await Shared<RelayMatch>(RelaysCollection, id, m => m ?? new RelayMatch
            {
                Id = id,
                Seed = seed ?? _newSeed(),
                GeneratorVersion = DifficultyTable.GeneratorVersion,
                A = a,
                B = b,
                CreatedAtUnixMs = NowMs,
            });
            return new RelayMatchResponse { Match = match };
        }

        /// <summary>
        /// Un duo envoie son relais (chaque coéquipier peut le faire, le plus complet compte). Le serveur le rejoue ; dès que
        /// les deux duos l'ont envoyé, ou une minute après le premier, le match est jugé et l'Elo mis à jour.
        /// </summary>
        public async Task<RelayResultResponse> SubmitRelayAsync(string me, string matchId, string starter, List<RelayInput> inputs, List<string> quitters)
        {
            var match = await Shared<RelayMatch>(RelaysCollection, matchId);
            if (match == null || match.SideOf(me) == null) return new RelayResultResponse { Error = "UNKNOWN" };
            if (!match.Settled)
            {
                var map = RelayArenaFor(match.Seed);
                var mine = match.SideOf(me);
                var race = RelayRace.Verify(map, inputs);
                var myQuitters = (quitters ?? new List<string>()).Where(mine.Has).ToList();
                var summary = race == null ? new RelaySummary { Lost = true } : RelaySummary.Of(race, myQuitters.Count > 0);
                bool isA = match.A == mine;
                await Shared<RelayMatch>(RelaysCollection, matchId, m =>
                {
                    if (m == null || m.Settled) return m;
                    var side = isA ? m.A : m.B;
                    foreach (var q in myQuitters) if (!side.Quitters.Contains(q)) side.Quitters.Add(q);
                    // Le relais le plus avancé des deux coéquipiers (l'un a pu quitter avant la fin).
                    if (side.Inputs == null || (inputs?.Count ?? 0) > side.Inputs.Count || race != null && side.Verified?.Lost == true && !summary.Lost)
                    {
                        side.Inputs = inputs ?? new List<RelayInput>();
                        side.Starter = side.Has(starter) ? starter : side.Starter;
                        side.Verified = summary;
                    }
                    if (side.Quitters.Count > 0 && side.Verified != null && !side.Verified.Finished) side.Verified.Lost = true;
                    if (m.FirstSubmitUnixMs == 0) m.FirstSubmitUnixMs = NowMs;
                    return m;
                });
            }
            return await RelayResultAsync(me, matchId);
        }

        /// <summary>Le résultat d'un match pour ce joueur (Pending tant que l'autre duo peut encore envoyer son relais).</summary>
        public async Task<RelayResultResponse> RelayResultAsync(string me, string matchId)
        {
            var match = await Shared<RelayMatch>(RelaysCollection, matchId);
            if (match == null || match.SideOf(me) == null) return new RelayResultResponse { Error = "UNKNOWN" };
            if (!match.Settled)
            {
                bool both = match.A.Verified != null && match.B.Verified != null;
                bool late = match.FirstSubmitUnixMs > 0 && NowMs - match.FirstSubmitUnixMs >= RelayServerConfig.SubmitWindowMs;
                if (both || late) match = await SettleRelayAsync(matchId) ?? match;
            }
            var mine = match.SideOf(me);
            bool isA = mine == match.A;
            var result = isA ? match.Result : DuelResolver.Invert(match.Result);
            var duo = mine.Bot ? null : await Shared<Duo>(DuosCollection, mine.DuoId);
            return new RelayResultResponse
            {
                Pending = !match.Settled,
                Result = result,
                EloDelta = isA ? match.EloDeltaA : match.EloDeltaB,
                NewElo = duo?.Elo ?? mine.Elo,
                Match = match,
            };
        }

        async Task<RelayMatch> SettleRelayAsync(string matchId)
        {
            var current = await Shared<RelayMatch>(RelaysCollection, matchId);
            if (current == null || current.Settled) return current;
            var duoA = current.A.Bot ? null : await Shared<Duo>(DuosCollection, current.A.DuoId);
            var duoB = current.B.Bot ? null : await Shared<Duo>(DuosCollection, current.B.DuoId);
            int eloA = duoA?.Elo ?? current.A.Elo, eloB = duoB?.Elo ?? current.B.Elo;
            bool settledNow = false;
            var match = await Shared<RelayMatch>(RelaysCollection, matchId, m =>
            {
                if (m == null || m.Settled) return m;
                // Un duo qui n'a rien envoyé à temps a quitté la partie.
                m.Result = RelayJudge.Resolve(m.A.Verified ?? new RelaySummary { Lost = true }, m.B.Verified ?? new RelaySummary { Lost = true });
                m.EloDeltaA = Elo.NewRating(eloA, eloB, m.Result, duoA?.Matches ?? 50, m.B.Bot) - eloA;
                m.EloDeltaB = Elo.NewRating(eloB, eloA, DuelResolver.Invert(m.Result), duoB?.Matches ?? 50, m.A.Bot) - eloB;
                m.Settled = true;
                settledNow = true;
                return m;
            });
            if (!settledNow) return match;
            await RecordRelayAsync(match);
            if (duoA != null) await SettleRelayDuoAsync(duoA.Id, eloA + match.EloDeltaA, match.Result);
            if (duoB != null) await SettleRelayDuoAsync(duoB.Id, eloB + match.EloDeltaB, DuelResolver.Invert(match.Result));
            var winners = match.Result == DuelResult.Win ? match.A : match.Result == DuelResult.Loss ? match.B : null;
            if (winners != null && !winners.Bot)
                foreach (var r in winners.Runners)
                    await AddGuildPointsForAsync(r.PlayerId, TeamConfig.PointsPerDuoWin);
            return match;
        }

        async Task SettleRelayDuoAsync(string duoId, int elo, DuelResult result)
        {
            var duo = await Shared<Duo>(DuosCollection, duoId, d =>
            {
                if (d == null) return null;
                d.Elo = Math.Max(PvpConfig.MinElo, elo);
                d.Matches++;
                if (result == DuelResult.Win) d.Wins++;
                else if (result == DuelResult.Loss) d.Losses++;
                else d.Draws++;
                return d;
            });
            if (duo != null) await IndexDuoAsync(duo);
        }

        /// <summary>
        /// « Quitter et signaler » : le coéquipier qui a quitté la partie est noté (un signalement par match et par joueur,
        /// <see cref="RelayServerConfig.MaxQuitReportsPerDay"/> par jour). Pas de sanction automatique.
        /// </summary>
        public async Task<ReportResponse> ReportRelayQuitAsync(string me, string matchId, string quitterId)
        {
            var match = await Shared<RelayMatch>(RelaysCollection, matchId);
            var side = match?.SideOf(me);
            if (side == null || quitterId == me || !side.Has(quitterId)) return new ReportResponse { Error = "UNKNOWN" };
            bool allowed = false;
            await Update(me, d =>
            {
                allowed = d.ReportsToday < RelayServerConfig.MaxQuitReportsPerDay;
                if (allowed) d.ReportsToday++;
            });
            if (!allowed) return new ReportResponse { Error = "LIMIT" };
            await Shared<QuitDossier>(QuitCollection, quitterId, q =>
            {
                q = q ?? new QuitDossier { PlayerId = quitterId };
                string key = me + ":" + matchId;
                if (q.Reporters.Contains(key)) return q;
                q.Reporters.Add(key);
                if (q.Reporters.Count > 50) q.Reporters.RemoveAt(0);
                q.TotalReports++;
                q.LastReportUnixMs = NowMs;
                return q;
            });
            return new ReportResponse { Ok = true };
        }
    }
}
