using System;
using System.Threading.Tasks;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The golden wallet kept by the server: purchases credited once, every spend checked and debited there.</summary>
    public class WalletTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

        static (PvpServer, MemoryPvpStore) NewServer()
        {
            var store = new MemoryPvpStore();
            return (new PvpServer(store, () => Now) { AllowTestPurchases = true }, store);
        }

        static Task Give(MemoryPvpStore store, string player, int gold) =>
            store.UpdatePlayerAsync(player, d => (d.Wallet ??= new Wallet()).Gold = gold);

        [Test]
        public async Task Purchase_IsCreditedOnce_OnTheFirstAccountOnly()
        {
            var (server, _) = NewServer();
            var r = await server.VerifyPurchaseAsync("alice", "mummyrush.gold.250", "test-order-1");
            Assert.IsNull(r.Error);
            Assert.AreEqual(300, r.Wallet.Gold, "250 + 50 bonus");

            r = await server.VerifyPurchaseAsync("alice", "mummyrush.gold.250", "test-order-1");
            Assert.IsNull(r.Error, "the same purchase again is fine");
            Assert.AreEqual(300, r.Wallet.Gold, "but credits nothing more");

            Assert.AreEqual("ALREADY_CREDITED", (await server.VerifyPurchaseAsync("bob", "mummyrush.gold.250", "test-order-1")).Error);
            Assert.AreEqual(0, (await server.GetWalletAsync("bob")).Wallet.Gold);
        }

        [Test]
        public async Task Purchase_NeedsAVerifiedStore()
        {
            var (server, _) = NewServer();
            server.AllowTestPurchases = false;
            Assert.AreEqual("NOT_VERIFIED", (await server.VerifyPurchaseAsync("alice", "mummyrush.gold.10", "test-x")).Error);
            Assert.AreEqual("NOT_VERIFIED", (await server.VerifyPurchaseAsync("alice", "mummyrush.gold.10", "real-token")).Error);
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.VerifyPurchaseAsync("alice", "mummyrush.gold.9999", "test-x")).Error);
            Assert.AreEqual(0, (await server.GetWalletAsync("alice")).Wallet.Gold);
        }

        [Test]
        public async Task Spend_DebitsTheServerWallet_OrRefusesWithoutEnoughGold()
        {
            var (server, store) = NewServer();
            var offer = GoldShop.ScarabOffers[0];
            await Give(store, "alice", offer.Gold - 1);
            var r = await server.BuyScarabsAsync("alice", offer.Id);
            Assert.AreEqual("GOLD", r.Error);
            Assert.AreEqual(offer.Gold - 1, r.Wallet.Gold, "nothing taken");

            await Give(store, "alice", offer.Gold + 5);
            r = await server.BuyScarabsAsync("alice", offer.Id);
            Assert.IsNull(r.Error);
            Assert.AreEqual(5, r.Wallet.Gold);

            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.BuyScarabsAsync("alice", "scarabs_free")).Error);
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.BuyGoldItemAsync("alice", "classic")).Error);
        }

        [Test]
        public async Task Spend_ReplayedRequest_IsDebitedOnce_AndListedForDelivery()
        {
            var (server, store) = NewServer();
            await Give(store, "alice", 25);
            var r = await server.BuyTiersAsync("alice", "req-1");
            Assert.IsNull(r.Error);
            Assert.AreEqual(25 - GoldShop.TierBundlePrice, r.Wallet.Gold);
            CollectionAssert.Contains(r.Wallet.Requests, "req-1");

            r = await server.BuyTiersAsync("alice", "req-1");
            Assert.IsNull(r.Error, "the answer was lost: asking again is fine");
            Assert.AreEqual(25 - GoldShop.TierBundlePrice, r.Wallet.Gold, "and is not debited again");

            r = await server.BuyTiersAsync("alice", "req-2");
            Assert.AreEqual(25 - 2 * GoldShop.TierBundlePrice, r.Wallet.Gold, "a new request is a new spend");

            r = await server.BuyTiersAsync("alice", "req-3");
            Assert.AreEqual("GOLD", r.Error);
            CollectionAssert.DoesNotContain(r.Wallet.Requests, "req-3", "a refused spend is not listed");
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.BuyTiersAsync("alice", "")).Error);
        }

        [Test]
        public async Task Spend_KeepsTheLastFiftyRequests()
        {
            var (server, store) = NewServer();
            await Give(store, "alice", 1000);
            for (int i = 0; i < 60; i++) await server.BuyTiersAsync("alice", "req-" + i);
            var w = (await server.GetWalletAsync("alice")).Wallet;
            Assert.AreEqual(50, w.Requests.Count);
            Assert.AreEqual("req-59", w.Requests[w.Requests.Count - 1]);
            Assert.AreEqual(1000 - 60 * GoldShop.TierBundlePrice, w.Gold);
        }

        [Test]
        public async Task Pass_CostsFortyGold_ActivatesTheSeason_AndIsBoughtOnce()
        {
            var (server, store) = NewServer();
            var season = BattlePass.At(Now);
            await Give(store, "alice", GoldShop.PassPrice - 1);
            Assert.AreEqual("GOLD", (await server.BuyPassAsync("alice")).Error);

            await Give(store, "alice", GoldShop.PassPrice + 10);
            var r = await server.BuyPassAsync("alice");
            Assert.IsNull(r.Error);
            Assert.AreEqual(40, GoldShop.PassPrice);
            Assert.AreEqual(10, r.Wallet.Gold);
            CollectionAssert.Contains(r.Wallet.Passes, season.Id);
            CollectionAssert.Contains(r.Wallet.Skins, season.Legendary);

            await Give(store, "alice", 100);
            r = await server.BuyPassAsync("alice");
            Assert.AreEqual("OWNED", r.Error);
            Assert.AreEqual(100, r.Wallet.Gold, "already premium: nothing taken");
        }

        [Test]
        public async Task Exclusive_IsBoughtOnce()
        {
            var (server, store) = NewServer();
            var item = GoldShop.Exclusives[0];
            await Give(store, "alice", item.Gold * 2);
            var r = await server.BuyGoldItemAsync("alice", item.SkinId);
            Assert.IsNull(r.Error);
            CollectionAssert.Contains(r.Wallet.Skins, item.SkinId);
            r = await server.BuyGoldItemAsync("alice", item.SkinId);
            Assert.AreEqual("OWNED", r.Error);
            Assert.AreEqual(item.Gold, r.Wallet.Gold);
        }

        [Test]
        public void PaidSkins_AreTheTreasureAndPassOnes()
        {
            foreach (var item in GoldShop.Exclusives) Assert.IsTrue(GoldShop.IsPaid(item.SkinId), item.SkinId);
            Assert.IsTrue(GoldShop.IsPaid(BattlePass.At(Now).Legendary));
            Assert.IsFalse(GoldShop.IsPaid("classic"));
            Assert.IsFalse(GoldShop.IsPaid(null));
        }
    }
}
