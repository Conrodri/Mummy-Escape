using System;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>Where the phone's notification system plugs in (its own assembly, like the ads).</summary>
    public interface INotificationProvider
    {
        /// <summary>Asks the system's permission (Android 13+ shows a dialog once).</summary>
        void RequestPermission();
        /// <summary>A local notification at <paramref name="atUtc"/>; nothing when it is in the past.</summary>
        void Schedule(string title, string body, DateTime atUtc);
        void CancelAll();
    }

    /// <summary>
    /// Local notifications, planned when the game goes to the background (Settings › Notifications): the solo energy full
    /// again, and the Halloween event opening. Nothing is sent from a server.
    /// </summary>
    public static class Notifications
    {
        public static INotificationProvider Provider;

        const string AskedKey = "notif_asked";

        /// <summary>The permission is asked once, after the first stars: a player who has played wants to come back.</summary>
        public static void MaybeAsk(GameApp app)
        {
            if (Provider == null || PlayerPrefs.GetInt(AskedKey, 0) == 1 || app.Save.TotalStars < 3) return;
            PlayerPrefs.SetInt(AskedKey, 1);
            PlayerPrefs.Save();
            Provider.RequestPermission();
        }

        /// <summary>Asked again from the settings when a toggle goes on (a refusal is remembered by the system, not by us).</summary>
        public static void Ask()
        {
            PlayerPrefs.SetInt(AskedKey, 1);
            Provider?.RequestPermission();
        }

        /// <summary>Replaces every planned notification by those the settings allow, from the state of now.</summary>
        public static void Plan(GameApp app)
        {
            if (Provider == null) return;
            Provider.CancelAll();
            var s = app.Settings;
            var now = DateTime.UtcNow;
            if (s.NotifyEnergy && !app.Save.Unlimited)
            {
                int missing = EnergyConfig.SoloMax - app.Save.SoloEnergyLeft;
                if (missing > 0)
                {
                    long fullMs = app.Save.SoloEnergyNextInMs + (missing - 1) * EnergyConfig.RegenMs;
                    Provider.Schedule(Loc.T("Énergie pleine"), Loc.T("Ta momie a repris des forces : les tombeaux t'attendent."), now.AddMilliseconds(fullMs));
                }
            }
            if (s.NotifyEvents && !Halloween.IsOpen(now))
            {
                var opens = Halloween.NextOpening(now);
                if ((opens - now).TotalDays <= 45)
                    Provider.Schedule(Loc.T("Halloween commence"), Loc.T("L'acte Halloween est ouvert : 10 tombeaux et une citrouille à gagner."), opens);
            }
        }
    }
}
