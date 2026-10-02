using System;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>
    /// Resolution tests: every level of every act is generated (several mazes each, since every run draws a new one),
    /// solved by the exact BFS solver and checked against the difficulty contract (par window, mandatory mechanics,
    /// far exit, meaningful dead ends, survivability, determinism).
    /// </summary>
    public class LevelGenerationTests
    {
        /// <summary>Mazes checked per level in the fast suite (the Slow suite sweeps many more).</summary>
        const int FastVariants = 3;

        static readonly Dictionary<(LevelId, int), Level> Cache = new Dictionary<(LevelId, int), Level>();

        static Level Get(LevelId id, int variant)
        {
            if (!Cache.TryGetValue((id, variant), out var level)) Cache[(id, variant)] = level = LevelGenerator.Generate(id, variant);
            return level;
        }

        public static IEnumerable<TestCaseData> AllMazes() =>
            DifficultyTable.AllLevels().SelectMany(id => Enumerable.Range(0, FastVariants)
                .Select(v => new TestCaseData(id.Act, id.Index, v).SetName($"Level {id} maze {v}")));

        public static IEnumerable<TestCaseData> AllLevels() =>
            DifficultyTable.AllLevels().Select(id => new TestCaseData(id.Act, id.Index).SetName($"Level {id}"));

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_RespectsItsSpec(int act, int index, int variant)
        {
            var id = new LevelId(act, index);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id, variant);
            var sol = level.Solution;

            Assert.NotNull(sol, "level must be solvable");
            Assert.AreEqual(variant, level.Variant);
            Assert.That(sol.Moves, Is.InRange(spec.MinMoves, spec.MaxMoves), $"par out of window for {spec}");
            Assert.That(sol.Moves, Is.GreaterThanOrEqualTo(DifficultyTable.GetAct(act).MinMoves), "act minimum");
            Assert.That(sol.Mechanics, Is.GreaterThanOrEqualTo(1), "at least one button or portal on the optimal route");
            Assert.That(sol.ButtonsPressed, Is.GreaterThanOrEqualTo(spec.RequiredButtons), "mandatory buttons");
            Assert.That(sol.Teleports, Is.GreaterThanOrEqualTo(spec.RequiredPortals), "mandatory portals");
            Assert.That(sol.HpLeft, Is.GreaterThanOrEqualTo(spec.MinHpLeftForPar));
            Assert.IsNull(LevelValidator.Validate(level, spec, sol));
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_ParReplaysInGameSession(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            var session = new GameSession(level);
            foreach (var action in level.Solution.Actions)
            {
                var r = session.Apply(action);
                Assert.IsFalse(r.Has(StepFlags.Blocked), $"solver action {action} rejected by the game at move {session.Moves}");
            }
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.AreEqual(level.Solution.Moves, session.Moves);
            Assert.AreEqual(level.Solution.Interactions, session.Interactions);
            Assert.AreEqual(0, session.BuildResult().OverPar, "the optimal run is a perfect score");
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_GatesAreMandatory(int act, int index, int variant)
        {
            var id = new LevelId(act, index);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id, variant);
            if (spec.RequiredButtons > 0)
            {
                var opts = SolverOptions.Default;
                opts.AllowButtons = false;
                Assert.IsNull(Solver.Solve(level, opts), "exit must not be reachable without pressing buttons");
            }
            if (spec.RequiredPortals > 0)
            {
                var opts = SolverOptions.Default;
                opts.AllowTeleporters = false;
                Assert.IsNull(Solver.Solve(level, opts), "exit must not be reachable without taking the portal");
            }
            var none = SolverOptions.Default;
            none.AllowButtons = false;
            none.AllowTeleporters = false;
            Assert.IsNull(Solver.Solve(level, none), "every level needs at least one mechanic");
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_DeadEndsAlwaysHoldSomething(int act, int index, int variant)
        {
            Assert.IsNull(LevelValidator.CheckDeadEnds(Get(new LevelId(act, index), variant)));
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_EasyActsPutPortalsInDeadEnds(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            if (act <= 2) Assert.IsNull(LevelValidator.CheckDeadEndPortals(level), "acts 1-2: one way out of each teleporter");
            Assert.That(level.Floors, Is.LessThanOrEqualTo(2), "a human memorises 2 floors at most");
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_ExitIsFarOrBehindAFarButton(int act, int index, int variant)
        {
            var id = new LevelId(act, index);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id, variant);
            bool far = level.Start.Manhattan(level.Exit) >= spec.MinExitDistance;
            bool farButton = level.AllCells().Any(c => level[c].IsTrigger
                                                     && (level.DecoyChannels & (1 << level[c].Channel)) == 0
                                                     && level.Start.Manhattan(c) >= spec.MinExitDistance);
            Assert.IsTrue(far || farButton, $"exit {level.Start.Manhattan(level.Exit)} tiles from the start");
        }

        /// <summary>Each act has its own signature mechanic, present in every one of its tombs.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_HasItsActMechanic(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            int Count(TileType type) => level.AllCells().Count(c => level[c].Type == type);
            switch (act)
            {
                case 2: Assert.That(Count(TileType.Current), Is.GreaterThanOrEqualTo(2), "flooded galleries: currents"); break;
                case 3: Assert.That(Count(TileType.Crumbling), Is.GreaterThanOrEqualTo(2), "ruins: fragile slabs"); break;
                case 4:
                    Assert.That(Count(TileType.Switch), Is.GreaterThanOrEqualTo(1), "city of Anubis: switches");
                    Assert.That(level.AllCells().Count(c => level[c].Type == TileType.Barrier && level[c].Param == 0), Is.GreaterThanOrEqualTo(1), "red barriers");
                    break;
                case 5: Assert.That(Count(TileType.FireJet), Is.GreaterThanOrEqualTo(2), "burning sanctuary: flame jets"); break;
            }
            if (act >= 2)
            {
                // The omniscient route never gets trapped (it reaches the exit), and the start is never a dead lock.
                Assert.IsTrue(Solver.CanEscape(level, Rules.Initial(level)));
            }
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_IsDeterministicPerVariant(int act, int index)
        {
            var id = new LevelId(act, index);
            var a = Get(id, 0);
            var b = LevelGenerator.Generate(id, 0);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "the same (level, variant) must rebuild the same tomb");
            Assert.AreEqual(a.Solution.Moves, b.Solution.Moves);
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_EveryRunDrawsANewMaze(int act, int index)
        {
            var id = new LevelId(act, index);
            var hashes = Enumerable.Range(0, FastVariants).Select(v => Get(id, v).ComputeHash()).ToList();
            Assert.AreEqual(hashes.Count, hashes.Distinct().Count(), "replaying must give a different tomb");
        }

        [Test]
        public void Act1_IsAtLeast15MovesWithOneMechanic()
        {
            Assert.AreEqual(15, DifficultyTable.GetAct(1).MinMoves);
            for (int i = 1; i <= DifficultyTable.GetAct(1).Levels; i++)
            {
                var spec = DifficultyTable.Spec(new LevelId(1, i));
                Assert.That(spec.Gates.Count, Is.GreaterThanOrEqualTo(1), $"1-{i} needs a button or a portal");
            }
        }

        [Test]
        public void Acts_MinimumMovesNeverDecrease()
        {
            for (int a = 2; a <= DifficultyTable.ActCount; a++)
                Assert.That(DifficultyTable.GetAct(a).MinMoves, Is.GreaterThan(DifficultyTable.GetAct(a - 1).MinMoves));
        }

        [Test]
        public void Acts_InteractionsGrowWithinAct()
        {
            foreach (var act in Enumerable.Range(1, DifficultyTable.ActCount))
            {
                int levels = DifficultyTable.GetAct(act).Levels;
                int Load(int i)
                {
                    var s = DifficultyTable.Spec(new LevelId(act, i));
                    return s.Gates.Count + s.DecoyDoors + s.SpikeTraps + s.DarknessTraps + s.DustPatches + s.Teleporters.Count + s.BreakableFloors;
                }
                Assert.That(Load(levels), Is.GreaterThanOrEqualTo(Load(1)), $"act {act} should end harder than it starts");
                for (int i = 2; i <= levels; i++)
                    Assert.That(Load(i), Is.GreaterThanOrEqualTo(Load(i - 1)), $"act {act} level {i} lighter than {i - 1}");
            }
        }

        /// <summary>Broad sweep: many mazes per level must all generate and pass the validator.</summary>
        [Test, Category("Slow")]
        public void ManyVariants_AllGenerateAndValidate()
        {
            foreach (var id in DifficultyTable.AllLevels())
                for (int v = 0; v < 25; v++)
                {
                    var level = LevelGenerator.Generate(id, v);
                    Assert.IsNull(LevelValidator.Validate(level, DifficultyTable.Spec(id), level.Solution), $"{id} maze {v}");
                }
        }
    }
}
