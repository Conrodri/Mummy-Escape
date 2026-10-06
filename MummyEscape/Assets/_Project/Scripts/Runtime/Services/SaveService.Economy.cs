using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;

namespace MummyEscape.Services
{
    /// <summary>
    /// The real-money side of the save: golden scarabs, the solo energy and the season pass. Kept on the device
    /// and in the cloud copy like the scarabs; receipts are not checked by a server yet.
    /// </summary>
    public sealed partial class SaveService
    {
        // ------------------------------------------------------------------ golden scarabs

        public int Gold => Data.GoldScarabs;

        /// <summary>Credits a store purchase once per transaction; false when it was already credited.</summary>
        public bool CreditPurchase(string transactionId, int gold)
        {
            if (string.IsNullOrEmpty(transactionId) || gold <= 0 || Data.Purchases.Contains(transactionId)) return false;
            Data.Purchases.Add(transactionId);
            Data.GoldScarabs += gold;
            Save();
            return true;
        }

        bool SpendGold(int amount)
        {
            if (amount < 0 || Data.GoldScarabs < amount) return false;
            Data.GoldScarabs -= amount;
            return true;
        }

        public bool BuyScarabs(ScarabOffer offer)
        {
            if (!SpendGold(offer.Gold)) return false;
            Data.Coins += offer.Scarabs;
            Save();
            return true;
        }

        /// <summary>Buys a treasure exclusive with golden scarabs.</summary>
        public bool BuyWithGold(GoldItem item)
        {
            if (Data.OwnedSkins.Contains(item.SkinId) || !SpendGold(item.Gold)) return false;
            Data.OwnedSkins.Add(item.SkinId);
            Save();
            return true;
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

        /// <summary>Buys the paid pass of the season with golden scarabs: its legendary comes at once.</summary>
        public bool BuyPass()
        {
            var season = BattlePass.Current;
            if (HasPass || !SpendGold(GoldShop.PassPrice)) return false;
            Data.PassesOwned.Add(season.Id);
            if (!Data.OwnedSkins.Contains(season.Legendary)) Data.OwnedSkins.Add(season.Legendary);
            Save();
            return true;
        }

        /// <summary>Skips 10 tiers (to the start of the tier 10 above) for golden scarabs.</summary>
        public bool BuyTiers()
        {
            int tier = PassTier;
            if (tier >= BattlePass.Tiers || !SpendGold(GoldShop.TierBundlePrice)) return false;
            int target = Math.Min(BattlePass.Tiers, tier + GoldShop.TierBundleSize);
            Data.PassXp = Math.Max(Data.PassXp, target * BattlePass.XpPerTier);
            Save();
            return true;
        }

        public bool IsClaimed(int tier, bool premium) => premium ? Data.PassPremiumClaimed.Contains(tier) : Data.PassFreeClaimed.Contains(tier);

        public bool CanClaim(int tier, bool premium) =>
            tier >= 1 && tier <= PassTier && !IsClaimed(tier, premium) && (!premium || HasPass);

        /// <summary>Collects one reward of the pass; returns it, or a None reward when it cannot be collected.</summary>
        public PassReward Claim(int tier, bool premium)
        {
            if (!CanClaim(tier, premium)) return default;
            var reward = premium ? BattlePass.Premium(BattlePass.Current, tier) : BattlePass.Free(tier);
            (premium ? Data.PassPremiumClaimed : Data.PassFreeClaimed).Add(tier);
            Grant(reward);
            Save();
            return reward;
        }

        /// <summary>Collects everything reached on both tracks (the paid one only with the pass).</summary>
        public List<PassReward> ClaimAll()
        {
            var rewards = new List<PassReward>();
            for (int tier = 1; tier <= PassTier; tier++)
                foreach (bool premium in new[] { false, true })
                    if (CanClaim(tier, premium))
                    {
                        var reward = premium ? BattlePass.Premium(BattlePass.Current, tier) : BattlePass.Free(tier);
                        (premium ? Data.PassPremiumClaimed : Data.PassFreeClaimed).Add(tier);
                        Grant(reward);
                        rewards.Add(reward);
                    }
            if (rewards.Count > 0) Save();
            return rewards;
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

        void Grant(PassReward reward)
        {
            switch (reward.Kind)
            {
                case PassRewardKind.Scarabs: Data.Coins += reward.Amount; break;
                case PassRewardKind.Gold: Data.GoldScarabs += reward.Amount; break;
                case PassRewardKind.Skin:
                    if (!Data.OwnedSkins.Contains(reward.SkinId)) Data.OwnedSkins.Add(reward.SkinId);
                    break;
            }
        }

        /// <summary>
        /// Cloud copy of another device: the larger golden wallet (never the sum), passes and credited purchases united,
        /// the furthest pass progress of the same season. The solo energy stays per device.
        /// </summary>
        void MergeEconomy(SaveData other)
        {
            Data.GoldScarabs = Math.Max(Data.GoldScarabs, other.GoldScarabs);
            foreach (var s in other.PassesOwned ?? new List<string>()) if (!Data.PassesOwned.Contains(s)) Data.PassesOwned.Add(s);
            foreach (var p in other.Purchases ?? new List<string>()) if (!Data.Purchases.Contains(p)) Data.Purchases.Add(p);
            RollPass();
            if (other.PassSeason != Data.PassSeason) return;
            Data.PassXp = Math.Max(Data.PassXp, other.PassXp);
            foreach (int t in other.PassFreeClaimed ?? new List<int>()) if (!Data.PassFreeClaimed.Contains(t)) Data.PassFreeClaimed.Add(t);
            foreach (int t in other.PassPremiumClaimed ?? new List<int>()) if (!Data.PassPremiumClaimed.Contains(t)) Data.PassPremiumClaimed.Add(t);
        }
    }
}
