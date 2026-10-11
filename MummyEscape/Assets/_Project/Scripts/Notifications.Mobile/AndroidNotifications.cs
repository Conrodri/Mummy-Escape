using System;
using MummyEscape.Services;
using Unity.Notifications.Android;
using UnityEngine;

namespace MummyEscape.Notifications.Mobile
{
    /// <summary>Local notifications on Android through com.unity.mobile.notifications (one channel, "Mummy Rush").</summary>
    public sealed class AndroidNotifications : INotificationProvider
    {
        const string Channel = "mummy_rush";
        const string Permission = "android.permission.POST_NOTIFICATIONS";

        AndroidNotifications()
        {
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(Channel, "Mummy Rush", "Énergie et événements", Importance.Default));
        }

        public void RequestPermission()
        {
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission)) UnityEngine.Android.Permission.RequestUserPermission(Permission);
        }

        public void Schedule(string title, string body, DateTime atUtc)
        {
            if (atUtc <= DateTime.UtcNow) return;
            AndroidNotificationCenter.SendNotification(new AndroidNotification(title, body, atUtc.ToLocalTime()), Channel);
        }

        public void CancelAll() => AndroidNotificationCenter.CancelAllScheduledNotifications();

#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Services.Notifications.Provider = new AndroidNotifications();
#endif
    }
}
