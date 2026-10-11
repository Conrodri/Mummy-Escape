using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The map draft before a PvP match: three PvP tombs drawn from the match seed, one ban per side, the last one played.</summary>
    public class DraftTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

        [Test]
        public void Draw_IsDeterministic_ThreeMapsOfRisingDifficulty_InTheLeaguesAct()
        {
            foreach (League league in Enum.GetValues(typeof(League)))
                foreach (int seed in new[] { 1, 77, int.MaxValue })
                {
                    var maps = PvpDraft.Draw(seed, league);
                    CollectionAssert.AreEqual(maps, PvpDraft.Draw(seed, league), "same seed, same draw");
                    Assert.AreEqual(PvpDraft.MapCount, maps.Count);
                    var ids = maps.Select(m => { Assert.IsTrue(PvpDraft.TryDecode(m, out var id)); return id; }).ToList();
                    Assert.AreEqual(1, ids.Select(id => (id.Mode, id.Act)).Distinct().Count(), "one act per league");
                    Assert.That(DifficultyTable.Target(ids[0]), Is.LessThanOrEqualTo(DifficultyTable.Target(ids[1])));
                    Assert.That(DifficultyTable.Target(ids[1]), Is.LessThanOrEqualTo(DifficultyTable.Target(ids[2])));
                    Assert.That(ids[0].Index, Is.Not.EqualTo(ids[1].Index).And.Not.EqualTo(ids[2].Index));
                }
            var bronze = PvpDraft.Draw(5, League.Bronze).Select(PvpArena.LevelFor).First();
            var diamond = PvpDraft.Draw(5, League.Diamant).Select(PvpArena.LevelFor).First();
            Assert.That(DifficultyTable.Target(diamond), Is.GreaterThan(DifficultyTable.Target(bronze)), "harder tombs higher up");
        }

        [Test]
        public void MapSeeds_AreTheirOwnSpace_AndOldDuelSeedsAreUntouched()
        {
            var id = new LevelId(Difficulty.Extreme, 3, 10);
            int seed = PvpDraft.MapSeed(id, 0xFFFFFFFF);
            Assert.That(seed, Is.LessThan(0));
            Assert.IsTrue(PvpDraft.TryDecode(seed, out var back));
            Assert.AreEqual(id, back);
            Assert.AreEqual(id, PvpArena.LevelFor(seed));
            Assert.AreEqual(id, RelayArena.LevelFor(seed));
            // Seeds of the duels played before the draft (positive) keep their tomb.
            Assert.IsFalse(PvpDraft.TryDecode(4242, out _));
            Assert.AreEqual(new LevelId(Difficulty.Normal, 1, 4), PvpArena.LevelFor(3));
        }

        [Test]
        public void TheServerRegeneratesTheMapFromItsSeed()
        {
            foreach (League league in new[] { League.Bronze, League.Platine, League.Diamant })
            {
                int map = PvpDraft.Draw(31, league)[2];
                var mine = PvpArena.Generate(map);
                Assert.AreEqual(PvpArena.LevelFor(map), mine.Id);
                Assert.AreEqual(mine.ComputeHash(), PvpArena.Generate(map).ComputeHash());
                Assert.AreEqual(mine.ComputeHash(), PvpServer.Arena(map).ComputeHash());
                Assert.IsNotNull(mine.Solution);
            }
            int relay = PvpDraft.Draw(31, League.Or)[1];
            Assert.AreEqual(RelayArena.Generate(relay).ComputeHash(), PvpServer.RelayArenaFor(relay).ComputeHash());
        }

        static MapDraft Draft(int seed, params (string id, int team, bool bot)[] voters) => PvpDraft.Open(seed, League.Or, voters);

        [Test]
        public void Bans_1v1_TheMapNobodyBanned_SameBanPicksAmongTheOtherTwo()
        {
            var d = Draft(9, ("a", 0, false), ("b", 1, false));
            Assert.IsTrue(PvpDraft.Vote(d, "a", 0, 1000));
            Assert.IsFalse(PvpDraft.TryClose(d, 1001), "waits for b");
            Assert.IsTrue(PvpDraft.Vote(d, "b", 2, 2000));
            Assert.IsFalse(PvpDraft.Vote(d, "b", 1, 2001), "one ban each");
            Assert.IsTrue(PvpDraft.TryClose(d, 2002));
            CollectionAssert.AreEqual(new[] { 0, 2 }, d.Bans);
            Assert.AreEqual(1, d.Pick);
            Assert.IsFalse(PvpDraft.Vote(d, "a", 1, 2003), "closed");

            var picks = new HashSet<int>();
            for (int seed = 0; seed < 40; seed++)
            {
                int pick = PvpDraft.Resolve(seed, 1, 1);
                Assert.AreNotEqual(1, pick);
                Assert.AreEqual(pick, PvpDraft.Resolve(seed, 1, 1), "deterministic");
                picks.Add(pick);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, picks, "either of the two");
        }

        [Test]
        public void Bans_2v2_TheDuosVote_ATieDrawsAmongItsVotes()
        {
            var seen = new HashSet<int>();
            for (int seed = 0; seed < 40; seed++)
            {
                var d = Draft(seed, ("a1", 0, false), ("a2", 0, false), ("b1", 1, false), ("b2", 1, false));
                PvpDraft.Vote(d, "a1", 2, 1);
                PvpDraft.Vote(d, "a2", 2, 2);   // agree
                PvpDraft.Vote(d, "b1", 0, 3);
                PvpDraft.Vote(d, "b2", 1, 4);   // tie
                Assert.IsTrue(PvpDraft.TryClose(d, 5));
                Assert.AreEqual(2, d.Bans[0]);
                Assert.That(d.Bans[1], Is.EqualTo(0).Or.EqualTo(1));
                Assert.AreEqual(d.Bans[1], PvpDraft.TeamBan(d, 1), "the same on every phone and on the server");
                seen.Add(d.Bans[1]);
                Assert.AreEqual(3 - d.Bans[1] - 2, d.Pick);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, seen);

            // One vote only (the teammate let the time run out): that one.
            var one = Draft(3, ("a1", 0, false), ("a2", 0, false), ("b1", 1, false), ("b2", 1, false));
            PvpDraft.Vote(one, "a1", 1, 1);
            PvpDraft.Vote(one, "a2", -1, 2);
            Assert.AreEqual(1, PvpDraft.TeamBan(one, 0));
        }

        [Test]
        public void Timeouts_AndBots_BanAtRandom()
        {
            // Nobody chose: a random ban for each side, always the same for this match.
            var d = Draft(12, ("a", 0, false), ("b", 1, false));
            PvpDraft.Vote(d, "a", -1, 10);
            PvpDraft.Vote(d, "b", 99, 11);  // out of range = no choice
            Assert.IsTrue(PvpDraft.TryClose(d, 12));
            var again = Draft(12, ("a", 0, false), ("b", 1, false));
            PvpDraft.Vote(again, "a", -1, 50);
            PvpDraft.Vote(again, "b", -1, 60);
            PvpDraft.TryClose(again, 70);
            CollectionAssert.AreEqual(d.Bans, again.Bans);
            Assert.AreEqual(d.Pick, again.Pick);
            Assert.That(d.Bans, Is.All.InRange(0, PvpDraft.MapCount - 1));

            // A silent player: the draft closes WindowMs after the first vote. A bot's vote starts no clock.
            var silent = Draft(13, ("a", 0, false), ("bot_1", 1, true));
            Assert.IsTrue(silent.VoteOf("bot_1").Voted, "bots ban at once");
            Assert.AreEqual(PvpDraft.BotBan(13, "bot_1"), silent.VoteOf("bot_1").Map);
            Assert.IsFalse(PvpDraft.TryClose(silent, long.MaxValue / 2), "nobody human voted yet");
            var duo = Draft(14, ("a1", 0, false), ("a2", 0, false), ("b1", 1, true), ("b2", 1, true));
            PvpDraft.Vote(duo, "a1", 1, 1000);
            Assert.IsFalse(PvpDraft.TryClose(duo, 1000 + PvpDraft.WindowMs - 1));
            Assert.IsTrue(PvpDraft.TryClose(duo, 1000 + PvpDraft.WindowMs));
            Assert.AreEqual(1, duo.Bans[0], "the absent teammate does not count");
        }

        // ------------------------------------------------------------------ server

        sealed class World
        {
            public DateTime Now = Today;
            public readonly MemoryPvpStore Store = new MemoryPvpStore();
            public readonly PvpServer Server;
            int _seed = 900;

            public World()
            {
                Server = new PvpServer(Store, () => Now, () => _seed++);
                foreach (var p in new[] { "alice", "bob" }) Store.SoloStars[p] = 50;
            }
        }

        static LiveDuelist Me(string id) => new LiveDuelist { PlayerId = id, Name = id };

        [Test]
        public void Server_LiveDuel_OldClientsAreOutdated_SilentRivalBansAtRandom()
        {
            var w = new World();
            Assert.AreEqual("OUTDATED", w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "l0", Me("alice"), Me("bob"), draftVersion: 0).Result.Error,
                            "a game without the draft cannot play");
            var duel = w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "l1", Me("alice"), Me("bob")).Result.Match;
            Assert.AreEqual(0, duel.Seed, "no tomb before the draft");
            Assert.AreEqual(3, duel.Draft.Maps.Count);
            Assert.IsTrue(duel.Draft.Maps.All(m => PvpArena.LevelFor(m).Act == 2 && PvpArena.LevelFor(m).Mode == Difficulty.Normal), "silver plays act 2");
            Assert.AreEqual("UNKNOWN", w.Server.BanLiveDuelAsync("carol", duel.Id, 0).Result.Error);

            duel = w.Server.BanLiveDuelAsync("alice", duel.Id, 1).Result.Match;
            Assert.IsFalse(duel.Draft.Closed, "bob has until the window ends");
            w.Now = w.Now.AddMilliseconds(PvpDraft.WindowMs);
            duel = w.Server.GetLiveDuelAsync("bob", duel.Id).Result.Match;
            Assert.IsTrue(duel.Draft.Closed);
            Assert.AreEqual(1, duel.Draft.Bans[0]);
            Assert.AreNotEqual(1, duel.Draft.Pick);
            Assert.AreEqual(duel.Draft.Maps[duel.Draft.Pick], duel.Seed);
            Assert.AreEqual(Ms(w.Now), duel.Draft.ClosedUnixMs, "the run's clock starts here");
            Assert.IsFalse(w.Server.BanLiveDuelAsync("bob", duel.Id, 0).Result.Match.Draft.VoteOf("bob").Voted, "too late");
        }

        [Test]
        public void Server_LiveDuel_ARunBeforeTheMapIsChosen_IsRefused()
        {
            var w = new World();
            var duel = w.Server.StartLiveDuelAsync("alice", DifficultyTable.GeneratorVersion, "l2", Me("alice"), Me("bob")).Result.Match;
            var level = PvpArena.Generate(duel.Draft.Maps[0]);
            var inputs = level.Solution.Actions.Select((a, i) => new RunInput { Tick = 50 * (i + 1), Direction = RunActions.Encode(a) }).ToList();
            var r = w.Server.SubmitLiveDuelAsync("alice", duel.Id, new RunSubmission { MatchId = duel.Id, Outcome = RunOutcome.Finished, Inputs = inputs }, "Alice").Result;
            Assert.AreEqual("INVALID_RUN", r.Error);
            Assert.IsTrue(w.Server.GetLiveDuelAsync("bob", duel.Id).Result.Match.Draft.VoteOf("alice").Voted, "sending a run ends one's draft");
        }
    }
}
