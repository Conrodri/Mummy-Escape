using System;
using System.Threading;
using System.Threading.Tasks;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// Google AdMob rewarded ads. Nothing starts before the player asks for a first ad (offline play sends nothing):
    /// then Google's consent form (UMP) where the law wants one, the SDK, and from there the next ad is kept loaded.
    /// Minors get ads that are not personalized. Development builds show Google's test ads: tapping real ads of one's
    /// own app gets the account suspended. Only on Android devices: the editor keeps the simulated ad.
    /// </summary>
    public sealed class AdMobAds : IAdProvider
    {
        const string RewardedUnit = "ca-app-pub-1561599034070376/2695470111"; // noloc
        const string TestRewardedUnit = "ca-app-pub-3940256099942544/5224354917"; // noloc
        static string Unit => Debug.isDebugBuild ? TestRewardedUnit : RewardedUnit;
        const int LoadWaitMs = 10000;

#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Ads.Provider = new AdMobAds();
#endif

        SynchronizationContext _main;
        Task<bool> _start;
        RewardedAd _ad;
        TaskCompletionSource<bool> _loading;
        bool _consentAsked;

        /// <summary>Always offered: the first ad starts the network, then waits for it to load.</summary>
        public bool IsReady => true;

        public bool HasPrivacyOptions =>
            _consentAsked && ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        public void ShowPrivacyOptions(Action closed) =>
            ConsentForm.ShowPrivacyOptionsForm(error => OnMain(() =>
            {
                if (error != null) Debug.LogWarning("[Ads] Privacy options: " + error.Message);
                closed?.Invoke();
            }));

        public async Task<bool> ShowRewardedAsync()
        {
            _start ??= Start();
            if (!await _start)
            {
                _start = null; // refused or failed: asked again next time
                return false;
            }
            if (_ad != null && !_ad.CanShowAd())
            {
                _ad.Destroy(); // expired (an ad keeps about an hour)
                _ad = null;
            }
            if (_ad == null)
            {
                var loaded = Load();
                if (await Task.WhenAny(loaded, Task.Delay(LoadWaitMs)) != loaded || !loaded.Result) return false;
            }
            var ad = _ad;
            _ad = null;
            var done = new TaskCompletionSource<bool>();
            bool rewarded = false;
            ad.OnAdFullScreenContentClosed += () => OnMain(() => Finish(ad, done, rewarded));
            ad.OnAdFullScreenContentFailed += error => OnMain(() =>
            {
                Debug.LogWarning("[Ads] Show: " + error?.GetMessage());
                Finish(ad, done, false);
            });
            ad.Show(_ => OnMain(() => rewarded = true));
            return await done.Task;
        }

        /// <summary>Consent first (a previous answer is kept by Google), then the SDK. False when ads may not be requested.</summary>
        Task<bool> Start()
        {
            _main = SynchronizationContext.Current;
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            bool minor = App.GameApp.I != null && App.GameApp.I.Privacy.Data.IsMinor;
            var started = new TaskCompletionSource<bool>();
            ConsentInformation.Update(new ConsentRequestParameters { TagForUnderAgeOfConsent = minor }, error => OnMain(() =>
            {
                if (error != null) Debug.LogWarning("[Ads] Consent: " + error.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => OnMain(() =>
                {
                    _consentAsked = true;
                    if (formError != null) Debug.LogWarning("[Ads] Consent form: " + formError.Message);
                    if (!ConsentInformation.CanRequestAds()) { started.TrySetResult(false); return; }
                    MobileAds.SetRequestConfiguration(new RequestConfiguration
                    {
                        TagForUnderAgeOfConsent = minor ? TagForUnderAgeOfConsent.True : TagForUnderAgeOfConsent.Unspecified,
                    });
                    MobileAds.Initialize(_ => OnMain(() => started.TrySetResult(true)));
                }));
            }));
            return started.Task;
        }

        /// <summary>Loads the next ad (once at a time); true when one is ready.</summary>
        Task<bool> Load()
        {
            if (_ad != null) return Task.FromResult(true);
            if (_loading != null) return _loading.Task;
            var loading = _loading = new TaskCompletionSource<bool>();
            RewardedAd.Load(Unit, new AdRequest(), (ad, error) => OnMain(() =>
            {
                _loading = null;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("[Ads] Load: " + error?.GetMessage());
                    loading.TrySetResult(false);
                    return;
                }
                _ad = ad;
                loading.TrySetResult(true);
            }));
            return loading.Task;
        }

        /// <summary>One ad is shown once: the next one loads right away, ready for the next request.</summary>
        void Finish(RewardedAd ad, TaskCompletionSource<bool> done, bool rewarded)
        {
            ad.Destroy();
            done.TrySetResult(rewarded);
            _ = Load();
        }

        /// <summary>The SDK may answer from its own threads: Unity is only touched from the main one.</summary>
        void OnMain(Action action)
        {
            if (_main == null || SynchronizationContext.Current == _main) action();
            else _main.Post(_ => action(), null);
        }
    }
}
