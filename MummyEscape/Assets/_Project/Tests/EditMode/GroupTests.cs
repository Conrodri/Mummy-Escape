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
        public void BotMate_IsAlwaysReady()
        {
            var (server, id) = ServerWithDuo();
            server.IsBot = pid => pid == "b";
            Assert.IsNull(server.LaunchGroupAsync("a", id, GroupLaunch.Coop).Result.Error);
        }
    }
}
