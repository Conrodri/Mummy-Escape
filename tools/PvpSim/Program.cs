// Simulates a week of ghost duels: human-like bots (HumanBot) find duels, play the real duel tombs and submit their runs to
// the real PvP server logic (PvpServer on an in-memory store). Nothing touches Unity Cloud.
//   dotnet run -c Release --project tools/PvpSim [-- players days seed]
using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.PvpSim;

int players = args.Length > 0 ? int.Parse(args[0]) : 60;
int days = args.Length > 1 ? int.Parse(args[1]) : 5;
int seed = args.Length > 2 ? int.Parse(args[2]) : 2026;

var rng = new Random(seed);
var now = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
var store = new MemoryPvpStore();
int nextSeed = 1000 + seed;
var server = new PvpServer(store, () => now, () => nextSeed++);

string[] names = { "Nefer", "Khepri", "Imhotep", "Sobek", "Hatshep", "Ramses", "Bastet", "Thot", "Merit", "Ptah", "Horus", "Seth",
                   "Isis", "Osiris", "Neith", "Sekhmet", "Amon", "Hathor", "Khonsu", "Maat" };
var bots = new List<HumanBot>();
for (int i = 0; i < players; i++)
{
    // Skills spread evenly, activity from casual (1-2 duels a day) to keen (10+).
    double skill = (i + 0.5) / players;
    int activity = 1 + (int)(Math.Pow(rng.NextDouble(), 1.6) * 12);
    var bot = new HumanBot("p" + i, names[i % names.Length] + i, skill, activity, rng);
    bots.Add(bot);
    store.SoloStars[bot.Id] = 40 + rng.Next(60);
}
var byId = bots.ToDictionary(b => b.Id);

// ------------------------------------------------------------------ metrics
int duels = 0, withGhost = 0, invalid = 0, resolved = 0, upsets = 0, clearFavourites = 0, draws = 0;
var outcomes = new Dictionary<RunOutcome, int>();
var eloGaps = new List<int>();
var skillGaps = new List<double>();
var ghostAgesMin = new List<double>();
var finishTimes = new List<int>();
var invalidReasons = new Dictionary<string, int>();
long lapses = 0, misswipes = 0, confusions = 0, bumps = 0, actions = 0;
var perArenaAct = new Dictionary<int, (int runs, int finished, double time)>();
var trapRuns = new Dictionary<string, (int runs, int finished, double msPerPar)>();
var timer = System.Diagnostics.Stopwatch.StartNew();

for (int day = 0; day < days; day++)
{
    // Each player comes back once to three times a day and plays a few duels in a row.
    var sessions = new List<(DateTime at, HumanBot bot, int count)>();
    var dayStart = now.Date.AddHours(8);
    foreach (var bot in bots)
    {
        int left = Math.Max(0, bot.Activity + rng.Next(-1, 2));
        while (left > 0)
        {
            int n = Math.Min(left, 1 + rng.Next(4));
            sessions.Add((dayStart.AddMinutes(rng.Next(0, 15 * 60)), bot, n));
            left -= n;
        }
    }
    sessions.Sort((a, b) => a.at.CompareTo(b.at));

    foreach (var (at, bot, count) in sessions)
    {
        now = at;
        for (int k = 0; k < count; k++)
        {
            var find = server.FindDuelAsync(bot.Id, DifficultyTable.GeneratorVersion).Result;
            if (find.Error != null) { Console.WriteLine("FindDuel: " + find.Error); continue; }
            duels++;
            var level = PvpServer.Arena(find.Seed);
            if (find.Ghost != null)
            {
                withGhost++;
                eloGaps.Add(Math.Abs(find.MyElo - find.Ghost.Elo));
                skillGaps.Add(Math.Abs(bot.Skill - byId[find.Ghost.PlayerId].Skill));
                ghostAgesMin.Add((new DateTimeOffset(now).ToUnixTimeMilliseconds() - find.Ghost.CreatedAtUnixMs) / 60000.0);
            }

            var (inputs, outcome, stats) = bot.Play(level, rng);
            lapses += stats.Lapses; misswipes += stats.Misswipes; confusions += stats.Confusions; bumps += stats.Bumps; actions += stats.Actions;

            // What the game sends (PvpMatch.BuildRun).
            var run = new RunSubmission { MatchId = find.MatchId, Outcome = outcome, Inputs = inputs };
            if (outcome != RunOutcome.Abandoned)
            {
                run.TimeMs = outcome == RunOutcome.TimedOut || inputs.Count == 0 ? PvpConfig.TimeLimitMs : RunActions.MsOf(inputs[^1].Tick);
                var verified = RunReplay.Verify(level, run);
                if (verified != null) { run.Outcome = verified.Outcome; run.TimeMs = verified.TimeMs; run.Progress = verified.Progress; }
                else
                {
                    RunValidator.IsPlausible(run, out var why);
                    string key = why ?? "replay";
                    invalidReasons[key] = invalidReasons.GetValueOrDefault(key) + 1;
                }
            }
            outcomes[run.Outcome] = outcomes.GetValueOrDefault(run.Outcome) + 1;
            if (run.Outcome == RunOutcome.Finished) finishTimes.Add(run.TimeMs);
            int act = level.Id.Act;
            var a = perArenaAct.GetValueOrDefault(act);
            perArenaAct[act] = (a.runs + 1, a.finished + (run.Outcome == RunOutcome.Finished ? 1 : 0), a.time + (run.Outcome == RunOutcome.Finished ? run.TimeMs : 0));
            string traps = string.Join("+", level.AllCells().Select(c => level[c]).Where(t => t.Type == TileType.Trap)
                                                   .Select(t => t.Trap.ToString()).Distinct().OrderBy(s => s));
            var tr = trapRuns.GetValueOrDefault(traps == "" ? "aucun" : traps);
            trapRuns[traps == "" ? "aucun" : traps] = (tr.runs + 1, tr.finished + (run.Outcome == RunOutcome.Finished ? 1 : 0),
                tr.msPerPar + (run.Outcome == RunOutcome.Finished ? run.TimeMs / (double)level.Solution.Moves : 0));

            var res = server.SubmitRunAsync(bot.Id, run, bot.Name).Result;
            if (res.Error == "INVALID_RUN") { invalid++; Console.WriteLine($"refusée : {run.Outcome}, {run.Inputs.Count} actions, {run.TimeMs} ms"); }
            else if (res.Error != null) Console.WriteLine("SubmitRun: " + res.Error);
            if (res.Resolved)
            {
                resolved++;
                if (res.Result == DuelResult.Draw) draws++;
                var rival = byId[find.Ghost.PlayerId];
                if (Math.Abs(bot.Skill - rival.Skill) >= 0.2 && res.Result != DuelResult.Draw)
                {
                    clearFavourites++;
                    bool favouriteWon = (bot.Skill > rival.Skill) == (res.Result == DuelResult.Win);
                    if (!favouriteWon) upsets++;
                }
            }
            // The next duel starts once this one is over (time of the run + result screen).
            now = now.AddMilliseconds((run.TimeMs > 0 ? run.TimeMs : 30_000) + 15_000 + rng.Next(30_000));
        }
    }
    now = now.Date.AddDays(1).AddHours(8);
    Console.WriteLine($"jour {day + 1} : {duels} duels, {withGhost} contre un fantôme, {timer.Elapsed.TotalSeconds:F0} s");
}

// ------------------------------------------------------------------ report
string Pct(int n, int of) => of == 0 ? "-" : $"{100.0 * n / of:F1} %";
double Median(List<double> l) { if (l.Count == 0) return 0; var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
double P90(List<double> l) { if (l.Count == 0) return 0; var s = l.OrderBy(x => x).ToList(); return s[(int)(s.Count * 0.9)]; }

Console.WriteLine();
Console.WriteLine($"=== {players} joueurs, {days} jours, {duels} duels ===");
Console.WriteLine($"Contre un fantôme : {withGhost} ({Pct(withGhost, duels)}) ; le reste a couru en premier (fantôme mis en file).");
Console.WriteLine($"Duels résolus : {resolved}, nuls : {draws}, courses refusées par le serveur : {invalid} {string.Join(", ", invalidReasons.Select(kv => kv.Key + "=" + kv.Value))}");
Console.WriteLine($"Issues : {string.Join(", ", outcomes.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {Pct(kv.Value, duels)}"))}");
var ft = finishTimes.Select(x => (double)x).ToList();
Console.WriteLine($"Temps de sortie : médiane {Median(ft) / 1000:F1} s, 90e centile {P90(ft) / 1000:F1} s");
Console.WriteLine($"Par course : {1.0 * lapses / duels:F2} trous de mémoire, {1.0 * misswipes / duels:F2} gestes ratés, {1.0 * confusions / duels:F2} confusions (tombeau tourné / miroir), {1.0 * bumps / duels:F2} coups dans un mur, {1.0 * actions / duels:F1} actions");
foreach (var kv in perArenaAct.OrderBy(k => k.Key))
    Console.WriteLine($"  tombeaux de l'acte {kv.Key} : {kv.Value.runs} courses, {Pct(kv.Value.finished, kv.Value.runs)} sorties, {kv.Value.time / Math.Max(1, kv.Value.finished) / 1000:F1} s en moyenne");
foreach (var kv in trapRuns.OrderBy(k => k.Key))
    Console.WriteLine($"  pièges {kv.Key} : {kv.Value.runs} courses, {Pct(kv.Value.finished, kv.Value.runs)} sorties, {kv.Value.msPerPar / Math.Max(1, kv.Value.finished) / 1000:F2} s par coup idéal");

var gaps = eloGaps.Select(x => (double)x).ToList();
Console.WriteLine($"Écart d'Elo avec le fantôme : moyenne {gaps.DefaultIfEmpty().Average():F0}, médiane {Median(gaps):F0}, 90e centile {P90(gaps):F0}, max {gaps.DefaultIfEmpty().Max():F0}");
Console.WriteLine($"Écart de niveau réel avec le fantôme : moyenne {skillGaps.DefaultIfEmpty().Average():F2} (au hasard : ~0,33)");
Console.WriteLine($"Âge du fantôme affronté : médiane {Median(ghostAgesMin):F0} min, 90e centile {P90(ghostAgesMin):F0} min");
Console.WriteLine($"Favori net (écart de niveau ≥ 0,2) battu : {upsets}/{clearFavourites} ({Pct(upsets, clearFavourites)})");
Console.WriteLine($"Fantômes jamais affrontés en fin de simulation : {store.Queue.Count}");

// Does the Elo find the real level?
var final = bots.Select(b => (bot: b, data: store.Players.GetValueOrDefault(b.Id))).Where(x => x.data != null).ToList();
double Spearman(List<double> x, List<double> y)
{
    List<double> Ranks(List<double> v) { var idx = v.Select((val, i) => (val, i)).OrderBy(p => p.val).ToList(); var r = new double[v.Count]; for (int i = 0; i < idx.Count; i++) r[idx[i].i] = i; return r.ToList(); }
    var rx = Ranks(x); var ry = Ranks(y);
    double mx = rx.Average(), my = ry.Average();
    double num = rx.Zip(ry, (p, q) => (p - mx) * (q - my)).Sum();
    double den = Math.Sqrt(rx.Sum(p => (p - mx) * (p - mx)) * ry.Sum(q => (q - my) * (q - my)));
    return den == 0 ? 0 : num / den;
}
var ranked = final.Where(x => x.data.TotalDuels >= 5).ToList();
Console.WriteLine($"Corrélation niveau réel / Elo (joueurs avec 5 duels résolus ou plus, n={ranked.Count}) : {Spearman(ranked.Select(x => x.bot.Skill).ToList(), ranked.Select(x => (double)x.data.Elo).ToList()):F2}");
Console.WriteLine("Elo moyen par quintile de niveau réel :");
for (int q = 0; q < 5; q++)
{
    var group = final.Where(x => x.bot.Skill >= q / 5.0 && x.bot.Skill < (q + 1) / 5.0).ToList();
    if (group.Count == 0) continue;
    Console.WriteLine($"  niveau {q * 20}-{q * 20 + 20} % : Elo {group.Average(x => x.data.Elo):F0} (de {group.Min(x => x.data.Elo)} à {group.Max(x => x.data.Elo)}), {group.Average(x => x.data.TotalDuels):F1} duels résolus, {group.Average(x => x.data.Seals):F0} sceaux");
}
Console.WriteLine("Ligues : " + string.Join(", ", final.GroupBy(x => Leagues.FromElo(x.data.Elo)).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
var never = bots.Count(b => !store.Players.ContainsKey(b.Id) || store.Players[b.Id].TotalDuels == 0);
Console.WriteLine($"Joueurs sans aucun duel résolu : {never}");

var board = server.GetBoardAsync("p0", 0, 10).Result;
Console.WriteLine("Top 10 du classement vérifié :");
foreach (var row in board.Rows)
    Console.WriteLine($"  {row.Rank,2}. {byId[row.PlayerId].Name,-10} Elo {row.Elo}  niveau réel {byId[row.PlayerId].Skill:F2}  activité {byId[row.PlayerId].Activity}/jour");
Console.WriteLine($"Durée de la simulation : {timer.Elapsed.TotalSeconds:F0} s");
