using System;
using System.Collections.Generic;
using System.IO;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Stored tombs (<see cref="TombArchive"/>) and the endless mode built on them.</summary>
    public class EndlessTests
    {
        /// <summary>The stored file, from the Unity project (EditMode) or from tools/CoreTests' output folder.</summary>
        static string StoredText()
        {
            const string rel = "Assets/_Project/Resources/Tombs/endless.txt";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    foreach (var path in new[] { Path.Combine(dir.FullName, rel), Path.Combine(dir.FullName, "MummyEscape", rel) })
                        if (File.Exists(path)) return File.ReadAllText(path);
            Assert.Fail("Stored tombs not found: " + rel);
            return null;
        }

        [Test]
        public void Archive_RoundTripsWholeTombs()
        {
            bool portals = false, floors = false;
            foreach (var id in new[] { new LevelId(Difficulty.Easy, 1, 5), new LevelId(Difficulty.Normal, 3, 9), new LevelId(Difficulty.Extreme, 4, 10), new LevelId(Difficulty.Extreme, 5, 6) })
                for (int v = 0; v < 3; v++)
                {
                    var level = LevelGenerator.Generate(id, v);
                    string line = TombArchive.Write(level);
                    StringAssert.DoesNotContain("\n", line);
                    var back = TombArchive.Load(line);
                    Assert.AreEqual(level.ComputeHash(), back.ComputeHash(), line);
                    Assert.AreEqual(level.Id, back.Id);
                    Assert.AreEqual(level.Variant, back.Variant);
                    Assert.AreEqual(level.MaxHp, back.MaxHp);
                    Assert.AreEqual(level.Solution.Moves, back.Solution.Moves);
                    foreach (var c in level.AllCells())
                    {
                        Assert.AreEqual(level[c].TrapIndex, back[c].TrapIndex);
                        Assert.AreEqual(level[c].Param, back[c].Param);
                        Assert.AreEqual(level.IsDecoy(c), back.IsDecoy(c));
                        Assert.AreEqual(level.TryGetTeleportTarget(c, out var a), back.TryGetTeleportTarget(c, out var b));
                        Assert.AreEqual(a, b);
                        portals |= level.TryGetTeleportTarget(c, out _);
                    }
                    floors |= level.Floors > 1;
                    Assert.AreEqual(line, TombArchive.Write(back), "writing a read tomb gives the same line");
                }
            Assert.IsTrue(portals && floors, "the sample covers teleporters and stacked floors");
        }

        [Test]
        public void Archive_RejectsUnknownLines()
        {
            Assert.Throws<FormatException>(() => TombArchive.Read("2 F1-1 g13 v0 3 3 1 2 0 1 AAAA - -"));
            Assert.Throws<FormatException>(() => TombArchive.Read("1 F1-1 g13 v0 3 3 1 2 0 1 AAAA -"));
            Assert.Throws<FormatException>(() => TombArchive.Read("1 F1-1 g13 v0 3 3 1 2 0 99 AAAAAAAAAAAA - -"));
        }

        /// <summary>
        /// Every stored tomb must still load and have a way out under today's rules: a Rules change that breaks an old tomb
        /// fails here (the stored tombs never change, so the rules must keep them playable).
        /// </summary>
        [Test]
        public void StoredTombs_AllLoadAndAreSolvable()
        {
            string text = StoredText();
            foreach (var mode in DifficultyExt.All)
            {
                var lines = TombArchive.Lines(text, mode);
                Assert.GreaterOrEqual(lines.Count, 100, $"{mode}: ladder too short");
                for (int i = 0; i < lines.Count; i++)
                {
                    var level = TombArchive.Load(lines[i]);
                    Assert.AreEqual(mode, level.Id.Mode, $"{mode} tomb {i + 1}");
                    Assert.Greater(level.Solution.Moves, 0, $"{mode} tomb {i + 1}");
                    Assert.Greater(level.Solution.HpLeft, 0, $"{mode} tomb {i + 1}: the way out must leave the mummy alive");
                }
            }
        }

        [Test]
        public void StoredTombs_SameSequenceEveryTime()
        {
            string text = StoredText();
            foreach (var mode in DifficultyExt.All)
            {
                var a = TombArchive.Lines(text, mode);
                var b = TombArchive.Lines(text, mode);
                CollectionAssert.AreEqual(a, b);
                for (int i = 0; i < Math.Min(20, a.Count); i++)
                    Assert.AreEqual(TombArchive.Read(a[i]).ComputeHash(), TombArchive.Read(b[i]).ComputeHash());
            }
            // The tool's draw of a depth is deterministic, and climbs over the ramp.
            for (int d = 0; d < 50; d++)
            {
                var first = Endless.Source(Difficulty.Normal, d, out int v1);
                Assert.AreEqual(first, Endless.Source(Difficulty.Normal, d, out int v2));
                Assert.AreEqual(v1, v2);
            }
            var lastHard = Endless.Source(Difficulty.Normal, Endless.RampLength * 2, out _);
            Assert.GreaterOrEqual(DifficultyTable.Sawtooth[lastHard.Index - 1], DifficultyTable.Sawtooth[8], "past the ramp, the hardest levels");
        }

        [Test]
        public void Score_FurtherFirstThenFaster()
        {
            Assert.Less(Endless.EncodeScore(12, 900_000), Endless.EncodeScore(11, 1_000));
            Assert.Less(Endless.EncodeScore(12, 1_000), Endless.EncodeScore(12, 2_000));
            Assert.AreEqual((37, 123_456), Endless.DecodeScore(Endless.EncodeScore(37, 123_456)));
            Assert.AreEqual("endless_x", Endless.BoardId(Difficulty.Extreme));

            var p = new EndlessProgress();
            Assert.AreEqual(1, p.Reached);
            p.Escaped(30_000);
            p.Escaped(40_000);
            Assert.AreEqual(3, p.Reached);
            Assert.AreEqual(70_000, p.TimeMs);
            var other = new EndlessProgress { Cleared = 2, TimeMs = 50_000, Deaths = 4 };
            p.MergeWith(other);
            Assert.AreEqual((2, 50_000, 4), (p.Cleared, p.TimeMs, p.Deaths));
            p.MergeWith(new EndlessProgress { Cleared = 1, TimeMs = 1 });
            Assert.AreEqual(2, p.Cleared, "a copy behind never takes the player back");
        }

        [Test]
        public void Energy_EndlessPool()
        {
            const long t0 = 1_800_000_000_000L, max = EnergyConfig.EndlessMax, regen = EnergyConfig.RegenMs;
            var m = new EnergyMeter();
            for (int i = 0; i < max; i++) Assert.IsTrue(Energy.Spend(m, (int)max, t0), $"point {i + 1}");
            Assert.IsFalse(Energy.Spend(m, (int)max, t0), "15 tombs, then empty");
            Assert.AreEqual(0, Energy.Left(m, (int)max, t0 + regen - 1));
            Assert.AreEqual(1, Energy.Left(m, (int)max, t0 + regen), "a point back every 2 minutes");
            Assert.AreEqual(regen, Energy.NextInMs(m, t0 + regen));
            Assert.IsTrue(Energy.Refill(m, EnergyConfig.EndlessAdRefill, t0 + regen));
            Assert.AreEqual(6, Energy.Left(m, (int)max, t0 + regen), "an ad gives 5");
            for (int i = 1; i < EnergyConfig.AdRefillsPerDay; i++) Assert.IsTrue(Energy.Refill(m, EnergyConfig.EndlessAdRefill, t0 + regen));
            Assert.IsFalse(Energy.Refill(m, EnergyConfig.EndlessAdRefill, t0 + regen), "same daily ad limit as the other pools");
            Assert.AreEqual(max, Energy.Left(m, (int)max, t0 + regen));
            Assert.AreEqual(max, Energy.Left(m, (int)max, t0 + regen * 30), "never above the max");
        }
    }
}
