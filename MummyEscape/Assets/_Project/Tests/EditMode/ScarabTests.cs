using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The scarabs held by the server: carried over once, earned by replayed runs, spent only there at the shared prices.</summary>
    public class ScarabTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc); // before Halloween

        static (PvpServer, MemoryPvpStore) NewServer()
        {
            var store = new MemoryPvpStore();
            return (new PvpServer(store, () => Now), store);
        }

        static Task<WalletResponse> Open(PvpServer server, string me, int scarabs, int stars = 0, params string[] owned)
        {
            var seed = new ScarabSeed { Scarabs = scarabs, Owned = new List<string>(owned) };
            // Three stars on each level until the count is reached.
            for (int i = 0; i * 3 < stars; i++) seed.Stars.Add(new LevelId(Difficulty.Easy, 1 + i / 10, 1 + i % 10).Key + "=3");
            return server.OpenScarabsAsync(me, seed);
        }

        static List<SoloRunReport> Runs(params SoloRunReport[] runs) => new List<SoloRunReport>(runs);

        [Test]
        public async Task Open_CarriesOverOnce_CappedAndChecked()
        {
            var (server, _) = NewServer();
            var seed = new ScarabSeed
            {
                Scarabs = 999_999,
                Stars = new List<string> { Progression.First(Difficulty.Easy).Key + "=9", "nonsense=3", "x" },
                Owned = new List<string> { "mummy_cat", "pvp_leg_seth", "made_up", "leg_ra" },
            };
            var r = await server.OpenScarabsAsync("alice", seed);
            Assert.IsNull(r.Error);
            Assert.IsTrue(r.Wallet.ScarabsOpen);
            Assert.AreEqual(PvpServer.ScarabsCarriedMax, r.Wallet.Scarabs, "an edited save keeps the cap");
            Assert.AreEqual(3, PvpServer.TotalStars(r.Wallet), "stars clamped to 3, unknown levels dropped");
            CollectionAssert.AreEquivalent(new[] { "mummy_cat", "leg_ra" }, r.Wallet.Owned, "only shop pieces and scarab wheel legendaries");

            r = await Open(server, "alice", 500);
            Assert.AreEqual(PvpServer.ScarabsCarriedMax, r.Wallet.Scarabs, "carried over once");
        }

        [Test]
        public async Task Runs_PayTheirNewStarsOnce_OnlyOnceHeld()
        {
            var (server, _) = NewServer();
            var level = Progression.First(Difficulty.Easy);
            var r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("a", level, 0)));
            Assert.AreEqual(0, r.Wallet.Scarabs, "before the carry-over, the phone counts them");

            await Open(server, "alice", 0);
            r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("b", level, 0), WalletTests.SolvedRun("b", level, 0)));
            Assert.AreEqual(3 * 10 + 2, r.Wallet.Scarabs, "three new stars and the escape, once");
            r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("c", level, 1)));
            Assert.AreEqual(3 * 10 + 2 + 2, r.Wallet.Scarabs, "no new star: the escape only");
            r = await server.SubmitSoloRunsAsync("alice", Runs(WalletTests.SolvedRun("d", level, 1, cheatFast: true)));
            Assert.AreEqual(34, r.Wallet.Scarabs, "a run the replay refuses pays nothing");
        }

        [Test]
        public async Task EndlessTombs_PayOnce()
        {
            var (server, _) = NewServer();
            string text = EndlessTests.StoredText();
            server.EndlessArchive = () => text;
            var lines = TombArchive.Lines(text, Difficulty.Normal);
            SoloRunReport At(string id, int depth)
            {
                var run = WalletTests.SolvedRun(id, TombArchive.Load(lines[depth]));
                run.EndlessMode = (int)Difficulty.Normal;
                run.Depth = depth;
                return run;
            }
            await server.OpenScarabsAsync("alice", new ScarabSeed { EndlessCleared = new List<int> { 0, 3, 0 } });
            var r = await server.SubmitSoloRunsAsync("alice", Runs(At("t2", 2)));
            Assert.AreEqual(0, r.Wallet.Scarabs, "tomb 3 was paid on the phone");
            r = await server.SubmitSoloRunsAsync("alice", Runs(At("t3", 3), At("t3-again", 3)));
            Assert.AreEqual(BattlePass.EndlessScarabs, r.Wallet.Scarabs, "a new tomb pays once");
        }

        [Test]
        public async Task Pieces_AreSoldAtTheSharedPrice_WithTheirStars()
        {
            var (server, _) = NewServer();
            Assert.AreEqual("CLOSED", (await server.BuyScarabPieceAsync("alice", "mummy_cat")).Error, "not held here yet");

            await Open(server, "alice", 500);
            int price = ScarabShop.PriceNow(ScarabShop.Get("mummy_cat"), Now);
            var r = await server.BuyScarabPieceAsync("alice", "mummy_cat");
            Assert.IsNull(r.Error);
            Assert.AreEqual(500 - price, r.Wallet.Scarabs);
            CollectionAssert.Contains(r.Wallet.Owned, "mummy_cat");
            Assert.AreEqual("OWNED", (await server.BuyScarabPieceAsync("alice", "mummy_cat")).Error);
            Assert.AreEqual("STARS", (await server.BuyScarabPieceAsync("alice", "mummy_glyphs")).Error, "an act piece asks for 50 stars");
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.BuyScarabPieceAsync("alice", "gold_leg_isis")).Error);
            Assert.AreEqual("EVENT", (await server.BuyScarabPieceAsync("alice", Halloween.ShopSkin)).Error, "Halloween's piece only during the event");
            Assert.AreEqual("SCARABS", (await server.BuyScarabPieceAsync("alice", "ebony")).Error);
            Assert.AreEqual(500 - price, (await server.GetWalletAsync("alice")).Wallet.Scarabs, "refusals take nothing");
        }

        [Test]
        public async Task ActCollection_CostsItsMissingPieces_AFifthLess()
        {
            var (server, _) = NewServer();
            await Open(server, "alice", 2000, 50, "torch_scepter");
            Assert.AreEqual("STARS", (await server.BuyScarabSetAsync("alice", 2)).Error, "act 2 asks for 100 stars");
            int price = ScarabShop.SetPrice(1, new[] { "torch_scepter" });
            Assert.AreEqual(ScarabShop.SetPrice(120 + 100 + 100), price, "the mummy, the hat and the shoes of act 1");
            var r = await server.BuyScarabSetAsync("alice", 1);
            Assert.IsNull(r.Error);
            Assert.AreEqual(2000 - price, r.Wallet.Scarabs);
            foreach (var piece in ScarabShop.ThemeSet(1)) CollectionAssert.Contains(r.Wallet.Owned, piece.Id);
            Assert.AreEqual("OWNED", (await server.BuyScarabSetAsync("alice", 1)).Error);
        }

        [Test]
        public async Task Wheel_IsSpunHere()
        {
            var (server, _) = NewServer();
            Assert.AreEqual("CLOSED", (await server.SpinScarabWheelAsync("alice")).Error);
            await Open(server, "alice", 60);
            var r = await server.SpinScarabWheelAsync("alice");
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(60 - Casino.Scarabs.Price + Casino.Winnings(r.Results), r.Wallet.Scarabs);
            await Open(server, "bob", Casino.Scarabs.Price - 1);
            Assert.AreEqual("SCARABS", (await server.SpinScarabWheelAsync("bob")).Error);
            Assert.AreEqual("COUNT", (await server.SpinScarabWheelAsync("alice", 3)).Error);

            await Open(server, "carol", Casino.Scarabs.DirectPrice);
            var b = await server.BuyWheelLegendaryAsync("carol", "leg_ra");
            Assert.IsNull(b.Error);
            Assert.AreEqual(0, b.Wallet.Scarabs);
            CollectionAssert.Contains(b.Wallet.Owned, "leg_ra");
        }

        [Test]
        public async Task GoldOffers_PassAndGuild_UseTheHeldScarabs()
        {
            var (server, store) = NewServer();
            server.AllowTestPurchases = true;
            await Open(server, "alice", 0);
            await store.UpdatePlayerAsync("alice", d => d.Wallet.Gold = 1000);
            var offer = GoldShop.ScarabOffers[0];
            var r = await server.BuyScarabsAsync("alice", offer.Id);
            Assert.AreEqual(offer.Scarabs, r.Wallet.Scarabs, "credited here");

            var season = BattlePass.At(Now);
            await store.UpdatePlayerAsync("alice", d => { d.Wallet.ClaimsSeason = season.Id; d.Wallet.PassXp = BattlePass.XpPerTier; });
            r = await server.ClaimPassRewardsAsync("alice", season.Id, new List<int> { 1 }, null);
            Assert.AreEqual(offer.Scarabs + BattlePass.Free(1).Amount, r.Wallet.Scarabs, "the pass's scarab tier, collected here");

            Assert.AreEqual("SCARABS", (await server.CreateGuildAsync("alice", "Alice", "Les Momies", "MOM")).Error);
            await store.UpdatePlayerAsync("alice", d => d.Wallet.Scarabs = TeamConfig.GuildCreationScarabs + 1);
            Assert.IsNull((await server.CreateGuildAsync("alice", "Alice", "Les Momies", "MOM")).Error);
            Assert.AreEqual(1, (await server.GetWalletAsync("alice")).Wallet.Scarabs, "the guild's price taken");
        }
    }
}
