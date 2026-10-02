using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>Hand-built micro tombs exercising each rule in isolation.</summary>
    public class RulesTests
    {
        /// <summary>Builds a 1-floor level from rows (top row first). S start, E exit, # wall, . floor.</summary>
        internal static Level FromAscii(params string[] rows) => TestLevels.FromAscii(rows);

        [Test]
        public void Walls_BlockAndCostNothing()
        {
            var s = new GameSession(FromAscii("#####", "#S.E#", "#####"));
            Assert.IsTrue(s.Move(Dir.Up).Has(StepFlags.Blocked));
            Assert.AreEqual(0, s.Moves);
            s.Move(Dir.Right);
            s.Move(Dir.Right);
            Assert.AreEqual(SessionStatus.Won, s.Status);
            Assert.AreEqual(2, s.Moves);
        }

        [Test]
        public void Doors_OpenWithTheirButton()
        {
            // Button 'a' below the start opens door 'A'.
            var lvl = FromAscii(
                "######",
                "#S.AE#",
                "#a####",
                "######");
            var s = new GameSession(lvl);
            s.Move(Dir.Right);
            Assert.IsTrue(s.Move(Dir.Right).Has(StepFlags.Blocked), "door closed");
            s.Move(Dir.Left);
            var press = s.Move(Dir.Down);
            Assert.IsTrue(press.Has(StepFlags.ButtonPressed));
            s.Move(Dir.Up); s.Move(Dir.Right); s.Move(Dir.Right); s.Move(Dir.Right);
            Assert.AreEqual(SessionStatus.Won, s.Status);
            Assert.AreEqual(1, s.Interactions);
            Assert.AreEqual(5, Solver.Solve(lvl).Moves, "down, up, right x3");
        }

        [Test]
        public void Spikes_HurtAndCanKill_DisarmCostsAMove()
        {
            var lvl = FromAscii("#######", "#S^.^E#", "#######");
            var hurt = new GameSession(lvl);
            hurt.Move(Dir.Right);
            Assert.AreEqual(1, hurt.Hp);
            hurt.Move(Dir.Right);
            var r = hurt.Move(Dir.Right);
            Assert.IsTrue(r.Has(StepFlags.Died));
            Assert.AreEqual(SessionStatus.Dead, hurt.Status);

            var careful = new GameSession(lvl);
            Assert.IsTrue(careful.Disarm(Dir.Right).Has(StepFlags.Disarmed));
            careful.Move(Dir.Right);
            Assert.AreEqual(2, careful.Hp);

            // Optimal: 4 steps + 1 disarm (walking through both spikes kills).
            var sol = Solver.Solve(lvl);
            Assert.AreEqual(5, sol.Moves);
            Assert.That(sol.Disarms, Is.GreaterThanOrEqualTo(1));
            Assert.That(sol.HpLeft, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Darkness_BlindsForThreeMoves()
        {
            var s = new GameSession(FromAscii("#########", "#S~....E#", "#########"));
            s.Move(Dir.Right);
            Assert.IsTrue(s.IsBlind);
            Assert.IsFalse(s.IsVisible(s.Position.Step(Dir.Right)));
            s.Move(Dir.Right); s.Move(Dir.Right); s.Move(Dir.Right);
            Assert.IsFalse(s.IsBlind);
        }

        [Test]
        public void Fog_RevealsNeighboursAndRemembersPath()
        {
            var s = new GameSession(FromAscii("#######", "#S...E#", "#######"));
            var start = s.Position;
            Assert.IsTrue(s.IsExplored(start.Step(Dir.Right)));
            Assert.IsFalse(s.IsExplored(start.Step(Dir.Right).Step(Dir.Right)));
            s.Move(Dir.Right); s.Move(Dir.Right);
            Assert.IsTrue(s.IsExplored(start), "already travelled tiles stay uncovered");
            Assert.IsFalse(s.IsVisible(start));
        }
    }
}
