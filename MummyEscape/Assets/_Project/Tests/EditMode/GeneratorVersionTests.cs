using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>
    /// Pins the mazes each <see cref="DifficultyTable.GeneratorVersion"/> draws. Seeds, leaderboards, ghosts and duels are
    /// keyed by that version, so a generator change that keeps the old number would mix tombs from different rules.
    /// History of the versions: GENERATEUR.md at the repository root.
    /// </summary>
    public class GeneratorVersionTests
    {
        /// <summary>Fingerprint of <see cref="Fingerprint"/> per generator version (recorded from v11 on).</summary>
        static readonly Dictionary<int, ulong> Recorded = new Dictionary<int, ulong>
        {
            { 11, 0xD323AB4665867852UL },
            { 12, 0x50A7922A87EB7AB5UL },
        };

        /// <summary>Variant 0 of every level, plus a few duel and 2v2 relay arenas, with their par.</summary>
        static ulong Fingerprint()
        {
            ulong h = 14695981039346656037UL;
            void Mix(ulong v) { unchecked { h ^= v; h *= 1099511628211UL; } }
            void MixLevel(Level level) { Mix(level.ComputeHash()); Mix((ulong)level.Solution.Moves); }

            foreach (var id in DifficultyTable.AllLevels()) MixLevel(LevelGenerator.Generate(id, 0));
            foreach (int seed in new[] { 1, 4242 }) MixLevel(PvpArena.Generate(seed));
            foreach (int seed in new[] { 1, 4242 }) Mix(RelayArena.Generate(seed).ComputeHash());
            return h;
        }

        [Test]
        public void Generator_OutputMatchesItsVersion()
        {
            int version = DifficultyTable.GeneratorVersion;
            ulong actual = Fingerprint();
            Assert.IsTrue(Recorded.TryGetValue(version, out ulong expected),
                $"generator v{version} has no recorded fingerprint: add {{ {version}, 0x{actual:X16}UL }} to Recorded and describe v{version} in GENERATEUR.md");
            Assert.AreEqual(expected, actual,
                $"the mazes of generator v{version} changed (fingerprint 0x{actual:X16}UL): bump DifficultyTable.GeneratorVersion, " +
                "record the new fingerprint and describe the change in GENERATEUR.md");
        }
    }
}
