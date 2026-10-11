using System;
using MummyEscape.Core;

namespace MummyEscape.Services
{
    /// <summary>What a tower floor's end did: the run goes on (or not), a reward skin won, a new record.</summary>
    public readonly struct TowerOutcome
    {
        public readonly bool Continues;
        public readonly string RewardSkin;
        public readonly bool Record;

        public TowerOutcome(bool continues, string rewardSkin, bool record)
        {
            Continues = continues;
            RewardSkin = rewardSkin;
            Record = record;
        }
    }

    /// <summary>The challenge tower (<see cref="Core.Tower"/>): the run in progress, the record and the floor skins.</summary>
    public sealed partial class SaveService
    {
        public bool IsTowerUnlocked => Core.Tower.IsUnlocked(IsCompleted);

        /// <summary>The run being climbed, null between runs.</summary>
        public TowerRun ActiveTower => Data.TowerActive ? Data.Tower : null;

        /// <summary>A new run (its energy point is spent by the caller, see PlayGate): new tombs, no handicap yet.</summary>
        public TowerRun StartTower()
        {
            Data.Tower = new TowerRun { Seed = new Random().Next(1, int.MaxValue) };
            Data.TowerActive = true;
            Data.TowerBest.Runs++;
            Save();
            return Data.Tower;
        }

        /// <summary>The handicap picked when one is due (<see cref="TowerRun.CurseDue"/>), kept for the rest of the run.</summary>
        public void TakeTowerCurse(TowerCurse curse)
        {
            if (!Data.TowerActive) return;
            Data.Tower.Take(curse);
            Save();
        }

        /// <summary>
        /// The end of the current floor: an escape with enough stars climbs on (a reward floor gives its skin, the record
        /// follows the run), anything else ends the run.
        /// </summary>
        public TowerOutcome ApplyTower(LevelResult result)
        {
            var run = Data.Tower;
            if (!Data.TowerActive) return new TowerOutcome(false, null, false);
            bool climbs = Core.Tower.Continues(result);
            string reward = null;
            bool record = false;
            if (climbs)
            {
                run.Escaped(result.TimeMs);
                int i = Array.IndexOf(Core.Tower.RewardFloors, run.Cleared);
                if (i >= 0 && !Data.OwnedSkins.Contains(Core.Tower.RewardSkins[i]))
                {
                    reward = Core.Tower.RewardSkins[i];
                    Data.OwnedSkins.Add(reward);
                }
                record = Data.TowerBest.Offer(run);
            }
            else Data.TowerActive = false;
            Save();
            return new TowerOutcome(climbs, reward, record);
        }

        /// <summary>The player leaves the run: it ends where it stands (its escaped floors are already in the record).</summary>
        public void EndTower()
        {
            if (!Data.TowerActive) return;
            Data.TowerActive = false;
            Save();
        }
    }
}
