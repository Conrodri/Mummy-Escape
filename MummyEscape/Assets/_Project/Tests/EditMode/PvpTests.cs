using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Rules of the PvP pack (Elo, leagues, duels, matchmaking, seasons, rewards), then the race and the server on real tombs.</summary>
    public class PvpTests
    {
        // ------------------------------------------------------------------ pack rules

        [Test]
        public void Elo_MatchesTheDesignExamples()
        {
            Assert.That(Elo.Expected(1200, 1000), Is.EqualTo(0.7597).Within(0.001));
            Assert.AreEqual(1206, Elo.NewRating(1200, 1000, DuelResult.Win, 50));
            Assert.AreEqual(994, Elo.NewRating(1000, 1200, DuelResult.Loss, 50));
            Assert.AreEqual(1194, Elo.NewRating(1200, 1000, DuelResult.Draw, 50));
            Assert.AreEqual(1182, Elo.NewRating(1200, 1000, DuelResult.Loss, 50));
            Assert.AreEqual(1018, Elo.NewRating(1000, 1200, DuelResult.Win, 50));
            Assert.AreEqual(40, Elo.KFactor(1000, 3), "placement");
            Assert.AreEqual(16, Elo.KFactor(1900, 50), "high level");
            Assert.That(Elo.NewRating(110, 2000, DuelResult.Loss, 3), Is.GreaterThanOrEqualTo(PvpConfig.MinElo));
        }

        [Test]
        public void Leagues_FollowTheThresholds()
        {
            Assert.AreEqual(League.Bronze, Leagues.FromElo(850));
            Assert.AreEqual(League.Argent, Leagues.FromElo(900));
            Assert.AreEqual(League.Or, Leagues.FromElo(1299));
            Assert.AreEqual(League.Platine, Leagues.FromElo(1300));
            Assert.AreEqual(League.Diamant, Leagues.FromElo(1500));
            Assert.AreEqual("Top 100", Leagues.DisplayName(1600, 42));
            Assert.AreEqual("Diamant", Leagues.DisplayName(1600, 150));
        }

        [Test]
        public void Duels_ExitBeatsAll_ThenSpeed_ThenProgress()
        {
            Assert.AreEqual(DuelResult.Win, DuelResolver.Resolve(RunOutcome.Finished, 60000, 1, RunOutcome.Finished, 61000, 1));
            Assert.AreEqual(DuelResult.Draw, DuelResolver.Resolve(RunOutcome.Finished, 60000, 1, RunOutcome.Finished, 60150, 1));
            Assert.AreEqual(DuelResult.Loss, DuelResolver.Resolve(RunOutcome.Died, 20000, 0.9f, RunOutcome.Finished, 170000, 1));
            Assert.AreEqual(DuelResult.Win, DuelResolver.Resolve(RunOutcome.Died, 20000, 0.8f, RunOutcome.Died, 30000, 0.4f));
            Assert.AreEqual(DuelResult.Draw, DuelResolver.Resolve(RunOutcome.TimedOut, 180000, 0.50f, RunOutcome.Died, 9000, 0.51f));
            Assert.AreEqual(DuelResult.Loss, DuelResolver.Resolve(RunOutcome.Abandoned, 0, 0, RunOutcome.Died, 5000, 0.1f));
            Assert.AreEqual(DuelResult.Loss, DuelResolver.Invert(DuelResult.Win));
        }

        [Test]
        public void Validator_RejectsImplausibleRuns()
        {
            var ok = new RunSubmission { Outcome = RunOutcome.Finished, TimeMs = 65000, Progress = 1,
                Inputs = new List<RunInput> { new RunInput { Tick = 0, Direction = 2 }, new RunInput { Tick = 40, Direction = 5 } } };
            Assert.IsTrue(RunValidator.IsPlausible(ok, out _));
            Assert.IsFalse(RunValidator.IsPlausible(new RunSubmission { Outcome = RunOutcome.Finished, TimeMs = 2000, Progress = 1 }, out var r1));
            Assert.AreEqual("too_fast", r1);
            var disorder = new RunSubmission { Outcome = RunOutcome.Died, TimeMs = 30000, Progress = 0.3f,
                Inputs = new List<RunInput> { new RunInput { Tick = 50, Direction = 1 }, new RunInput { Tick = 10, Direction = 2 } } };
            Assert.IsFalse(RunValidator.IsPlausible(disorder, out var r2));
            Assert.AreEqual("bad_ticks", r2);
            var machineGun = new RunSubmission { Outcome = RunOutcome.Died, TimeMs = 30000, Progress = 0.3f,
                Inputs = new List<RunInput> { new RunInput { Tick = 50, Direction = 1 }, new RunInput { Tick = 51, Direction = 2 } } };
            Assert.IsFalse(RunValidator.IsPlausible(machineGun, out var r3));
            Assert.AreEqual("inputs_too_close", r3);
            var none = new RunSubmission { Outcome = RunOutcome.Died, TimeMs = 30000,
                Inputs = new List<RunInput> { new RunInput { Tick = 50, Direction = 0 } } };
            Assert.IsFalse(RunValidator.IsPlausible(none, out var r4));
            Assert.AreEqual("bad_direction", r4);
        }

        [Test]
        public void Matchmaking_PicksTheClosestValidGhost()
        {
            long now = 1_800_000_000_000;
            var buckets = GhostPicker.BucketsToSearch(1050);
            Assert.AreEqual(10, buckets[0]);
            CollectionAssert.Contains(buckets, 13);
            CollectionAssert.Contains(buckets, 7);
            Assert.AreEqual(7, buckets.Count);
            var ghosts = new List<GhostRun>
            {
                new GhostRun { GhostId = "me", PlayerId = "p0", Elo = 1050, CreatedAtUnixMs = now },
                new GhostRun { GhostId = "far", PlayerId = "p1", Elo = 1400, CreatedAtUnixMs = now },
                new GhostRun { GhostId = "close", PlayerId = "p2", Elo = 1080, CreatedAtUnixMs = now },
                new GhostRun { GhostId = "seen", PlayerId = "p3", Elo = 1050, CreatedAtUnixMs = now },
                new GhostRun { GhostId = "old", PlayerId = "p4", Elo = 1050, CreatedAtUnixMs = now - 25L * 3600_000 },
            };
            var seen = new Dictionary<string, int> { { "p3", 3 } };
            Assert.AreEqual("close", GhostPicker.Pick(ghosts, "p0", 1050, seen, now)?.GhostId);
            Assert.IsNull(GhostPicker.Pick(new List<GhostRun> { ghosts[1] }, "p0", 1050, seen, now), "never beyond ±300");
        }

        [Test]
        public void Seasons_SoftResetAndKeepASummary()
        {
            var d = new PlayerPvpData { Elo = 1600, Season = "2026-09", Day = "2026-09-30", SeasonDuels = 40, SeasonDuelsLastWeek = 8 };
            Seasons.Roll(d, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
            Assert.AreEqual(1300, d.Elo);
            Assert.AreEqual(1600, d.LastSeason.FinalElo);
            Assert.AreEqual(40, d.LastSeason.Duels);
            Assert.AreEqual(0, d.SeasonDuels);
            Assert.AreEqual("2026-10", d.Season);
            Assert.AreEqual("2026-10-01", d.Day);
            Assert.AreEqual(900, Seasons.SoftReset(800));
            Assert.IsTrue(Seasons.IsLastWeekOfSeason(new DateTime(2026, 10, 25)));
            Assert.IsFalse(Seasons.IsLastWeekOfSeason(new DateTime(2026, 10, 24)));
        }

        [Test]
        public void DailyRewards_ChestOnce_SealsPerDuel_CappedParticipation()
        {
            var p = new PlayerPvpData { Elo = 1150 };
            var day = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
            Seasons.Roll(p, day);
            Assert.AreEqual(45, DuelBookkeeping.RecordDuelPlayed(p, 45000, day, false), "Or chest");
            Assert.AreEqual(0, DuelBookkeeping.RecordDuelPlayed(p, 45000, day, false), "once a day");
            Assert.AreEqual(20, DuelBookkeeping.ApplyResult(p, "p9", 1150, DuelResult.Win));
            Assert.AreEqual(20, DuelBookkeeping.ApplyResult(p, "p9", 1150, DuelResult.Win), "every win");
            Assert.AreEqual(2, p.OpponentsToday["p9"]);
            Assert.AreEqual(2, p.Wins);
            Assert.AreEqual(10, DuelBookkeeping.ApplyResult(p, "p8", 1150, DuelResult.Loss));
            Assert.AreEqual(9, DuelBookkeeping.ApplyResult(p, "p7", 1150, DuelResult.Draw), "a draw counts as a loss");
            for (int i = 0; i < 12; i++) DuelBookkeeping.ApplyResult(p, "x" + i, 1150, DuelResult.Loss);
            Assert.AreEqual(0, DuelBookkeeping.ApplyResult(p, "p6", 1150, DuelResult.Loss), "down to nothing");
            Assert.AreEqual(20, DuelBookkeeping.ApplyResult(p, "p5", 1150, DuelResult.Win));
            for (int i = 0; i < 30; i++) DuelBookkeeping.RecordDuelPlayed(p, 45000, day, false);
            Assert.AreEqual(PvpConfig.MaxCountedDuelsPerDay, p.CountedDuelsToday);
            DuelBookkeeping.RecordDuelPlayed(p, 5000, day, false);
            Assert.AreEqual(15, p.SeasonCountedDuels, "a too short duel does not count");
            Seasons.Roll(p, day.AddDays(1));
            Assert.AreEqual(10, DuelBookkeeping.ApplyResult(p, "p8", 1150, DuelResult.Loss), "a new day starts again");
        }

        [Test]
        public void SeasonRewards_LeagueAndBelow_Participation_Top100()
        {
            var sum = new SeasonSummary { Season = "2026-10", FinalElo = 1320, Duels = 40, DuelsLastWeek = 6, CountedDuels = 30 };
            var rewards = SeasonRewards.Compute(sum, out bool eligible);
            Assert.IsTrue(eligible);
            Assert.That(rewards, Does.Contain("pvp_2026-10_rank_platine").And.Contain("pvp_2026-10_rank_bronze"));
            Assert.That(rewards, Does.Not.Contain("pvp_2026-10_rank_diamant"));
            Assert.That(rewards, Does.Contain("pvp_2026-10_participation_25").And.Not.Contain("pvp_2026-10_participation_50"));
            var lazy = SeasonRewards.Compute(new SeasonSummary { Season = "2026-10", FinalElo = 1600, Duels = 20, DuelsLastWeek = 2, CountedDuels = 12 }, out bool lazyEligible);
            Assert.IsFalse(lazyEligible);
            Assert.AreEqual(new[] { "pvp_2026-10_participation_10" }, lazy.ToArray());
            Assert.AreEqual("pvp_2026-10_rank_top100", SeasonRewards.Top100("2026-10", 42, true));
            Assert.IsNull(SeasonRewards.Top100("2026-10", 142, true));
            Assert.IsNull(SeasonRewards.Top100("2026-10", 42, false));
        }

        [Test]
        public void SealShop_ChecksLeagueAndBalance()
        {
            var d = new PlayerPvpData { Seals = 600, HighestLeague = League.Argent };
            Assert.AreEqual("LEAGUE", SealShop.TryBuy(d, "pvp_shop_obsidian"));
            Assert.IsNull(SealShop.TryBuy(d, "pvp_shop_feather"));
            Assert.AreEqual(100, d.Seals);
            Assert.AreEqual("OWNED", SealShop.TryBuy(d, "pvp_shop_feather"));
            Assert.AreEqual("SEALS", SealShop.TryBuy(d, "pvp_shop_scales"));
            Assert.AreEqual("UNKNOWN_ITEM", SealShop.TryBuy(d, "nope"));
        }

        // ------------------------------------------------------------------ the race

        [Test]
        public void Actions_RoundTrip()
        {
            foreach (var d in DirExt.All)
                foreach (var a in new[] { PlayerAction.Move(d), PlayerAction.Disarm(d) })
                {
                    Assert.IsTrue(RunActions.TryDecode(RunActions.Encode(a), out var back));
                    Assert.AreEqual(a, back);
                }
            Assert.AreEqual(1, RunActions.Encode(PlayerAction.Move(Dir.Up)), "the pack's numbering: 1 = up");
            Assert.AreEqual(4, RunActions.Encode(PlayerAction.Move(Dir.Left)));
            Assert.IsFalse(RunActions.TryDecode(0, out _));
        }

        [Test]
        public void Arena_IsDeterministic_AndDrawsMiddleLevelsOfActs1To3()
        {
            foreach (int seed in new[] { 1, 2, 3, 77, 123456, int.MaxValue })
            {
                var id = PvpArena.LevelFor(seed);
                Assert.That(id.Act, Is.InRange(1, 3));
                Assert.That(id.Index, Is.InRange(4, 7));
            }
            var a = PvpArena.Generate(4242);
            var b = PvpArena.Generate(4242);
            Assert.AreEqual(a.ToAscii(), b.ToAscii(), "same seed, same tomb on every device");
        }

        /// <summary>
        /// The ideal route, one action every <paramref name="gapMs"/> ms, or later when the game is still animating the
        /// previous one (a portal, a trap: <see cref="RunTiming"/>).
        /// </summary>
        static List<RunInput> Stamp(Level level, IEnumerable<PlayerAction> actions, int gapMs, int startMs = 500)
        {
            var inputs = new List<RunInput>();
            var s = Rules.Initial(level);
            int ms = startMs;
            foreach (var a in actions)
            {
                inputs.Add(new RunInput { Tick = RunActions.TickOf(ms), Direction = RunActions.Encode(a) });
                var r = Rules.Step(level, s, a);
                ms += Math.Max(gapMs, RunTiming.MinGapMs(level, s, a, r));
                s = r.State;
            }
            return inputs;
        }

        /// <summary>A finished run as the game sends it: the clock stops on the last action.</summary>
        static RunSubmission Finish(string matchId, List<RunInput> inputs) => new RunSubmission
        {
            MatchId = matchId, Outcome = RunOutcome.Finished, Progress = 1, Inputs = inputs,
            TimeMs = RunActions.MsOf(inputs[inputs.Count - 1].Tick),
        };

        [Test]
        public void Replay_VerifiesWhatItPlays()
        {
            var level = PvpArena.Generate(1001);
            var inputs = Stamp(level, level.Solution.Actions, 400);
            var won = RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.Finished, TimeMs = 1, Progress = 0, Inputs = inputs });
            Assert.IsNotNull(won);
            Assert.AreEqual(RunOutcome.Finished, won.Outcome);
            Assert.AreEqual(1f, won.Progress);
            Assert.AreEqual(RunActions.MsOf(inputs[inputs.Count - 1].Tick), won.TimeMs, "the server's own clock, not the client's");

            var half = inputs.Take(inputs.Count / 2).ToList();
            Assert.IsNull(RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.Finished, Inputs = half }), "claims an exit it never reached");
            var timedOut = RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.TimedOut, Inputs = half });
            Assert.AreEqual(RunOutcome.TimedOut, timedOut.Outcome);
            Assert.That(timedOut.Progress, Is.GreaterThan(0f).And.LessThan(1f));

            // A move into a wall is never recorded by the game: a run containing one was forged.
            var forged = new List<RunInput>();
            foreach (var d in DirExt.All)
                if (Rules.Step(level, Rules.Initial(level), PlayerAction.Move(d)).Has(StepFlags.Blocked))
                {
                    forged.Add(new RunInput { Tick = 10, Direction = RunActions.Encode(PlayerAction.Move(d)) });
                    break;
                }
            Assume.That(forged.Count, Is.EqualTo(1));
            Assert.IsNull(RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.TimedOut, Inputs = forged }));
        }

        [Test]
        public void Timing_FastestRunIsAcceptedAndNothingFaster()
        {
            foreach (int seed in new[] { 1001, 2002, 3003, 4004, 5005, 6006 })
            {
                var level = PvpArena.Generate(seed);
                var fastest = Solver.SolveFastest(level, (st, a, r) => RunTiming.MinGapMs(level, st, a, r), SolverOptions.Default);
                Assert.IsNotNull(fastest);
                Assert.AreEqual(fastest.Cost, RunTiming.MinFinishMs(level));
                Assert.That(fastest.Cost, Is.GreaterThanOrEqualTo((level.Solution.Moves - 1) * RunTiming.WalkMs), "never under one walk per move");

                // Swiping the instant the game allows, from the moment the fog falls.
                var inputs = new List<RunInput>();
                var s = Rules.Initial(level);
                int ms = 0;
                foreach (var a in fastest.Actions)
                {
                    inputs.Add(new RunInput { Tick = RunActions.TickOf(ms), Direction = RunActions.Encode(a) });
                    var r = Rules.Step(level, s, a);
                    ms += RunTiming.MinGapMs(level, s, a, r);
                    s = r.State;
                }
                var replay = new RunReplay(level, inputs);
                replay.AdvanceTo(int.MaxValue);
                Assert.AreEqual(SessionStatus.Won, replay.Session.Status);
                Assert.IsFalse(replay.TooFast, $"seed {seed}: the fastest possible run");
                Assert.That(RunActions.MsOf(replay.LastTick), Is.InRange(fastest.Cost - 20, fastest.Cost));

                // One action sent 40 ms before the previous one finished animating: only a modified game does that.
                int k = inputs.Count / 2;
                var cheat = inputs.Select(i => new RunInput { Tick = i.Tick, Direction = i.Direction }).ToList();
                for (int i = k; i < cheat.Count; i++) cheat[i].Tick -= 2;
                var forged = new RunReplay(level, cheat);
                forged.AdvanceTo(int.MaxValue);
                Assert.IsTrue(forged.TooFast, $"seed {seed}: 40 ms ahead of the animation");

                // Every step a little quicker (100 ms walks instead of 130): each gap alone looks almost fine, the sum does not.
                var hurried = new List<RunInput>();
                var hs = Rules.Initial(level);
                int hms = 0;
                foreach (var a in fastest.Actions)
                {
                    hurried.Add(new RunInput { Tick = RunActions.TickOf(hms), Direction = RunActions.Encode(a) });
                    var r = Rules.Step(level, hs, a);
                    hms += RunTiming.MinGapMs(level, hs, a, r) * 100 / 130;
                    hs = r.State;
                }
                var quick = new RunReplay(level, hurried);
                quick.AdvanceTo(int.MaxValue);
                Assert.IsTrue(quick.TooFast, $"seed {seed}: 23 % faster than the animations");
                Assert.IsNull(RunReplay.Verify(level, new RunSubmission { Outcome = RunOutcome.Finished, Inputs = cheat }));
            }
        }

        [Test]
        public void Timing_HonestRecordingAndBotsRespectTheAnimations()
        {
            var rng = new Random(21);
            for (int i = 0; i < 20; i++)
            {
                var ghost = PvpBots.Make(rng, 1000 + 60 * i, 0);
                var level = PvpArena.Generate(ghost.Seed);
                var replay = new RunReplay(level, ghost.Inputs);
                replay.AdvanceTo(int.MaxValue);
                Assert.IsFalse(replay.TooFast);
                if (ghost.Outcome == RunOutcome.Finished) Assert.That(ghost.TimeMs, Is.GreaterThanOrEqualTo(RunTiming.MinFinishMs(level)));
            }

            // The game records each action when it starts, the animation of the last one having just ended.
            var arena = PvpArena.Generate(3003);
            var match = new PvpMatch(new FindDuelResponse { MatchId = "t1", Seed = 3003 });
            match.Begin(arena);
            var session = new GameSession(arena);
            double clock = 0;
            foreach (var a in arena.Solution.Actions)
            {
                var before = session.State;
                var r = session.Apply(a);
                match.Record(a, (int)clock, before, session);
                clock += RunTiming.MinGapMs(arena, before, a, r) * 0.9999; // the float clock lands a hair early
            }
            var run = match.BuildRun(RunOutcome.Finished);
            var check = new RunReplay(arena, run.Inputs);
            check.AdvanceTo(int.MaxValue);
            Assert.IsFalse(check.TooFast);
        }

        [Test]
        public void Pace_SoloTimeToBeatAndPerfectMinimum()
        {
            foreach (var id in new[] { new LevelId(1, 5), new LevelId(3, 10), new LevelId(5, 5) })
            {
                var level = LevelGenerator.Generate(id, 2);
                var pace = TombPace.Of(level);
                Assert.IsTrue(pace.PerfectMs.HasValue && pace.TargetMs.HasValue, id.ToString());
                Assert.Greater(pace.TargetMs.Value, pace.PerfectMs.Value, "the expert mummy is not perfect");
                Assert.AreEqual(pace.TargetMs, TombPace.Of(level).TargetMs, "the same time to beat on every device");

                int par = level.Solution.Moves, perfect = pace.PerfectMs.Value;
                Assert.AreEqual(PaceVerdict.Normal, pace.Judge(pace.TargetMs.Value, par));
                Assert.AreEqual(PaceVerdict.Impossible, pace.Judge(perfect - 100, par));
                Assert.AreEqual(par >= TombPace.SuspiciousMinPar ? PaceVerdict.Suspicious : PaceVerdict.Normal, pace.Judge(perfect, par));
                Assert.AreEqual(PaceVerdict.Normal, pace.Judge(perfect * 2, par));
            }
        }

        [Test]
        public void Progress_GoesFromZeroToOne()
        {
            var level = PvpArena.Generate(2002);
            Assert.AreEqual(0f, PvpProgress.Of(level, Rules.Initial(level)));
            var replay = new RunReplay(level, Stamp(level, level.Solution.Actions, 300));
            replay.AdvanceTo(RunActions.TickOf(500 + 300 * (level.Solution.Moves / 2)));
            Assert.That(replay.Progress, Is.InRange(0.3f, 0.7f));
            replay.AdvanceTo(int.MaxValue);
            Assert.AreEqual(1f, replay.Progress);
        }

        [Test]
        public void Bots_RunValidRaces()
        {
            var rng = new Random(5);
            for (int i = 0; i < 5; i++)
            {
                var ghost = PvpBots.Make(rng, 1000, 0);
                var check = RunReplay.Verify(PvpArena.Generate(ghost.Seed), new RunSubmission { Outcome = ghost.Outcome, Inputs = ghost.Inputs });
                Assert.IsNotNull(check);
                // Bots play like people: most reach the exit, some die in a trap or run out of time.
                Assert.AreEqual(ghost.Outcome, check.Outcome);
                Assert.AreEqual(ghost.TimeMs, check.TimeMs);
                if (ghost.Outcome != RunOutcome.Finished) continue;
                Assert.That(RunValidator.IsPlausible(new RunSubmission { Outcome = ghost.Outcome, TimeMs = ghost.TimeMs, Progress = 1, Inputs = ghost.Inputs }, out var why), Is.True, why);
            }
        }

        // ------------------------------------------------------------------ the server

        static readonly DateTime Today = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        /// <summary>The server clock of the current test (a search waits on it).</summary>
        static DateTime _now;

        static (PvpServer server, MemoryPvpStore store) NewServer()
        {
            var store = new MemoryPvpStore();
            store.SoloStars["alice"] = 40;
            store.SoloStars["bob"] = 60;
            int seed = 3003;
            _now = Today;
            return (new PvpServer(store, () => _now, () => seed++), store);
        }

        /// <summary>FindDuel as the game calls it: asked again while the server searches the league (the minute passes).</summary>
        static FindDuelResponse Find(PvpServer server, string player, bool allowBots = false)
        {
            var r = server.FindDuelAsync(player, DifficultyTable.GeneratorVersion, allowBots).Result;
            if (!r.Searching) return r;
            _now = _now.AddMilliseconds(PvpConfig.BotFallbackMs + 1000);
            return server.FindDuelAsync(player, DifficultyTable.GeneratorVersion, allowBots).Result;
        }

        [Test]
        public void Server_LocksPvpBelowAct3_AndRefusesOldClients()
        {
            var (server, store) = NewServer();
            store.SoloStars["carol"] = PvpConfig.RequiredSoloStars - 1;
            Assert.AreEqual("LOCKED", Find(server, "carol").Error);
            Assert.AreEqual("OUTDATED", server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion - 1).Result.Error);
        }

        [Test]
        public void Server_SearchesTheLeagueForAMinute_ThenThePlayerRunsFirst()
        {
            var (server, store) = NewServer();
            var r = server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion, false).Result;
            Assert.IsTrue(r.Searching, "nobody of the league yet: the server keeps looking");
            Assert.IsNull(r.MatchId);
            Assert.IsNull(store.GetPendingAsync("alice").Result, "nothing is created while searching");
            _now = _now.AddSeconds(30);
            r = server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion, false).Result;
            Assert.IsTrue(r.Searching);
            Assert.AreEqual(30_000, r.SearchedMs, "the minute is counted by the server, not the game");
            _now = _now.AddSeconds(31);
            r = server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion, false).Result;
            Assert.IsFalse(r.Searching);
            Assert.IsNull(r.Ghost, "no bots wanted: alice runs first and becomes the ghost of the next player of her league");
            Assert.AreEqual(0, store.Players["alice"].DuelSearchSinceUnixMs, "the next search starts from zero");
        }

        [Test]
        public void Server_AfterAMinute_ABotOfTheLeague_OnlyThePlayerIsUpdated()
        {
            var (server, store) = NewServer();
            var duel = Find(server, "alice", allowBots: true);
            Assert.IsNotNull(duel.Ghost);
            Assert.IsTrue(duel.Ghost.Bot);
            Assert.AreEqual(Leagues.FromElo(PvpConfig.StartingElo), Leagues.FromElo(duel.Ghost.Elo), "a bot of her league");
            var level = PvpServer.Arena(duel.Seed);
            var result = server.SubmitRunAsync("alice", Finish(duel.MatchId, Stamp(level, level.Solution.Actions, 300)), "Alice").Result;
            Assert.IsTrue(result.Resolved);
            Assert.AreNotEqual(result.EloBefore, result.EloAfter);
            Assert.IsFalse(store.Players.ContainsKey(duel.Ghost.PlayerId), "a bot has no record");
            Assert.IsFalse(store.Board.ContainsKey(duel.Ghost.PlayerId), "nor a place in the ranking");
        }

        [Test]
        public void Ghosts_AreAlwaysOfThePlayersLeague()
        {
            var platinum = new GhostRun { PlayerId = "p", Elo = PvpConfig.DiamantMin - 50 };
            var diamond = new GhostRun { PlayerId = "d", Elo = PvpConfig.DiamantMin + 200 };
            Assert.IsNull(GhostPicker.Pick(new[] { platinum }, "me", PvpConfig.DiamantMin + 20, null, 0), "closer in Elo, but not a diamond");
            Assert.AreSame(diamond, GhostPicker.Pick(new[] { platinum, diamond }, "me", PvpConfig.DiamantMin + 20, null, 0));
            var rng = new Random(5);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(League.Diamant, Leagues.FromElo(PvpBots.Make(rng, PvpConfig.DiamantMin + 10, 0).Elo));
                Assert.AreEqual(League.Bronze, Leagues.FromElo(PvpBots.Make(rng, PvpConfig.ArgentMin - 10, 0).Elo));
            }
        }

        [Test]
        public void Server_FirstRunBecomesAGhost_SecondPlayerRacesItOnTheSameTomb()
        {
            var (server, store) = NewServer();

            var first = Find(server, "alice");
            Assert.IsNull(first.Error);
            Assert.IsNull(first.Ghost, "nobody waiting: alice runs first");
            var level = PvpServer.Arena(first.Seed);
            var slow = Finish(first.MatchId, Stamp(level, level.Solution.Actions, 900));
            var queued = server.SubmitRunAsync("alice", slow, "Alice").Result;
            Assert.IsNull(queued.Error);
            Assert.IsFalse(queued.Resolved);
            Assert.AreEqual(1, store.Queue.Count);
            Assert.That(queued.SealsGained, Is.GreaterThan(0), "daily chest");

            var second = Find(server, "bob");
            Assert.IsNotNull(second.Ghost);
            Assert.AreEqual("Alice", second.Ghost.PlayerName);
            Assert.AreEqual(first.Seed, second.Seed, "same seed, same tomb");
            Assert.AreEqual(0, store.Queue.Count, "a ghost serves one duel only");

            var fast = Finish(second.MatchId, Stamp(level, level.Solution.Actions, 400));
            var duel = server.SubmitRunAsync("bob", fast, "Bob").Result;
            Assert.IsTrue(duel.Resolved);
            Assert.AreEqual(DuelResult.Win, duel.Result);
            Assert.That(duel.EloAfter, Is.GreaterThan(duel.EloBefore));
            Assert.That(store.Players["alice"].Elo, Is.LessThan(PvpConfig.StartingElo));
            Assert.AreEqual(1, store.Players["alice"].Losses);

            // Both Elos are published, and both players count as ranked this month.
            Assert.AreEqual(store.Players["bob"].Elo, store.Board["bob"]);
            Assert.AreEqual(store.Players["alice"].Elo, store.Board["alice"]);
            var board = server.GetBoardAsync("alice", 0, 100).Result;
            Assert.AreEqual(new[] { "bob", "alice" }, board.Rows.Select(r => r.PlayerId).ToArray());
            Assert.AreEqual(2, board.Me.Rank);
        }

        static PlayerPvpData RankedPlayer(int elo) =>
            new PlayerPvpData { Elo = elo, Season = "2026-10", Day = "2026-10-04", SeasonDuels = 3, Ranked = true };

        [Test]
        public void Board_RewritesForgedScores_AndDropsPlayersWithoutADuel()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = RankedPlayer(1100);
            store.Players["bob"] = RankedPlayer(1050);
            store.Players["carol"] = new PlayerPvpData { Season = "2026-10", Day = "2026-10-04" }; // opened the duel screen only
            store.Board["alice"] = 1100;
            store.Board["bob"] = 5000;      // posted by the client itself
            store.Board["carol"] = 1000;
            store.Board["mallory"] = 9000;  // no PvP data at all

            var board = server.GetBoardAsync("alice", 0, 100).Result;
            Assert.AreEqual(new[] { "alice", "bob" }, board.Rows.Select(r => r.PlayerId).ToArray());
            Assert.AreEqual(new[] { 1100, 1050 }, board.Rows.Select(r => r.Elo).ToArray());
            Assert.AreEqual(1, board.Me.Rank);
            Assert.IsTrue(board.Me.IsMe);

            // The leaderboard itself is repaired.
            Assert.AreEqual(1050, store.Board["bob"]);
            Assert.AreEqual(0, store.Board["carol"]);
            Assert.AreEqual(0, store.Board["mallory"]);
        }

        [Test]
        public void Board_ACheaterCannotBuyAWorldRank()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = RankedPlayer(1100);
            store.Players["bob"] = RankedPlayer(900);
            store.Board["alice"] = 1100;
            store.Board["bob"] = 99_999;

            var profile = server.GetProfileAsync("bob").Result;
            Assert.AreEqual(2, profile.WorldRank);
            Assert.AreEqual(900, store.Board["bob"]);
            Assert.AreEqual(1, server.GetProfileAsync("alice").Result.WorldRank);
            Assert.AreEqual(0, server.GetProfileAsync("carol").Result.WorldRank);
        }

        [Test]
        public void Board_SeesFreshEloDespiteTheCache()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = RankedPlayer(1100);
            store.Players["bob"] = RankedPlayer(1000);
            store.Board["alice"] = 1100;
            store.Board["bob"] = 1000;
            Assert.AreEqual(2, server.GetBoardAsync("bob", 0, 100).Result.Me.Rank);

            // Bob climbs within the cache's lifetime: his own row is read again.
            store.Players["bob"].Elo = 1200;
            store.Board["bob"] = 1200;
            var board = server.GetBoardAsync("bob", 0, 100).Result;
            Assert.AreEqual(1, board.Me.Rank);
            Assert.AreEqual(new[] { "bob", "alice" }, board.Rows.Select(r => r.PlayerId).ToArray());
        }

        [Test]
        public void Board_ShowsEachPlayersRecordOfTheMonth()
        {
            var (server, store) = NewServer();
            PlayDuel(server, store, "bob");
            var board = server.GetBoardAsync("alice", 0, 100).Result;
            var bobRow = board.Rows.Single(r => r.PlayerId == "bob");
            Assert.AreEqual((1, 0, 0), (bobRow.Wins, bobRow.Draws, bobRow.Losses));
            Assert.AreEqual((0, 0, 1), (board.Me.Wins, board.Me.Draws, board.Me.Losses));

            // A new month starts from zero; last month's board keeps the final record.
            var data = store.Players["bob"];
            Seasons.Roll(data, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
            Assert.AreEqual(0, data.SeasonWins);
            Assert.AreEqual(1, data.LastSeason.Wins);
            Assert.AreEqual(1, data.Wins, "the overall record is kept");
        }

        [Test]
        public void Board_LastMonthIsRecomputedFromTheFinalElo()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = new PlayerPvpData { Elo = 1320, Season = "2026-09", SeasonDuels = 40, Ranked = true };
            store.Players["bob"] = new PlayerPvpData { Elo = 1000, Season = "2026-09", SeasonDuels = 4, Ranked = true };
            store.LastBoard["alice"] = 1320;
            store.LastBoard["bob"] = 7000;
            store.LastBoard["mallory"] = 8000;

            var board = server.GetBoardAsync("bob", 1, 100).Result;
            Assert.AreEqual("2026-09", board.Season);
            Assert.AreEqual(new[] { "alice", "bob" }, board.Rows.Select(r => r.PlayerId).ToArray());
            Assert.AreEqual(2, board.Me.Rank);
            Assert.AreEqual(7000, store.LastBoard["bob"], "archives are frozen: only the shown ranking changes");
        }

        [Test]
        public void Server_AcceptsAForfeitMidRun()
        {
            var (server, store) = NewServer();
            var duel = Find(server, "alice");
            var level = PvpServer.Arena(duel.Seed);
            // What the pause menu sends (PvpMatch.BuildRun): the steps already taken, no time.
            var forfeit = new RunSubmission
            {
                MatchId = duel.MatchId, Outcome = RunOutcome.Abandoned,
                Inputs = Stamp(level, level.Solution.Actions.Take(8), 900, 3000),
            };
            var result = server.SubmitRunAsync("alice", forfeit, "Alice").Result;
            Assert.IsNull(result.Error);
            Assert.IsFalse(result.Resolved);
            Assert.AreEqual(0, store.Queue.Count, "a forfeit never becomes a ghost");
        }

        [Test]
        public void Server_ReplaysTheRun_ACheaterLoses()
        {
            var (server, store) = NewServer();
            var first = Find(server, "alice");
            var level = PvpServer.Arena(first.Seed);
            server.SubmitRunAsync("alice", Finish(first.MatchId, Stamp(level, level.Solution.Actions, 900)), "Alice").Wait();

            var second = Find(server, "bob");
            // Bob claims a lightning exit after only three moves.
            var lie = new RunSubmission { MatchId = second.MatchId, Outcome = RunOutcome.Finished, TimeMs = 5000, Progress = 1,
                                          Inputs = Stamp(level, level.Solution.Actions.Take(3), 400) };
            var result = server.SubmitRunAsync("bob", lie, "Bob").Result;
            Assert.AreEqual("INVALID_RUN", result.Error);
            Assert.AreEqual(DuelResult.Loss, result.Result);
            Assert.AreEqual(1, store.Players["alice"].Wins);
        }

        /// <summary>Alice runs first on a tomb, then <paramref name="rival"/> races her ghost.</summary>
        static (DuelRecord alice, DuelRecord rival) PlayDuel(PvpServer server, MemoryPvpStore store, string rival)
        {
            store.SoloStars[rival] = 50;
            var first = Find(server, "alice");
            var level = PvpServer.Arena(first.Seed);
            var run = Finish(first.MatchId, Stamp(level, level.Solution.Actions, 900));
            run.Look = new PlayerLook { Mummy = "mummy_classic", Hat = "BAD hat!" };
            server.SubmitRunAsync("alice", run, "Alice").Wait();

            var second = Find(server, rival);
            Assert.AreEqual("mummy_classic", second.Ghost.Look.Mummy, "the ghost carries its look for the VS screen");
            Assert.IsNull(second.Ghost.Look.Hat, "unsafe ids are dropped");
            server.SubmitRunAsync(rival, Finish(second.MatchId, Stamp(level, level.Solution.Actions, 700)), rival).Wait();
            return (server.GetHistoryAsync("alice").Result.Duels.Find(d => d.MatchId == first.MatchId),
                    server.GetHistoryAsync(rival).Result.Duels.Find(d => d.MatchId == second.MatchId));
        }

        [Test]
        public void History_KeepsBothRuns_AndCompletesTheFirstRunnersDuel()
        {
            var (server, store) = NewServer();
            var first = Find(server, "alice");
            var level = PvpServer.Arena(first.Seed);
            server.SubmitRunAsync("alice", Finish(first.MatchId, Stamp(level, level.Solution.Actions, 900)), "Alice").Wait();
            var open = server.GetHistoryAsync("alice").Result.Duels.Single();
            Assert.IsFalse(open.Resolved);
            Assert.IsNull(open.Rival);
            Assert.AreEqual(level.Solution.Actions.Count, open.Me.Inputs.Count);

            var second = Find(server, "bob");
            server.SubmitRunAsync("bob", Finish(second.MatchId, Stamp(level, level.Solution.Actions, 700)), "Bob").Wait();

            var alice = server.GetHistoryAsync("alice").Result.Duels.Single();
            Assert.IsTrue(alice.Resolved, "racing her ghost completes Alice's duel");
            Assert.AreEqual(DuelResult.Loss, alice.Result);
            Assert.AreEqual("Bob", alice.Rival.PlayerName);
            Assert.Less(alice.EloAfter, alice.EloBefore);
            var bob = server.GetHistoryAsync("bob").Result.Duels.Single();
            Assert.AreEqual(DuelResult.Win, bob.Result);
            Assert.AreEqual(first.Seed, bob.Seed);
            Assert.AreEqual("Alice", bob.Rival.PlayerName);

            // Both runs replay to their verdict on the same tomb.
            foreach (var run in new[] { bob.Me, bob.Rival })
            {
                var replay = new RunReplay(PvpServer.Arena(bob.Seed), run.Inputs);
                replay.AdvanceTo(int.MaxValue);
                Assert.AreEqual(SessionStatus.Won, replay.Session.Status);
            }
        }

        [Test]
        public void History_KeepsTheTenLatestDuels()
        {
            var history = new List<DuelRecord>();
            for (int i = 0; i < 14; i++) PvpServer.Remember(history, new DuelRecord { MatchId = "m" + i, PlayedAtUnixMs = 1000 + i });
            Assert.AreEqual(PvpConfig.HistorySize, history.Count);
            Assert.AreEqual("m13", history[0].MatchId);
            Assert.AreEqual("m4", history[9].MatchId);
            // Completing an open duel keeps its place.
            PvpServer.Remember(history, new DuelRecord { MatchId = "m8", PlayedAtUnixMs = 1008, Resolved = true });
            Assert.AreEqual(10, history.Count);
            Assert.IsTrue(history[5].Resolved);
        }

        [Test]
        public void Report_FilesTheDuelInADossier_FlaggedAfterThreeReporters()
        {
            var (server, store) = NewServer();
            var (alice, bob) = PlayDuel(server, store, "bob");
            Assert.AreEqual("UNKNOWN_DUEL", server.ReportCheatAsync("bob", "nope").Result.Error);
            Assert.IsTrue(server.ReportCheatAsync("bob", bob.MatchId).Result.Ok);
            Assert.AreEqual("ALREADY_REPORTED", server.ReportCheatAsync("bob", bob.MatchId).Result.Error);
            Assert.IsTrue(server.GetHistoryAsync("bob").Result.Duels.Single().Reported);

            var dossier = store.Dossiers["alice"];
            Assert.AreEqual(1, dossier.Reports.Count);
            Assert.AreEqual(bob.Seed, dossier.Reports[0].Seed);
            Assert.AreEqual("Alice", dossier.Reports[0].Suspect.PlayerName);
            Assert.IsFalse(dossier.Flagged);

            // The same reporter again on another duel: still one reporter.
            var (_, bob2) = PlayDuel(server, store, "bob");
            server.ReportCheatAsync("bob", bob2.MatchId).Wait();
            Assert.AreEqual(1, store.Dossiers["alice"].Reporters.Count);
            Assert.IsFalse(store.Dossiers["alice"].Flagged);

            foreach (var other in new[] { "carol", "dan" })
            {
                var (_, theirs) = PlayDuel(server, store, other);
                Assert.IsTrue(server.ReportCheatAsync(other, theirs.MatchId).Result.Ok);
            }
            Assert.AreEqual(3, store.Dossiers["alice"].Reporters.Count);
            Assert.IsTrue(store.Dossiers["alice"].Flagged, "three different players: to be checked");
            Assert.IsFalse(store.Dossiers.ContainsKey("bob"));
            Assert.AreEqual("alice", alice.Me.PlayerId);
        }

        [Test]
        public void Report_LimitedPerDay_AndNeverWithoutARival()
        {
            var (server, store) = NewServer();
            store.Players["bob"] = new PlayerPvpData { Day = "2026-10-04", ReportsToday = PvpConfig.MaxReportsPerDay };
            var (_, bob) = PlayDuel(server, store, "bob");
            Assert.AreEqual("LIMIT", server.ReportCheatAsync("bob", bob.MatchId).Result.Error);
            var first = Find(server, "alice");
            var level = PvpServer.Arena(first.Seed);
            server.SubmitRunAsync("alice", Finish(first.MatchId, Stamp(level, level.Solution.Actions, 900)), "Alice").Wait();
            Assert.AreEqual("NO_RIVAL", server.ReportCheatAsync("alice", first.MatchId).Result.Error);
        }

        [Test]
        public void Server_SearchingAgainKeepsTheSameDuel()
        {
            var (server, _) = NewServer();
            var a = Find(server, "alice");
            var b = Find(server, "alice");
            Assert.AreEqual(a.MatchId, b.MatchId);
            Assert.AreEqual(a.Seed, b.Seed);
        }

        [Test]
        public void Server_ClaimsSeasonRewards_Once()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = new PlayerPvpData
            {
                Elo = 1320, Season = "2026-09", Day = "2026-09-30", SeasonDuels = 40, SeasonDuelsLastWeek = 6, SeasonCountedDuels = 30,
                Ranked = true,
            };
            store.LastBoard["alice"] = 1320;
            var claim = server.ClaimSeasonRewardsAsync("alice").Result;
            Assert.AreEqual("2026-09", claim.Season);
            Assert.That(claim.NewRewards, Does.Contain("pvp_2026-09_rank_platine").And.Contain("pvp_2026-09_rank_top100")
                                              .And.Contain("pvp_2026-09_participation_25"));
            Assert.AreEqual("NOTHING_TO_CLAIM", server.ClaimSeasonRewardsAsync("alice").Result.Error);
            Assert.That(store.Players["alice"].UnlockedRewards, Does.Contain("pvp_2026-09_rank_top100"));
        }

        [Test]
        public void Server_SellsSealItems()
        {
            var (server, store) = NewServer();
            store.Players["alice"] = new PlayerPvpData { Seals = 350, HighestLeague = League.Bronze };
            var buy = server.BuyWithSealsAsync("alice", "pvp_shop_scales").Result;
            Assert.IsTrue(buy.Ok);
            Assert.AreEqual(50, buy.Seals);
            Assert.That(buy.UnlockedRewards, Does.Contain("pvp_shop_scales"));
            Assert.AreEqual("OWNED", server.BuyWithSealsAsync("alice", "pvp_shop_scales").Result.Error);
            Assert.AreEqual("LEAGUE", server.BuyWithSealsAsync("alice", "pvp_shop_feather").Result.Error);
        }

        // ------------------------------------------------------------------ the game side of a duel

        [Test]
        public void Match_RecordsTheRunTheServerWillAccept()
        {
            var ghost = PvpBots.Make(new Random(11), 1100, 0);
            var match = new PvpMatch(new FindDuelResponse { MatchId = "m1", Seed = ghost.Seed, Ghost = ghost });
            var level = PvpArena.Generate(ghost.Seed);
            match.Begin(level);
            var session = new GameSession(level);

            // Plays the ideal path a little faster than the ghost, two actions sometimes in the same 20 ms.
            int ms = 700;
            foreach (var action in level.Solution.Actions)
            {
                var before = session.State;
                var r = session.Apply(action);
                Assert.IsFalse(r.Has(StepFlags.Blocked));
                match.Record(action, ms, before, session);
                ms += 300;
            }
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.AreEqual(1f, match.MyProgress);
            for (int i = 1; i < match.Inputs.Count; i++)
                Assert.GreaterOrEqual(match.Inputs[i].Tick - match.Inputs[i - 1].Tick, PvpConfig.MinInputGapTicks);

            var run = match.BuildRun(RunOutcome.Finished);
            Assert.AreEqual(RunOutcome.Finished, run.Outcome);
            Assert.That(RunValidator.IsPlausible(run, out var why), Is.True, why);
            var server = RunReplay.Verify(level, run);
            Assert.AreEqual(run.TimeMs, server.TimeMs);
            Assert.AreEqual(RunActions.MsOf(match.Inputs[match.Inputs.Count - 1].Tick), run.TimeMs);
        }

        [Test]
        public void Match_GhostFollowsThePlayersClock()
        {
            // Bots play like people and may die in a trap: take one that reaches the exit.
            var rng = new Random(12);
            GhostRun ghost;
            do ghost = PvpBots.Make(rng, 1000, 0);
            while (ghost.Outcome != RunOutcome.Finished);
            var match = new PvpMatch(new FindDuelResponse { MatchId = "m2", Seed = ghost.Seed, Ghost = ghost });
            match.Begin(PvpArena.Generate(ghost.Seed));

            Assert.IsFalse(match.AdvanceGhost(0), "nothing before its first action");
            Assert.AreEqual(match.Level.Start, match.GhostReplay.Session.Position);
            int half = RunActions.MsOf(ghost.Inputs[ghost.Inputs.Count / 2].Tick);
            Assert.IsTrue(match.AdvanceGhost(half));
            Assert.That(match.GhostProgress, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.IsFalse(match.GhostDone(half));
            match.AdvanceGhost(ghost.TimeMs);
            Assert.AreEqual(SessionStatus.Won, match.GhostReplay.Session.Status);
            Assert.AreEqual(1f, match.GhostProgress);
            Assert.IsTrue(match.GhostDone(ghost.TimeMs));
        }

        [Test]
        public void Match_TimeOutAndForfeit()
        {
            var match = new PvpMatch(new FindDuelResponse { MatchId = "m3", Seed = 77 });
            var level = PvpArena.Generate(77);
            match.Begin(level);
            Assert.IsNull(match.GhostReplay, "first on this tomb: no ghost");
            var session = new GameSession(level);
            var first = level.Solution.Actions[0];
            var before = session.State;
            session.Apply(first);
            match.Record(first, 1500, before, session);

            var timedOut = match.BuildRun(RunOutcome.TimedOut);
            Assert.AreEqual(RunOutcome.TimedOut, timedOut.Outcome);
            Assert.AreEqual(PvpConfig.TimeLimitMs, timedOut.TimeMs);
            Assert.That(timedOut.Progress, Is.GreaterThan(0f));

            var forfeit = match.BuildRun(RunOutcome.Abandoned);
            Assert.AreEqual(RunOutcome.Abandoned, forfeit.Outcome);
            match.Over = true;
            match.Record(first, 3000, before, session);
            Assert.AreEqual(1, match.Inputs.Count, "nothing is recorded once the run is over");
        }
    }
}
