using System.Collections.Generic;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The two-player campaign: its rules, and every hand-made tomb solvable by the pair (and only by the pair).</summary>
    public class CoopTests
    {
        static CoopTomb Tomb(params string[] rows) => CoopCampaign.Parse(rows);

        static CoopSession Play(CoopSession s, params (int mummy, Dir dir)[] moves)
        {
            foreach (var (m, d) in moves) Assert.IsFalse(s.Apply(m, PlayerAction.Move(d)).Has(StepFlags.Blocked), $"mummy {m} {d} blocked");
            return s;
        }

        [Test]
        public void ExitNeedsBothMummies()
        {
            var s = new CoopSession(Tomb("#####", "#1E2#", "#####"));
            var r = s.Apply(0, PlayerAction.Move(Dir.Right));
            Assert.IsFalse(r.Has(StepFlags.Won));
            Assert.AreEqual(SessionStatus.Playing, s.Status);
            r = s.Apply(1, PlayerAction.Move(Dir.Left));
            Assert.IsTrue(r.Has(StepFlags.Won));
            Assert.AreEqual(SessionStatus.Won, s.Status);
            Assert.AreEqual(SessionStatus.Won, s.Mummy(0).Status, "both views end");
            Assert.AreEqual(2, s.Moves);
        }

        [Test]
        public void MummiesMayShareATile()
        {
            var s = new CoopSession(Tomb("#####", "#12E#", "#####"));
            Play(s, (0, Dir.Right));
            Assert.AreEqual(s.Mummy(0).Position, s.Mummy(1).Position);
        }

        [Test]
        public void AButtonPressedByOneOpensTheDoorForBoth()
        {
            // Mummy 1 presses 'a' below it; mummy 0's door opens.
            var s = new CoopSession(Tomb("#######", "#1AE.2#", "#####a#", "#######"));
            Assert.IsTrue(s.Apply(0, PlayerAction.Move(Dir.Right)).Has(StepFlags.Blocked));
            var press = s.Apply(1, PlayerAction.Move(Dir.Down));
            Assert.IsTrue(press.Has(StepFlags.ButtonPressed));
            Assert.IsTrue(s.Mummy(0).IsDoorOpen(new Cell(0, 2, 2)), "mummy 0 sees its door open");
            Play(s, (0, Dir.Right));
        }

        [Test]
        public void AHeldPlateOpensItsDoorOnlyWhileAMummyStandsOnIt()
        {
            var tomb = Tomb("########", "#1.P.E.#", "#####p2#", "########");
            var door = new Cell(0, 3, 2);
            var s = new CoopSession(tomb);
            Play(s, (0, Dir.Right));
            Assert.IsTrue(s.Apply(0, PlayerAction.Move(Dir.Right)).Has(StepFlags.Blocked), "closed");
            Play(s, (1, Dir.Left));
            Assert.IsTrue(s.Mummy(0).IsDoorOpen(door), "held open");
            Play(s, (1, Dir.Right));
            Assert.IsFalse(s.Mummy(0).IsDoorOpen(door), "closes when the plate is left");
            Assert.AreEqual(0, s.State.A.Pressed, "a plate never latches");
            Play(s, (1, Dir.Left), (0, Dir.Right), (0, Dir.Right));
        }

        [Test]
        public void ASwitchTogglesBarriersForBoth()
        {
            var s = new CoopSession(Tomb("########", "#1kM.E2#", "########"));
            Play(s, (0, Dir.Right));
            Assert.IsFalse(s.Apply(1, PlayerAction.Move(Dir.Left)).Has(StepFlags.Won), "mummy 0 is not on the exit");
            var blue = new Cell(0, 3, 1);
            Assert.IsFalse(s.Mummy(1).IsDoorOpen(blue), "the switch closed the blue barrier for the other mummy too");
        }

        [Test]
        public void OneDeathEndsTheTry()
        {
            var s = new CoopSession(Tomb("#######", "#1^^.E#", "#2....#", "#######"));
            Play(s, (0, Dir.Right));
            var r = s.Apply(0, PlayerAction.Move(Dir.Right));
            Assert.IsTrue(r.Has(StepFlags.Died));
            Assert.AreEqual(SessionStatus.Dead, s.Status);
            Assert.AreEqual(0, s.Fallen);
            Assert.AreEqual(SessionStatus.Dead, s.Mummy(1).Status);
            Assert.IsTrue(s.Apply(1, PlayerAction.Move(Dir.Right)).Has(StepFlags.Blocked), "nothing moves after");
        }

        [Test]
        public void DisarmedSpikesAreDisarmedForBoth()
        {
            var s = new CoopSession(Tomb("######", "#1^.E#", "#2...#", "######"));
            Assert.IsTrue(s.Apply(1, PlayerAction.Disarm(Dir.Up)).Has(StepFlags.Blocked), "not next to it");
            Assert.IsTrue(s.Apply(0, PlayerAction.Disarm(Dir.Right)).Has(StepFlags.Disarmed));
            Assert.IsFalse(s.Mummy(1).IsTrapArmed(new Cell(0, 2, 2)));
        }

        [Test]
        public void EachMummySeesWhatTheOtherTorchLights()
        {
            var s = new CoopSession(Tomb("#########", "#1.....2#", "#...E...#", "#########"));
            var nearMate = new Cell(0, 6, 2);
            Assert.IsTrue(s.Mummy(0).IsVisible(nearMate));
            Assert.IsFalse(s.Mummy(0).IsLitByTorch(nearMate));
            Assert.IsTrue(s.Mummy(0).IsExplored(nearMate), "and remembers it");
            Assert.IsFalse(s.Mummy(0).IsVisible(new Cell(0, 4, 2)), "nobody lights the middle");
        }

        [Test]
        public void TheSolverFindsTheJointWayAndProvesBothAreNeeded()
        {
            var tomb = Tomb("#######", "#1AE.2#", "#####a#", "#######");
            var rules = new CoopRules(tomb.Level);
            var start = rules.Initial(tomb.Starts[0], tomb.Starts[1]);
            var sol = CoopSolver.Solve(rules, start);
            Assert.IsNotNull(sol);
            Assert.AreEqual(6, sol.Moves, "mummy 1: down (press), up, left, left; mummy 0: right, right");
            Assert.IsFalse(CoopSolver.CanReachExitAlone(rules, start, 0));
            Assert.IsTrue(CoopSolver.CanReachExitAlone(rules, start, 1));
        }

        // ------------------------------------------------------------------ the campaign

        static IEnumerable<int> Numbers()
        {
            for (int i = 1; i <= CoopCampaign.Count; i++) yield return i;
        }

        [Test]
        public void TheCampaignHasAboutTwentyTombsOverThreeActs()
        {
            Assert.GreaterOrEqual(CoopCampaign.Count, 18);
            for (int act = 1; act <= CoopCampaign.Acts; act++)
                Assert.IsTrue(new List<CoopTomb>(CoopCampaign.Tombs).Exists(t => t.Act == act), $"act {act} has tombs");
            for (int i = 0; i < CoopCampaign.Count; i++) Assert.AreEqual(i + 1, CoopCampaign.Tombs[i].Number);
        }

        [TestCaseSource(nameof(Numbers))]
        public void EveryTombIsSolvableByThePairAndOnlyByThePair(int number)
        {
            var tomb = CoopCampaign.Get(number);
            var rules = new CoopRules(tomb.Level);
            var start = rules.Initial(tomb.Starts[0], tomb.Starts[1]);
            var sol = CoopSolver.Solve(rules, start);
            Assert.IsNotNull(sol, $"{tomb.Name}: the pair cannot get out\n{tomb.Level.ToAscii()}");
            Assert.AreEqual(sol.Moves, tomb.Par);
            TestContext.WriteLine($"{number} {tomb.Name}: par {sol.Moves}");

            // Played through the session, the solution wins.
            var s = new CoopSession(tomb);
            foreach (var (m, a) in sol.Actions) Assert.IsFalse(s.Apply(m, a).Has(StepFlags.Blocked));
            Assert.AreEqual(SessionStatus.Won, s.Status);

            if (!tomb.NeedsBoth) return;
            Assert.IsFalse(CoopSolver.CanReachExitAlone(rules, start, 0), $"{tomb.Name}: mummy 0 gets out alone");
            Assert.IsFalse(CoopSolver.CanReachExitAlone(rules, start, 1), $"{tomb.Name}: mummy 1 gets out alone");
        }

        [Test]
        public void TombsFitTheSoloLimitsAndAreAtMostTwiceASoloTomb()
        {
            foreach (var t in CoopCampaign.Tombs)
            {
                Assert.LessOrEqual(t.Level.Width * t.Level.Height, 2 * 21 * 15, t.Name);
                Assert.LessOrEqual(t.Level.ChannelCount, Solver.MaxChannels, t.Name);
                Assert.IsTrue(Solver.CanPack(t.Level), t.Name);
            }
        }

        [Test]
        public void StarsFollowMovesAndTime()
        {
            var tomb = CoopCampaign.Get(1);
            Assert.AreEqual(3, tomb.Result(true, tomb.Par, tomb.TargetMs).Stars);
            Assert.AreEqual(1, tomb.Result(true, tomb.Par * 2, tomb.TargetMs).Stars);
            Assert.AreEqual(2, tomb.Result(true, tomb.Par, tomb.TargetMs * 14 / 10).Stars);
            Assert.AreEqual(0, tomb.Result(false, tomb.Par, tomb.TargetMs).Stars);
        }
    }
}
