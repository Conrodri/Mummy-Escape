using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Guild wars between guilds of the same level, rounds nobody blocks, and the 2v2 invites between friends.</summary>
    public class TeamMatchmakingTests
    {
        static int Gen => DifficultyTable.GeneratorVersion;

        /// <summary>An open guild: its leader and these members, each with this duel Elo.</summary>
        static string MakeGuild(PvpServer server, MemoryPvpStore store, string lead, string tag, int duelElo, params string[] members)
        {
            var made = server.CreateGuildAsync(lead, lead, "Guilde " + tag, tag).Result;
            Assert.IsNull(made.Error, tag);
            server.SetGuildPolicyAsync(lead, GuildJoinPolicy.Open).Wait();
            foreach (var m in members) Assert.IsNull(server.JoinGuildAsync(m, m, made.Guild.Id).Result.Error);
            foreach (var id in members.Append(lead)) store.Players[id].Elo = duelElo;
            return made.Guild.Id;
        }

        static TeamBattle War(PvpServer server, string officer, params string[] order)
        {
            var r = server.StartWarAsync(officer, order.Length, order.ToList(), Gen).Result;
            Assert.IsNull(r.Error, officer);
            return r.Wars.Single(w => w.Id == r.Guild.ActiveWar);
        }

        static Guild StoredGuild(MemoryPvpStore store, string id) => (Guild)store.Shared[PvpServer.GuildsCollection + "/" + id];
        static TeamBattle StoredBattle(MemoryPvpStore store, string id) => (TeamBattle)store.Shared[PvpServer.BattlesCollection + "/" + id];

        // ------------------------------------------------------------------ rules

        [Test]
        public void WarGap_WidensWithTheWait_UpToItsCap_AndTheRatingMixesWarAndDuelElo()
        {
            const long hour = 3_600_000L;
            Assert.AreEqual(150, TeamLogic.WarGap(0));
            Assert.AreEqual(150, TeamLogic.WarGap(6 * hour - 1));
            Assert.AreEqual(200, TeamLogic.WarGap(6 * hour));
            Assert.AreEqual(350, TeamLogic.WarGap(24 * hour));
            Assert.AreEqual(400, TeamLogic.WarGap(48 * hour), "capped");
            Assert.AreEqual(900, TeamLogic.WarRating(1000, 800));
        }

        [Test]
        public void WarElo_IsClassicElo_WorthMoreAgainstAStrongerGuild()
        {
            Assert.AreEqual(1016, Elo.Classic(1000, 1000, DuelResult.Win, TeamConfig.WarEloK));
            Assert.AreEqual(984, Elo.Classic(1000, 1000, DuelResult.Loss, TeamConfig.WarEloK));
            Assert.AreEqual(1000, Elo.Classic(1000, 1000, DuelResult.Draw, TeamConfig.WarEloK));
            Assert.AreEqual(1403, Elo.Classic(1400, 1000, DuelResult.Win, TeamConfig.WarEloK), "beating weaker: little");
            Assert.AreEqual(1029, Elo.Classic(1000, 1400, DuelResult.Win, TeamConfig.WarEloK), "beating stronger: a lot");
        }

        [Test]
        public void WarRounds_AreIndependent_AndARoundNotRunCanChangeRunner()
        {
            int seed = 1;
            var b = TeamLogic.NewBattle("x", BattleKind.GuildWar, 3, () => seed++, Gen, 0, TeamLogic.NewSide("A", "A", 1000, new[] { "a1", "a2", "a3" }, id => id));
            TeamLogic.Join(b, TeamLogic.NewSide("B", "B", 1000, new[] { "b1", "b2", "b3" }, id => id), 0);
            Assert.AreEqual(2, TeamLogic.NextSlot(b, "a3"), "the third runner does not wait for the first two");
            TeamLogic.Record(b, true, 2, new SlotRun { PlayerId = "a3", Outcome = RunOutcome.Finished, TimeMs = 5000, Progress = 1 }, 1);
            Assert.AreEqual(2, TeamLogic.LeftToRun(b, true));

            Assert.IsNull(TeamLogic.Replace(b, "A", 1, "a4", "Ali"));
            Assert.AreEqual("a4", b.A.Order[1]);
            Assert.AreEqual("Ali", b.A.Names[1]);
            Assert.AreEqual(1, TeamLogic.NextSlot(b, "a4"));
            Assert.AreEqual(-1, TeamLogic.NextSlot(b, "a2"), "replaced");
            Assert.AreEqual("RAN", TeamLogic.Replace(b, "A", 2, "a5", "a5"), "a round run keeps its runner");
            Assert.AreEqual("ORDER", TeamLogic.Replace(b, "A", 0, "a3", "a3"), "one round each");
            Assert.AreEqual("UNKNOWN", TeamLogic.Replace(b, "Z", 0, "a5", "a5"));
        }

        // ------------------------------------------------------------------ server: matchmaking

        [Test]
        public void War_MeetsOnlyAGuildOfRoughlyItsLevel_TheGapWideningWithTheWait()
        {
            var (server, store, _, advance) = TeamTests.NewServer();
            MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3");     // rating (1000 + 800) / 2 = 900
            string strong = MakeGuild(server, store, "b", "BBB", 1600, "b1", "b2", "b3"); // 1300
            MakeGuild(server, store, "c", "CCC", 900, "c1", "c2", "c3");     // 950
            string mid = MakeGuild(server, store, "d", "DDD", 1000, "d1", "d2", "d3");    // 1000

            var weak = War(server, "a", "a1", "a2", "a3");
            var far = War(server, "b", "b1", "b2", "b3");
            Assert.IsNull(far.B, "400 apart: too far, the strong guild waits");
            Assert.AreNotEqual(weak.Id, far.Id);
            var close = War(server, "c", "c1", "c2", "c3");
            Assert.AreEqual(weak.Id, close.Id, "50 apart: they meet");
            Assert.AreEqual("AAA", close.A.TeamName.Substring(1, 3));

            var middle = War(server, "d", "d1", "d2", "d3");
            Assert.IsNull(middle.B, "300 apart: not yet");
            Assert.IsNull(server.GetGuildAsync("b").Result.Wars.Single().B, "still waiting after a look");

            advance(TimeSpan.FromHours(18)); // the gap is now 300
            var seen = server.GetGuildAsync("b").Result;
            var met = seen.Wars.Single(w => w.Id == seen.Guild.ActiveWar);
            Assert.AreEqual(middle.Id, met.Id, "the waiting guild joins the other waiting war");
            Assert.AreEqual(strong, met.B.TeamId);
            Assert.AreEqual(mid, met.A.TeamId);
            Assert.IsTrue(StoredBattle(store, far.Id).Void, "its own waiting war is dropped without an outcome");
            var other = server.GetGuildAsync("d").Result;
            Assert.AreEqual(middle.Id, other.Guild.ActiveWar);
            Assert.IsNotNull(other.Wars.Single(w => w.Id == middle.Id).B);
        }

        [Test]
        public void War_OnlyWarsOfTheSameSizeMeet_InAQueueOfTheGeneratorVersion()
        {
            var (server, store, _, _) = TeamTests.NewServer();
            MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3", "a4", "a5");
            MakeGuild(server, store, "b", "BBB", 800, "b1", "b2", "b3", "b4", "b5");
            var five = War(server, "a", "a1", "a2", "a3", "a4", "a5");
            var three = War(server, "b", "b1", "b2", "b3");
            Assert.IsNull(three.B, "a 3v3 does not meet a 5v5");
            Assert.IsTrue(store.Shared.ContainsKey(PvpServer.QueueCollection + "/war5_g" + Gen));
            Assert.IsTrue(store.Shared.ContainsKey(PvpServer.QueueCollection + "/war3_g" + Gen));
            Assert.IsNull(five.B);
        }

        [Test]
        public void War_OfADissolvedGuild_IsDroppedFromTheQueue()
        {
            var (server, store, _, _) = TeamTests.NewServer();
            string gone = MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3");
            MakeGuild(server, store, "b", "BBB", 800, "b1", "b2", "b3");
            var waiting = War(server, "a", "a1", "a2", "a3");
            foreach (var id in new[] { "a1", "a2", "a3", "a" }) server.LeaveGuildAsync(id).Wait();
            var war = War(server, "b", "b1", "b2", "b3");
            Assert.IsNull(war.B, "nobody left to run it");
            Assert.AreNotEqual(waiting.Id, war.Id);
            Assert.IsTrue(StoredBattle(store, waiting.Id).Void);
            Assert.IsNull(StoredGuild(store, gone).ActiveWar);
        }

        [Test]
        public void War_AtMostTwiceAWeekAgainstTheSameGuild_AndAWalkoverEarnsNothing()
        {
            var (server, store, _, advance) = TeamTests.NewServer();
            string g1 = MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3");
            string g2 = MakeGuild(server, store, "b", "BBB", 800, "b1", "b2", "b3");
            for (int i = 0; i < TeamConfig.MaxWarsPerWeekVsSameGuild; i++)
            {
                var war = War(server, "a", "a1", "a2", "a3");
                Assert.AreEqual(war.Id, War(server, "b", "b1", "b2", "b3").Id);
                // Only the first guild runs: the second never shows.
                foreach (var id in new[] { "a1", "a2", "a3" }) TeamTests.RunRound(server, id, war.Id, 500);
                advance(TimeSpan.FromHours(TeamConfig.BattleHours + 1));
                Assert.IsTrue(server.GetGuildAsync("a").Result.Wars.First(w => w.Id == war.Id).Finished);
            }
            var winner = StoredGuild(store, g1);
            Assert.AreEqual(2, winner.WarWins);
            Assert.AreEqual(0, winner.Points, "no points for wins by forfeit");
            Assert.AreEqual(0, store.Players["a1"].Seals, "no seals either");
            Assert.That(winner.WarElo, Is.GreaterThan(TeamConfig.GuildStartingWarElo), "the Elo still moves");

            var third = War(server, "a", "a1", "a2", "a3");
            var refused = War(server, "b", "b1", "b2", "b3");
            Assert.AreNotEqual(third.Id, refused.Id, "a third war in the week against the same guild waits for another one");
            Assert.IsNull(refused.B);
            Assert.AreEqual(2, StoredGuild(store, g2).Foes.Count);
        }

        // ------------------------------------------------------------------ server: running a war

        [Test]
        public void War_EachRunsWhenHeLikes_AnOfficerReplacesAnAbsentee_ALeaverNoLongerRuns()
        {
            var (server, store, _, _) = TeamTests.NewServer();
            string g1 = MakeGuild(server, store, "lead", "AAA", 800, "m1", "m2", "m3", "m4");
            string g2 = MakeGuild(server, store, "boss", "BBB", 800, "r1", "r2", "r3");
            var war = War(server, "lead", "m1", "m2", "m3");
            Assert.AreEqual(war.Id, War(server, "boss", "r1", "r2", "r3").Id);

            TeamTests.RunRound(server, "m3", war.Id, 450);
            // The second runner of a round knows the time to beat, not the route.
            var r3 = server.StartBattleRunAsync("r3", war.Id, Gen).Result;
            Assert.AreEqual("m3", r3.Ghost.PlayerId);
            Assert.IsEmpty(r3.Ghost.Inputs, "the rival's actions stay hidden");
            Assert.That(r3.Ghost.TimeMs, Is.GreaterThan(0));
            Assert.AreEqual(RunOutcome.Finished, r3.Ghost.Outcome);
            server.SubmitRunAsync("r3", new RunSubmission { MatchId = r3.MatchId, Outcome = RunOutcome.Abandoned }, "r3").Wait();

            Assert.AreEqual("RIGHTS", server.ReplaceWarRunnerAsync("m1", war.Id, 1, "m4").Result.Error, "the leader or an officer");
            Assert.AreEqual("RAN", server.ReplaceWarRunnerAsync("lead", war.Id, 2, "m4").Result.Error);
            Assert.AreEqual("ORDER", server.ReplaceWarRunnerAsync("lead", war.Id, 1, "m3").Result.Error, "one round each");
            Assert.AreEqual("ORDER", server.ReplaceWarRunnerAsync("lead", war.Id, 1, "r1").Result.Error, "members only");
            Assert.AreEqual("UNKNOWN", server.ReplaceWarRunnerAsync("lead", "nope", 1, "m4").Result.Error);
            var replaced = server.ReplaceWarRunnerAsync("lead", war.Id, 1, "m4").Result;
            Assert.IsNull(replaced.Error);
            Assert.AreEqual("m4", replaced.Wars.Single(w => w.Id == war.Id).A.Order[1]);
            Assert.AreEqual("NOT_YOUR_TURN", server.StartBattleRunAsync("m2", war.Id, Gen).Result.Error, "m2 no longer runs");

            server.LeaveGuildAsync("m1").Wait();
            Assert.AreEqual("NOT_MEMBER", server.StartBattleRunAsync("m1", war.Id, Gen).Result.Error);
            Assert.IsNull(server.ReplaceWarRunnerAsync("lead", war.Id, 0, "lead").Result.Error, "his round goes to another member");

            TeamTests.RunRound(server, "m4", war.Id, 450);
            TeamTests.RunRound(server, "r1", war.Id, 600);
            TeamTests.RunRound(server, "lead", war.Id, 450);
            var end = TeamTests.RunRound(server, "r2", war.Id, 600);
            Assert.IsTrue(end.Battle.Finished);
            Assert.AreEqual(DuelResult.Win, end.Battle.Result, "seen from side A: 3-0");
            Assert.AreEqual(1016, StoredGuild(store, g1).WarElo, "classic Elo between equals: +16");
            Assert.AreEqual(984, StoredGuild(store, g2).WarElo);
            Assert.AreEqual(3 * TeamConfig.PointsPerWarRaceWin + 3 * TeamConfig.WarWinBonusPerSlot, StoredGuild(store, g1).Points);
            Assert.AreEqual(TeamConfig.WarWinSealsPerRunner, store.Players["m4"].Seals);
        }

        [Test]
        public void StartWar_ClaimedUnderTheGuildLock_IsBusyUntilTheClaimGoesStale()
        {
            var (server, store, clock, advance) = TeamTests.NewServer();
            string g = MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3");
            var guild = StoredGuild(store, g);
            guild.ActiveWar = "claimed";
            guild.ActiveWarAtUnixMs = new DateTimeOffset(clock()).ToUnixTimeMilliseconds();
            var order = new List<string> { "a1", "a2", "a3" };
            Assert.AreEqual("BUSY", server.StartWarAsync("a", 3, order, Gen).Result.Error, "another officer is starting one");
            advance(TimeSpan.FromMilliseconds(TeamConfig.ClaimStaleMs + 1));
            Assert.IsNull(server.StartWarAsync("a", 3, order, Gen).Result.Error, "a claim never followed by a war frees the guild");
            Assert.AreEqual("BUSY", server.StartWarAsync("a", 3, order, Gen).Result.Error, "now a real war is on");
        }

        [Test]
        public void Battle_OfAnOlderGenerator_EndsVoid_WithoutElo()
        {
            var (server, store, _, _) = TeamTests.NewServer();
            string g1 = MakeGuild(server, store, "a", "AAA", 800, "a1", "a2", "a3");
            string g2 = MakeGuild(server, store, "b", "BBB", 800, "b1", "b2", "b3");
            var war = War(server, "a", "a1", "a2", "a3");
            War(server, "b", "b1", "b2", "b3");
            TeamTests.RunRound(server, "a1", war.Id, 500);
            StoredBattle(store, war.Id).GeneratorVersion = Gen - 1; // as if the generator had moved on since
            var seen = server.GetGuildAsync("a").Result;
            Assert.IsNull(seen.Guild.ActiveWar, "the guild is free again");
            var b = StoredBattle(store, war.Id);
            Assert.IsTrue(b.Finished && b.Void);
            Assert.AreEqual(TeamConfig.GuildStartingWarElo, StoredGuild(store, g1).WarElo);
            Assert.AreEqual(TeamConfig.GuildStartingWarElo, StoredGuild(store, g2).WarElo);
            Assert.AreEqual(0, StoredGuild(store, g1).WarWins + StoredGuild(store, g1).WarDraws + StoredGuild(store, g1).WarLosses);
        }

        // ------------------------------------------------------------------ server: 2v2

        [Test]
        public void DuoInvite_OnlyToSomeoneWhoHasThePlayerAsFriend_AndOneDuoEach()
        {
            var (server, store, clock, advance) = TeamTests.NewServer();
            Assert.AreEqual("NOT_FRIEND", server.InviteDuoAsync("a", "a", "b").Result.Error, "strangers can't fill the invite slots");
            server.SyncChatProfileAsync("b", new List<string> { "a" }, false).Wait();
            Assert.IsTrue(server.InviteDuoAsync("a", "a", "b").Result.Ok);

            // Crossing invites: the second accept finds both places taken.
            server.SyncChatProfileAsync("a", new List<string> { "b" }, false).Wait();
            Assert.IsTrue(server.InviteDuoAsync("b", "b", "a").Result.Ok);
            var fromA = server.GetTeamsAsync("b").Result.Invites.Single();
            var fromB = server.GetTeamsAsync("a").Result.Invites.Single();
            Assert.IsTrue(server.RespondDuoAsync("b", "b", fromA.Id, true).Result.Ok);
            Assert.AreEqual("LIMIT", server.RespondDuoAsync("a", "a", fromB.Id, true).Result.Error);
            Assert.AreEqual(1, store.Players["a"].Duos.Count);
            Assert.AreEqual(1, store.Players["b"].Duos.Count);

            // The duo's battle is claimed under its lock too.
            string duoId = store.Players["a"].Duos[0];
            var duo = (Duo)store.Shared[PvpServer.DuosCollection + "/" + duoId];
            duo.ActiveBattle = "claimed";
            duo.ActiveBattleAtUnixMs = new DateTimeOffset(clock()).ToUnixTimeMilliseconds();
            Assert.AreEqual("BUSY", server.FindDuoMatchAsync("b", duoId, true, Gen).Result.Error);
            advance(TimeSpan.FromMilliseconds(TeamConfig.ClaimStaleMs + 1));
            var battle = server.FindDuoMatchAsync("b", duoId, true, Gen).Result.Battle;
            Assert.IsNotNull(battle);
            Assert.AreEqual(battle.Id, ((Duo)store.Shared[PvpServer.DuosCollection + "/" + duoId]).ActiveBattle);
            Assert.IsTrue(store.Shared.ContainsKey(PvpServer.QueueCollection + "/duo_g" + Gen));
        }
    }
}
