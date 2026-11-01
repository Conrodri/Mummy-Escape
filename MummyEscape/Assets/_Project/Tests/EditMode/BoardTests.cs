using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The tower and endless ladders, written by the server only from the runs it replays (the game cannot write them).</summary>
    public class BoardTests
    {
        static (PvpServer server, MemoryPvpStore store) NewServer()
        {
            var store = new MemoryPvpStore();
            return (new PvpServer(store), store);
        }

        static List<SoloRunReport> Runs(params SoloRunReport[] runs) => new List<SoloRunReport>(runs);

        /// <summary>
        /// The optimal route timed to live: each action at the first moment (in 0.1 s steps after the animation) the replay
        /// survives it, so the flame jets and idol gazes of the later acts are passed between two beats.
        /// </summary>
        static SoloRunReport SafeRun(string id, Level maze)
        {
            var inputs = new List<RunInput>();
            var state = Rules.Initial(maze);
            int ms = 500;
            foreach (var action in maze.Solution.Actions)
            {
                var step = Rules.Step(maze, state, action);
                int at = ms;
                for (int wait = 0; ; wait += 100)
                {
                    Assert.Less(wait, 10_000, "no safe moment for this action");
                    at = ms + wait;
                    inputs.Add(new RunInput { Tick = RunActions.TickOf(at), Direction = RunActions.Encode(action) });
                    var replay = new RunReplay(maze, inputs);
                    replay.AdvanceTo(int.MaxValue);
                    if (!replay.Invalid && !replay.TooFast && replay.Session.Status != SessionStatus.Dead) break;
                    inputs.RemoveAt(inputs.Count - 1);
                }
                ms = at + RunTiming.MinGapMs(maze, state, action, step);
                state = step.State;
            }
            return new SoloRunReport { Id = id, Inputs = inputs };
        }

        static long? Score(MemoryPvpStore store, string board, string player) =>
            store.Scores.TryGetValue(board, out var rows) && rows.TryGetValue(player, out long s) ? s : (long?)null;

        [Test]
        public async Task ACampaignLevel_WritesNoBoard_ButItsEscapeStillCounts()
        {
            var (server, store) = NewServer();
            var r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("a", Progression.First(Difficulty.Easy), 0)));
            Assert.IsEmpty(store.Scores, "only the four ladders: no board per level any more");
            Assert.Greater(r.Wallet.PassXp, 0, "the escape is real: its XP still counts");
        }

        [Test]
        public async Task TheEndlessLadder_WeighsTheTombsOfEachMode_NotTooFarAhead()
        {
            var (server, store) = NewServer();
            string text = EndlessTests.StoredText();
            server.EndlessArchive = () => text;
            SoloRunReport At(string id, Difficulty mode, int depth)
            {
                var run = SafeRun(id, TombArchive.Load(TombArchive.Lines(text, mode)[depth]));
                run.EndlessMode = (int)mode;
                run.Depth = depth;
                return run;
            }
            (int points, int timeMs) Points(string player) => Ladders.DecodeEndless(Score(store, Ladders.EndlessBoard, player).Value);
            await server.SetCountryAsync("alice", "FR");
            await server.OpenScarabsAsync("alice", new ScarabSeed { EndlessCleared = new List<int> { 0, 2, 0 } });
            await server.SubmitSoloRunsAsync("alice", Runs(At("n2", Difficulty.Normal, 2)));
            Assert.AreEqual(3 * 2, Points("alice").points, "tomb 3 of Normal escaped: 3 tombs, 2 points each");
            Assert.AreEqual("FR", store.Countries[Ladders.EndlessBoard + "/alice"], "labelled with the country held here");

            int far = 3 + PvpServer.EndlessGapMax + 1;
            await server.SubmitSoloRunsAsync("alice", Runs(At("far", Difficulty.Normal, far)));
            Assert.AreEqual(6, Points("alice").points, "too far past the last tomb seen");
            await server.SubmitSoloRunsAsync("alice", Runs(At("e0", Difficulty.Easy, 0)));
            var (points, timeMs) = Points("alice");
            Assert.AreEqual(6 + 1, points, "one Easy tomb: 1 point");
            Assert.AreEqual(store.Players["alice"].EndlessTimeMs.Sum(), timeMs, "the tie-break: every tomb's time added up");
            Assert.AreEqual(new List<int> { 1, 3 }, store.Players["alice"].EndlessCleared);
            Assert.AreEqual(7, Ladders.EndlessPoints(new[] { 1, 0, 2 }), "Easy x1, Normal x2, Extreme x3");
        }

        [Test]
        public async Task TheTower_IsFollowedFloorByFloor()
        {
            var (server, store) = NewServer();
            SoloRunReport Floor(string id, int seed, int floor, TowerCurses curses = default)
            {
                var run = SafeRun(id, Tower.Generate(floor, curses, seed));
                run.TowerSeed = seed;
                run.TowerFloor = floor;
                run.TowerCurses = curses;
                return run;
            }
            Assert.AreEqual("ladder_tower", Ladders.TowerBoard, "one board for every generator: no version in its id");
            int Floors() => Tower.DecodeScore(Score(store, Ladders.TowerBoard, "alice").Value).floors;

            await server.SubmitSoloRunsAsync("alice", Runs(Floor("a1", 7, 1), Floor("a2", 7, 2)));
            Assert.AreEqual(2, Floors());
            await server.SubmitSoloRunsAsync("alice", Runs(Floor("x4", 7, 4), Floor("y3", 8, 3)));
            Assert.AreEqual(2, Floors(), "a floor skipped, another run's floor: ignored");
            await server.SubmitSoloRunsAsync("alice", Runs(Floor("a3", 7, 3)));
            Assert.AreEqual(3, Floors());
            await server.SubmitSoloRunsAsync("alice", Runs(Floor("a4", 7, 4)));
            Assert.AreEqual(3, Floors(), "a handicap was due after floor 3: the run is over");

            await server.SubmitSoloRunsAsync("alice", Runs(Floor("b1", 9, 1, new TowerCurses { Traps = 1 })));
            Assert.AreEqual(3, Floors(), "a handicap on floor 1 is not a run of the game");
            await server.SubmitSoloRunsAsync("alice", Runs(Floor("c1", 5, 1), Floor("c2", 5, 2), Floor("c3", 5, 3),
                Floor("c4", 5, 4, new TowerCurses { Storeys = 1 })));
            Assert.AreEqual(4, Floors(), "the handicap due, taken: the run climbs on");
        }
    }
}
