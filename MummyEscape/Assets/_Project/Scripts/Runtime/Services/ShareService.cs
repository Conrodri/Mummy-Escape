using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MummyEscape.Services
{
    /// <summary>Opens the native share sheet (Android intent chooser / iOS UIActivityViewController).</summary>
    public sealed class ShareService
    {
        /// <summary>Store link appended to shared results. Replace with the real store URL before release.</summary>
        public const string GameUrl = "https://play.google.com/store/apps/details?id=com.mummyescape.game";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _MummyShareText(string text);
#endif

        public void ShareText(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var intentClass = new AndroidJavaClass("android.content.Intent"))
            using (var intent = new AndroidJavaObject("android.content.Intent"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, Loc.T("Partager mon évasion")))
                    activity.Call("startActivity", chooser);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            _MummyShareText(text);
#else
            GUIUtility.systemCopyBuffer = text;
            Debug.Log($"[Share] (editor) copied to clipboard:\n{text}");
#endif
        }
    }
}
