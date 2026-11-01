using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The solo leaderboards, written by the server only from the runs it replays (the game cannot write them).</summary>
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
        public async Task ALevel_GoesToItsBoard_OnceTheServerSawItOpen()
        {
            var (server, store) = NewServer();
            var first = Progression.First(Difficulty.Easy);
            var run = WalletTests.SolvedRun("a", first, 0);
            run.Country = "FR";
            await server.SubmitSoloRunsAsync("alice", Runs(run));
            var (overPar, timeMs) = LevelResult.DecodeScore(Score(store, LevelResult.BoardId(first), "alice").Value);
            Assert.AreEqual(0, overPar, "the optimal route, as replayed");
            Assert.AreEqual(RunActions.MsOf(run.Inputs[run.Inputs.Count - 1].Tick), timeMs, "the time the inputs show");

            var third = new LevelId(Difficulty.Easy, 1, 3);
            var r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("c", third, 0)));
            Assert.IsNull(Score(store, LevelResult.BoardId(third), "alice"), "F1-2 never escaped here: a save edited past it");
            Assert.Greater(r.Wallet.PassXp, 0, "the escape is real: its XP still counts");
        }

        [Test]
        public async Task TheEndlessBoard_FollowsTheTombsReplayed_NotTooFarAhead()
        {
            var (server, store) = NewServer();
            string text = EndlessTests.StoredText();
            server.EndlessArchive = () => text;
            var lines = TombArchive.Lines(text, Difficulty.Normal);
            SoloRunReport At(string id, int depth)
            {
                var run = SafeRun(id, TombArchive.Load(lines[depth]));
                run.EndlessMode = (int)Difficulty.Normal;
                run.Depth = depth;
                return run;
            }
            string board = Endless.BoardId(Difficulty.Normal);
            int Reached(string player) => Endless.DecodeScore(Score(store, board, player).Value).reached;
            await server.OpenScarabsAsync("alice", new ScarabSeed { EndlessCleared = new List<int> { 0, 2, 0 } });
            await server.SubmitSoloRunsAsync("alice", Runs(At("t2", 2)));
            Assert.AreEqual(4, Reached("alice"), "tomb 3 escaped: before tomb 4");

            int far = 3 + PvpServer.EndlessGapMax + 1;
            await server.SubmitSoloRunsAsync("alice", Runs(At("far", far)));
            Assert.AreEqual(4, Reached("alice"), "too far past the last tomb seen");
            await server.SubmitSoloRunsAsync("alice", Runs(At("t3", 3)));
            Assert.AreEqual(5, Reached("alice"));

            await server.OpenScarabsAsync("bob", new ScarabSeed { EndlessCleared = new List<int> { 0, far, 0 } });
            await server.SubmitSoloRunsAsync("bob", Runs(At("bob-far", far)));
            Assert.AreEqual(far + 2, Reached("bob"), "the same escape counts where the ladder stands");
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
            int Floors() => Tower.DecodeScore(Score(store, Tower.BoardId, "alice").Value).floors;

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
