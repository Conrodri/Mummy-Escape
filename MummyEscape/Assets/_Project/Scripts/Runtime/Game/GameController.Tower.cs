using System;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.UI.Screens;
using UnityEngine;

namespace MummyEscape.Game
{
    /// <summary>
    /// The challenge tower (<see cref="Tower"/>): the run's next floor, drawn for this run with its handicaps, played like a
    /// campaign tomb. An escape with 2 stars or more climbs on; anything else ends the run. The floor above is drawn while
    /// this one is played.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>The floor being played (1-based), 0 outside the tower.</summary>
        public int TowerFloor { get; private set; }
        public bool InTower => TowerFloor > 0;

        (int seed, int floor, TowerCurses curses, Task<Level> level) _towerNext;

        /// <summary>Plays the next floor of the run in progress (the run's energy point was spent when it started).</summary>
        public async Task StartTower()
        {
            var run = _app.Save.ActiveTower;
            if (run == null || run.CurseDue) return; // the tower screen asks for the handicap first
            int token = ++_loadToken;
            ResetForLoad();
            DetachDuelLink();
            Match = null;
            Pace = null;
            int floor = run.Floor;
            var id = Tower.Source(floor);
            TowerFloor = floor;
            CurrentLevel = id;
            _app.Audio.PlayMusic(id.Act);
            LevelLoading?.Invoke(id);
            Level level;
            try
            {
                level = await TowerLevel(run.Seed, floor, run.Curses);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Game] Could not draw tower floor {floor} (handicaps {run.Curses.Traps}/{run.Curses.Bigger}/{run.Curses.Storeys}): {e}");
                TowerFloor = 0;
                return;
            }
            if (token != _loadToken || this == null) return;
            Setup(level);
            Pace = Task.Run(() => TombPace.Of(level));
            _towerNext = (run.Seed, floor + 1, run.Curses, DrawTower(run.Seed, floor + 1, run.Curses));
            LevelStarted?.Invoke();
            Changed?.Invoke();
            StartCoroutine(PreviewRoutine());
        }

        /// <summary>The floor drawn ahead when it is that one, else a new draw.</summary>
        Task<Level> TowerLevel(int seed, int floor, TowerCurses curses)
        {
            var next = _towerNext;
            _towerNext = default;
            return next.level != null && next.seed == seed && next.floor == floor && next.curses.Equals(curses) ? next.level : DrawTower(seed, floor, curses);
        }

        static Task<Level> DrawTower(int seed, int floor, TowerCurses curses) => Task.Run(() => Tower.Generate(floor, curses, seed));

        /// <summary>End of a floor: the run climbs on or stops, the board gets a new best, then the result.</summary>
        void FinishTower(LevelResult result)
        {
            int floor = TowerFloor;
            var outcome = _app.Save.ApplyTower(result);
            // Faster than the game's animations allow: a modified game, kept off the leaderboard.
            if (outcome.Record && result.Pace != (int)PaceVerdict.Impossible) _ = _app.SubmitTower();
            _app.UI.Open<EndlessResultScreen>().ShowTower(floor, result, outcome, _app.Save.Data.Tower);
        }
    }
}
