using System.Linq;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The act intros: each can be finished, the way through meets what it announces, and it stays Easy content.</summary>
    public class ActIntroTests
    {
        static StepFlags Expected(int act)
        {
            switch (act)
            {
                case 1: return StepFlags.ButtonPressed | StepFlags.Blinded | StepFlags.Teleported;
                case 2: return StepFlags.TorchSmothered | StepFlags.TorchRelit | StepFlags.WrappingsBurnt | StepFlags.Swept;
                case 3: return StepFlags.Teleported | StepFlags.Collapsed | StepFlags.Reversed;
                case 4: return StepFlags.Switched | StepFlags.FogReset | StepFlags.Rotated;
                default: return StepFlags.Collapsed;
            }
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void TheWayThroughMeetsTheActMechanics(int act)
        {
            var level = ActIntro.Build(act);
            var solution = Solver.Solve(level);
            Assert.IsNotNull(solution, "the intro can be finished");

            var session = new GameSession(level);
            var seen = StepFlags.None;
            var walked = new System.Collections.Generic.List<Cell>();
            int rotations = 0;
            foreach (var a in solution.Actions)
            {
                var r = session.Apply(a);
                Assert.IsFalse(r.Has(StepFlags.Blocked), $"{a} blocked at {session.Position}");
                seen |= r.Flags;
                walked.Add(session.Position);
                if (r.Has(StepFlags.Rotated)) rotations++;
            }
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.Greater(session.Hp, 0);
            Assert.AreEqual(0, session.State.Rotation, "the tomb ends straight");
            var expected = Expected(act);
            Assert.AreEqual(expected, seen & expected, "mechanics never met");
            if (act == 4) Assert.AreEqual(2, rotations);
            if (act == 5) Assert.IsTrue(level.AllCells().Any(c => level[c].Type == TileType.FireJet));
            if (act == 4) Assert.IsTrue(level.AllCells().Any(c => level[c].Type == TileType.Idol && Rules.Gaze(level, c).Count > 0));
            if (act == 3) Assert.IsTrue(walked.Any(c => level[c].Type == TileType.FalseWall), "the shortest way crosses the mirage");
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void EveryStretchOfTheWayHasItsLesson(int act)
        {
            var level = ActIntro.Build(act);
            foreach (var c in level.AllCells())
            {
                if (level[c].IsSolid || c.Y != 2) continue;
                Assert.IsNotNull(ActIntro.LessonAt(act, c), $"no lesson at {c}");
            }
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void OnlyEasyContent(int act)
        {
            var level = ActIntro.Build(act);
            Assert.AreEqual(1, level.Floors);
            foreach (var c in level.AllCells())
            {
                var t = level[c];
                Assert.AreNotEqual(TileType.LadderUp, t.Type, c.ToString());
                Assert.AreNotEqual(TileType.LadderDown, t.Type, c.ToString());
                Assert.AreNotEqual(TileType.BreakableFloor, t.Type, c.ToString());
                if (t.Type == TileType.Teleporter) Assert.AreNotEqual(TeleporterKind.Hidden, t.Teleporter, c.ToString());
            }
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void IdAndLessons(int act)
        {
            Assert.IsTrue(ActIntro.Build(act).Id.Equals(ActIntro.Id(act)));
            Assert.IsNotEmpty(ActIntro.Lessons(act));
        }
    }
}
