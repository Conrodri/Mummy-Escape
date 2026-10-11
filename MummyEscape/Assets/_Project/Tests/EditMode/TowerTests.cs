using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The challenge tower: its Facile climb, the handicap every 3 floors, what ends a run and how runs rank.</summary>
    public class TowerTests
    {
        [Test]
        public void Source_StaysFacile_TurnsTheActs_AndGetsHarder()
        {
            for (int floor = 1; floor <= 80; floor++) Assert.AreEqual(Difficulty.Easy, Tower.Source(floor).Mode);
            Assert.AreNotEqual(Tower.Source(1).Act, Tower.Source(2).Act);
            Assert.That(DifficultyTable.Target(Tower.Source(Tower.RampFloors)), Is.GreaterThan(DifficultyTable.Target(Tower.Source(1))));
        }

        [Test]
        public void Curses_MakeTheTombHarder()
        {
            var plain = Tower.Spec(15, default);
            var cursed = Tower.Spec(15, new TowerCurses { Traps = 2, Bigger = 1, Storeys = 1 });
            Assert.AreEqual(plain.SpikeTraps + plain.DarknessTraps + 2, cursed.SpikeTraps + cursed.DarknessTraps);
            Assert.AreEqual(plain.Floors + 1, cursed.Floors);
        }

        [Test]
        public void ACurse_IsDueEveryThreeFloors_UntilEveryCapIsReached()
        {
            var run = new TowerRun();
            for (int i = 0; i < 2; i++) run.Escaped(100);
            Assert.IsFalse(run.CurseDue);
            run.Escaped(100);
            Assert.IsTrue(run.CurseDue, "after floor 3");
            run.Take(TowerCurse.Storeys);
            Assert.IsFalse(run.CurseDue);
            run.Take(TowerCurse.Traps);
            Assert.AreEqual(1, run.Curses.Count, "one per 3 floors, no more");

            var full = new TowerRun { Cleared = 60, Curses = new TowerCurses { Traps = 3, Bigger = 2, Storeys = 2 } };
            Assert.IsFalse(full.CurseDue, "every handicap at its cap");
            var capped = new TowerRun { Cleared = 9, Curses = new TowerCurses { Storeys = 2 } };
            Assert.IsTrue(capped.CurseDue);
            capped.Take(TowerCurse.Storeys);
            Assert.AreEqual(2, capped.Curses.Storeys, "past its cap: refused");
        }

        [Test]
        public void Floors_AreTheSameForTheSameRun_AndNewForAnother()
        {
            var a = Tower.Generate(3, default, 42);
            var b = Tower.Generate(3, default, 42);
            var c = Tower.Generate(3, default, 43);
            Assert.AreEqual(TombArchive.Write(a), TombArchive.Write(b));
            Assert.AreNotEqual(TombArchive.Write(a), TombArchive.Write(c));
        }

        [Test]
        public void Run_EndsOnADeath_OrUnderTwoStars()
        {
            Assert.IsFalse(Tower.Continues(new LevelResult { Won = false }));
            Assert.IsTrue(Tower.Continues(new LevelResult { Won = true, Moves = 20, Par = 20 }));
            Assert.IsFalse(Tower.Continues(new LevelResult { Won = true, Moves = 40, Par = 20 }), "1 star");
        }

        [Test]
        public void Record_KeepsTheMostFloors_ThenTheFastest()
        {
            var record = new TowerRecord();
            var run = new TowerRun();
            for (int i = 0; i < 3; i++) run.Escaped(1000);
            Assert.IsTrue(record.Offer(run));
            var faster = new TowerRun();
            for (int i = 0; i < 3; i++) faster.Escaped(100);
            Assert.IsTrue(record.Offer(faster), "as many floors, faster");
            var slower = new TowerRun();
            for (int i = 0; i < 3; i++) slower.Escaped(5000);
            Assert.IsFalse(record.Offer(slower), "as many floors, slower");
            Assert.That(Tower.EncodeScore(4, 50000), Is.LessThan(Tower.EncodeScore(3, 10)), "more floors rank first");
            Assert.AreEqual((3, 300), Tower.DecodeScore(record.Score));
        }

        [Test, Category("Slow")]
        public void EveryFloor_IsGenerated_UpToEveryCap([Values(0, 1, 2, 3, 4)] int set)
        {
            var curses = set switch
            {
                0 => default,
                1 => new TowerCurses { Traps = 3 },
                2 => new TowerCurses { Bigger = 2 },
                3 => new TowerCurses { Storeys = 2 },
                _ => new TowerCurses { Traps = 3, Bigger = 2, Storeys = 2 },
            };
            for (int floor = 1; floor <= 40; floor += set == 0 ? 1 : 2)
                Assert.DoesNotThrow(() => Tower.Generate(floor, curses, floor * 977), $"floor {floor} curses {curses.Traps}/{curses.Bigger}/{curses.Storeys}");
        }
    }
}
