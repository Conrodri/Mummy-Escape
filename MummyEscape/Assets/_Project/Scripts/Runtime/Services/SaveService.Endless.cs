using System;
using MummyEscape.Core;
using MummyEscape.Pvp;

namespace MummyEscape.Services
{
    /// <summary>The endless ladders: the place on each (escapes, time) and their own energy, one point per tomb.</summary>
    public sealed partial class SaveService
    {
        public EndlessProgress EndlessOf(Difficulty mode) => Data.Endless[(int)mode];

        /// <summary>A ladder opens once its mode's campaign is finished.</summary>
        public bool IsEndlessUnlocked(Difficulty mode) => Endless.IsUnlocked(mode, IsCompleted);

        /// <summary>The run's outcome on the ladder: an escape moves on to the next tomb, a death keeps the same one.</summary>
        public EndlessProgress ApplyEndless(Difficulty mode, LevelResult result)
        {
            var p = EndlessOf(mode);
            if (result.Won)
            {
                p.Escaped(result.TimeMs);
                Data.Coins += Monetization.BattlePass.EndlessScarabs; // a death retries the same tomb: each tomb pays once
                AddPassXp(Monetization.BattlePass.EndlessXp, false);
            }
            else p.Deaths++;
            Save();
            return p;
        }

        EnergyMeter EndlessMeter => Data.EndlessEnergy ??= new EnergyMeter();

        public int EndlessEnergyLeft => Energy.Left(EndlessMeter, EnergyConfig.EndlessMax, NowMs);

        public long EndlessEnergyNextInMs => Energy.NextInMs(EndlessMeter, NowMs);

        public int EndlessAdsLeft => Energy.AdsLeft(EndlessMeter, NowMs);

        public bool CanPlayEndless => Unlimited || EndlessEnergyLeft > 0;

        /// <summary>Spends the point of one tomb (nothing with the pass); false when none is left.</summary>
        public bool UseEndless()
        {
            if (Unlimited) return true;
            if (!Energy.Spend(EndlessMeter, EnergyConfig.EndlessMax, NowMs)) return false;
            Save();
            return true;
        }

        /// <summary>The reward of an ad: <see cref="EnergyConfig.EndlessAdRefill"/> points; false once the ads of the day are used.</summary>
        public bool RefillEndless()
        {
            if (!Energy.Refill(EndlessMeter, EnergyConfig.EndlessAdRefill, NowMs)) return false;
            Save();
            return true;
        }
    }
}
