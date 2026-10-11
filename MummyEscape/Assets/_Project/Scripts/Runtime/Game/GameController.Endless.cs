using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.UI.Screens;
using UnityEngine;

namespace MummyEscape.Game
{
    /// <summary>
    /// Endless mode, solo: the next stored tomb of a ladder (<see cref="TombArchive"/>), played like a campaign tomb but
    /// without stars nor records. An escape moves the ladder on, a death keeps the same tomb.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>The ladder being climbed, null outside the endless mode.</summary>
        public Difficulty? EndlessMode { get; private set; }
        public bool InEndless => EndlessMode.HasValue;
        /// <summary>Number of the tomb being played on its ladder (1-based).</summary>
        public int EndlessTomb { get; private set; }

        static Dictionary<Difficulty, List<string>> _endlessLines;

        /// <summary>The stored tombs of a ladder, read once from Resources.</summary>
        static List<string> EndlessLines(Difficulty mode)
        {
            if (_endlessLines == null)
            {
                var asset = Resources.Load<TextAsset>(Endless.ResourcePath);
                string text = asset != null ? asset.text : "";
                if (asset == null) Debug.LogError("[Game] Stored endless tombs missing: Resources/" + Endless.ResourcePath);
                _endlessLines = new Dictionary<Difficulty, List<string>>();
                foreach (var m in DifficultyExt.All) _endlessLines[m] = TombArchive.Lines(text, m);
            }
            return _endlessLines[mode];
        }

        /// <summary>Tombs stored on a ladder: past the last one, the player waits for the next update.</summary>
        public static int EndlessTombCount(Difficulty mode) => EndlessLines(mode).Count;

        /// <summary>Starts the player's current tomb of a ladder (the energy point is spent by the caller, see PlayGate).</summary>
        public async Task StartEndless(Difficulty mode)
        {
            int token = ++_loadToken;
            ResetForLoad();
            DetachDuelLink();
            Match = null;
            Pace = null;
            int index = _app.Save.EndlessOf(mode).Cleared;
            var lines = EndlessLines(mode);
            if (index >= lines.Count) return; // the ladder's end: the menus say so before getting here
            string line = lines[index];
            Level level;
            try
            {
                var id = TombArchive.Read(line).Id; // quick: the look and the music, while the way out is worked out
                EndlessMode = mode;
                EndlessTomb = index + 1;
                CurrentLevel = id;
                _app.Audio.PlayMusic(id.Act);
                LevelLoading?.Invoke(id);
                level = await Task.Run(() => TombArchive.Load(line));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not load endless tomb {mode} {index + 1}: {e}");
                return;
            }
            if (token != _loadToken || this == null) return;
            Setup(level);
            Pace = Task.Run(() => TombPace.Of(level));
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        /// <summary>End of an endless run: the ladder moves on (or not), the board gets the furthest tomb, then the result.</summary>
        void FinishEndless(LevelResult result)
        {
            var mode = EndlessMode.Value;
            int tomb = EndlessTomb;
            var progress = _app.Save.ApplyEndless(mode, result);
            // Faster than the game's animations allow: a modified game, kept off the leaderboard.
            if (result.Won && result.Pace != (int)PaceVerdict.Impossible) _ = _app.SubmitEndless(mode);
            _app.UI.Open<EndlessResultScreen>().Show(mode, tomb, result, progress);
        }
    }
}
