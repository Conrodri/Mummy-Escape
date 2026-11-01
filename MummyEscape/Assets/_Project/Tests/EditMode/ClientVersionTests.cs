using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The store version code and the oldest version the server still lets play.</summary>
    public class ClientVersionTests
    {
        [Test]
        public void Versions_CompareByTheirStoreCode()
        {
            Assert.AreEqual(11401, ClientVersion.Code("0.1.14"), "the code the build script gives the store");
            Assert.AreEqual(1020301, ClientVersion.Code("1.2.3"));
            Assert.IsTrue(ClientVersion.IsOutdated("0.1.14", "0.1.15"));
            Assert.IsTrue(ClientVersion.IsOutdated("0.1.9", "0.1.10"), "numbers, not text");
            Assert.IsFalse(ClientVersion.IsOutdated("0.1.15", "0.1.15"));
            Assert.IsFalse(ClientVersion.IsOutdated("0.2.0", "0.1.15"));
            Assert.IsFalse(ClientVersion.IsOutdated("0.1.0", null), "no minimum: nothing asked");
            Assert.AreEqual("0.1.16", ClientVersion.Higher("0.1.16", "0.1.15"));
            Assert.AreEqual("0.1.15", ClientVersion.Higher(null, "0.1.15"));
        }

        [Test]
        public void TheServer_SendsItsMinimum_OrTheTeamsIfHigher()
        {
            var server = new PvpServer(new MemoryPvpStore());
            Assert.AreEqual(ClientVersion.Minimum, server.GetStatusAsync().Result.MinVersion);
            server.SetStatusAsync("af5s3oREeHqvpfcMh2Pc5Ymuml2D", new ServiceStatus { MinVersion = "9.0.0" }).Wait(); // noloc: the team's account
            Assert.AreEqual("9.0.0", server.GetStatusAsync().Result.MinVersion);
        }
    }
}
