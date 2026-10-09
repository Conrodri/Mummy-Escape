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

        static LevelId Id(string name) => LevelId.TryParse(name, out var id) ? id : throw new ArgumentException(name);

        static Level Get(LevelId id, int variant)
        {
            if (!Cache.TryGetValue((id, variant), out var level)) Cache[(id, variant)] = level = LevelGenerator.Generate(id, variant);
            return level;
        }

        public static IEnumerable<TestCaseData> AllMazes() =>
            DifficultyTable.AllLevels().SelectMany(id => Enumerable.Range(0, FastVariants)
                .Select(v => new TestCaseData(id.ToString(), v).SetName($"Level {id} maze {v}")));

        public static IEnumerable<TestCaseData> AllLevels() =>
            DifficultyTable.AllLevels().Select(id => new TestCaseData(id.ToString()).SetName($"Level {id}"));

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_RespectsItsSpec(string name, int variant)
        {
            var id = Id(name);
            int act = id.Act;
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
        public void Maze_ParReplaysInGameSession(string name, int variant)
        {
            var level = Get(Id(name), variant);
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
        public void Maze_GatesAreMandatory(string name, int variant)
        {
            var id = Id(name);
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
        public void Maze_DeadEndsAlwaysHoldSomething(string name, int variant)
        {
            Assert.IsNull(LevelValidator.CheckDeadEnds(Get(Id(name), variant)));
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_PutsPortalsInDeadEnds(string name, int variant)
        {
            var level = Get(Id(name), variant);
            Assert.IsNull(LevelValidator.CheckDeadEndPortals(level), "one way out of each teleporter");
            Assert.That(level.Floors, Is.LessThanOrEqualTo(3), "a human memorises 3 floors at most");
        }

        [TestCaseSource(nameof(AllMazes))]
        public void Maze_ExitIsFarOrBehindAFarButton(string name, int variant)
        {
            var id = Id(name);
            var spec = DifficultyTable.Spec(id);
            var level = Get(id, variant);
            bool far = level.Start.Manhattan(level.Exit) >= spec.MinExitDistance;
            bool farButton = level.AllCells().Any(c => level[c].IsTrigger && level.Start.Manhattan(c) >= spec.MinExitDistance);
            Assert.IsTrue(far || farButton, $"exit {level.Start.Manhattan(level.Exit)} tiles from the start");
        }

        /// <summary>Each act has its own signature mechanic, present in every one of its tombs.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_HasItsActMechanic(string name, int variant)
        {
            var id = Id(name);
            int act = id.Act;
            var level = Get(id, variant);
            int Count(TileType type) => level.AllCells().Count(c => level[c].Type == type);
            switch (act)
            {
                case 2: Assert.That(Count(TileType.Current), Is.GreaterThanOrEqualTo(2), "flooded galleries: currents"); break;
                case 3: Assert.That(Count(TileType.Crumbling), Is.GreaterThanOrEqualTo(1), "ruins: fragile slabs"); break;
                case 4:
                    Assert.That(Count(TileType.Switch), Is.GreaterThanOrEqualTo(1), "city of Anubis: switches");
                    Assert.That(level.AllCells().Count(c => level[c].Type == TileType.Barrier && level[c].Param == 0), Is.GreaterThanOrEqualTo(1), "red barriers");
                    break;
                case 5: Assert.That(Count(TileType.FireJet), Is.GreaterThanOrEqualTo(1), "burning sanctuary: flame jets"); break;
            }
            if (act >= 2)
            {
                // The omniscient route never gets trapped (it reaches the exit), and the start is never a dead lock.
                Assert.IsTrue(Solver.CanEscape(level, Rules.Initial(level)));
            }
        }

        /// <summary>Currents, fragile slabs and barriers may cost a detour, never the run: no move sequence walls the mummy in.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_NeverWallsThePlayerIn(string name, int variant)
        {
            Assert.IsNull(LevelValidator.CheckNoDeadLock(Get(Id(name), variant)));
        }

        /// <summary>
        /// Spikes guard shortcuts: at least the mode's <see cref="ModeDefinition.MinSpikes"/> per tomb, each saving moves on the
        /// walk the player makes over a spike-free way round (but those barring the way past dust, to disarm by
        /// torchlight), and the exit can be reached without a hit (a life is traded for moves, never owed).
        /// </summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_SpikesOnlyGuardShortcuts(string name, int variant)
        {
            var level = Get(Id(name), variant);
            Assert.That(level.Spec.SpikeTraps, Is.GreaterThanOrEqualTo(DifficultyTable.GetMode(level.Id.Mode).MinSpikes));
            Assert.IsNull(LevelValidator.CheckSpikeShortcuts(level, level.Solution, level.Spec), level.ToAscii());
        }

        /// <summary>No lure: every button, door, portal, ladder, hazard and wall torch serves the ideal route.</summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_EveryElementServesTheRoute(string name, int variant)
        {
            var level = Get(Id(name), variant);
            Assert.IsNull(LevelValidator.CheckEverythingUsed(level, level.Solution));
            Assert.IsFalse(level.AllCells().Any(c => level[c].Teleporter == TeleporterKind.Hidden), "every portal shows on the preview");
        }

        /// <summary>
        /// Dust early on, then a reason to light the wall torch again: either spikes with no way round past it (the exit
        /// can't be reached unhurt without relighting the torch to see them and disarm them), or a torch shape (spikes to
        /// cross in the dark, beside a safe way past the torch, a few moves longer).
        /// </summary>
        [TestCaseSource(nameof(AllMazes))]
        public void Maze_WallTorchIsWorthLighting(string name, int variant)
        {
            var level = Get(Id(name), variant);
            if (!level.AllCells().Any(c => level[c].Type == TileType.WallTorch)) return;
            int toll = level.AllCells().Count(c => level[c].Type == TileType.Trap && level[c].Trap == TrapKind.Spikes && LevelValidator.SpikeDetour(level, c) < 0);
            if (toll == 0)
            {
                Assert.IsNull(LevelValidator.CheckTorches(level, level.Solution), level.ToAscii());
                return;
            }
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
        public void Maze_HasNoUnusedWing(string name, int variant)
        {
            var level = Get(Id(name), variant);
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
                bool decoy = false; // the alcoves of a turning-slab corridor, on purpose
                var q = new Queue<Cell>();
                seen[level.IndexOf(c0)] = true;
                q.Enqueue(c0);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    decoy |= level.IsDecoy(c);
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
                bool apart = touchesTorch || decoy || touches.Any(a => touches.Any(b => a.Floor == b.Floor && a.Manhattan(b) >= 2));
                Assert.IsTrue(apart, $"{c0} sits in a pocket the ideal walk never needs\n{level.ToAscii(new[] { c0 })}");
            }
        }

        /// <summary>Both ends of a teleporter share a pair number (its colour on screen), and no two pairs share one.</summary>
        [Test]
        public void Teleporters_EachPairHasItsOwnColour()
        {
            int levelsWithPairs = 0;
            foreach (var id in DifficultyTable.AllLevels())
                {
                    var level = Get(id, 0);
                    var pairs = new Dictionary<int, HashSet<Cell>>();
                    for (int c = 0; c < level.Width * level.Height * level.Floors; c++)
                    {
                        var cell = level.CellAt(c);
                        if (!level.TryGetTeleportTarget(cell, out var to)) continue;
                        int pair = level.TeleporterPair(cell);
                        Assert.AreEqual(pair, level.TeleporterPair(to), $"{cell} and {to} are one pair");
                        if (!pairs.TryGetValue(pair, out var ends)) pairs[pair] = ends = new HashSet<Cell>();
                        ends.Add(cell);
                        ends.Add(to);
                    }
                    Assert.IsTrue(pairs.Values.All(e => e.Count == 2), "one number per pair");
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, pairs.Count), pairs.Keys);
                    if (pairs.Count >= 2) levelsWithPairs++;
                }
            Assert.Greater(levelsWithPairs, 0, "some tombs have several pairs");
        }

        /// <summary>The map preview is 7 s per floor, pooled: a 3-floor tomb gives 21 s to share out between its floors.</summary>
        [Test]
        public void Preview_SevenSecondsPerFloor()
        {
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var level = Get(new LevelId(Difficulty.Normal, act, DifficultyTable.GetAct(act).Levels), 0);
                Assert.AreEqual(7 * level.Floors, level.PreviewSeconds);
            }
        }

        /// <summary>The game plays on memory and logic, not length: the ideal route stays short in every act.</summary>
        [Test]
        public void Acts_IdealRouteStaysShort()
        {
            // Gates, floors and the dust each widen the window, never past DifficultyTable.MaxMovesCap; the first modes stay shorter.
            foreach (var id in DifficultyTable.AllLevels())
            {
                var spec = DifficultyTable.Spec(id);
                int cap = id.Mode == Difficulty.Extreme || spec.DustPatches > 0 ? DifficultyTable.MaxMovesCap : 53;
                Assert.That(spec.MaxMoves, Is.LessThanOrEqualTo(cap), $"{id}: a level must fit in about 2 minutes");
            }
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_IsDeterministicPerVariant(string name)
        {
            var id = Id(name);
            var a = Get(id, 0);
            var b = LevelGenerator.Generate(id, 0);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "the same (level, variant) must rebuild the same tomb");
            Assert.AreEqual(a.Solution.Moves, b.Solution.Moves);
        }

        [TestCaseSource(nameof(AllLevels))]
        public void Level_EveryRunDrawsANewMaze(string name)
        {
            var id = Id(name);
            var hashes = Enumerable.Range(0, FastVariants).Select(v => Get(id, v).ComputeHash()).ToList();
            Assert.AreEqual(hashes.Count, hashes.Distinct().Count(), "replaying must give a different tomb");
        }

        [Test]
        public void Act1_IsAtLeast14MovesWithOneMechanic()
        {
            Assert.AreEqual(14, DifficultyTable.GetAct(1).MinMoves);
            foreach (var id in DifficultyTable.AllLevels())
            {
                if (id.Act != 1) continue;
                var spec = DifficultyTable.Spec(id);
                Assert.That(spec.Gates.Count, Is.GreaterThanOrEqualTo(1), $"{id} needs a button or a portal");
            }
        }

        [Test]
        public void Acts_MinimumMovesNeverDecrease()
        {
            for (int a = 2; a <= DifficultyTable.ActCount; a++)
                Assert.That(DifficultyTable.GetAct(a).MinMoves, Is.GreaterThan(DifficultyTable.GetAct(a - 1).MinMoves));
        }

        /// <summary>Each mode of an act climbs its band: scores never drop level after level and stay inside the band.</summary>
        [Test]
        public void Acts_DifficultyClimbsWithinEachModeBand()
        {
            foreach (var mode in DifficultyExt.All)
                foreach (var act in Enumerable.Range(1, DifficultyTable.ActCount))
                {
                    DifficultyTable.Band(mode, act, out int min, out int max);
                    int levels = DifficultyTable.GetAct(act).Levels, previous = int.MinValue;
                    for (int i = 1; i <= levels; i++)
                    {
                        var id = new LevelId(mode, act, i);
                        int score = DifficultyScore.Of(DifficultyTable.Spec(id)).Score;
                        Assert.That(score, Is.InRange(min, max), $"{id} out of its band");
                        Assert.That(score, Is.GreaterThanOrEqualTo(previous), $"{id} lighter than the level before");
                        previous = score;
                    }
                }
        }

        /// <summary>The modes do not overlap: Normal starts where Facile ends, Extrême where Normal ends.</summary>
        [Test]
        public void Acts_ModeBandsFollowEachOther()
        {
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var t = DifficultyTable.GetAct(act).Thresholds;
                Assert.AreEqual(4, t.Length);
                for (int k = 1; k < t.Length; k++) Assert.That(t[k], Is.GreaterThan(t[k - 1]), $"act {act} thresholds");
            }
        }

        /// <summary>Acts 1-2 have one floor, acts 3-4 two, act 5 three, in every mode.</summary>
        [Test]
        public void Acts_FloorsPerAct()
        {
            var floors = new[] { 1, 1, 2, 2, 3 };
            foreach (var id in DifficultyTable.AllLevels())
                Assert.AreEqual(floors[id.Act - 1], DifficultyTable.Spec(id).Floors, id.ToString());
        }

        /// <summary>Finishing a mode opens the next one, at its first level.</summary>
        [Test]
        public void Progression_ModesOpenOneAfterTheOther()
        {
            var lastEasy = Progression.Last(Difficulty.Easy);
            Assert.AreEqual(new LevelId(Difficulty.Normal, 1, 1), Progression.Next(lastEasy));
            Assert.AreEqual(lastEasy, Progression.Previous(new LevelId(Difficulty.Normal, 1, 1)));
            Assert.IsTrue(Progression.IsUnlocked(new LevelId(Difficulty.Easy, 1, 1), _ => false));
            Assert.IsFalse(Progression.IsModeUnlocked(Difficulty.Normal, _ => false));
            Assert.IsTrue(Progression.IsModeUnlocked(Difficulty.Normal, id => id.Equals(lastEasy)));
            Assert.IsNull(Progression.Next(Progression.Last(Difficulty.Extreme)));
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
