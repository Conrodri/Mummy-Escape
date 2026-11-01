using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// A solo run sent for the pass: the tomb (a campaign level or an endless ladder's depth) and the timed actions. The
    /// server rebuilds the tomb, replays the actions and grants the XP itself.
    /// </summary>
    [Serializable]
    public class SoloRunReport
    {
        /// <summary>Made on the phone: a run sent twice (lost answer) counts once.</summary>
        public string Id;
        /// <summary>The campaign level's key (<see cref="LevelId.Key"/>), or null for an endless tomb.</summary>
        public string Level;
        public int Variant;
        /// <summary>Endless tomb: its ladder (<see cref="Difficulty"/>) and depth.</summary>
        public int EndlessMode = -1;
        public int Depth;
        /// <summary>Tower floor (1-based; 0: not the tower), drawn from the run's seed with its handicaps.</summary>
        public int TowerFloor;
        public int TowerSeed;
        public TowerCurses TowerCurses;
        /// <summary>The player's country (ISO code), shown with the leaderboard rows the server writes; null: none.</summary>
        public string Country;
        public List<RunInput> Inputs = new List<RunInput>();
    }

    public sealed partial class PvpServer
    {
        /// <summary>Runs per call: the phone sends them in batches (one call for several runs keeps Cloud Code cheap).</summary>
        public const int SoloRunsPerCall = 20;
        const int MaxRunInputs = 4000;

        /// <summary>
        /// Replays the runs and adds their XP to the pass held here (and their scarabs, see <see cref="ScarabsOf"/>): a campaign escape 40 + 10 per star (stars on the moves
        /// alone: the expert time would need a solver per run), a death 10, an endless tomb 70, a tower floor nothing. A run
        /// that the rules refuse, or played faster than the game's animations allow, gives nothing. The leaderboards are
        /// written here only, from these replays (<see cref="BoardsOf"/>, <see cref="TowerFloorPlayed"/>): the game cannot.
        /// </summary>
        public async Task<WalletResponse> SubmitSoloRunsAsync(string me, List<SoloRunReport> runs)
        {
            if (runs == null || runs.Count > SoloRunsPerCall) return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            var season = BattlePass.At(_utcNow());
            var replays = new List<(SoloRunReport run, int xp, LevelResult result)>();
            foreach (var run in runs)
            {
                if (string.IsNullOrEmpty(run?.Id) || run.Id.Length > 64 || run.Inputs == null || run.Inputs.Count > MaxRunInputs) continue;
                var result = Replay(run, out int xp);
                replays.Add((run, xp, result));
            }
            var boards = new Dictionary<string, long>();
            string country = null;
            var data = await Update(me, d =>
            {
                boards.Clear(); // a write conflict runs this again on the fresh copy
                var w = WalletOf(d);
                // A run counts once: its XP, its scarabs (see ScarabsOf), its escape for the quests and the boards.
                foreach (var (run, xp, result) in replays)
                    if (AddXp(w, season, "run:" + run.Id, xp)) // noloc
                    {
                        country = CountryOf(run) ?? country;
                        if (run.TowerFloor > 0) { TowerFloorPlayed(d, run, result, boards); continue; }
                        BoardsOf(d, run, result, boards);
                        w.Scarabs += ScarabsOf(w, run, result);
                        if (result?.Won == true) CountQuest(d, run.EndlessMode >= 0 ? QuestKind.EndlessWin : QuestKind.CampaignWin);
                        if (result != null && run.EndlessMode < 0) CountQuest(d, QuestKind.CampaignPlay);
                    }
            });
            foreach (var pair in boards) await _store.SubmitScoreAsync(pair.Key, me, pair.Value, country);
            return new WalletResponse { Wallet = WalletOf(data) };
        }

        /// <summary>
        /// The board score a replayed escape earns (the boards keep the best): a campaign level once the server has seen the
        /// level before it escaped (<see cref="Progression"/>), an endless ladder's tomb reached with the times added up.
        /// Called before <see cref="ScarabsOf"/>, which notes this escape.
        /// </summary>
        static void BoardsOf(PlayerPvpData d, SoloRunReport run, LevelResult result, Dictionary<string, long> boards)
        {
            if (result == null || !result.Won) return;
            var w = WalletOf(d);
            if (run.EndlessMode >= 0)
            {
                int paid = run.EndlessMode < w.EndlessPaid.Count ? w.EndlessPaid[run.EndlessMode] : 0;
                if (!EndlessCounts(paid, run.Depth)) return;
                while (d.EndlessTimeMs.Count <= run.EndlessMode) d.EndlessTimeMs.Add(0);
                int time = (int)Math.Min(int.MaxValue, (long)d.EndlessTimeMs[run.EndlessMode] + result.TimeMs);
                d.EndlessTimeMs[run.EndlessMode] = time;
                // Escaping tomb Depth (0-based) leaves the player before tomb Depth + 2 (1-based), as EndlessProgress.Reached.
                Best(boards, Endless.BoardId((Difficulty)run.EndlessMode), Endless.EncodeScore(run.Depth + 2, time));
                return;
            }
            if (!Progression.IsUnlocked(result.Level, id => StarsAt(w, id.Key) > 0)) return; // a level the server never saw opened
            Best(boards, LevelResult.BoardId(result.Level), result.LeaderboardScore);
        }

        /// <summary>
        /// Most endless tombs a run may jump past the last one escaped here and still count (scarabs, board): runs of two
        /// phones can arrive out of order, a long-offline phone drops its oldest ones. Deeper, a save edited gains nothing.
        /// </summary>
        public const int EndlessGapMax = 10;

        /// <summary>A new tomb of the ladder, close enough to the last one escaped here (<paramref name="paid"/> tombs).</summary>
        static bool EndlessCounts(int paid, int depth) => depth >= paid && depth <= paid + EndlessGapMax;

        static void Best(Dictionary<string, long> boards, string board, long score)
        {
            if (!boards.TryGetValue(board, out long kept) || score < kept) boards[board] = score;
        }

        /// <summary>
        /// A tower floor replayed here, against the run the server follows: floor 1 starts a run (no handicap yet), any other
        /// floor must be the next one of the same run, with the same handicaps or the one that was due. An escape with
        /// 2 stars climbs on (stars on the moves alone), anything else ends the run. A better run goes to the board.
        /// </summary>
        static void TowerFloorPlayed(PlayerPvpData d, SoloRunReport run, LevelResult result, Dictionary<string, long> boards)
        {
            if (run.TowerFloor == 1) d.Tower = run.TowerCurses.Count == 0 ? new TowerRun { Seed = run.TowerSeed } : null;
            var t = d.Tower;
            if (t == null || t.Seed != run.TowerSeed || t.Floor != run.TowerFloor) return; // not where the run stands: ignored
            foreach (var c in Tower.Curses)
                if (!run.TowerCurses.Equals(t.Curses) && run.TowerCurses.Equals(t.Curses.With(c))) t.Take(c); // refused when not due
            if (!run.TowerCurses.Equals(t.Curses) || t.CurseDue || result == null || !Tower.Continues(result)) { d.Tower = null; return; }
            t.Escaped(result.TimeMs);
            d.TowerBest ??= new TowerRecord();
            if (d.TowerBest.Offer(t)) Best(boards, Tower.BoardId, d.TowerBest.Score);
        }

        /// <summary>The country sent with the runs, if it looks like one (2 or 3 capitals): it only labels the board rows.</summary>
        static string CountryOf(SoloRunReport run)
        {
            string c = run.Country;
            if (c == null || c.Length < 2 || c.Length > 3) return null;
            foreach (char ch in c) if (ch < 'A' || ch > 'Z') return null;
            return c;
        }

        /// <summary>
        /// A run replayed here: its result, and the XP it earns (null and 0 when the tomb is unknown, the run impossible or
        /// unfinished).
        /// </summary>
        LevelResult Replay(SoloRunReport run, out int xp)
        {
            xp = 0;
            var level = SoloLevel(run);
            if (level == null) return null;
            var replay = new RunReplay(level, run.Inputs);
            replay.AdvanceTo(int.MaxValue);
            if (replay.Invalid || replay.TooFast || !replay.IsOver && run.EndlessMode < 0) return null;
            var result = replay.Session.BuildResult();
            if (result.Won) result.TimeMs = RunActions.MsOf(replay.LastTick); // the time the inputs show, as for the duels
            xp = run.TowerFloor > 0 ? 0 : run.EndlessMode >= 0 ? result.Won ? BattlePass.EndlessXp : 0 : BattlePass.SoloXp(result.Won, result.Stars);
            return result;
        }

        // Generating a tomb costs far more than replaying it: the last ones are kept (a warm module serves many players
        // on the same levels).
        static readonly Dictionary<string, Level> _levels = new Dictionary<string, Level>();
        static readonly List<string> _levelOrder = new List<string>();
        const int LevelsKept = 64;

        Level SoloLevel(SoloRunReport run)
        {
            string key;
            Func<Level> build;
            if (run.TowerFloor > 0)
            {
                var curses = run.TowerCurses;
                if (run.TowerFloor > 9999) return null; // far above any real climb
                foreach (var c in Tower.Curses) if (curses.Of(c) < 0 || curses.Of(c) > Tower.Cap(c)) return null;
                int floor = run.TowerFloor, seed = run.TowerSeed;
                key = $"tower{seed}#{floor}#{curses.Traps}{curses.Bigger}{curses.Storeys}"; // noloc
                build = () => Tower.Generate(floor, curses, seed);
            }
            else if (run.EndlessMode >= 0)
            {
                // The ladders are stored tombs (the game's archive, embedded in the module): the depth is the line.
                if (run.EndlessMode >= DifficultyExt.All.Length || run.Depth < 0) return null;
                var lines = EndlessLines((Difficulty)run.EndlessMode);
                if (lines == null || run.Depth >= lines.Count) return null;
                key = "endless" + run.EndlessMode + "#" + run.Depth; // noloc
                string line = lines[run.Depth];
                build = () => TombArchive.Load(line);
            }
            else
            {
                if (run.Level == null || !LevelId.TryParseKey(run.Level, out var id) || run.Variant < 0 || run.Variant > 1000) return null;
                key = id.Key + "#" + run.Variant;
                int variant = run.Variant;
                build = () => LevelGenerator.Generate(id, variant);
            }
            lock (_levels)
                if (_levels.TryGetValue(key, out var cached)) return cached;
            Level level;
            try { level = build(); }
            catch (Exception) { return null; } // not a level of this generator
            lock (_levels)
            {
                if (_levels.ContainsKey(key)) return level;
                _levels[key] = level;
                _levelOrder.Add(key);
                if (_levelOrder.Count > LevelsKept) { _levels.Remove(_levelOrder[0]); _levelOrder.RemoveAt(0); }
            }
            return level;
        }

        /// <summary>The endless ladders' stored tombs (the text of Resources/Tombs/endless.txt); null: endless runs give no XP here.</summary>
        public Func<string> EndlessArchive;
        static Dictionary<Difficulty, List<string>> _endlessLines;

        List<string> EndlessLines(Difficulty mode)
        {
            if (_endlessLines == null)
            {
                string text = EndlessArchive?.Invoke();
                if (text == null) return null;
                var all = new Dictionary<Difficulty, List<string>>();
                foreach (var m in DifficultyExt.All) all[m] = TombArchive.Lines(text, m);
                _endlessLines = all;
            }
            return _endlessLines.TryGetValue(mode, out var lines) ? lines : null;
        }

        /// <summary>A match's XP, once per match: playing it, and winning it (asked again with won=true when the result is known).</summary>
        async Task GrantMatchXpAsync(string me, string matchId, bool won)
        {
            if (string.IsNullOrEmpty(matchId) || !IsHuman(me)) return;
            var season = BattlePass.At(_utcNow());
            await Update(me, d =>
            {
                var w = WalletOf(d);
                AddXp(w, season, "match:" + matchId, BattlePass.MatchXp); // noloc
                if (won) AddXp(w, season, "win:" + matchId, BattlePass.WinBonusXp); // noloc
            });
        }
    }
}
