using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MummyEscape.Services
{
    /// <summary>
    /// Keeps the start-of-run map from being captured (it would defeat the memory challenge).
    /// <list type="bullet">
    /// <item>Android: FLAG_SECURE on the activity window while armed. Screenshots and recordings come out black, and
    /// the app is blanked in the recent apps list.</item>
    /// <item>iOS: no API blocks a screenshot, so the guard detects it (the screenshot notification) and the game
    /// throws the tomb away and draws a new one. Screen recording / mirroring (<c>UIScreen.isCaptured</c>) is detected
    /// live, and the map stays hidden while it lasts.</item>
    /// </list>
    /// The editor and desktop builds get no protection (no API); <see cref="SimulateScreenshot"/> lets tools test the flow.
    /// </summary>
    public sealed class ScreenGuard : MonoBehaviour
    {
        /// <summary>A screenshot was taken while the guard was armed (iOS; Android blocks it instead).</summary>
        public event Action ScreenshotTaken;

        /// <summary>The screen is being recorded or mirrored right now (iOS).</summary>
        public bool IsCaptured { get; private set; }

        public bool Armed { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _MummyGuard_Start();
        [DllImport("__Internal")] static extern int _MummyGuard_ScreenshotCount();
        [DllImport("__Internal")] static extern bool _MummyGuard_IsCaptured();
        int _lastCount;
        bool _started;
#endif

        public void Arm(bool on)
        {
            if (Armed == on) return;
            Armed = on;
#if UNITY_ANDROID && !UNITY_EDITOR
            SetAndroidSecure(on);
#elif UNITY_IOS && !UNITY_EDITOR
            if (!_started) { _MummyGuard_Start(); _started = true; }
            _lastCount = _MummyGuard_ScreenshotCount();
#endif
            if (!on) IsCaptured = false;
        }

        /// <summary>Test hook (editor tools): behaves as if the player had just taken a screenshot.</summary>
        public void SimulateScreenshot()
        {
            if (Armed) ScreenshotTaken?.Invoke();
        }

        /// <summary>Test hook (editor tools): behaves as if a screen recording had started or stopped.</summary>
        public void SimulateCapture(bool on) => IsCaptured = Armed && on;

#if UNITY_IOS && !UNITY_EDITOR
        void Update()
        {
            if (!Armed) return;
            IsCaptured = _MummyGuard_IsCaptured();
            int count = _MummyGuard_ScreenshotCount();
            if (count != _lastCount)
            {
                _lastCount = count;
                ScreenshotTaken?.Invoke();
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        const int FlagSecure = 0x00002000; // WindowManager.LayoutParams.FLAG_SECURE

        static void SetAndroidSecure(bool on)
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    // Window flags must be changed on the UI thread.
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        {
                            if (on) window.Call("addFlags", FlagSecure);
                            else window.Call("clearFlags", FlagSecure);
                        }
                    }));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ScreenGuard] FLAG_SECURE unavailable: {e.Message}");
            }
        }
#endif

        void OnDestroy()
        {
            if (Armed) Arm(false);
        }
    }
}
