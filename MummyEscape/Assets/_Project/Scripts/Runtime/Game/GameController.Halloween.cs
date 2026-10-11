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
    /// The Halloween act: its stored tombs (<see cref="Halloween"/>), played like a campaign tomb in the Halloween look,
    /// without stars nor records. Escaping the next tomb moves the act on; the tenth gives the act's skin.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>Number of the Halloween tomb being played (1-based), 0 outside the event's act.</summary>
        public int HalloweenTomb { get; private set; }
        public bool InHalloween => HalloweenTomb > 0;

        static List<string> _halloweenLines;

        static List<string> HalloweenLines()
        {
            if (_halloweenLines != null) return _halloweenLines;
            var asset = Resources.Load<TextAsset>(Halloween.ResourcePath);
            if (asset == null) Debug.LogError("[Game] Stored Halloween tombs missing: Resources/" + Halloween.ResourcePath);
            return _halloweenLines = Halloween.Lines(asset != null ? asset.text : "");
        }

        /// <summary>Starts a Halloween tomb (the energy point is spent by the caller, see PlayGate.Solo).</summary>
        public async Task StartHalloween(int tomb)
        {
            int token = ++_loadToken;
            ResetForLoad();
            DetachDuelLink();
            Match = null;
            Pace = null;
            var lines = HalloweenLines();
            if (tomb < 1 || tomb > lines.Count) return;
            string line = lines[tomb - 1];
            Level level;
            try
            {
                var id = TombArchive.Read(line).Id;
                HalloweenTomb = tomb;
                CurrentLevel = id;
                _app.Audio.PlayMusic(id.Act);
                LevelLoading?.Invoke(id);
                level = await Task.Run(() => TombArchive.Load(line));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not load Halloween tomb {tomb}: {e}");
                return;
            }
            if (token != _loadToken || this == null) return;
            Setup(level);
            Pace = Task.Run(() => TombPace.Of(level));
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        void FinishHalloween(LevelResult result)
        {
            int tomb = HalloweenTomb;
            bool finished = _app.Save.ApplyHalloween(tomb, result.Won);
            _app.UI.Open<EndlessResultScreen>().ShowHalloween(tomb, result, finished);
        }
    }
}
