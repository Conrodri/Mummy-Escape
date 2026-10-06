using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;

namespace MummyEscape.Services
{
    /// <summary>
    /// The real-money side of the save: the copy of the golden wallet (kept by the PvP server), the solo energy and the
    /// season pass progress (kept on the device and in the cloud copy).
    /// </summary>
    public sealed partial class SaveService
    {
        // ------------------------------------------------------------------ golden scarabs (a copy of the server's wallet)

        public int Gold => Data.GoldScarabs;

        /// <summary>
        /// Takes the wallet the server sent: its golden scarabs, its passes and its paid skins. What the server does not
        /// list stays as it is on the device (a skin is never taken away here; the server just won't show it to others).
        /// </summary>
        public void ApplyWallet(Wallet wallet)
        {
            if (wallet == null) return;
            Data.GoldScarabs = wallet.Gold;
            foreach (var s in wallet.Passes) if (!Data.PassesOwned.Contains(s)) Data.PassesOwned.Add(s);
            foreach (var s in wallet.Skins) if (!Data.OwnedSkins.Contains(s)) Data.OwnedSkins.Add(s);
            if (wallet.ClaimsSeason == BattlePass.Current.Id)
            {
                RollPass();
                foreach (int t in wallet.FreeClaimed) if (!Data.PassFreeClaimed.Contains(t)) Data.PassFreeClaimed.Add(t);
                foreach (int t in wallet.PremiumClaimed) if (!Data.PassPremiumClaimed.Contains(t)) Data.PassPremiumClaimed.Add(t);
            }
            Save();
        }

        /// <summary>Scarabs bought with golden scarabs (the server took the gold).</summary>
        public void ReceiveScarabs(int scarabs)
        {
            Data.Coins += Math.Max(0, scarabs);
            Save();
        }

        // ------------------------------------------------------------------ solo energy

        static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        EnergyMeter SoloMeter => Data.SoloEnergy ??= new EnergyMeter();

        /// <summary>The pass of the season lifts every limit.</summary>
        public bool Unlimited => HasPass;

        /// <summary>Act 1 is free: the energy counts from act 2.</summary>
        public static bool CostsEnergy(LevelId level) => level.Act >= 2;

        public int SoloEnergyLeft => Energy.Left(SoloMeter, EnergyConfig.SoloMax, NowMs);

        /// <summary>Time before the next point comes back (0: full).</summary>
        public long SoloEnergyNextInMs => Energy.NextInMs(SoloMeter, NowMs);

        public int SoloAdsLeft => Energy.AdsLeft(SoloMeter, NowMs);

        public bool CanPlaySolo(LevelId level) => Unlimited || !CostsEnergy(level) || SoloEnergyLeft > 0;

        /// <summary>Spends a point for that level (nothing in act 1 or with the pass); false when none is left.</summary>
        public bool UseSolo(LevelId level)
        {
            if (Unlimited || !CostsEnergy(level)) return true;
            if (!Energy.Spend(SoloMeter, EnergyConfig.SoloMax, NowMs)) return false;
            Save();
            return true;
        }

        /// <summary>The reward of an ad: <see cref="EnergyConfig.SoloAdRefill"/> points; false once the ads of the day are used.</summary>
        public bool RefillSolo()
        {
            if (!Energy.Refill(SoloMeter, EnergyConfig.SoloAdRefill, NowMs)) return false;
            Save();
            return true;
        }

        // ------------------------------------------------------------------ season pass

        /// <summary>Progress belongs to one season: a new season starts from tier 0 with nothing collected.</summary>
        void RollPass()
        {
            string season = BattlePass.Current.Id;
            if (Data.PassSeason == season) return;
            Data.PassSeason = season;
            Data.PassXp = 0;
            Data.PassFreeClaimed.Clear();
            Data.PassPremiumClaimed.Clear();
        }

        public bool HasPass => Data.PassesOwned.Contains(BattlePass.Current.Id);

        public int PassXp
        {
            get { RollPass(); return Data.PassXp; }
        }

        public int PassTier => BattlePass.TierOf(PassXp);

        public void AddPassXp(int xp, bool save = true)
        {
            if (xp <= 0) return;
            RollPass();
            Data.PassXp = Math.Min(BattlePass.Tiers * BattlePass.XpPerTier, Data.PassXp + xp);
            if (save) Save();
        }

        /// <summary>Moves 10 tiers ahead (to the start of the tier 10 above), once the server took the golden scarabs.</summary>
        public void SkipTiers()
        {
            int target = Math.Min(BattlePass.Tiers, PassTier + GoldShop.TierBundleSize);
            Data.PassXp = Math.Max(Data.PassXp, target * BattlePass.XpPerTier);
            Save();
        }

        public bool IsClaimed(int tier, bool premium) => premium ? Data.PassPremiumClaimed.Contains(tier) : Data.PassFreeClaimed.Contains(tier);

        public bool CanClaim(int tier, bool premium) =>
            tier >= 1 && tier <= PassTier && !IsClaimed(tier, premium) && (!premium || HasPass);

        public static PassReward RewardOf(int tier, bool premium) => premium ? BattlePass.Premium(BattlePass.Current, tier) : BattlePass.Free(tier);

        /// <summary>Golden scarabs and skins of the pass are given by the server (the wallet); scarabs by the device.</summary>
        public static bool FromServer(PassReward reward) => reward.Kind == PassRewardKind.Gold || reward.Kind == PassRewardKind.Skin;

        /// <summary>Collects one scarab reward of the pass; returns it, or a None reward when it cannot be collected here.</summary>
        public PassReward Claim(int tier, bool premium)
        {
            var reward = RewardOf(tier, premium);
            if (!CanClaim(tier, premium) || FromServer(reward)) return default;
            (premium ? Data.PassPremiumClaimed : Data.PassFreeClaimed).Add(tier);
            if (reward.Kind == PassRewardKind.Scarabs) Data.Coins += reward.Amount;
            Save();
            return reward;
        }

        public int ClaimableCount
        {
            get
            {
                int n = 0;
                for (int tier = 1; tier <= PassTier; tier++)
                {
                    if (CanClaim(tier, false)) n++;
                    if (CanClaim(tier, true)) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Cloud copy of another device: passes united, the furthest pass progress of the same season. The golden scarabs
        /// come from the server only, and the solo energy stays per device.
        /// </summary>
        void MergeEconomy(SaveData other)
        {
            foreach (var s in other.PassesOwned ?? new List<string>()) if (!Data.PassesOwned.Contains(s)) Data.PassesOwned.Add(s);
            RollPass();
            if (other.PassSeason != Data.PassSeason) return;
            Data.PassXp = Math.Max(Data.PassXp, other.PassXp);
            foreach (int t in other.PassFreeClaimed ?? new List<int>()) if (!Data.PassFreeClaimed.Contains(t)) Data.PassFreeClaimed.Add(t);
            foreach (int t in other.PassPremiumClaimed ?? new List<int>()) if (!Data.PassPremiumClaimed.Contains(t)) Data.PassPremiumClaimed.Add(t);
        }
    }
}
