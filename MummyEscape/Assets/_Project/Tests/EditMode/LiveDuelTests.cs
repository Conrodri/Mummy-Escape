using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The live duel: the judge (first out wins, first dead loses), the server, the bots of the league.</summary>
    public class LiveDuelTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

        static RunSubmission Run(RunOutcome outcome, int timeMs, float progress = 0.5f) =>
            new RunSubmission { Outcome = outcome, TimeMs = timeMs, Progress = outcome == RunOutcome.Finished ? 1f : progress };

        [Test]
        public void Judge_FirstOutWins_FirstDeadLoses()
        {
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.Finished, 30_000), Run(RunOutcome.Finished, 31_000)));
            Assert.AreEqual(DuelResult.Loss, LiveDuelJudge.Resolve(Run(RunOutcome.Finished, 31_000), Run(RunOutcome.Finished, 30_000)));
            Assert.AreEqual(DuelResult.Draw, LiveDuelJudge.Resolve(Run(RunOutcome.Finished, 30_000), Run(RunOutcome.Finished, 30_100)));
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.Finished, 90_000), Run(RunOutcome.Died, 10_000)), "out beats dead");
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.Finished, 90_000), Run(RunOutcome.TimedOut, PvpConfig.TimeLimitMs, 0.9f)));
            Assert.AreEqual(DuelResult.Loss, LiveDuelJudge.Resolve(Run(RunOutcome.Died, 10_000), Run(RunOutcome.Died, 20_000)), "the first death loses");
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.Died, 20_000), Run(RunOutcome.Died, 10_000)));
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.TimedOut, PvpConfig.TimeLimitMs, 0.1f), Run(RunOutcome.Died, 50_000, 0.9f)),
                            "alive beats dead, however far the dead one went");
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.TimedOut, PvpConfig.TimeLimitMs, 0.8f), Run(RunOutcome.TimedOut, PvpConfig.TimeLimitMs, 0.4f)));
            Assert.AreEqual(DuelResult.Win, LiveDuelJudge.Resolve(Run(RunOutcome.Died, 1_000), null), "nothing sent = forfeit");
            Assert.AreEqual(DuelResult.Loss, LiveDuelJudge.Resolve(Run(RunOutcome.Abandoned, 0), Run(RunOutcome.Died, 1_000)));
        }

        sealed class World
        {
            public DateTime Now = Today;
            public MemoryPvpStore Store = new MemoryPvpStore();
            public PvpServer Server;
            int _seed = 4100;

            public World()
            {
                Server = new PvpServer(Store, () => Now, () => _seed++);
                foreach (var p in new[] { "alice", "bob", "carol" }) Store.SoloStars[p] = 50;
            }

            /// <summary>The server clock when a run of <paramref name="timeMs"/> ends, for a duel created at <paramref name="created"/>.</summary>
            public void At(LiveDuel duel, int timeMs)
            {
                var level = PvpServer.Arena(duel.Seed);
                long preview = level.PreviewSeconds * 1000L + 350;
                Now = DateTimeOffset.FromUnixTimeMilliseconds(duel.CreatedAtUnixMs).UtcDateTime
                      .AddMilliseconds(preview + LiveDuelConfig.VsScreenMs + 1000 + timeMs + 500);
            }
        }

        static LiveDuelist Me(string id) => new LiveDuelist { PlayerId = id, Name = id, Look = new PlayerLook { Mummy = "classic" } };

        static RunSubmission Exit(LiveDuel duel, int gapMs)
        {
            var level = PvpServer.Arena(duel.Seed);
            var inputs = new List<RunInput>();
            var s = Rules.Initial(level);
            int ms = 500;
            foreach (var a in level.Solution.Actions)
            {
                inputs.Add(new RunInput { Tick = RunActions.TickOf(ms), Direction = RunActions.Encode(a) });
                var r = Rules.Step(level, s, a);
                ms += Math.Max(gapMs, RunTiming.MinGapMs(level, s, a, r));
                s = r.State;
            }
            return new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Finished, Progress = 1, Inputs = inputs, TimeMs = RunActions.MsOf(inputs[inputs.Count - 1].Tick) };
        }

        [Test]
        public void Server_OnlyTwoPlayersOfTheSameLeague_TheFasterWins_BothAreUpdated()
        {
            var w = new World();
            w.Store.Players["carol"] = new PlayerPvpData { Elo = PvpConfig.DiamantMin + 10 };
            Assert.AreEqual("DIVISION", w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "lobbyX", Me("alice"), Me("carol")).Result.Error);
            Assert.AreEqual("OUTDATED", w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion - 1, "lobbyA", Me("alice"), Me("bob")).Result.Error);
            Assert.AreEqual("UNKNOWN", w.Server.StartLiveDuelAsync("carol", DifficultyTable.GeneratorVersion, "lobbyA", Me("alice"), Me("bob")).Result.Error,
                            "only a player of the duel creates it");

            var duel = w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "lobbyA", Me("alice"), Me("bob")).Result.Match;
            Assert.IsNotNull(duel);
            Assert.AreEqual(duel.Id, w.Server.GetLiveDuelAsync("bob", duel.Id).Result.Match.Id, "the guest reads it");
            Assert.AreEqual("UNKNOWN", w.Server.GetLiveDuelAsync("carol", duel.Id).Result.Error);

            var fast = Exit(duel, 300);
            var slow = Exit(duel, 600);
            w.At(duel, fast.TimeMs);
            var first = w.Server.SubmitLiveDuelAsync("alice", duel.Id, fast, "Alice").Result;
            Assert.IsNull(first.Error);
            Assert.IsFalse(first.Resolved, "waits for bob's run");
            w.At(duel, slow.TimeMs);
            var second = w.Server.SubmitLiveDuelAsync("bob", duel.Id, slow, "Bob").Result;
            Assert.IsTrue(second.Resolved);
            Assert.AreEqual(DuelResult.Loss, second.Result);
            Assert.That(second.EloAfter, Is.LessThan(second.EloBefore));
            var forAlice = w.Server.LiveDuelResultAsync("alice", duel.Id).Result;
            Assert.AreEqual(DuelResult.Win, forAlice.Result);
            Assert.That(forAlice.EloAfter, Is.GreaterThan(forAlice.EloBefore));
            Assert.AreEqual(1, w.Store.Players["alice"].Wins);
            Assert.AreEqual(1, w.Store.Players["bob"].Losses);
            var history = w.Server.GetHistoryAsync("bob").Result.Duels;
            Assert.AreEqual(duel.Id, history[0].MatchId, "both runs are kept to watch again");
            Assert.AreEqual("alice", history[0].Rival.PlayerId);
        }

        [Test]
        public void Server_ARunMuchLaterThanItsTime_IsRefused_AndASilentRivalLoses()
        {
            var w = new World();
            var duel = w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "lobbyB", Me("alice"), Me("bob")).Result.Match;
            var run = Exit(duel, 300);
            // Sent five minutes after the start for an exit "in" a few seconds: the actions were stamped again.
            w.Now = w.Now.AddMinutes(5);
            var forged = w.Server.SubmitLiveDuelAsync("bob", duel.Id, run, "Bob").Result;
            Assert.AreEqual("INVALID_RUN", forged.Error);

            // Alice never sends anything: a minute after bob's run, she has forfeited.
            w.Now = w.Now.AddSeconds(LiveDuelConfig.SubmitWindowMs / 1000 + 1);
            var verdict = w.Server.LiveDuelResultAsync("bob", duel.Id).Result;
            Assert.IsTrue(verdict.Resolved);
            Assert.AreEqual(DuelResult.Draw, verdict.Result, "a refused run counts as a forfeit, as does nothing at all");
        }

        [Test]
        public void Server_BotDuel_OfTheLeague_IsJudgedAtOnce()
        {
            var w = new World();
            w.Store.Players["alice"] = new PlayerPvpData { Elo = PvpConfig.PlatineMin + 30 };
            var duel = w.Server.StartLiveBotDuelAsync("alice", DifficultyTable.GeneratorVersion, "lobbyC", Me("alice")).Result.Match;
            Assert.IsNotNull(duel);
            Assert.IsTrue(duel.VsBot);
            Assert.IsTrue(duel.B.Bot);
            Assert.AreEqual(Leagues.FromElo(duel.A.Elo), Leagues.FromElo(duel.B.Elo), "a bot of her league");
            Assert.IsNotNull(duel.B.Run, "its run is played in advance");
            var run = Exit(duel, 250);
            w.At(duel, run.TimeMs);
            var r = w.Server.SubmitLiveDuelAsync("alice", duel.Id, run, "Alice").Result;
            Assert.IsNull(r.Error);
            Assert.IsTrue(r.Resolved, "nothing to wait for");
            Assert.IsFalse(w.Store.Players.ContainsKey(duel.B.PlayerId), "a bot has no record");
        }
    }
}
