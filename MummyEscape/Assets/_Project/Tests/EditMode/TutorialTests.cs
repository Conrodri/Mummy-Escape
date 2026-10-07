using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The tutorial corridor: it can be finished, and the way through meets every mechanic it announces.</summary>
    public class TutorialTests
    {
        [Test]
        public void TheWayThroughMeetsEveryMechanic()
        {
            var level = Tutorial.Build();
            var solution = Solver.Solve(level);
            Assert.IsNotNull(solution, "the tutorial can be finished");

            var session = new GameSession(level);
            var seen = StepFlags.None;
            int rotations = 0;
            foreach (var a in solution.Actions)
            {
                var r = session.Apply(a);
                Assert.IsFalse(r.Has(StepFlags.Blocked), $"{a} blocked at {session.Position}");
                seen |= r.Flags;
                if (r.Has(StepFlags.Rotated)) rotations++;
            }
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.Greater(session.Hp, 0);
            foreach (var f in new[]
                     {
                         StepFlags.ButtonPressed, StepFlags.Blinded, StepFlags.Teleported, StepFlags.HiddenRevealed, StepFlags.FogReset,
                         StepFlags.Climbed, StepFlags.TorchSmothered, StepFlags.TorchRelit, StepFlags.Swept, StepFlags.Collapsed,
                         StepFlags.Switched, StepFlags.Fell, StepFlags.Reversed, StepFlags.Rotated,
                     })
                Assert.AreEqual(f, seen & f, f + " never met");
            Assert.AreEqual(2, rotations);
            Assert.AreEqual(0, session.State.Rotation, "the second slab sets the tomb straight again");
        }

        [Test]
        public void EveryStretchOfTheWayHasItsLesson()
        {
            var level = Tutorial.Build();
            foreach (var c in level.AllCells())
            {
                if (level[c].IsSolid || c.Y != 2) continue;
                Assert.IsNotNull(Tutorial.LessonAt(c), $"no lesson at {c}");
            }
        }
    }
}
