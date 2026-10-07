using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Team battles: 2v2 duos of friends and guild wars, from the rules to the server on real tombs.</summary>
    public class TeamTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        static int Gen => DifficultyTable.GeneratorVersion;

        static (PvpServer server, MemoryPvpStore store, Func<DateTime> clock, Action<TimeSpan> advance) NewServer()
        {
            var store = new MemoryPvpStore();
            var now = Today;
            int seed = 4000;
            var server = new PvpServer(store, () => now, () => seed++);
            return (server, store, () => now, t => now += t);
        }

        static List<RunInput> Stamp(Level level, IEnumerable<PlayerAction> actions, int gapMs)
        {
            var inputs = new List<RunInput>();
            var s = Rules.Initial(level);
            int ms = 500;
            foreach (var a in actions)
            {
                inputs.Add(new RunInput { Tick = RunActions.TickOf(ms), Direction = RunActions.Encode(a) });
                var r = Rules.Step(level, s, a);
                ms += Math.Max(gapMs, RunTiming.MinGapMs(level, s, a, r));
                s = r.State;
            }
            return inputs;
        }

        /// <summary>Starts this player's round and runs it on the ideal route, one action every <paramref name="gapMs"/> ms.</summary>
        static SubmitRunResponse RunRound(PvpServer server, string player, string battleId, int gapMs)
        {
            var start = server.StartBattleRunAsync(player, battleId, Gen).Result;
            Assert.IsNull(start.Error, player + " can run");
            var level = PvpServer.Arena(start.Seed);
            var inputs = Stamp(level, level.Solution.Actions, gapMs);
            var run = new RunSubmission
            {
                MatchId = start.MatchId, Outcome = RunOutcome.Finished, Progress = 1, Inputs = inputs,
                TimeMs = RunActions.MsOf(inputs[inputs.Count - 1].Tick), Look = new PlayerLook { Mummy = "mummy_classic" },
            };
            var r = server.SubmitRunAsync(player, run, player).Result;
            Assert.IsNull(r.Error, player + " run accepted");
            return r;
        }

        static string MakeDuo(PvpServer server, string a, string b)
        {
            Assert.IsTrue(server.InviteDuoAsync(a, a, b).Result.Ok);
            var invite = server.GetTeamsAsync(b).Result.Invites.Single(i => i.FromId == a);
            Assert.IsTrue(server.RespondDuoAsync(b, b, invite.Id, true).Result.Ok);
            return server.GetTeamsAsync(a).Result.Duos.Single(d => d.Members.Contains(b)).Id;
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void DuoOrder_Alternates_FirstRunnerRunsTwice()
        {
            var duo = new Duo { Members = { "a", "b" } };
            CollectionAssert.AreEqual(new[] { "a", "b", "a" }, TeamLogic.DuoOrder(duo, true));
            CollectionAssert.AreEqual(new[] { "b", "a", "b" }, TeamLogic.DuoOrder(duo, false));
        }

        static TeamBattle Battle(BattleKind kind, int slots, IList<string> a, IList<string> b)
        {
            int seed = 1;
            var battle = TeamLogic.NewBattle("x", kind, slots, () => seed++, Gen, 0, TeamLogic.NewSide("A", "A", 1000, a, id => id));
            TeamLogic.Join(battle, TeamLogic.NewSide("B", "B", 1000, b, id => id), 0);
            return battle;
        }

        static SlotRun Ran(string id, int ms) => new SlotRun { PlayerId = id, Outcome = RunOutcome.Finished, TimeMs = ms, Progress = 1 };

        [Test]
        public void Duo_EndsAtTwoWins_AndRivalRunsStayHiddenUntilTheRoundIsDecided()
        {
            var b = Battle(BattleKind.Duo, 3, new[] { "a1", "a2", "a1" }, new[] { "b1", "b2", "b1" });
            Assert.AreEqual(0, TeamLogic.NextSlot(b, "a1"));
            Assert.AreEqual(-1, TeamLogic.NextSlot(b, "a2"), "a side runs its rounds in order");

            TeamLogic.Record(b, true, 0, Ran("a1", 5000), 1);
            var seenByB = TeamLogic.ViewFor(b, "B");
            Assert.IsTrue(TeamLogic.IsHidden(seenByB.A.Runs[0]), "no scouting a tomb before running it");
            Assert.IsFalse(TeamLogic.IsHidden(TeamLogic.ViewFor(b, "A").A.Runs[0]));
            Assert.AreEqual(1, TeamLogic.NextSlot(b, "a2"));

            TeamLogic.Record(b, false, 0, Ran("b1", 6000), 2);
            Assert.AreEqual((int)DuelResult.Win, b.SlotResults[0]);
            Assert.IsFalse(TeamLogic.IsHidden(TeamLogic.ViewFor(b, "B").A.Runs[0]), "a decided round can be watched");

            TeamLogic.Record(b, false, 1, Ran("b2", 4000), 3);
            TeamLogic.Record(b, true, 1, Ran("a2", 3000), 4);
            Assert.IsTrue(b.Finished, "2 wins out of 3: the third round is not needed");
            Assert.AreEqual(DuelResult.Win, b.Result);
        }

        [Test]
        public void Deadline_TurnsRoundsNotRunIntoForfeits()
        {
            var b = Battle(BattleKind.GuildWar, 3, new[] { "a1", "a2", "a3" }, new[] { "b1", "b2", "b3" });
            TeamLogic.Record(b, true, 0, Ran("a1", 5000), 1);
            TeamLogic.Record(b, false, 0, Ran("b1", 4000), 1);
            TeamLogic.Record(b, true, 1, Ran("a2", 5000), 1);
            Assert.IsFalse(TeamLogic.CheckFinished(b, b.DeadlineUnixMs));
            Assert.IsTrue(TeamLogic.CheckFinished(b, b.DeadlineUnixMs + 1));
            CollectionAssert.AreEqual(new[] { (int)DuelResult.Loss, (int)DuelResult.Win, (int)DuelResult.Draw }, b.SlotResults);
            Assert.AreEqual(DuelResult.Draw, b.Result);
        }

        [Test]
        public void WarOrder_AndGuildNames_AreChecked()
        {
            var g = new Guild();
            foreach (var id in new[] { "a", "b", "c", "d" }) g.Members.Add(new GuildMember { PlayerId = id });
            Assert.IsNull(TeamLogic.CheckWarOrder(g, new[] { "a", "b", "c" }, 3));
            Assert.AreEqual("ORDER", TeamLogic.CheckWarOrder(g, new[] { "a", "a", "c" }, 3), "one round each");
            Assert.AreEqual("ORDER", TeamLogic.CheckWarOrder(g, new[] { "a", "b", "z" }, 3), "members only");
            Assert.AreEqual("ORDER", TeamLogic.CheckWarOrder(g, new[] { "a", "b", "c", "d" }, 4), "3, 5 or 10");
            Assert.IsNull(TeamLogic.CheckGuildName("Fils d'Anubis", "ANUB"));
            Assert.AreEqual("NAME", TeamLogic.CheckGuildName("ab", "ANUB"));
            Assert.AreEqual("TAG", TeamLogic.CheckGuildName("Les Momies", "A-B"));
            CollectionAssert.AreEqual(new[] { "pvp_guild_banner", "pvp_guild_livery" }, TeamLogic.GuildRewards(600));
        }

        // ------------------------------------------------------------------ server: 2v2

        [Test]
        public void Duo_FullBattle_BetweenTwoDuosOfFriends()
        {
            var (server, store, _, _) = NewServer();
            string ab = MakeDuo(server, "alice", "bob");
            string cd = MakeDuo(server, "carol", "dave");
            Assert.AreEqual("EXISTS", server.InviteDuoAsync("alice", "alice", "bob").Result.Error);

            var waiting = server.FindDuoMatchAsync("alice", ab, true, Gen).Result.Battle;
            Assert.IsNull(waiting.B, "nobody waiting: the battle waits for a rival duo");
            Assert.AreEqual("NOT_YOUR_TURN", server.StartBattleRunAsync("alice", waiting.Id, Gen).Result.Error, "no rival yet");
            var joined = server.FindDuoMatchAsync("dave", cd, true, Gen).Result.Battle;
            Assert.AreEqual(waiting.Id, joined.Id, "the rival duo joins the waiting battle");
            CollectionAssert.AreEqual(new[] { "alice", "bob", "alice" }, joined.A.Order);
            CollectionAssert.AreEqual(new[] { "dave", "carol", "dave" }, joined.B.Order);
            Assert.AreEqual("NOT_YOUR_TURN", server.StartBattleRunAsync("bob", joined.Id, Gen).Result.Error);

            var first = RunRound(server, "alice", joined.Id, 500);
            Assert.IsFalse(first.Resolved, "alone on her tomb so far");
            var daveStart = server.StartBattleRunAsync("dave", joined.Id, Gen).Result;
            Assert.AreEqual("alice", daveStart.Ghost.PlayerId, "the second runner of a round races the first one's ghost");
            server.SubmitRunAsync("dave", new RunSubmission { MatchId = daveStart.MatchId, Outcome = RunOutcome.Abandoned }, "dave").Wait();

            RunRound(server, "carol", joined.Id, 500);
            var last = RunRound(server, "bob", joined.Id, 450);
            Assert.IsTrue(last.Resolved);
            Assert.AreEqual(DuelResult.Win, last.Result);
            Assert.IsTrue(last.Battle.Finished, "2-0: done");
            Assert.That(last.EloAfter, Is.GreaterThan(last.EloBefore));

            var teams = server.GetTeamsAsync("alice").Result;
            var duo = teams.Duos.Single();
            Assert.AreEqual(1, duo.Wins);
            Assert.IsNull(duo.ActiveBattle);
            Assert.That(duo.Elo, Is.GreaterThan(TeamConfig.DuoStartingElo));
            Assert.AreEqual(joined.Id, teams.Battles.Single().Id);
            var board = server.GetDuoBoardAsync(10).Result.Rows;
            Assert.AreEqual(ab, board[0].Id);
            Assert.AreEqual(cd, board[1].Id);
            Assert.AreEqual(1, board[1].Losses);
        }

        [Test]
        public void Battle_LeftUnrunPastTheDeadline_IsForfeited()
        {
            var (server, _, _, advance) = NewServer();
            string ab = MakeDuo(server, "alice", "bob");
            string cd = MakeDuo(server, "carol", "dave");
            server.FindDuoMatchAsync("alice", ab, true, Gen).Wait();
            var battle = server.FindDuoMatchAsync("carol", cd, true, Gen).Result.Battle;
            RunRound(server, "alice", battle.Id, 500);
            advance(TimeSpan.FromHours(TeamConfig.BattleHours + 1));
            var teams = server.GetTeamsAsync("carol").Result;
            Assert.AreEqual(1, teams.Duos.Single().Losses, "carol and dave never ran");
        }

        // ------------------------------------------------------------------ server: guilds

        [Test]
        public void Guild_Roles_War_PointsAndSkins()
        {
            var (server, store, _, _) = NewServer();
            var made = server.CreateGuildAsync("lead", "Lead", "Fils d'Anubis", "anub").Result;
            Assert.AreEqual("ANUB", made.Guild.Tag);
            Assert.AreEqual("TAKEN", server.CreateGuildAsync("x", "x", "fils d'anubis", "ZZ").Result.Error);
            string g1 = made.Guild.Id;
            Assert.AreEqual(GuildJoinPolicy.Request, made.Guild.JoinPolicy, "a new guild takes requests");
            Assert.IsNull(server.SetGuildPolicyAsync("lead", GuildJoinPolicy.Open).Result.Error);
            foreach (var id in new[] { "m1", "m2", "m3" }) Assert.IsNull(server.JoinGuildAsync(id, id, g1).Result.Error);
            Assert.AreEqual("IN_GUILD", server.JoinGuildAsync("m1", "m1", g1).Result.Error);

            Assert.AreEqual("RIGHTS", server.StartWarAsync("m1", 3, new List<string> { "m1", "m2", "m3" }, Gen).Result.Error);
            Assert.IsNull(server.SetGuildRoleAsync("lead", "m1", true).Result.Error);
            Assert.AreEqual("RIGHTS", server.KickGuildMemberAsync("m1", "lead").Result.Error, "an officer can't kick the leader");
            Assert.IsNull(server.KickGuildMemberAsync("m1", "m3").Result.Error);
            Assert.IsNull(server.JoinGuildAsync("m3", "m3", g1).Result.Error);

            string g2 = server.CreateGuildAsync("boss", "Boss", "Scarabées", "SCAR").Result.Guild.Id;
            server.SetGuildPolicyAsync("boss", GuildJoinPolicy.Open).Wait();
            foreach (var id in new[] { "r1", "r2" }) server.JoinGuildAsync(id, id, g2).Wait();
            Assert.AreEqual("ORDER", server.StartWarAsync("boss", 3, new List<string> { "boss", "r1", "m1" }, Gen).Result.Error);

            var war = server.StartWarAsync("m1", 3, new List<string> { "m1", "m2", "m3" }, Gen).Result.Wars.Single();
            Assert.IsNull(war.B);
            Assert.AreEqual("BUSY", server.StartWarAsync("lead", 3, new List<string> { "lead", "m2", "m3" }, Gen).Result.Error);
            var joined = server.StartWarAsync("boss", 3, new List<string> { "boss", "r1", "r2" }, Gen).Result.Wars.Single();
            Assert.AreEqual(war.Id, joined.Id);

            // Guild 1 wins rounds 1 and 2, guild 2 the third: 2-1.
            RunRound(server, "m1", war.Id, 450);
            RunRound(server, "boss", war.Id, 600);
            RunRound(server, "m2", war.Id, 450);
            RunRound(server, "r1", war.Id, 600);
            RunRound(server, "m3", war.Id, 700);
            var end = RunRound(server, "r2", war.Id, 450);
            Assert.IsTrue(end.Battle.Finished, "every round of a war is run");
            Assert.AreEqual(DuelResult.Win, end.Battle.Result, "seen from side A: guild 1 won 2-1");

            var guild = server.GetGuildAsync("lead").Result.Guild;
            Assert.AreEqual(1, guild.WarWins);
            Assert.AreEqual(2 * TeamConfig.PointsPerWarRaceWin + 3 * TeamConfig.WarWinBonusPerSlot, guild.Points);
            Assert.AreEqual(TeamConfig.WarWinSealsPerRunner, store.Players["m2"].Seals);
            Assert.AreEqual(0, store.Players["lead"].Seals, "seals for the runners only");
            Assert.AreEqual(1, server.GetGuildAsync("boss").Result.Guild.WarLosses);

            // The first guild skin, at 100 points.
            Tweak(guild, x => x.Points = 99);
            Assert.IsEmpty(server.GetGuildAsync("lead").Result.NewRewards);
            Tweak(guild, x => x.Points = 100);
            CollectionAssert.AreEqual(new[] { "pvp_guild_banner" }, server.GetGuildAsync("lead").Result.NewRewards);
            Assert.Contains("pvp_guild_banner", store.Players["lead"].UnlockedRewards);
            Assert.IsEmpty(server.GetGuildAsync("lead").Result.NewRewards, "given once");

            var board = server.GetGuildBoardAsync(10).Result.Guilds;
            Assert.AreEqual(g1, board[0].Id);
        }

        static Guild Tweak(Guild g, Action<Guild> change)
        {
            change(g);
            return g;
        }

        [Test]
        public void Guild_LeaderLeaving_PassesTheLead()
        {
            var (server, _, _, _) = NewServer();
            string g = server.CreateGuildAsync("lead", "Lead", "Les Momies", "MOM").Result.Guild.Id;
            server.SetGuildPolicyAsync("lead", GuildJoinPolicy.Open).Wait();
            server.JoinGuildAsync("m1", "m1", g).Wait();
            server.JoinGuildAsync("m2", "m2", g).Wait();
            server.SetGuildRoleAsync("lead", "m2", true).Wait();
            server.LeaveGuildAsync("lead").Wait();
            var guild = server.GetGuildAsync("m1").Result.Guild;
            Assert.AreEqual(GuildRole.Leader, guild.Member("m2").Role, "the officer takes the lead");
            Assert.IsNull(server.GetGuildAsync("lead").Result.Guild);
        }

        [Test]
        public void Guild_OnRequestClosedAndInvitations()
        {
            var (server, _, _, _) = NewServer();
            string g = server.CreateGuildAsync("lead", "Lead", "Les Momies", "MOM").Result.Guild.Id;
            server.JoinGuildAsync("off", "Off", g).Wait();
            Assert.IsNull(server.GetGuildAsync("lead").Result.Guild.Member("off"), "nobody walks into a guild on request");
            Assert.IsNull(server.AnswerGuildRequestAsync("lead", "off", true).Result.Error);
            server.SetGuildRoleAsync("lead", "off", true).Wait();

            // On request: an application, answered by the leader or an officer.
            var asked = server.JoinGuildAsync("ann", "Ann", g).Result;
            Assert.AreEqual("REQUESTED", asked.Error);
            CollectionAssert.AreEqual(new[] { g }, asked.Applied);
            Assert.AreEqual("NO_GUILD", server.AnswerGuildRequestAsync("ann", "ann", true).Result.Error);
            server.JoinGuildAsync("bob", "Bob", g).Wait();
            Assert.IsNull(server.AnswerGuildRequestAsync("off", "bob", false).Result.Error);
            Assert.IsEmpty(server.GetGuildAsync("bob").Result.Applied, "a refusal clears the application");
            var accepted = server.AnswerGuildRequestAsync("off", "ann", true).Result;
            Assert.IsNotNull(accepted.Guild.Member("ann"));
            Assert.IsEmpty(accepted.Guild.Applicants);
            Assert.AreEqual(GuildRole.Member, server.GetGuildAsync("ann").Result.Guild.Member("ann").Role);

            // Only the leader sets the door; closed, nobody gets in, not even by applying.
            Assert.AreEqual("LEADER", server.SetGuildPolicyAsync("off", GuildJoinPolicy.Open).Result.Error);
            Assert.IsNull(server.SetGuildPolicyAsync("lead", GuildJoinPolicy.Closed).Result.Error);
            Assert.AreEqual(GuildJoinPolicy.Closed, server.SearchGuildsAsync("MOM", 5).Result.Guilds.Single().JoinPolicy);
            Assert.AreEqual("CLOSED", server.JoinGuildAsync("cid", "Cid", g).Result.Error);

            // An invitation opens even a closed guild; a plain member can't send one there.
            Assert.AreEqual("RIGHTS", server.InviteToGuildAsync("ann", "Ann", "cid").Result.Error);
            Assert.IsTrue(server.InviteToGuildAsync("off", "Off", "cid").Result.Ok);
            Assert.AreEqual("EXISTS", server.InviteToGuildAsync("lead", "Lead", "cid").Result.Error);
            Assert.AreEqual("MEMBER", server.InviteToGuildAsync("lead", "Lead", "ann").Result.Error);
            var invites = server.GetGuildAsync("cid").Result.Invites;
            Assert.AreEqual("Off", invites.Single().FromName);
            var joined = server.RespondGuildInviteAsync("cid", "Cid", g, true).Result;
            Assert.IsNull(joined.Error);
            Assert.IsNotNull(joined.Guild.Member("cid"));

            // A player in another guild is neither invited nor taken from an old application.
            string other = server.CreateGuildAsync("dan", "Dan", "Scarabées", "SCAR").Result.Guild.Id;
            Assert.AreEqual("THEIR_GUILD", server.InviteToGuildAsync("lead", "Lead", "dan").Result.Error);
            server.SetGuildPolicyAsync("lead", GuildJoinPolicy.Request).Wait();
            server.JoinGuildAsync("eve", "Eve", g).Wait();
            server.JoinGuildAsync("eve", "Eve", other).Wait();
            Assert.IsNull(server.AnswerGuildRequestAsync("dan", "eve", true).Result.Error);
            Assert.IsEmpty(server.GetGuildAsync("lead").Result.Guild.Applicants, "joining one guild withdraws the other applications");

            // Inviting someone who already applied lets them in at once.
            server.JoinGuildAsync("fay", "Fay", g).Wait();
            var invited = server.InviteToGuildAsync("lead", "Lead", "fay").Result;
            Assert.IsTrue(invited.Joined);
            Assert.IsNotNull(server.GetGuildAsync("fay").Result.Guild);
        }
    }
}
