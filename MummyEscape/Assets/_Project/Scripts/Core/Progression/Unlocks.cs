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
    /// When each feature opens: the duels (and the group) once act 1 is escaped, the guild, the chat and the casino once
    /// act 2 is. The endless mode and the tower keep their own gate (the whole campaign, see <see cref="Endless"/>).
    /// </summary>
    public static class Unlocks
    {
        public static readonly Feature[] All = { Feature.Duel, Feature.Guild, Feature.Chat, Feature.Casino };

        /// <summary>The tomb to escape first.</summary>
        public static LevelId Gate(Feature feature) => EndOfAct(feature == Feature.Duel ? 1 : 2);

        public static LevelId EndOfAct(int act) => new LevelId(Difficulty.Easy, act, DifficultyTable.GetAct(act).Levels);

        public static bool IsUnlocked(Feature feature, Func<LevelId, bool> isCompleted) => isCompleted(Gate(feature));

        /// <summary>Every open feature as a bitmask (what the player has seen is kept the same way).</summary>
        public static int Mask(Func<LevelId, bool> isCompleted)
        {
            int mask = 0;
            foreach (var f in All) if (IsUnlocked(f, isCompleted)) mask |= (int)f;
            return mask;
        }
    }
}
