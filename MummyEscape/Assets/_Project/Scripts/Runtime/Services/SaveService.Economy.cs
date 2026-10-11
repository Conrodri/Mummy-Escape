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
        /// Takes the wallet the server sent, which is the truth: its golden scarabs, its passes and its paid skins replace the
        /// device's copy (a paid skin the server does not list is taken off), and the spends it confirmed are delivered.
        /// Gold held on the device before the server kept the wallet was never paid (no store existed then): it is not carried over.
        /// </summary>
        public void ApplyWallet(Wallet wallet)
        {
            if (wallet == null) return;
            Data.GoldScarabs = wallet.Gold;
            Data.PassesOwned = new List<string>(wallet.Passes ?? new List<string>());
            var skins = wallet.Skins ?? new List<string>();
            Data.OwnedSkins.RemoveAll(s => GoldShop.IsPaid(s) && !skins.Contains(s));
            foreach (var s in skins) if (!Data.OwnedSkins.Contains(s)) Data.OwnedSkins.Add(s);
            string Worn(string id, string fallback) => GoldShop.IsPaid(id) && !Data.OwnedSkins.Contains(id) ? fallback : id;
            Data.SelectedMummy = Worn(Data.SelectedMummy, Visual.SkinCatalog.DefaultMummyId);
            Data.SelectedSkin = Worn(Data.SelectedSkin, Visual.SkinCatalog.DefaultSkinId);
            Data.SelectedTorch = Worn(Data.SelectedTorch, Visual.SkinCatalog.DefaultTorchId);
            Data.SelectedHat = Worn(Data.SelectedHat, Visual.SkinCatalog.NoHatId);
            Data.SelectedShoes = Worn(Data.SelectedShoes, Visual.SkinCatalog.NoShoesId);
            // The pass XP is the server's (replayed runs, matches, tiers bought), plus the runs it has not replayed yet.
            RollPass();
            int xp = wallet.ClaimsSeason == BattlePass.Current.Id ? wallet.PassXp : 0;
            foreach (var p in Data.PendingRuns) xp += p.Xp;
            Data.PassXp = Math.Min(BattlePass.Tiers * BattlePass.XpPerTier, xp);
            if (wallet.ClaimsSeason == BattlePass.Current.Id)
            {
                foreach (int t in wallet.FreeClaimed) if (!Data.PassFreeClaimed.Contains(t)) Data.PassFreeClaimed.Add(t);
                foreach (int t in wallet.PremiumClaimed) if (!Data.PassPremiumClaimed.Contains(t)) Data.PassPremiumClaimed.Add(t);
            }
            foreach (var spend in Data.GoldSpends.ToArray())
                if (wallet.Requests != null && wallet.Requests.Contains(spend.Id)) Deliver(spend);
            Save();
        }

        // ------------------------------------------------------------------ spends the device delivers (scarabs, tiers)

        public const string TiersSpend = "tiers"; // noloc

        /// <summary>Remembers a spend before asking the server, so a lost answer is still delivered once the wallet lists it.</summary>
        public GoldSpend StartGoldSpend(string offer)
        {
            var spend = new GoldSpend { Id = Guid.NewGuid().ToString("N"), Offer = offer, AtMs = NowMs };
            Data.GoldSpends.Add(spend);
            Save();
            return spend;
        }

        /// <summary>Forgets a spend the server refused (or never received).</summary>
        public void DropGoldSpend(string id)
        {
            if (Data.GoldSpends.RemoveAll(s => s.Id == id) > 0) Save();
        }

        /// <summary>Spends still waiting after <paramref name="ageMs"/> that the server's wallet does not list: never debited.</summary>
        public void DropStaleGoldSpends(long ageMs, ICollection<string> inFlight)
        {
            if (Data.GoldSpends.RemoveAll(s => NowMs - s.AtMs > ageMs && !inFlight.Contains(s.Id)) > 0) Save();
        }

        /// <summary>Delivers a confirmed spend, once: it leaves the list as it is delivered.</summary>
        void Deliver(GoldSpend spend)
        {
            Data.GoldSpends.Remove(spend);
            var offer = GoldShop.Offer(spend.Offer);
            if (offer != null) Data.Coins += offer.Scarabs;
            else if (spend.Offer == TiersSpend)
            {
                RollPass();
                int target = Math.Min(BattlePass.Tiers, PassTier + GoldShop.TierBundleSize);
                Data.PassXp = Math.Max(Data.PassXp, target * BattlePass.XpPerTier);
            }
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

        const int PendingRunsKept = 50; // they ride in the cloud copy: kept small (sent by fives when online)

        /// <summary>
        /// A finished solo run for the server to replay: its XP shows at once, and stays only if the server's replay grants
        /// it (see <see cref="ApplyWallet"/>).
        /// </summary>
        public void QueueRun(SoloRunReport run, int xp)
        {
            Data.PendingRuns.Add(new PendingRun { Run = run, Xp = xp });
            if (Data.PendingRuns.Count > PendingRunsKept) Data.PendingRuns.RemoveAt(0); // long offline: the oldest runs give up their XP
            AddPassXp(xp, false);
            Save();
        }

        public int PendingRunCount => Data.PendingRuns.Count;

        /// <summary>The next runs to send, oldest first.</summary>
        public List<SoloRunReport> NextRuns(int count)
        {
            var runs = new List<SoloRunReport>();
            for (int i = 0; i < Data.PendingRuns.Count && runs.Count < count; i++) runs.Add(Data.PendingRuns[i].Run);
            return runs;
        }

        /// <summary>The server replayed these runs (granted or not): they leave the queue, before its wallet is applied.</summary>
        public void RunsSent(List<SoloRunReport> runs)
        {
            Data.PendingRuns.RemoveAll(p => runs.Contains(p.Run));
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
        /// Cloud copy of another device: the furthest pass progress of the same season. The golden scarabs, the passes and
        /// the paid skins come from the server's wallet only, and the solo energy stays per device.
        /// </summary>
        void MergeEconomy(SaveData other)
        {
            RollPass();
            if (other.PassSeason != Data.PassSeason) return;
            Data.PassXp = Math.Max(Data.PassXp, other.PassXp);
            foreach (int t in other.PassFreeClaimed ?? new List<int>()) if (!Data.PassFreeClaimed.Contains(t)) Data.PassFreeClaimed.Add(t);
            foreach (int t in other.PassPremiumClaimed ?? new List<int>()) if (!Data.PassPremiumClaimed.Contains(t)) Data.PassPremiumClaimed.Add(t);
        }
    }
}
