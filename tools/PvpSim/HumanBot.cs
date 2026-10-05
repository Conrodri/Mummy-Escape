using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;

namespace MummyEscape.PvpSim
{
    /// <summary>A simulated player: a <see cref="HumanPlayer"/> (the same model as the game's offline ghosts) who duels now and then.</summary>
    public sealed class HumanBot
    {
        public readonly string Id;
        public readonly string Name;
        public readonly double Skill;
        /// <summary>Duels a day this player tends to play.</summary>
        public readonly int Activity;

        readonly HumanPlayer _player;

        public HumanBot(string id, string name, double skill, int activity, Random rng)
        {
            Id = id; Name = name; Skill = skill; Activity = activity;
            _player = new HumanPlayer(skill, rng);
        }

        /// <summary>Plays the duel tomb; rage-quits now and then.</summary>
        public (List<RunInput> inputs, RunOutcome outcome, HumanPlayer.RunStats stats) Play(Level level, Random rng)
            => _player.Play(level, rng, quitChance: 0.015);
    }
}
