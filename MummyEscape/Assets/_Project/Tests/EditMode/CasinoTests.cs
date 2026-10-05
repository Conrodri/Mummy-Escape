using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The casino wheels (odds, legendaries, the server's seal wheel) and the titles.</summary>
    public class CasinoTests
    {
        [Test]
        public void Wheels_WeighExactlyTheTotal_AndGiveHalfALegendaryPercent()
        {
            foreach (var wheel in new[] { Casino.Scarabs, Casino.Seals })
            {
                int total = 0, legendary = 0;
                foreach (var s in wheel.Segments)
                {
                    total += s.Weight;
                    if (s.Kind == PrizeKind.Legendary) legendary += s.Weight;
                }
                Assert.AreEqual(Casino.WeightTotal, total, wheel.Id);
                Assert.AreEqual(50, legendary, "0.5 % on " + wheel.Id);
            }
        }

        [Test]
        public void Wheels_GiveBackLessThanTheyCost()
        {
            foreach (var wheel in new[] { Casino.Scarabs, Casino.Seals })
            {
                double back = 0;
                foreach (var s in wheel.Segments)
                    if (s.Kind == PrizeKind.Currency) back += s.Amount * (double)s.Weight / Casino.WeightTotal;
                Assert.That(back, Is.LessThan(wheel.Price), wheel.Id);
                Assert.That(back, Is.GreaterThan(wheel.Price * 0.3), wheel.Id);
            }
        }

        [Test]
        public void Spin_LandsOnTheWedgeOfTheTicket()
        {
            var wheel = Casino.Scarabs;
            // The first 50 tickets of 10 000 are the legendary wedge.
            var r = Casino.Spin(wheel, 0.0, 0.0, new List<string>());
            Assert.AreEqual(PrizeKind.Legendary, r.Kind);
            Assert.AreEqual(wheel.Legendaries[0], r.Legendary);
            r = Casino.Spin(wheel, 49.5 / Casino.WeightTotal, 0.99, new List<string>());
            Assert.AreEqual(wheel.Legendaries[wheel.Legendaries.Length - 1], r.Legendary);
            r = Casino.Spin(wheel, 50.5 / Casino.WeightTotal, 0.0, new List<string>());
            Assert.AreEqual(1, r.Segment);
            Assert.AreEqual(PrizeKind.Currency, r.Kind);
            Assert.AreEqual(wheel.Segments[1].Amount, r.Amount);
            r = Casino.Spin(wheel, 0.999999, 0.0, new List<string>());
            Assert.AreEqual(wheel.Segments.Length - 1, r.Segment);
        }

        [Test]
        public void Spin_NeverGivesALegendaryTwice_ThenPaysTheJackpot()
        {
            var wheel = Casino.Scarabs;
            var owned = new List<string>(wheel.Legendaries);
            owned.Remove(wheel.Legendaries[2]);
            Assert.AreEqual(wheel.Legendaries[2], Casino.Spin(wheel, 0.0, 0.0, owned).Legendary);
            owned.Add(wheel.Legendaries[2]);
            var r = Casino.Spin(wheel, 0.0, 0.0, owned);
            Assert.IsNull(r.Legendary);
            Assert.AreEqual(PrizeKind.Currency, r.Kind);
            Assert.AreEqual(wheel.Segments[0].Amount, r.Amount);
            Assert.AreEqual(0, r.Segment, "still the legendary wedge");
        }

        [Test]
        public void Spin_MatchesTheOddsOverManyTurns()
        {
            var rng = new Random(7);
            int legendary = 0, n = 200_000;
            for (int i = 0; i < n; i++)
                if (Casino.Spin(Casino.Seals, rng.NextDouble(), rng.NextDouble(), null).Kind == PrizeKind.Legendary) legendary++;
            Assert.That(legendary / (double)n, Is.EqualTo(0.005).Within(0.001));
        }

        [Test]
        public async Task SealWheel_IsPaidAndDrawnByTheServer()
        {
            var store = new MemoryPvpStore();
            double next = 0.0; // the legendary wedge
            var server = new PvpServer(store, () => new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), null, () => next);

            var broke = await server.SpinSealWheelAsync("alice");
            Assert.IsFalse(broke.Ok);
            Assert.AreEqual("SEALS", broke.Error);

            await store.UpdatePlayerAsync("alice", d => d.Seals = 120);
            var won = await server.SpinSealWheelAsync("alice");
            Assert.IsTrue(won.Ok);
            Assert.AreEqual(PrizeKind.Legendary, won.Result.Kind);
            Assert.AreEqual(70, won.Seals);
            CollectionAssert.Contains(won.UnlockedRewards, won.Result.Legendary);
            StringAssert.StartsWith("pvp_leg_", won.Result.Legendary);

            next = 0.999; // the last wedge: scarabs back
            var small = await server.SpinSealWheelAsync("alice");
            Assert.AreEqual(PrizeKind.Currency, small.Result.Kind);
            Assert.AreEqual(70 - 50 + small.Result.Amount, small.Seals);
        }

        [Test]
        public void Titles_DuelOnesAreCheckedAgainstTheServerData()
        {
            var d = new PlayerPvpData { Wins = 60, HighestLeague = League.Or };
            Assert.AreEqual("title_wins50", Titles.Check("title_wins50", d));
            Assert.IsNull(Titles.Check("title_wins100", d));
            Assert.AreEqual("title_argent", Titles.Check("title_argent", d));
            Assert.IsNull(Titles.Check("title_platine", d));
            Assert.IsNull(Titles.Check("title_diamant", null));
            Assert.AreEqual("title_act3", Titles.Check("title_act3", null), "solo titles are trusted");
            Assert.IsNull(Titles.Check("title_unknown", d));
        }

        [Test]
        public void Titles_Progress()
        {
            var act = Titles.Get("title_act2");
            Assert.AreEqual((30, 30), Titles.Progress(act, a => a == 2 ? 31 : 0, 0, 0, League.Bronze));
            Assert.AreEqual((12, 30), Titles.Progress(act, a => a == 2 ? 12 : 30, 0, 0, League.Bronze));
            Assert.AreEqual((7, 15), Titles.Progress(Titles.Get("title_speed15"), _ => 0, 7, 0, League.Bronze));
            Assert.AreEqual((1, 1), Titles.Progress(Titles.Get("title_platine"), _ => 0, 0, 0, League.Diamant));
            Assert.AreEqual((0, 1), Titles.Progress(Titles.Get("title_platine"), _ => 0, 0, 0, League.Or));
        }

        [Test]
        public void Look_KeepsOnlyKnownTitles()
        {
            Assert.AreEqual("title_speed5", PlayerLook.Sanitize(new PlayerLook { Title = "title_speed5" }).Title);
            Assert.IsNull(PlayerLook.Sanitize(new PlayerLook { Title = "title_god" }));
            Assert.IsNull(PlayerLook.Sanitize(new PlayerLook { Mummy = "m", Title = "<b>Admin</b>" }).Title);
        }
    }
}
