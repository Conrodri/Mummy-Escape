using System;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The challenge tower: its climb, its handicaps, what ends a run and how runs rank.</summary>
    public class TowerTests
    {
        [Test]
        public void Source_ClimbsThroughTheModes_AndTurnsTheActs()
        {
            Assert.AreEqual(Difficulty.Easy, Tower.Source(1).Mode);
            Assert.AreEqual(Difficulty.Easy, Tower.Source(Tower.EasyFloors).Mode);
            Assert.AreEqual(Difficulty.Normal, Tower.Source(Tower.EasyFloors + 1).Mode);
            Assert.AreEqual(Difficulty.Extreme, Tower.Source(Tower.NormalFloors + 1).Mode);
            Assert.AreEqual(Difficulty.Extreme, Tower.Source(500).Mode);
            Assert.AreNotEqual(Tower.Source(1).Act, Tower.Source(2).Act);
            Assert.That(DifficultyTable.Target(Tower.Source(Tower.EasyFloors)), Is.GreaterThan(DifficultyTable.Target(Tower.Source(1))));
        }

        [Test]
        public void Curses_MakeTheTombHarder()
        {
            var plain = Tower.Spec(15, TowerCurse.None);
            var cursed = Tower.Spec(15, TowerCurse.Traps | TowerCurse.Bigger | TowerCurse.Storeys);
            Assert.AreEqual(plain.SpikeTraps + 1, cursed.SpikeTraps);
            Assert.That(cursed.Floors, Is.GreaterThan(plain.Floors));
            Assert.AreEqual(5, Tower.PointsPerFloor(TowerCurse.Traps | TowerCurse.Bigger | TowerCurse.Storeys));
            Assert.AreEqual(2, Tower.PointsPerFloor(TowerCurse.None));
        }

        [Test]
        public void Floors_AreTheSameForTheSameRun_AndNewForAnother()
        {
            var a = Tower.Generate(3, TowerCurse.None, 42);
            var b = Tower.Generate(3, TowerCurse.None, 42);
            var c = Tower.Generate(3, TowerCurse.None, 43);
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
        public void Record_KeepsTheMostPoints_ThenTheFastest()
        {
            var record = new TowerRecord();
            var run = new TowerRun { Curses = TowerCurse.Traps };
            run.Escaped(1000);
            run.Escaped(1000);
            Assert.IsTrue(record.Offer(run));
            Assert.AreEqual(6, record.BestPoints);
            var plain = new TowerRun();
            for (int i = 0; i < 3; i++) plain.Escaped(100);
            Assert.IsTrue(record.Offer(plain), "as many points, faster");
            Assert.AreEqual(300, record.BestTimeMs);
            var slow = new TowerRun();
            for (int i = 0; i < 3; i++) slow.Escaped(5000);
            Assert.IsFalse(record.Offer(slow), "as many points, slower");
            Assert.AreEqual(3, record.HighestCleared);
            Assert.That(Tower.EncodeScore(7, 5000), Is.LessThan(Tower.EncodeScore(6, 10)), "more points rank first");
            Assert.AreEqual((6, 300), Tower.DecodeScore(record.Score));
        }

        [Test, Category("Slow")]
        public void EveryFloor_IsGenerated_WithEveryHandicap([Values(0, 1, 2, 3, 4, 5, 6, 7)] int curses)
        {
            for (int floor = 1; floor <= 60; floor += curses == 0 ? 1 : 3)
                Assert.DoesNotThrow(() => Tower.Generate(floor, (TowerCurse)curses, floor * 977), $"floor {floor} curses {(TowerCurse)curses}");
        }
    }
}
