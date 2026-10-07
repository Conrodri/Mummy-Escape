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
            var level = Get(id, variant);
            var spec = level.Spec; // the act spec, or its slightly wider fallback for a rare unlucky seed
            var sol = level.Solution;
            Assert.That(spec.MaxMoves, Is.LessThanOrEqualTo(DifficultyTable.Spec(id).MaxMoves + LevelGenerator.MaxRelaxSteps * LevelGenerator.RelaxMoves));

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
        public void Maze_PutsPortalsInDeadEnds(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            Assert.IsNull(LevelValidator.CheckDeadEndPortals(level), "one way out of each teleporter");
            Assert.That(level.Floors, Is.LessThanOrEqualTo(3), "a human memorises 3 floors at most");
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_ExitIsFarOrBehindAFarButton(int act, int index, int variant)
        {
            var id = new LevelId(act, index);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id, variant);
            bool far = level.Start.Manhattan(level.Exit) >= spec.MinExitDistance;
            bool farButton = level.AllCells().Any(c => level[c].IsTrigger && level.Start.Manhattan(c) >= spec.MinExitDistance);
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
                case 3: Assert.That(Count(TileType.Crumbling), Is.GreaterThanOrEqualTo(1), "ruins: fragile slabs"); break;
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

        /// <summary>Currents, fragile slabs and barriers may cost a detour, never the run: no move sequence walls the mummy in.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_NeverWallsThePlayerIn(int act, int index, int variant)
        {
            Assert.IsNull(LevelValidator.CheckNoDeadLock(Get(new LevelId(act, index), variant)));
        }

        /// <summary>
        /// Spikes guard shortcuts: each one has a longer spike-free way round (but those barring the way past dust, to
        /// disarm by torchlight), and the exit can be reached without a hit (a life is traded for moves, never owed).
        /// </summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_SpikesOnlyGuardShortcuts(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            Assert.IsNull(LevelValidator.CheckSpikeShortcuts(level));
        }

        /// <summary>No lure: every button, door, portal, ladder, hazard and wall torch serves the ideal route.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_EveryElementServesTheRoute(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            Assert.IsNull(LevelValidator.CheckEverythingUsed(level, level.Solution));
            Assert.IsFalse(level.AllCells().Any(c => level[c].Teleporter == TeleporterKind.Hidden), "every portal shows on the preview");
        }

        /// <summary>
        /// Dust early on, then spikes with no way round past it: the exit can't be reached unhurt without relighting the
        /// torch at the wall sconce to see them and disarm them.
        /// </summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_WallTorchIsNeededForTheSpikes(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            if (!level.AllCells().Any(c => level[c].Type == TileType.WallTorch)) return;
            int toll = level.AllCells().Count(c => level[c].Type == TileType.Trap && level[c].Trap == TrapKind.Spikes && LevelValidator.SpikeDetour(level, c) < 0);
            Assert.That(toll, Is.GreaterThanOrEqualTo(LevelValidator.TollSpikes), level.ToAscii());
            var unhurt = SolverOptions.Default;
            unhurt.AvoidSpikes = true;
            Assert.IsNull(Solver.Solve(level, unhurt), "no way out unhurt without disarming\n" + level.ToAscii());
            unhurt.DisarmSpikes = true;
            Assert.IsNotNull(Solver.Solve(level, unhurt), "disarming by torchlight gets out unhurt\n" + level.ToAscii());
        }

        /// <summary>
        /// No wing nobody needs: ground off the ideal walk always links two separate points of it (a short or a long
        /// way to choose between), never a pocket entered and left through the same spot.
        /// </summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_HasNoUnusedWing(int act, int index, int variant)
        {
            var level = Get(new LevelId(act, index), variant);
            var onWalk = new bool[level.CellCount];
            var s = Rules.Initial(level);
            onWalk[level.IndexOf(s.Position)] = true;
            foreach (var a in level.Solution.Actions)
            {
                var r = Rules.Step(level, s, a);
                onWalk[level.IndexOf(r.SteppedOn)] = true; // a ladder or a pad, before the move carries on
                s = r.State;
                onWalk[level.IndexOf(s.Position)] = true;
            }
            bool Ground(Cell c) => level.InBounds(c) && !level[c].IsSolid;
            var seen = new bool[level.CellCount];
            foreach (var c0 in level.AllCells())
            {
                if (seen[level.IndexOf(c0)] || onWalk[level.IndexOf(c0)] || !Ground(c0)) continue;
                var touches = new List<Cell>();
                bool touchesTorch = false; // the alcove of a wall torch: a dead end worth its moves
                var q = new Queue<Cell>();
                seen[level.IndexOf(c0)] = true;
                q.Enqueue(c0);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    foreach (var d in DirExt.All)
                    {
                        var n = c.Step(d);
                        if (level.Get(n).Type == TileType.WallTorch) touchesTorch = true;
                        if (!Ground(n)) continue;
                        if (onWalk[level.IndexOf(n)]) { touches.Add(n); continue; }
                        if (seen[level.IndexOf(n)]) continue;
                        seen[level.IndexOf(n)] = true;
                        q.Enqueue(n);
                    }
                }
                // 2 apart is the way around a fragile slab or a current, kept so they never wall the player in.
                bool apart = touchesTorch || touches.Any(a => touches.Any(b => a.Floor == b.Floor && a.Manhattan(b) >= 2));
                Assert.IsTrue(apart, $"{c0} sits in a pocket the ideal walk never needs\n{level.ToAscii(new[] { c0 })}");
            }
        }

        /// <summary>The map preview lasts as long as the tomb asks for: a glance for a first tomb, a real look later on.</summary>
        [Test]
        public void Preview_GrowsWithWhatThereIsToRemember()
        {
            // Whole preview of a level (every floor): stacked floors are smaller, but there are more of them.
            double Average(int act)
            {
                var seconds = new List<int>();
                for (int i = 1; i <= DifficultyTable.GetAct(act).Levels; i++)
                {
                    var level = Get(new LevelId(act, i), 0);
                    int sum = 0;
                    for (int f = 0; f < level.Floors; f++)
                    {
                        int s = level.PreviewSeconds(f);
                        Assert.That(s, Is.InRange(Level.MinPreviewSeconds, Level.MaxPreviewSeconds));
                        sum += s;
                    }
                    seconds.Add(sum);
                }
                return seconds.Average();
            }
            Assert.That(Average(1), Is.LessThanOrEqualTo(7), "a first tomb takes a few seconds, not 10");
            Assert.That(Average(3), Is.GreaterThan(Average(1)));
            Assert.That(Average(5), Is.GreaterThan(Average(3)));
        }

        /// <summary>The game plays on memory and logic, not length: the ideal route stays short in every act.</summary>
        [Test]
        public void Acts_IdealRouteStaysShort()
        {
            // A tomb with dust gets room for the torch and the spikes it shows, up to DifficultyTable.MaxMovesCap.
            foreach (var id in DifficultyTable.AllLevels())
            {
                var spec = DifficultyTable.Spec(id);
                Assert.That(spec.MaxMoves, Is.LessThanOrEqualTo(spec.DustPatches > 0 ? DifficultyTable.MaxMovesCap : 48), $"{id}: a level must fit in about 2 minutes");
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
        public void Act1_IsAtLeast14MovesWithOneMechanic()
        {
            Assert.AreEqual(14, DifficultyTable.GetAct(1).MinMoves);
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
                    return s.Gates.Count + s.SpikeTraps + s.DarknessTraps + s.ReverseTraps + s.RotateTraps + s.DustPatches + s.Currents + s.CrumblingTiles + s.FireJets;
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
                for (int v = 0; v < 60; v++)
                {
                    var level = LevelGenerator.Generate(id, v);
                    Assert.IsNull(LevelValidator.Validate(level, level.Spec, level.Solution), $"{id} maze {v}");
                }
        }
    }
}
