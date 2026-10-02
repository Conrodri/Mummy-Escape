using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    public class BakedTableTests
    {
        /// <summary>
        /// The baked attempt table must match a full search, otherwise devices would build different tombs than
        /// the reference generator. If this fails: bump DifficultyTable.GeneratorVersion when layouts changed on
        /// purpose, then run: dotnet run --project tools/LevelLab -- --bake
        /// </summary>
        [Test, Category("Slow")]
        public void BakedAttempts_MatchFullSearch()
        {
            foreach (var id in DifficultyTable.AllLevels())
            {
                Assert.IsTrue(LevelAttemptTable.TryGet(id, out int baked), $"{id} missing from LevelAttemptTable (re-bake)");
                var level = LevelGenerator.GenerateFromScratch(id);
                Assert.AreEqual(level.Attempt, baked, $"{id}: baked attempt is stale (re-bake)");
            }
        }
    }
}
