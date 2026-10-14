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
            Assert.AreEqual(item.Gold * 2 - ShopSale.GoldPrice(item, Now), r.Wallet.Gold, "paid once (at the sale price when it is on sale)");
        }

        [Test]
        public async Task GoldSet_CostsItsMissingPieces_TwentyPercentOff()
        {
            var (server, store) = NewServer();
            var set = GoldShop.Sets[0];
            var first = GoldShop.Exclusive(set.SkinIds[0]);
            await Give(store, "alice", 500);
            await server.BuyGoldItemAsync("alice", first.SkinId);
            int full = 0;
            foreach (var id in set.SkinIds) full += GoldShop.Exclusive(id).Gold;
            int expected = (full - first.Gold) * 4 / 5;

            var r = await server.BuyGoldItemAsync("alice", set.Id);
            Assert.IsNull(r.Error);
            Assert.AreEqual(500 - ShopSale.GoldPrice(first, Now) - expected, r.Wallet.Gold, "the piece already owned is not paid again");
            foreach (var id in set.SkinIds) CollectionAssert.Contains(r.Wallet.Skins, id);
            Assert.AreEqual("OWNED", (await server.BuyGoldItemAsync("alice", set.Id)).Error);

            await Give(store, "bob", 1);
            Assert.AreEqual("GOLD", (await server.BuyGoldItemAsync("bob", set.Id)).Error);
            Assert.AreEqual(0, (await server.GetWalletAsync("bob")).Wallet.Skins.Count, "nothing given without the gold");
        }

        [Test]
        public async Task Purchase_StoppedBeforeTheWallet_IsCreditedOnRetry_OnlyOnce()
        {
            var (server, store) = NewServer();
            // A first attempt wrote the record, then failed before the wallet.
            store.Shared[PvpServer.OrdersCollection + "/test-cut"] = new PurchaseRecord { OrderId = "test-cut", PlayerId = "alice", Gold = 100, Pending = true };
            var r = await server.VerifyPurchaseAsync("alice", "mummyrush.gold.100", "test-cut");
            Assert.IsNull(r.Error);
            Assert.AreEqual(100, r.Wallet.Gold, "credited on the retry");
            r = await server.VerifyPurchaseAsync("alice", "mummyrush.gold.100", "test-cut");
            Assert.AreEqual(100, r.Wallet.Gold, "and only once");

            // A record from before the flag (credited, long gone from the wallet's recent orders) is never paid again.
            store.Shared[PvpServer.OrdersCollection + "/test-old"] = new PurchaseRecord { OrderId = "test-old", PlayerId = "alice", Gold = 100 };
            Assert.AreEqual(100, (await server.VerifyPurchaseAsync("alice", "mummyrush.gold.100", "test-old")).Wallet.Gold);
            Assert.AreEqual("ALREADY_CREDITED", (await server.VerifyPurchaseAsync("bob", "mummyrush.gold.100", "test-cut")).Error);
        }

        [Test]
        public async Task Purchase_ClaimedByAnotherAccount_IsRefused_EvenWithoutItsRecord()
        {
            var (server, store) = NewServer();
            Assert.IsNull((await server.VerifyPurchaseAsync("alice", "mummyrush.gold.100", "test-race")).Error);
            store.Shared.Remove(PvpServer.OrdersCollection + "/test-race"); // the per-order record lost in a race
            Assert.AreEqual("ALREADY_CREDITED", (await server.VerifyPurchaseAsync("bob", "mummyrush.gold.100", "test-race")).Error, "the arbiter still knows");
            Assert.AreEqual(0, (await server.GetWalletAsync("bob")).Wallet.Gold);
        }

        [Test]
        public async Task PassClaims_FollowTheXpHeldByTheServer()
        {
            var (server, store) = NewServer();
            var season = BattlePass.At(Now);
            var all = new System.Collections.Generic.List<int>();
            for (int t = 1; t <= BattlePass.Tiers; t++) all.Add(t);
            await store.UpdatePlayerAsync("alice", d => (d.Wallet ??= new Wallet()).Passes.Add(season.Id));

            var r = await server.ClaimPassRewardsAsync("alice", season.Id, all, all);
            Assert.IsNull(r.Error);
            Assert.IsEmpty(r.Wallet.PremiumClaimed, "no XP held here, no tier");

            await Give(store, "alice", GoldShop.TierBundlePrice * 2);
            await server.BuyTiersAsync("alice", "req-1");
            await server.BuyTiersAsync("alice", "req-2");
            r = await server.ClaimPassRewardsAsync("alice", season.Id, all, all);
            CollectionAssert.Contains(r.Wallet.PremiumClaimed, 20, "bought tiers are XP held here");
            CollectionAssert.DoesNotContain(r.Wallet.PremiumClaimed, 30);

            var huge = new System.Collections.Generic.List<int>(new int[BattlePass.Tiers + 1]);
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.ClaimPassRewardsAsync("alice", season.Id, huge, null)).Error);
        }

        /// <summary>The optimal route of a tomb, timed like a quick player (each action when the previous animation ends).</summary>
        internal static SoloRunReport SolvedRun(string id, Core.LevelId level, int variant, bool cheatFast = false)
        {
            var run = SolvedRun(id, Core.LevelGenerator.Generate(level, variant), cheatFast);
            run.Level = level.Key;
            run.Variant = variant;
            return run;
        }

        internal static SoloRunReport SolvedRun(string id, Core.Level maze, bool cheatFast = false)
        {
            var recorder = new RunRecorder(maze);
            var session = new Core.GameSession(maze);
            int ms = 0;
            foreach (var action in maze.Solution.Actions)
            {
                var before = session.State;
                recorder.Record(action, ms, before);
                session.Apply(action);
                ms += cheatFast ? 0 : 2000;
            }
            if (cheatFast) foreach (var input in recorder.Inputs) input.Tick = 0;
            return new SoloRunReport { Id = id, Inputs = recorder.Inputs };
        }

        [Test]
        public async Task SoloRuns_AreReplayedHere_AndCountOnce()
        {
            var (server, _) = NewServer();
            var level = Core.Progression.First(Core.Difficulty.Easy);
            var good = SolvedRun("run-1", level, 0);
            var r = await server.SubmitSoloRunsAsync("alice", new System.Collections.Generic.List<SoloRunReport> { good, good });
            Assert.IsNull(r.Error);
            Assert.AreEqual(BattlePass.SoloXp(true, 3), r.Wallet.PassXp, "the optimal route: three stars, counted once");

            var fast = SolvedRun("run-2", level, 0, cheatFast: true);
            var forged = new SoloRunReport { Id = "run-3", Level = level.Key, Variant = 0, Inputs = new System.Collections.Generic.List<RunInput>() };
            var unknown = new SoloRunReport { Id = "run-4", Level = "level_z9_9", Variant = 0, Inputs = new System.Collections.Generic.List<RunInput>() };
            r = await server.SubmitSoloRunsAsync("alice", new System.Collections.Generic.List<SoloRunReport> { fast, forged, unknown });
            Assert.AreEqual(BattlePass.SoloXp(true, 3), r.Wallet.PassXp, "too fast, unfinished or unknown: nothing");

            var many = new System.Collections.Generic.List<SoloRunReport>(new SoloRunReport[PvpServer.SoloRunsPerCall + 1]);
            Assert.AreEqual("UNKNOWN_PRODUCT", (await server.SubmitSoloRunsAsync("alice", many)).Error);
        }

        [Test]
        public async Task SoloRuns_EndlessTombs_AreReadFromTheArchive()
        {
            var (server, _) = NewServer();
            string text = EndlessTests.StoredText();
            server.EndlessArchive = () => text;
            var lines = Core.TombArchive.Lines(text, Core.Difficulty.Normal);
            var tomb = Core.TombArchive.Load(lines[3]);
            SoloRunReport At(string id, int depth)
            {
                var run = SolvedRun(id, tomb);
                run.EndlessMode = (int)Core.Difficulty.Normal;
                run.Depth = depth;
                return run;
            }
            var r = await server.SubmitSoloRunsAsync("alice", new System.Collections.Generic.List<SoloRunReport> { At("deep-3", 3), At("deep-4", 4), At("deep-far", lines.Count) });
            Assert.AreEqual(BattlePass.EndlessXp, r.Wallet.PassXp, "only the run played on the tomb at its depth");
        }

        [Test]
        public void Sale_DealsEachItemOncePerDeck_NeverTwiceADay()
        {
            var pool = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" }; // noloc
            const int perDay = 3;
            var shown = new System.Collections.Generic.List<string>();
            for (int day = 0; day < 400; day++)
            {
                var today = ShopSale.Deal(pool, perDay, day, 7);
                Assert.AreEqual(perDay, today.Count);
                CollectionAssert.AllItemsAreUnique(today, $"day {day}");
                CollectionAssert.AreEqual(today, ShopSale.Deal(pool, perDay, day, 7), "the same sale everywhere");
                shown.AddRange(today);
            }
            for (int deck = 0; (deck + 1) * pool.Length <= shown.Count; deck++)
                CollectionAssert.AreEquivalent(pool, shown.GetRange(deck * pool.Length, pool.Length), $"deck {deck}: every item once");
            for (int p = 1; p < shown.Count; p++)
                Assert.AreNotEqual(shown[p - 1], shown[p], "no item dealt twice in a row across decks");
        }

        [Test]
        public async Task Sale_TreasureItemOnSale_CostsAQuarterLess()
        {
            var (server, store) = NewServer();
            var sale = ShopSale.GoldOnSale(Now);
            Assert.AreEqual(2, sale.Count, "one epic, one legendary");
            GoldItem full = null;
            foreach (var g in ShopSale.GoldEpics) if (!sale.Contains(g)) full = g;

            await Give(store, "alice", 500);
            var r = await server.BuyGoldItemAsync("alice", sale[1].SkinId);
            Assert.IsNull(r.Error);
            Assert.AreEqual(500 - ShopSale.Price(sale[1].Gold), r.Wallet.Gold);
            Assert.Less(ShopSale.Price(sale[1].Gold), sale[1].Gold);
            r = await server.BuyGoldItemAsync("alice", full.SkinId);
            Assert.AreEqual(500 - ShopSale.Price(sale[1].Gold) - full.Gold, r.Wallet.Gold, "off the sale, full price");
        }

        [Test]
        public void GoldSets_AreMadeOfTreasureExclusives()
        {
            foreach (var set in GoldShop.Sets)
                foreach (var id in set.SkinIds) Assert.IsNotNull(GoldShop.Exclusive(id), set.Id + " " + id);
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
