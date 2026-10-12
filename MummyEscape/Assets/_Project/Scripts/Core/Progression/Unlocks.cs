using System;

namespace MummyEscape.Core
{
    /// <summary>A room of the game that opens along the campaign, so a new player meets one thing at a time.</summary>
    [Flags]
    public enum Feature
    {
        Duel = 1,
        Guild = 2,
        Chat = 4,
        Casino = 8,
    }

    /// <summary>
    /// When each feature opens: the duels (and the group) once act 1 is escaped, the casino once act 2 is. The guild and
    /// the chat are open to everyone from the start. The endless mode and the tower keep their own gate (the whole
    /// campaign, see <see cref="Endless"/>).
    /// </summary>
    public static class Unlocks
    {
        /// <summary>The features that do open along the campaign.</summary>
        public static readonly Feature[] All = { Feature.Duel, Feature.Casino };

        /// <summary>The tomb to escape first (null: open from the start).</summary>
        public static LevelId? Gate(Feature feature) =>
            feature == Feature.Duel ? EndOfAct(1) : feature == Feature.Casino ? EndOfAct(2) : (LevelId?)null;

        public static LevelId EndOfAct(int act) => new LevelId(Difficulty.Easy, act, DifficultyTable.GetAct(act).Levels);

        public static bool IsUnlocked(Feature feature, Func<LevelId, bool> isCompleted)
        {
            var gate = Gate(feature);
            return gate == null || isCompleted(gate.Value);
        }

        /// <summary>Every open feature as a bitmask (what the player has seen is kept the same way).</summary>
        public static int Mask(Func<LevelId, bool> isCompleted)
        {
            int mask = 0;
            foreach (var f in All) if (IsUnlocked(f, isCompleted)) mask |= (int)f;
            return mask;
        }
    }
}
