using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The 2v2 relay: two separate mazes played in turn, from the generated map to the server's check and the bots.</summary>
    public class RelayTests
    {
        static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        /// <summary>Plays every leg on its ideal route, each action as soon as the game allows it.</summary>
        static RelayRace PlayPerfect(RelayMap map, int legs = int.MaxValue)
        {
            var race = new RelayRace(map);
            while (!race.IsOver && race.Segment < legs)
            {
                int maze = race.ActiveMaze, segment = race.Segment;
                var sol = Solver.Solve(map.Mazes[maze].WithGoal(race.Goal), race.Session(maze).State, SolverOptions.Default);
                Assert.IsNotNull(sol, $"seed {map.Seed}: leg {segment} has no way to its plate");
                foreach (var a in sol.Actions)
                {
                    var r = race.Apply(maze, a, race.MinTick(maze));
                    Assert.IsTrue(r.HasValue && !r.Value.Has(StepFlags.Blocked), $"seed {map.Seed}: leg {segment} blocked");
                }
                Assert.AreEqual(segment + 1, race.Segment, $"seed {map.Seed}: leg {segment} did not end on its plate");
            }
            return race;
        }

        [Test]
        public void Arena_IsDeterministic_AndSizedByAct()
        {
            foreach (int seed in Seeds.Take(4))
            {
                var a = RelayArena.Generate(seed);
                var b = RelayArena.Generate(seed);
                Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), $"seed {seed}");
                Assert.AreEqual(RelayConfig.Mazes, a.Mazes.Length);
                Assert.AreNotEqual(a.Mazes[0].ComputeHash(), a.Mazes[1].ComputeHash(), "two different mazes");
                int legs = a.Id.Act < 3 ? 2 : 3;
                Assert.AreEqual(legs, a.LegsPerRunner, $"seed {seed} ({a.Id})");
                Assert.AreEqual(legs * 2, a.Segments);
                Assert.AreEqual((a.Mazes[0].Floors + a.Mazes[1].Floors) * 7, a.PreviewSeconds);
            }
            Assert.AreNotEqual(RelayArena.Generate(1).ComputeHash(), RelayArena.Generate(2).ComputeHash());
        }

        [Test]
        public void Arena_RelayPlatesAreMandatory_AndInOrder()
        {
            foreach (int seed in Seeds)
            {
                var map = RelayArena.Generate(seed);
                for (int m = 0; m < RelayConfig.Mazes; m++)
                {
                    var maze = map.Mazes[m];
                    foreach (var plate in map.Relays[m])
                    {
                        Assert.AreEqual(TileType.Button, maze[plate].Type, $"seed {seed} maze {m}");
                        Assert.IsNull(Solver.Solve(maze.WithTile(plate, Tile.Floor)), $"seed {seed} maze {m}: plate {plate} can be skipped");
                    }
                }
            }
        }

        [Test]
        public void Race_AlternatesMazes_AndFinishesOnTheSecondExit()
        {
            foreach (int seed in Seeds)
            {
                var map = RelayArena.Generate(seed);
                var race = PlayPerfect(map);
                Assert.AreEqual(RelayStatus.Finished, race.Status, $"seed {seed}");
                Assert.AreEqual(map.Segments, race.SegmentEndTicks.Count);
                Assert.AreEqual(SessionStatus.Won, race.Session(1).Status, "the finisher walked out");
                // Every leg after the first starts after the hand-off.
                for (int s = 1; s < map.Segments; s++)
                {
                    int maze = RelayMap.MazeOf(s);
                    var first = race.Inputs.First(i => i.Tick > race.SegmentEndTicks[s - 1] && i.Maze == maze);
                    Assert.That(RunActions.MsOf(first.Tick - race.SegmentEndTicks[s - 1]), Is.GreaterThanOrEqualTo(RelayConfig.HandoffMs),
                                $"seed {seed} leg {s}");
                }
                Assert.IsNotNull(RelayRace.Verify(map, race.Inputs.ToList()), $"seed {seed}: the server rejects a fair relay");
            }
        }

        [Test]
        public void Race_RefusesActionsOutOfTurn()
        {
            var map = RelayArena.Generate(3);
            var race = new RelayRace(map);
            foreach (var d in DirExt.All)
            {
                var probe = new RelayRace(map);
                var r = probe.Apply(1, PlayerAction.Move(d), 10);
                Assert.IsNull(r, "the second runner waits on the start");
                Assert.IsTrue(probe.Invalid);
            }
            // After the first leg, the first runner is frozen on the plate.
            race = PlayPerfect(map, legs: 1);
            Assert.AreEqual(1, race.ActiveMaze);
            Assert.AreEqual(map.Relays[0][0], race.Session(0).Position);
            Assert.IsNull(race.Apply(0, PlayerAction.Move(Dir.Up), race.MinTick(0)));
        }

        [Test]
        public void Verify_RejectsAQuickHandoff_AndAnOutOfTurnInput()
        {
            var map = RelayArena.Generate(5);
            var fair = PlayPerfect(map).Inputs.Select(i => new RelayInput { Tick = i.Tick, Maze = i.Maze, Direction = i.Direction }).ToList();
            Assert.IsNotNull(RelayRace.Verify(map, fair));

            // The second runner sets off as soon as the plate is pressed: faster than the hand-off.
            var rushed = fair.Select(i => new RelayInput { Tick = i.Tick, Maze = i.Maze, Direction = i.Direction }).ToList();
            int firstOfLeg2 = rushed.FindIndex(i => i.Maze == 1);
            int shift = rushed[firstOfLeg2].Tick - rushed[firstOfLeg2 - 1].Tick - 1;
            for (int k = firstOfLeg2; k < rushed.Count; k++) rushed[k].Tick -= shift;
            Assert.IsNull(RelayRace.Verify(map, rushed), "partner left before the hand-off");

            var swapped = fair.Select(i => new RelayInput { Tick = i.Tick, Maze = i.Maze, Direction = i.Direction }).ToList();
            swapped[0].Maze = 1;
            Assert.IsNull(RelayRace.Verify(map, swapped), "the second runner moved first");
        }

        [Test]
        public void Race_ADeadMummyLosesTheRelay()
        {
            // A real tomb whose first leg crosses spikes: walk onto them until the mummy dies.
            foreach (int seed in Enumerable.Range(1, 60))
            {
                var map = RelayArena.Generate(seed);
                var maze = map.Mazes[0];
                var spike = maze.AllCells().FirstOrDefault(c => maze[c].Type == TileType.Trap && maze[c].Trap == TrapKind.Spikes);
                if (maze[spike].Type != TileType.Trap) continue;
                var race = new RelayRace(map);
                var path = Solver.Solve(maze.WithGoal(spike), race.Session(0).State, SolverOptions.Default);
                if (path == null) continue;
                // Path onto the spikes (the WithGoal copy turns them into the exit), then off and on again.
                foreach (var a in path.Actions) race.Apply(0, a, race.MinTick(0));
                if (race.IsOver || race.Session(0).Position != spike) continue;
                for (int k = 0; k < 6 && !race.IsOver; k++)
                    foreach (var d in DirExt.All)
                    {
                        if (!Rules.CanEnter(maze, spike.Step(d), race.Session(0).State)) continue;
                        race.Apply(0, PlayerAction.Move(d), race.MinTick(0));
                        if (!race.IsOver) race.Apply(0, PlayerAction.Move(d.Opposite()), race.MinTick(0));
                        break;
                    }
                Assert.AreEqual(RelayStatus.Lost, race.Status, $"seed {seed}");
                Assert.AreEqual(RelayDefeat.Died, race.Defeat);
                return;
            }
            Assert.Inconclusive("no relay with spikes on the first maze");
        }

        [Test]
        public void Vote_AgreementWins_ConflictIsRandom()
        {
            var rng = new Random(1);
            Assert.AreEqual("a", RelayVote.Resolve("a", "b", "a", "a", rng));
            Assert.AreEqual("b", RelayVote.Resolve("a", "b", "b", "b", rng));
            Assert.AreEqual("b", RelayVote.Resolve("a", "b", null, "b", rng), "a single vote decides");
            var picks = new HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                picks.Add(RelayVote.Resolve("a", "b", "a", "b", rng)); // both say "me"
                picks.Add(RelayVote.Resolve("a", "b", "b", "a", rng)); // both say "you"
            }
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, picks);
        }

        // ------------------------------------------------------------------ server

        static readonly DateTime Today = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

        static (PvpServer server, Action<TimeSpan> advance) NewServer()
        {
            var now = Today;
            int seed = 7;
            var server = new PvpServer(new MemoryPvpStore(), () => now, () => seed++);
            return (server, t => now += t);
        }

        static string MakeDuo(PvpServer server, string a, string b)
        {
            Assert.IsTrue(server.InviteDuoAsync(a, a, b).Result.Ok);
            var invite = server.GetTeamsAsync(b).Result.Invites.Single(i => i.FromId == a);
            Assert.IsTrue(server.RespondDuoAsync(b, b, invite.Id, true).Result.Ok);
            return server.GetTeamsAsync(a).Result.Duos.Single(d => d.Members.Contains(b)).Id;
        }

        static RelaySide Claim(string duoId, params string[] players) => new RelaySide
        {
            DuoId = duoId,
            Runners = players.Select(p => new RelayRunner { PlayerId = p, Name = "forged", Look = new PlayerLook { Mummy = "classic" } }).ToList(),
        };

        static List<RelayInput> Copy(IEnumerable<RelayInput> inputs) =>
            inputs.Select(i => new RelayInput { Tick = i.Tick, Maze = i.Maze, Direction = i.Direction }).ToList();

        [Test]
        public void Server_LiveMatch_OnlyBetweenDuosOfTheSameDivision()
        {
            var store = new MemoryPvpStore();
            var now = Today;
            int seed = 7;
            var server = new PvpServer(store, () => now, () => seed++);
            string ab = MakeDuo(server, "alice", "bob"), cd = MakeDuo(server, "carol", "dave"), ef = MakeDuo(server, "erin", "fred");
            // A diamond with a bronze: the duo plays in diamond.
            store.Players["alice"] = new PlayerPvpData { Elo = PvpConfig.DiamantMin + 40 };
            store.Players["bob"] = new PlayerPvpData { Elo = PvpConfig.ArgentMin - 50 };
            store.Players["carol"] = new PlayerPvpData { Elo = PvpConfig.OrMin };
            store.Players["dave"] = new PlayerPvpData { Elo = PvpConfig.OrMin + 10 };
            store.Players["erin"] = new PlayerPvpData { Elo = PvpConfig.ArgentMin };
            store.Players["fred"] = new PlayerPvpData { Elo = PvpConfig.DiamantMin };
            Assert.AreEqual("DIVISION", server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "l1", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Error,
                            "a diamond duo never meets a gold one");
            Assert.IsNotNull(server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "l2", Claim(ab, "alice", "bob"), Claim(ef, "erin", "fred")).Result.Match,
                             "at least one diamond on the other side");
            var bots = server.StartRelayBotsAsync("carol", DifficultyTable.GeneratorVersion, "l3", Claim(cd, "carol", "dave")).Result.Match;
            Assert.IsNotNull(bots);
            Assert.IsTrue(bots.B.Bot);
        }

        [Test]
        public void Server_LiveMatch_FasterDuoWins_AndBothElosMove()
        {
            var (server, _) = NewServer();
            string ab = MakeDuo(server, "alice", "bob"), cd = MakeDuo(server, "carol", "dave");
            Assert.AreEqual("UNKNOWN", server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "lobby1", Claim(ab, "alice", "carol"), Claim(cd, "carol", "dave")).Result.Error,
                            "runners must be the duo's members");
            Assert.AreEqual("UNKNOWN", server.StartRelayMatchAsync("eve", DifficultyTable.GeneratorVersion, "lobby1", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Error,
                            "only a player of the match creates it");
            var start = server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "lobby1", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave"));
            var match = start.Result.Match;
            Assert.IsNotNull(match);
            Assert.AreEqual("alice & bob", match.A.Name, "names come from the duo, not the client");
            Assert.AreEqual(match.Id, server.StartRelayMatchAsync("carol", DifficultyTable.GeneratorVersion, "lobby1", Claim(cd, "carol", "dave"), Claim(ab, "alice", "bob")).Result.Match.Id,
                            "same lobby, same match");

            var map = RelayArena.Generate(match.Seed);
            var perfect = PlayPerfect(map);
            var slower = Copy(perfect.Inputs);
            foreach (var i in slower) i.Tick += 50; // one second behind from the start

            var first = server.SubmitRelayAsync("alice", match.Id, "alice", Copy(perfect.Inputs), null).Result;
            Assert.IsTrue(first.Pending, "waits for the other duo");
            var second = server.SubmitRelayAsync("dave", match.Id, "dave", slower, null).Result;
            Assert.IsFalse(second.Pending);
            Assert.AreEqual(DuelResult.Loss, second.Result);
            Assert.That(second.EloDelta, Is.LessThan(0));
            var forAlice = server.RelayResultAsync("bob", match.Id).Result;
            Assert.AreEqual(DuelResult.Win, forAlice.Result);
            Assert.That(forAlice.NewElo, Is.GreaterThan(TeamConfig.DuoStartingElo));
            var duo = server.GetTeamsAsync("bob").Result.Duos.Single(d => d.Id == ab);
            Assert.AreEqual(1, duo.Wins);
            // A late submission changes nothing.
            server.SubmitRelayAsync("carol", match.Id, "carol", Copy(perfect.Inputs), null).Wait();
            Assert.AreEqual(1, server.GetTeamsAsync("bob").Result.Duos.Single(d => d.Id == ab).Wins);
        }

        [Test]
        public void Server_ForgedOrQuitRelayLoses_SilentDuoLosesAfterTheWindow()
        {
            var (server, advance) = NewServer();
            string ab = MakeDuo(server, "alice", "bob"), cd = MakeDuo(server, "carol", "dave");
            var match = server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "lobby2", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            var map = RelayArena.Generate(match.Seed);
            var perfect = PlayPerfect(map);

            // Carol's duo sends a relay that skips the hand-off: forged, so it counts as lost.
            var rushed = Copy(perfect.Inputs);
            int k = rushed.FindIndex(i => i.Maze == 1);
            int shift = rushed[k].Tick - rushed[k - 1].Tick - 1;
            for (int j = k; j < rushed.Count; j++) rushed[j].Tick -= shift;
            server.SubmitRelayAsync("carol", match.Id, "carol", rushed, null).Wait();
            // Alice's duo only played two legs before Bob quit.
            var partial = Copy(perfect.Inputs.Where(i => i.Tick <= perfect.SegmentEndTicks[1]));
            var r = server.SubmitRelayAsync("alice", match.Id, "alice", partial, new List<string> { "bob" }).Result;
            Assert.IsFalse(r.Pending);
            Assert.AreEqual(DuelResult.Win, r.Result, "both lost: legs compared, and the forged relay counts for none");

            // A duo that never sends its relay loses once the window has passed.
            var match2 = server.StartRelayMatchAsync("alice", DifficultyTable.GeneratorVersion, "lobby3", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            var map2 = RelayArena.Generate(match2.Seed);
            var p2 = server.SubmitRelayAsync("bob", match2.Id, "bob", Copy(PlayPerfect(map2, legs: 2).Inputs), null).Result;
            Assert.IsTrue(p2.Pending);
            advance(TimeSpan.FromMilliseconds(RelayServerConfig.SubmitWindowMs + 1));
            var late = server.RelayResultAsync("alice", match2.Id).Result;
            Assert.IsFalse(late.Pending);
            Assert.AreEqual(DuelResult.Win, late.Result, "still running beats gone");

            Assert.IsTrue(server.ReportRelayQuitAsync("alice", match.Id, "bob").Result.Ok);
            Assert.AreEqual("UNKNOWN", server.ReportRelayQuitAsync("alice", match.Id, "carol").Result.Error, "only a teammate can be reported");
        }

        [Test]
        public void Server_BotMatch_IsPlayedInAdvance()
        {
            var (server, _) = NewServer();
            string ab = MakeDuo(server, "alice", "bob");
            var match = server.StartRelayBotsAsync("alice", DifficultyTable.GeneratorVersion, "party1", Claim(ab, "alice", "bob")).Result.Match;
            Assert.IsTrue(match.VsBots);
            Assert.IsTrue(match.B.Bot);
            Assert.AreEqual(2, match.B.Runners.Count);
            Assert.IsTrue(match.B.Has(match.B.Starter));
            var map = RelayArena.Generate(match.Seed);
            Assert.IsNotNull(RelayRace.Verify(map, match.B.Inputs), "the bots' relay replays");
            var r = server.SubmitRelayAsync("bob", match.Id, "alice", Copy(PlayPerfect(map).Inputs), null).Result;
            Assert.IsFalse(r.Pending, "nothing to wait for");
            Assert.AreEqual(DuelResult.Win, r.Result, "perfect play beats any bot");
        }

        [Test]
        public void Judge_FirstHomeWins_DeathLoses()
        {
            var fin = new RelaySummary { Finished = true, TimeMs = 40000, Segments = 4 };
            var slow = new RelaySummary { Finished = true, TimeMs = 45000, Segments = 4 };
            var running = new RelaySummary { TimeMs = 30000, Segments = 2 };
            var dead = new RelaySummary { Lost = true, TimeMs = 20000, Segments = 3 };
            Assert.AreEqual(DuelResult.Win, RelayJudge.Resolve(fin, slow));
            Assert.AreEqual(DuelResult.Draw, RelayJudge.Resolve(fin, new RelaySummary { Finished = true, TimeMs = 40100, Segments = 4 }));
            Assert.AreEqual(DuelResult.Loss, RelayJudge.Resolve(running, slow));
            Assert.AreEqual(DuelResult.Win, RelayJudge.Resolve(running, dead), "a death loses even with more legs done");
            Assert.AreEqual(DuelResult.Win, RelayJudge.Resolve(dead, new RelaySummary { Lost = true, Segments = 1 }));
        }

        [Test]
        public void Bots_RunRelaysTheServerAccepts()
        {
            int finished = 0, total = 0;
            var times = new List<int>();
            foreach (int seed in Seeds)
            {
                var map = RelayArena.Generate(seed);
                var race = RelayBots.Play(map, new Random(seed), 1100);
                total++;
                var verified = RelayRace.Verify(map, race.Inputs.ToList());
                Assert.IsNotNull(verified, $"seed {seed}: bot relay rejected");
                if (race.Status != RelayStatus.Finished) continue;
                Assert.AreEqual(RelayStatus.Finished, verified.Status);
                finished++;
                times.Add(race.TimeMs);
                int perfect = PlayPerfect(map).TimeMs;
                Assert.That(race.TimeMs, Is.GreaterThan(perfect), $"seed {seed}: bots faster than perfect play");
            }
            Assert.That(finished, Is.GreaterThanOrEqualTo(total / 2), "most bot duos reach the exit");
            TestContext.WriteLine($"bots 1100: {finished}/{total} finished, times {string.Join(", ", times.Select(t => t / 1000.0))} s");
        }
    }
}
