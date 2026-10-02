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
        public void Torch_LightsNeighbours_DustSmothersIt_WallTorchRelightsIt()
        {
            // Dust after one step, a wall torch above the 5th tile.
            var s = new GameSession(FromAscii(
                "#####!###",
                "#S.,...E#",
                "#########"));
            Assert.IsTrue(s.TorchLit);
            Assert.IsTrue(s.IsVisible(s.Position.Step(Dir.Right)), "a lit torch shows the neighbouring tiles");

            s.Move(Dir.Right);
            var smother = s.Move(Dir.Right);
            Assert.IsTrue(smother.Has(StepFlags.TorchSmothered));
            Assert.IsFalse(s.TorchLit);
            Assert.IsFalse(s.IsVisible(s.Position.Step(Dir.Right)), "without the torch only the own tile is lit");
            Assert.IsFalse(s.IsExplored(s.Position.Step(Dir.Right)));

            Assert.IsFalse(s.Move(Dir.Right).Has(StepFlags.TorchRelit), "still in the dark");
            var relit = s.Move(Dir.Right);
            Assert.IsTrue(relit.Has(StepFlags.TorchRelit), "passing next to the wall torch relights");
            Assert.IsTrue(s.TorchLit);
            Assert.IsTrue(s.IsVisible(s.Position.Step(Dir.Right)));
        }

        [Test]
        public void Torch_Out_CannotDisarm()
        {
            var lvl = FromAscii("########", "#S,^..E#", "########");
            var s = new GameSession(lvl);
            s.Move(Dir.Right);
            Assert.IsTrue(s.Disarm(Dir.Right).Has(StepFlags.Blocked), "a trap you cannot see cannot be disarmed");
            Assert.IsTrue(Rules.Step(lvl, s.State, PlayerAction.Disarm(Dir.Right)).Has(StepFlags.Blocked), "the rule itself forbids it");
        }

        [Test]
        public void WallTorch_IsSolid()
        {
            var s = new GameSession(FromAscii("#!#", "#S#", "#E#", "###"));
            Assert.IsTrue(s.Move(Dir.Up).Has(StepFlags.Blocked));
        }

        [Test]
        public void Solver_CanForbidPortals()
        {
            // Two halves joined only by a portal pair.
            var lvl = FromAscii("#######", "#S.#.E#", "#######");
            var a = new Cell(0, 2, 1);
            var b = new Cell(0, 4, 1);
            lvl[a] = new Tile { Type = TileType.Teleporter, Teleporter = TeleporterKind.Visible };
            lvl[b] = new Tile { Type = TileType.Teleporter, Teleporter = TeleporterKind.Visible };
            lvl.LinkTeleporters(a, b);

            var sol = Solver.Solve(lvl);
            Assert.AreEqual(2, sol.Moves);
            Assert.AreEqual(1, sol.Teleports);
            Assert.AreEqual(1, sol.Mechanics);
            var opts = SolverOptions.Default;
            opts.AllowTeleporters = false;
            Assert.IsNull(Solver.Solve(lvl, opts));
        }

        [Test]
        public void Score_ComparesRunsByMovesOverPar()
        {
            var perfect = new LevelResult { Won = true, Moves = 30, Par = 30, MaxHp = 2, HpLeft = 2 };
            var sloppyShortMaze = new LevelResult { Won = true, Moves = 22, Par = 18, MaxHp = 2, HpLeft = 2 };
            Assert.AreEqual(0, perfect.OverPar);
            Assert.AreEqual(4, sloppyShortMaze.OverPar);
            Assert.Less(perfect.LeaderboardScore, sloppyShortMaze.LeaderboardScore, "a perfect run beats fewer raw moves in an easier maze");
            Assert.AreEqual((4, 0, 0), LevelResult.DecodeScore(sloppyShortMaze.LeaderboardScore));
            Assert.AreEqual("parfait", LevelResult.FormatOverPar(0));
            Assert.AreEqual("+1 coup", LevelResult.FormatOverPar(1));
            Assert.AreEqual("+4 coups", LevelResult.FormatOverPar(4));

            var rec = new LevelRecord();
            Assert.IsTrue(rec.Merge(sloppyShortMaze));
            Assert.IsTrue(rec.Merge(perfect));
            Assert.AreEqual(0, rec.BestOverPar);
            Assert.IsFalse(rec.Merge(sloppyShortMaze));
            Assert.AreEqual(3, rec.Completions);
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
