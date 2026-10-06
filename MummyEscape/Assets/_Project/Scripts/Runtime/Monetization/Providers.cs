using System.Threading.Tasks;

namespace MummyEscape.Monetization
{
    /// <summary>Rewarded video ads (an ad network SDK: Unity LevelPlay, AdMob...).</summary>
    public interface IAdProvider
    {
        bool IsReady { get; }
        /// <summary>
        /// True when the player watched the ad to the end (the reward is due). The network starts on the first one
        /// (nothing leaves the phone before the player asks for an ad), after its consent form where the law wants it.
        /// </summary>
        Task<bool> ShowRewardedAsync();
        /// <summary>The network's own consent choices can be reopened (Settings › Privacy).</summary>
        bool HasPrivacyOptions { get; }
        void ShowPrivacyOptions(System.Action closed);
    }

    public struct PurchaseOutcome
    {
        public bool Ok;
        public bool Cancelled;
        /// <summary>The store's transaction id: a purchase is credited once, even if reported twice.</summary>
        public string TransactionId;
        public string Error;
    }

    /// <summary>The platform store (Google Play Billing, App Store) behind a purchasing SDK such as Unity IAP.</summary>
    public interface IStoreProvider
    {
        bool IsReady { get; }
        /// <summary>The price as the store shows it in the player's currency, null while unknown.</summary>
        string LocalizedPrice(string productId);
        Task<PurchaseOutcome> BuyAsync(string productId);
    }

    /// <summary>
    /// Where the ad and store SDKs plug in (each from its own assembly, like Google sign-in). Until one does, the editor
    /// and development builds use simulated ones (nothing is paid, the ad is a countdown); a release build without
    /// them simply shows the offers as unavailable.
    /// </summary>
    public static class Ads
    {
        public static IAdProvider Provider;
        public static bool Available => Provider != null && Provider.IsReady;
    }

    public static class Store
    {
        public static IStoreProvider Provider;
        public static bool Available => Provider != null && Provider.IsReady;

        public static string Price(GoldPack pack) => Provider?.LocalizedPrice(pack.ProductId) ?? pack.FallbackPrice;
    }
}
