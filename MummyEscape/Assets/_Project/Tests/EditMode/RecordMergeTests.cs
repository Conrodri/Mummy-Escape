using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Cloud save sync: two devices' records fold into one without losing anything.</summary>
    public class RecordMergeTests
    {
        static LevelRecord Rec(int overPar, int timeMs, int stars, int completions, int deaths = 0, int runs = 0) => new LevelRecord
        {
            Key = "1-1", BestOverPar = overPar, BestTimeMs = timeMs, BestMoves = 10 + overPar, BestStars = stars,
            BestHpLeft = 3, Completions = completions, Deaths = deaths, Runs = runs,
        };

        [Test]
        public void Merge_KeepsTheBetterRun()
        {
            var local = Rec(4, 30000, 1, 2);
            local.MergeWith(Rec(1, 50000, 3, 1));
            Assert.AreEqual(1, local.BestOverPar);
            Assert.AreEqual(50000, local.BestTimeMs);
            Assert.AreEqual(11, local.BestMoves);
            Assert.AreEqual(3, local.BestStars);
        }

        [Test]
        public void Merge_KeepsOwnRunWhenBetter()
        {
            var local = Rec(0, 20000, 3, 1);
            local.MergeWith(Rec(2, 10000, 2, 5));
            Assert.AreEqual(0, local.BestOverPar);
            Assert.AreEqual(20000, local.BestTimeMs);
            Assert.AreEqual(5, local.Completions);
        }

        [Test]
        public void Merge_TakesCountersMax_AndIgnoresOtherLevels()
        {
            var local = Rec(0, 20000, 3, 1, deaths: 2, runs: 7);
            local.MergeWith(Rec(0, 20000, 3, 1, deaths: 5, runs: 3));
            Assert.AreEqual(5, local.Deaths);
            Assert.AreEqual(7, local.Runs);

            var other = Rec(0, 1, 3, 9);
            other.Key = "2-1";
            local.MergeWith(other);
            Assert.AreEqual(1, local.Completions);
            local.MergeWith(null);
        }

        [Test]
        public void Merge_FillsAnEmptyRecord()
        {
            var local = new LevelRecord { Key = "1-1", BestOverPar = -1 };
            local.MergeWith(Rec(2, 40000, 2, 1));
            Assert.IsTrue(local.HasBest);
            Assert.AreEqual(2, local.BestOverPar);
        }
    }
}
