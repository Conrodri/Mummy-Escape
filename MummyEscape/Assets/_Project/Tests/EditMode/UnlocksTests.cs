using System.Collections.Generic;
using MummyEscape.Core;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    public class UnlocksTests
    {
        static System.Func<LevelId, bool> Done(params LevelId[] ids)
        {
            var set = new HashSet<LevelId>(ids);
            return id => set.Contains(id);
        }

        [Test]
        public void Duels_OpenAfterAct1_TheCasinoAfterAct2_GuildAndChatAlways()
        {
            var nothing = Done();
            Assert.AreEqual(0, Unlocks.Mask(nothing));
            Assert.IsFalse(Unlocks.IsUnlocked(Feature.Duel, nothing));
            Assert.IsFalse(Unlocks.IsUnlocked(Feature.Casino, nothing));
            Assert.IsTrue(Unlocks.IsUnlocked(Feature.Guild, nothing));
            Assert.IsTrue(Unlocks.IsUnlocked(Feature.Chat, nothing));
            Assert.IsNull(Unlocks.Gate(Feature.Chat));

            var act1 = Done(Unlocks.EndOfAct(1));
            Assert.IsTrue(Unlocks.IsUnlocked(Feature.Duel, act1));
            Assert.IsFalse(Unlocks.IsUnlocked(Feature.Casino, act1));
            Assert.AreEqual((int)Feature.Duel, Unlocks.Mask(act1));

            var act2 = Done(Unlocks.EndOfAct(1), Unlocks.EndOfAct(2));
            Assert.AreEqual((int)(Feature.Duel | Feature.Casino), Unlocks.Mask(act2));
        }

        [Test]
        public void Gate_IsTheLastTombOfTheAct()
        {
            var gate = Unlocks.Gate(Feature.Duel).Value;
            Assert.AreEqual(Difficulty.Easy, gate.Mode);
            Assert.AreEqual(1, gate.Act);
            Assert.AreEqual(DifficultyTable.GetAct(1).Levels, gate.Index);
            Assert.AreEqual(2, Unlocks.Gate(Feature.Casino).Value.Act);
        }
    }
}
