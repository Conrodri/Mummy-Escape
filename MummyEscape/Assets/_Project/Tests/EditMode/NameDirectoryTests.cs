using System;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The friend-code directory: a code found whatever its capitals, a renamed player found by the name alone.</summary>
    public class NameDirectoryTests
    {
        static PvpServer NewServer()
        {
            var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
            int seed = 1;
            return new PvpServer(new MemoryPvpStore(), () => now, () => seed++);
        }

        [Test]
        public void FindsACodeWhateverItsCapitals()
        {
            var server = NewServer();
            Assert.IsTrue(server.RegisterNameAsync("lettoh", "Lettoh#76668").Result.Ok);
            Assert.AreEqual("lettoh", server.FindPlayerAsync("me", "lettoh#76668").Result.PlayerId);
            Assert.AreEqual("lettoh", server.FindPlayerAsync("me", "LETTOH#76668").Result.PlayerId);
            Assert.AreEqual("SELF", server.FindPlayerAsync("lettoh", "Lettoh#76668").Result.Error);
            Assert.AreEqual("NOT_FOUND", server.FindPlayerAsync("me", "Nobody#1234").Result.Error);
        }

        [Test]
        public void ARenamedPlayerIsFoundByTheNameAloneWhenItIsUnique()
        {
            var server = NewServer();
            server.RegisterNameAsync("ann", "Ann#1111").Wait();
            server.RegisterNameAsync("ann", "Annie#2222").Wait();
            Assert.AreEqual("NOT_FOUND", server.FindPlayerAsync("me", "Ann#1111").Result.Error, "the old name left the directory");
            Assert.AreEqual("ann", server.FindPlayerAsync("me", "annie#9999").Result.PlayerId, "an old #number, one Annie: found");
            Assert.AreEqual("ann", server.FindPlayerAsync("me", "Annie").Result.PlayerId);

            server.RegisterNameAsync("bob", "Annie#3333").Wait();
            Assert.AreEqual("AMBIGUOUS", server.FindPlayerAsync("me", "Annie#9999").Result.Error, "two Annies: the full code is needed");
            Assert.AreEqual("bob", server.FindPlayerAsync("me", "annie#3333").Result.PlayerId);
        }

        [Test]
        public void DeletingTheDataLeavesTheDirectory()
        {
            var server = NewServer();
            server.RegisterNameAsync("cid", "Cid#4444").Wait();
            server.DeletePlayerDataAsync("cid").Wait();
            Assert.AreEqual("NOT_FOUND", server.FindPlayerAsync("me", "Cid#4444").Result.Error);
        }
    }
}
