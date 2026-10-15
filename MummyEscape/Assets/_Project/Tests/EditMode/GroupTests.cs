using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The group: a duo of two where each says he is ready and the leader (the creator) launches a mode.</summary>
    public class GroupTests
    {
        static (PvpServer server, string duo) ServerWithDuo()
        {
            var now = new System.DateTime(2026, 10, 12, 12, 0, 0, System.DateTimeKind.Utc);
            var server = new PvpServer(new MemoryPvpStore(), () => now);
            Assert.IsTrue(server.InviteDuoAsync("a", "Ana", "b").Result.Ok);
            var invite = server.GetTeamsAsync("b").Result.Invites[0];
            Assert.IsTrue(server.RespondDuoAsync("b", "Bob", invite.Id, true).Result.Ok);
            return (server, server.GetTeamsAsync("a").Result.Duos[0].Id);
        }

        [Test]
        public void Leader_IsTheCreator_AndMaxOneGroup()
        {
            var (server, id) = ServerWithDuo();
            Assert.AreEqual(1, TeamConfig.MaxDuosPerPlayer);
            Assert.AreEqual("a", server.GetDuoAsync("a", id, null).Result.Duo.Leader);
            Assert.AreEqual("UNKNOWN", server.GetDuoAsync("c", id, null).Result.Error);
        }

        [Test]
        public void Launch_NeedsTheLeader_AndAReadyMate()
        {
            var (server, id) = ServerWithDuo();
            Assert.AreEqual("MODE", server.LaunchGroupAsync("a", id, "guild").Result.Error);
            Assert.AreEqual("LEADER", server.LaunchGroupAsync("b", id, GroupLaunch.Coop).Result.Error);
            Assert.AreEqual("NOT_READY", server.LaunchGroupAsync("a", id, GroupLaunch.Coop).Result.Error);

            var duo = server.SetGroupReadyAsync("b", id, true, new PlayerLook { Mummy = "classic" }).Result.Duo;
            Assert.IsTrue(duo.IsReady("b"));
            Assert.IsNotNull(duo.LookOf("b"));
            Assert.IsNull(duo.LookOf("a"));

            var r = server.LaunchGroupAsync("a", id, GroupLaunch.Relay).Result;
            Assert.IsNull(r.Error);
            Assert.AreEqual(GroupLaunch.Relay, r.Duo.Launch);
            Assert.Greater(r.Duo.LaunchAtUnixMs, 0);
            Assert.IsFalse(r.Duo.IsReady("b"), "ready is consumed by the launch");
        }

        [Test]
        public void MateSearch_PairsTheSameMode_TheLongestSearchingLeads()
        {
            var now = new System.DateTime(2026, 10, 12, 12, 0, 0, System.DateTimeKind.Utc);
            var server = new PvpServer(new MemoryPvpStore(), () => now);
            Assert.AreEqual("MODE", server.SearchMateAsync("a", "Ana", "guild").Result.Error);
            Assert.IsNull(server.SearchMateAsync("b", "Bob", GroupLaunch.Relay).Result.Duo);
            now = now.AddSeconds(2);
            Assert.IsNull(server.SearchMateAsync("c", "Cid", GroupLaunch.Coop).Result.Duo, "another mode");
            now = now.AddSeconds(2);
            Assert.IsNull(server.SearchMateAsync("b", "Bob", GroupLaunch.Relay).Result.Duo, "still alone in its mode");

            now = now.AddSeconds(2);
            var duo = server.SearchMateAsync("d", "Dan", GroupLaunch.Relay).Result.Duo;
            Assert.IsNotNull(duo);
            Assert.AreEqual("b", duo.Leader, "b searched first");
            CollectionAssert.AreEqual(new[] { "Bob", "Dan" }, duo.Names);
            Assert.AreEqual(GroupLaunch.Relay, duo.Mode);
            Assert.AreEqual(duo.Id, server.SearchMateAsync("b", "Bob", GroupLaunch.Relay).Result.Duo.Id, "b's phone finds the group");
            Assert.AreEqual(duo.Id, server.GetTeamsAsync("d").Result.Duos[0].Id);

            now = now.AddSeconds(2);
            Assert.IsNull(server.SearchMateAsync("e", "Eve", GroupLaunch.Relay).Result.Duo, "b and d are no longer searching");
        }

        [Test]
        public void MateSearch_ForgetsWhoStoppedAsking_AndWhoCancelled()
        {
            var now = new System.DateTime(2026, 10, 12, 12, 0, 0, System.DateTimeKind.Utc);
            var server = new PvpServer(new MemoryPvpStore(), () => now);
            server.SearchMateAsync("a", "Ana", GroupLaunch.Coop).Wait();
            now = now.AddMilliseconds(TeamConfig.MateSearchStaleMs + 1);
            Assert.IsNull(server.SearchMateAsync("b", "Bob", GroupLaunch.Coop).Result.Duo, "a stopped asking");
            Assert.IsTrue(server.CancelMateSearchAsync("b").Result.Ok);
            Assert.IsNull(server.SearchMateAsync("c", "Cid", GroupLaunch.Coop).Result.Duo, "b cancelled");
        }

        [Test]
        public void MateSearch_Offline_ASimulatedMateComes_AndTheSearcherLeads()
        {
            var now = new System.DateTime(2026, 10, 12, 12, 0, 0, System.DateTimeKind.Utc);
            var server = new PvpServer(new MemoryPvpStore(), () => now) { IsBot = id => id != "a", BotName = id => "Bot" };
            Assert.IsNull(server.SearchMateAsync("a", "Ana", GroupLaunch.Coop).Result.Duo);
            now = now.AddSeconds(5);
            var duo = server.SearchMateAsync("a", "Ana", GroupLaunch.Coop).Result.Duo;
            Assert.AreEqual("a", duo.Leader);
            Assert.AreEqual("Bot", duo.Names[1]);
            Assert.IsNull(server.LaunchGroupAsync("a", duo.Id, GroupLaunch.Coop).Result.Error);
        }

        [Test]
        public void BotMate_IsAlwaysReady()
        {
            var (server, id) = ServerWithDuo();
            server.IsBot = pid => pid == "b";
            Assert.IsNull(server.LaunchGroupAsync("a", id, GroupLaunch.Coop).Result.Error);
        }
    }
}
