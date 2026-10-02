using System.Collections.Generic;
using System.Linq;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>
    /// Resolution tests: every level of every act is generated, solved by the exact BFS solver and checked against
    /// the difficulty contract (par window, mandatory interactions, spacing, survivability, determinism).
    /// </summary>
    public class LevelGenerationTests
    {
        static readonly Dictionary<LevelId, Level> Cache = new Dictionary<LevelId, Level>();

        static Level Get(LevelId id)
        {
            if (!Cache.TryGetValue(id, out var level)) Cache[id] = level = LevelGenerator.Generate(id);
            return level;
        }

        public static IEnumerable<TestCaseData> AllLevels() =>
            DifficultyTable.AllLevels().Select(id => new TestCaseData(id.Act, id.Index).SetName($"Level {id}"));

        [TestCaseSource(nameof(AllLevels))]
        public void Level_RespectsItsSpec(int act, int index)
        {
            var id = new LevelId(act, index);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id);
            var sol = level.Solution;

            Assert.NotNull(sol, "level must be solvable");
            Assert.That(sol.Moves, Is.InRange(spec.MinMoves, spec.MaxMoves), $"par out of window for {spec}");
            Assert.That(sol.Moves, Is.GreaterThanOrEqualTo(DifficultyTable.GetAct(act).MinMoves), "act minimum");
            Assert.That(sol.ButtonsPressed, Is.GreaterThanOrEqualTo(spec.RequiredButtons), "mandatory buttons");
            Assert.That(sol.HpLeft, Is.GreaterThanOrEqualTo(spec.MinHpLeftForPar));
            Assert.IsNull(LevelValidator.Validate(level, spec, sol));
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_IsDeterministic(int act, int index)
        {
            var id = new LevelId(act, index);
            var a = Get(id);
            var b = LevelGenerator.Generate(id);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "two players must get the same tomb");
            Assert.AreEqual(a.Solution.Moves, b.Solution.Moves);
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_ParReplaysInGameSession(int act, int index)
        {
            var level = Get(new LevelId(act, index));
            var session = new GameSession(level);
            foreach (var action in level.Solution.Actions)
            {
                var r = session.Apply(action);
                Assert.IsFalse(r.Has(StepFlags.Blocked), $"solver action {action} rejected by the game at move {session.Moves}");
            }
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.AreEqual(level.Solution.Moves, session.Moves);
            Assert.AreEqual(level.Solution.Interactions, session.Interactions);
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_DoorsAreMandatoryWhenRequired(int act, int index)
        {
            var id = new LevelId(act, index);
            if (DifficultyTable.Spec(id).RequiredButtons == 0) Assert.Pass("no mandatory door in this level");
            var opts = SolverOptions.Default;
            opts.AllowButtons = false;
            Assert.IsNull(Solver.Solve(Get(id), opts), "exit must not be reachable without pressing buttons");
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
                    return s.RequiredButtons + s.DecoyDoors + s.SpikeTraps + s.DarknessTraps + s.Teleporters.Count + s.BreakableFloors;
                }
                Assert.That(Load(levels), Is.GreaterThanOrEqualTo(Load(1)), $"act {act} should end harder than it starts");
                for (int i = 2; i <= levels; i++)
                    Assert.That(Load(i), Is.GreaterThanOrEqualTo(Load(i - 1)), $"act {act} level {i} lighter than {i - 1}");
            }
        }
    }
}
