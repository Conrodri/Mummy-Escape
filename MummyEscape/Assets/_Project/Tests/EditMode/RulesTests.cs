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
        public void Mirror_ReversesTheNextTenSteps_Once()
        {
            var lvl = FromAscii("#################", "#SX............E#", "#################");
            var s = new GameSession(lvl);
            var hit = s.Move(Dir.Right);
            Assert.IsTrue(hit.Has(StepFlags.Reversed));
            Assert.AreEqual(Rules.ReverseDuration, s.State.Reversed);

            // Bumping into a wall does not wear the curse off.
            Assert.IsTrue(s.Move(Dir.Up).Has(StepFlags.Blocked), "up means down: the wall below");
            Assert.AreEqual(Rules.ReverseDuration, s.State.Reversed);

            for (int i = 0; i < Rules.ReverseDuration; i++) Assert.AreEqual(Dir.Right, s.Move(Dir.Left).Dir);
            Assert.AreEqual(12, s.Position.X);
            Assert.AreEqual(0, s.State.Reversed);
            Assert.AreEqual(11, s.Move(Dir.Left).SteppedOn.X, "back to normal");

            // Single use: walking over the spent mirror again changes nothing.
            for (int i = 0; i < 9; i++) s.Move(Dir.Left);
            Assert.AreEqual(2, s.Position.X);
            Assert.AreEqual(0, s.State.Reversed);
        }

        [Test]
        public void TurningSlab_TurnsTheControlsWithTheScreen_ForGood()
        {
            var lvl = FromAscii("#######", "#SR..E#", "#######");
            var s = new GameSession(lvl);
            Assert.IsTrue(s.Move(Dir.Right).Has(StepFlags.Rotated));
            Assert.AreEqual(1, s.State.Rotation);
            // Turned a quarter clockwise: the corridor's east now points down on screen.
            Assert.IsTrue(s.Move(Dir.Right).Has(StepFlags.Blocked));
            Assert.AreEqual(Dir.Right, s.Move(Dir.Down).Dir);
            Assert.AreEqual(Dir.Left, s.Move(Dir.Up).Dir);
            Assert.IsFalse(s.Move(Dir.Up).Has(StepFlags.Rotated), "single use");
            Assert.AreEqual(1, s.State.Rotation, "the turn lasts the whole run");

            var sol = Solver.Solve(lvl);
            Assert.AreEqual(4, sol.Moves);
            Assert.AreEqual(new[] { Dir.Right, Dir.Down, Dir.Down, Dir.Down }, sol.Actions.ConvertAll(a => a.Dir).ToArray());
        }

        [Test]
        public void TurningSlab_Anticlockwise_AndMirror_Combine()
        {
            var lvl = FromAscii("########", "#SWX..E#", "########");
            var s = new GameSession(lvl);
            s.Move(Dir.Right);
            Assert.AreEqual(3, s.State.Rotation);
            // Anticlockwise: east points up on screen.
            Assert.IsTrue(s.Move(Dir.Up).Has(StepFlags.Reversed));
            // Reversed as well: east is now a swipe down.
            Assert.AreEqual(Dir.Right, s.Move(Dir.Down).Dir);
            Assert.AreEqual(Dir.Right, s.Move(Dir.Down).Dir);
            Assert.AreEqual(Dir.Right, s.Move(Dir.Down).Dir);
            Assert.AreEqual(SessionStatus.Won, s.Status);
        }

        [Test]
        public void Darkness_CannotBeDisarmed()
        {
            var lvl = FromAscii("#######", "#S~..E#", "#######");
            var s = new GameSession(lvl);
            Assert.IsFalse(s.CanDisarm(out _), "only spikes offer the disarm button");
            Assert.IsTrue(s.Disarm(Dir.Right).Has(StepFlags.Blocked));
        }

        [Test]
        public void Spikes_DisarmButtonOnlyWhenSeen()
        {
            var s = new GameSession(FromAscii("########", "#S.^..E#", "########"));
            Assert.IsFalse(s.CanDisarm(out _), "not next to the spikes yet");
            s.Move(Dir.Right);
            Assert.IsTrue(s.CanDisarm(out var dir));
            Assert.AreEqual(Dir.Right, dir);
            s.Disarm(dir);
            Assert.IsFalse(s.CanDisarm(out _), "already disarmed");

            var dark = new GameSession(FromAscii("########", "#S,^..E#", "########"));
            dark.Move(Dir.Right);
            Assert.IsFalse(dark.CanDisarm(out _), "torch out: the spikes cannot be seen");
        }

        [Test]
        public void Spikes_ShortcutHasAWayRound()
        {
            // Spikes across the short way, a spike-free loop above.
            var lvl = FromAscii(
                "########",
                "#.....##",
                "#.###.##",
                "#S.^..E#",
                "########");
            Assert.AreEqual(8, LevelValidator.SpikeDetour(lvl, new Cell(0, 3, 1)), "10 moves round instead of 2");
            Assert.IsNull(LevelValidator.CheckSpikeShortcuts(lvl));
            var safe = SolverOptions.Default;
            safe.AvoidSpikes = true;
            Assert.AreEqual(9, Solver.Solve(lvl, safe).Moves, "the long way: up, across, down");
            Assert.AreEqual(5, Solver.Solve(lvl).Moves, "par trades a life for the shortcut");

            var corridor = FromAscii("#######", "#S.^.E#", "#######");
            Assert.IsNotNull(LevelValidator.CheckSpikeShortcuts(corridor), "spikes with no way round are refused");
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
            Assert.AreEqual((4, 0), LevelResult.DecodeScore(sloppyShortMaze.LeaderboardScore), "no time measured");
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
        public void Stars_LostAt130And160PercentOfMovesOrTime()
        {
            LevelResult Run(int moves, int timeMs = 0, int targetMs = 0, bool won = true) =>
                new LevelResult { Won = won, Moves = moves, Par = 20, TimeMs = timeMs, TargetMs = targetMs, MaxHp = 2, HpLeft = 2 };
            Assert.AreEqual(3, Run(20).Stars, "ideal path");
            Assert.AreEqual(3, Run(25).Stars, "125% of the ideal path");
            Assert.AreEqual(2, Run(26).Stars, "130% of the ideal path");
            Assert.AreEqual(2, Run(31).Stars);
            Assert.AreEqual(1, Run(32).Stars, "160% of the ideal path");
            Assert.AreEqual(1, Run(400).Stars, "escaping always earns a star");
            Assert.AreEqual(0, Run(20, won: false).Stars);

            Assert.AreEqual(3, Run(20, 12_999, 10_000).Stars, "just under 130% of the expert's time");
            Assert.AreEqual(2, Run(20, 13_000, 10_000).Stars, "130% of the expert's time");
            Assert.AreEqual(1, Run(20, 16_000, 10_000).Stars, "160% of the expert's time");
            Assert.AreEqual(1, Run(32, 5_000, 10_000).Stars, "the worse of moves and time decides");
            Assert.AreEqual(3, Run(20, 90_000).Stars, "no expert time: the moves alone");

            Assert.AreEqual(25, Run(20).MaxMovesFor(3));
            Assert.AreEqual(31, Run(20).MaxMovesFor(2));
            Assert.AreEqual(12_999, Run(20, 1, 10_000).MaxTimeFor(3));
            Assert.AreEqual(0, Run(20).MaxTimeFor(3));
        }

        [Test]
        public void Score_TiesOnMovesAreBrokenByTime()
        {
            var fastSloppy = new LevelResult { Won = true, Moves = 26, Par = 24, MaxHp = 2, HpLeft = 2, TimeMs = 9_000 };
            var slowPerfect = new LevelResult { Won = true, Moves = 24, Par = 24, MaxHp = 2, HpLeft = 2, TimeMs = 95_400 };
            var fastPerfect = new LevelResult { Won = true, Moves = 30, Par = 30, MaxHp = 2, HpLeft = 1, TimeMs = 31_250 };
            Assert.Less(slowPerfect.LeaderboardScore, fastSloppy.LeaderboardScore, "moves over par come first");
            Assert.Less(fastPerfect.LeaderboardScore, slowPerfect.LeaderboardScore, "then the fastest run wins");
            Assert.AreEqual((0, 31_250), LevelResult.DecodeScore(fastPerfect.LeaderboardScore));
            Assert.Less(LevelResult.EncodeScore(0, 3_600_000), LevelResult.EncodeScore(0, 0), "an unknown time ranks after any measured one");
            Assert.Less(LevelResult.EncodeScore(0, 0), LevelResult.EncodeScore(1, 1), "...but never above a better gap");
            Assert.AreEqual("31,2 s", LevelResult.FormatTime(31_250));
            Assert.AreEqual("1:35,4", LevelResult.FormatTime(95_400));
            Assert.AreEqual("parfait · 31,2 s", LevelResult.FormatScore(0, 31_250));

            var rec = new LevelRecord();
            rec.Merge(slowPerfect);
            Assert.IsTrue(rec.Merge(fastPerfect), "same gap, faster: new record");
            Assert.AreEqual(31_250, rec.BestTimeMs);
            Assert.IsFalse(rec.Merge(slowPerfect));
            Assert.IsFalse(rec.Merge(fastSloppy), "faster but more moves is not a record");
        }

        [Test]
        public void Clock_RunsOnlyWhilePlaying()
        {
            var s = new GameSession(FromAscii("#####", "#S.E#", "#####"));
            s.Tick(1.25);
            s.Tick(-3);
            s.Move(Dir.Right);
            s.Move(Dir.Right);
            Assert.AreEqual(SessionStatus.Won, s.Status);
            s.Tick(10);
            Assert.AreEqual(1250, s.ElapsedMs, "frozen once escaped");
            Assert.AreEqual(1250, s.BuildResult().TimeMs);
        }

        [Test]
        public void Current_CarriesDownstream_AndCannotBeClimbed()
        {
            var s = new GameSession(FromAscii("########", "#S>>..E#", "########"));
            var start = s.Position;
            var ride = s.Move(Dir.Right);
            Assert.IsTrue(ride.Has(StepFlags.Swept));
            Assert.AreEqual(start.X + 3, s.Position.X, "two current tiles carry the mummy to still ground in one move");
            Assert.AreEqual(1, s.Moves);
            var upstream = s.Move(Dir.Left);
            Assert.IsTrue(upstream.Has(StepFlags.Swept), "walking into the current pushes the mummy back");
            Assert.AreEqual(start.X + 3, s.Position.X);
            Assert.AreEqual(3, Solver.Solve(FromAscii("########", "#S>>..E#", "########")).Moves);
        }

        [Test]
        public void Crumbling_SlabCollapsesBehindTheMummy()
        {
            var s = new GameSession(FromAscii("#######", "#Sx..E#", "#######"));
            s.Move(Dir.Right);
            Assert.IsFalse(s.IsCollapsed(s.Position), "it holds while the mummy stands on it");
            var off = s.Move(Dir.Right);
            Assert.IsTrue(off.Has(StepFlags.Collapsed));
            Assert.IsTrue(s.IsCollapsed(s.Position.Step(Dir.Left)));
            Assert.IsTrue(s.Move(Dir.Left).Has(StepFlags.Blocked), "rubble blocks the way back");
        }

        [Test]
        public void Switch_FlipsRedAndBlueBarriers()
        {
            var lvl = FromAscii("#######", "#S=$|E#", "#######");
            var s = new GameSession(lvl);
            Assert.IsTrue(s.IsDoorOpen(s.Position.Step(Dir.Right)), "blue is open while the switch is off");
            s.Move(Dir.Right);
            var flip = s.Move(Dir.Right);
            Assert.IsTrue(flip.Has(StepFlags.Switched) && flip.Has(StepFlags.ButtonPressed));
            Assert.IsFalse(s.IsDoorOpen(s.Position.Step(Dir.Left)), "blue closes");
            Assert.IsTrue(s.IsDoorOpen(s.Position.Step(Dir.Right)), "red opens");
            Assert.AreEqual(SessionStatus.Playing, s.Status, "stepping back onto the switch reopens blue: not trapped");
            s.Move(Dir.Right);
            s.Move(Dir.Right);
            Assert.AreEqual(SessionStatus.Won, s.Status);
            Assert.AreEqual(4, Solver.Solve(lvl).Moves);
            Assert.AreEqual(1, Solver.Solve(lvl).ButtonsPressed);
        }

        [Test]
        public void FireJet_BurnsOnItsBeat()
        {
            // Phase 1 fires right after the first move: walking straight in burns.
            var burnt = new GameSession(FromAscii("######", "#S1.E#", "######"));
            Assert.IsTrue(burnt.IsAboutToFire(burnt.Position.Step(Dir.Right)));
            var r = burnt.Move(Dir.Right);
            Assert.IsTrue(r.Has(StepFlags.Burned) && r.Has(StepFlags.Damaged));
            Assert.AreEqual(1, burnt.Hp);
            Assert.IsTrue(burnt.IsFiring(burnt.Position));

            var safe = new GameSession(FromAscii("######", "#S0.E#", "######"));
            Assert.IsFalse(safe.Move(Dir.Right).Has(StepFlags.Burned), "phase 0 is quiet on that move");
            Assert.AreEqual(2, safe.Hp);
        }

        [Test]
        public void Trapped_WhenNoWayOutIsLeft()
        {
            // The current carries the mummy into a pocket it can never leave: the run ends at once.
            var s = new GameSession(FromAscii("#######", "#E.S>.#", "#######"));
            var r = s.Move(Dir.Right);
            Assert.IsTrue(r.Has(StepFlags.Trapped));
            Assert.AreEqual(SessionStatus.Dead, s.Status);
            Assert.AreEqual(DefeatCause.Trapped, s.Defeat);
            Assert.IsNotNull(Solver.Solve(FromAscii("#######", "#E.S>.#", "#######")), "the omniscient route simply walks left");
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
