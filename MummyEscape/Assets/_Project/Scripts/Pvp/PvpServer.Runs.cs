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
        public List<RunInput> Inputs = new List<RunInput>();
    }

    public sealed partial class PvpServer
    {
        /// <summary>Runs per call: the phone sends them in batches (one call for several runs keeps Cloud Code cheap).</summary>
        public const int SoloRunsPerCall = 20;
        const int MaxRunInputs = 4000;

        /// <summary>
        /// Replays the runs and adds their XP to the pass held here (and their scarabs, see <see cref="ScarabsOf"/>): a campaign escape 40 + 10 per star (stars on the moves
        /// alone: the expert time would need a solver per run), a death 10, an endless tomb 70. A run that the rules refuse,
        /// or played faster than the game's animations allow, gives nothing.
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
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                // A run counts once: its XP, its scarabs (see ScarabsOf) and its escape for the quests.
                foreach (var (run, xp, result) in replays)
                    if (AddXp(w, season, "run:" + run.Id, xp)) // noloc
                    {
                        w.Scarabs += ScarabsOf(w, run, result);
                        if (result?.Won == true) CountQuest(d, run.EndlessMode >= 0 ? QuestKind.EndlessWin : QuestKind.CampaignWin);
                    }
            });
            return new WalletResponse { Wallet = WalletOf(data) };
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
            xp = run.EndlessMode >= 0 ? result.Won ? BattlePass.EndlessXp : 0 : BattlePass.SoloXp(result.Won, result.Stars);
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
            if (run.EndlessMode >= 0)
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
