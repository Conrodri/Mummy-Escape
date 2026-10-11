using System;
using System.IO;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The Halloween event: its stored tombs, its Paris-time window and what only sells inside it.</summary>
    public class HalloweenTests
    {
        static DateTime Utc(int y, int mo, int d, int h, int mi = 0, int s = 0) => new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

        static string StoredText()
        {
            const string rel = "Assets/_Project/Resources/Tombs/halloween.txt";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    foreach (var path in new[] { Path.Combine(dir.FullName, rel), Path.Combine(dir.FullName, "MummyEscape", rel) })
                        if (File.Exists(path)) return File.ReadAllText(path);
            Assert.Fail("Stored tombs not found: " + rel);
            return null;
        }

        [Test]
        public void StoredTombs_TenLoadAndAreSolvable_EasyThenNormal()
        {
            var lines = Halloween.Lines(StoredText());
            Assert.AreEqual(Halloween.TombCount, lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var level = TombArchive.Load(lines[i]);
                Assert.Greater(level.Solution.Moves, 0, $"tomb {i + 1}");
                Assert.AreEqual(i < 5 ? Difficulty.Easy : Difficulty.Normal, level.Id.Mode, $"tomb {i + 1}");
                Assert.GreaterOrEqual(level.Id.Act, 2, $"tomb {i + 1} costs solo energy like a campaign tomb past act 1");
            }
        }

        [Test]
        public void ParisTime_FollowsTheEuRule_IncludingTheEndOfSummerTime()
        {
            // Summer time ends on Sunday 25 October 2026 at 01:00 UTC (03:00 Paris becomes 02:00).
            Assert.AreEqual(Utc(2026, 10, 25, 2, 59).Ticks, Halloween.ParisTime(Utc(2026, 10, 25, 0, 59)).Ticks);
            Assert.AreEqual(Utc(2026, 10, 25, 2, 0).Ticks, Halloween.ParisTime(Utc(2026, 10, 25, 1, 0)).Ticks);
            Assert.AreEqual(Utc(2026, 7, 1, 14).Ticks, Halloween.ParisTime(Utc(2026, 7, 1, 12)).Ticks);
            Assert.AreEqual(Utc(2026, 1, 1, 13).Ticks, Halloween.ParisTime(Utc(2026, 1, 1, 12)).Ticks);
            // And starts on Sunday 29 March 2026 at 01:00 UTC.
            Assert.AreEqual(Utc(2026, 3, 29, 1, 59).Ticks, Halloween.ParisTime(Utc(2026, 3, 29, 0, 59)).Ticks);
            Assert.AreEqual(Utc(2026, 3, 29, 3, 0).Ticks, Halloween.ParisTime(Utc(2026, 3, 29, 1, 0)).Ticks);
        }

        [Test]
        public void Window_Is24OctoberTo7NovemberIncluded_ParisTime()
        {
            // 24 October 00:00 Paris is still summer time (UTC+2); 8 November 00:00 is winter time (UTC+1).
            Assert.IsFalse(Halloween.IsOpen(Utc(2026, 10, 23, 21, 59, 59)));
            Assert.IsTrue(Halloween.IsOpen(Utc(2026, 10, 23, 22, 0)));
            Assert.IsTrue(Halloween.IsOpen(Utc(2026, 10, 25, 1, 30)), "across the change of time");
            Assert.IsTrue(Halloween.IsOpen(Utc(2026, 11, 7, 22, 59, 59)));
            Assert.IsFalse(Halloween.IsOpen(Utc(2026, 11, 7, 23, 0)));
            Assert.IsFalse(Halloween.IsOpen(Utc(2026, 10, 11, 12)));
            // Another year, the same rule.
            Assert.IsTrue(Halloween.IsOpen(Utc(2027, 10, 23, 22, 0)));
            Assert.IsFalse(Halloween.IsOpen(Utc(2027, 11, 7, 23, 0)));
        }

        [Test]
        public void ScarabWheel_HasTheHalloweenLegendary_OnlyDuringTheEvent_WithTheSameOdds()
        {
            var off = Casino.ScarabsAt(Utc(2026, 10, 1, 12));
            var on = Casino.ScarabsAt(Utc(2026, 10, 31, 12));
            CollectionAssert.DoesNotContain(off.Legendaries, Halloween.WheelLegendary);
            CollectionAssert.Contains(on.Legendaries, Halloween.WheelLegendary);
            Assert.AreSame(off.Segments, on.Segments, "the odds shown stay the wheel's");
            Assert.AreEqual(off.Legendaries.Length + 1, on.Legendaries.Length);

            // Every other legendary owned: the legendary wedge gives the Halloween one in the event, the jackpot outside.
            var owned = new System.Collections.Generic.List<string>(off.Legendaries);
            Assert.AreEqual(Halloween.WheelLegendary, Casino.Spin(on, 0, 0.5, owned).Legendary);
            Assert.IsNull(Casino.Spin(off, 0, 0.5, owned).Legendary);
        }

        [Test]
        public async Task GoldItem_IsSoldByTheServer_OnlyDuringTheEvent()
        {
            var now = Utc(2026, 11, 8, 12);
            var store = new MemoryPvpStore();
            var server = new PvpServer(store, () => now);
            await store.UpdatePlayerAsync("alice", d => (d.Wallet ??= new Wallet()).Gold = 100);
            var item = GoldShop.Exclusive(Halloween.GoldSkin);
            Assert.IsNotNull(item);
            Assert.IsTrue(GoldShop.IsPaid(item.SkinId), "kept by the server's wallet");

            var r = await server.BuyGoldItemAsync("alice", item.SkinId);
            Assert.AreEqual("EVENT", r.Error);
            Assert.AreEqual(100, (await server.GetWalletAsync("alice")).Wallet.Gold, "nothing taken");

            now = Utc(2026, 10, 30, 12);
            r = await server.BuyGoldItemAsync("alice", item.SkinId);
            Assert.IsNull(r.Error);
            CollectionAssert.Contains(r.Wallet.Skins, item.SkinId);
            Assert.AreEqual(100 - item.Gold, r.Wallet.Gold);
        }

        [Test]
        public void Bots_WearTheEventPieces_OnlyDuringTheEvent()
        {
            long inside = new DateTimeOffset(Utc(2026, 10, 30, 12)).ToUnixTimeMilliseconds();
            long outside = new DateTimeOffset(Utc(2026, 11, 8, 12)).ToUnixTimeMilliseconds();
            Assert.IsNull(PvpBots.LookOf("bot_42", outside), "the classic outfit");
            var hats = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                var look = PvpBots.LookOf("bot_" + i, inside);
                Assert.That(look.Hat, Is.EqualTo(Halloween.RewardSkin).Or.EqualTo(Halloween.GoldSkin));
                Assert.AreEqual(look.Hat, PvpBots.LookOf("bot_" + i, inside).Hat, "always the same for a bot");
                hats.Add(look.Hat);
            }
            Assert.AreEqual(2, hats.Count, "both hats are worn");
            var run = PvpBots.Make(new Random(1), 1200, inside);
            Assert.IsNotNull(run.Look, "a server bot's ghost carries its outfit");
        }
    }
}
