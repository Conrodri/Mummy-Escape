using System;
using System.Collections.Generic;
using MummyEscape.Monetization;

namespace MummyEscape.Services
{
    /// <summary>
    /// The real-money side of the save: golden scarabs, the daily game limits and the season pass. Kept on the device
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

        // ------------------------------------------------------------------ daily limits

        static string Today => DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A new day gives every kind of game back its full count.</summary>
        void RollPlays()
        {
            if (Data.PlaysDay == Today) return;
            Data.PlaysDay = Today;
            Data.SoloLeft = PlayLimits.Max(PlayMode.Solo);
            Data.DuelLeft = PlayLimits.Max(PlayMode.Duel);
            Data.DuoLeft = PlayLimits.Max(PlayMode.Duo);
        }

        /// <summary>The pass of the season lifts every limit.</summary>
        public bool Unlimited => HasPass;

        public int PlaysLeft(PlayMode mode)
        {
            RollPlays();
            return mode == PlayMode.Solo ? Data.SoloLeft : mode == PlayMode.Duel ? Data.DuelLeft : Data.DuoLeft;
        }

        public bool CanPlay(PlayMode mode) => Unlimited || PlaysLeft(mode) > 0;

        /// <summary>Uses one game of that kind (nothing with the pass); false when none is left.</summary>
        public bool UsePlay(PlayMode mode)
        {
            if (Unlimited) return true;
            int left = PlaysLeft(mode);
            if (left <= 0) return false;
            SetPlays(mode, left - 1);
            Save();
            return true;
        }

        /// <summary>The reward of an ad: more games of that kind (<see cref="PlayLimits.AdRefill"/>).</summary>
        public void AddPlays(PlayMode mode, int count)
        {
            SetPlays(mode, PlaysLeft(mode) + count);
            Save();
        }

        void SetPlays(PlayMode mode, int value)
        {
            if (mode == PlayMode.Solo) Data.SoloLeft = value;
            else if (mode == PlayMode.Duel) Data.DuelLeft = value;
            else Data.DuoLeft = value;
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
        /// the furthest pass progress of the same season. The daily limits stay per device.
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
