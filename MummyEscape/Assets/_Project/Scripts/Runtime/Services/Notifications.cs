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
    /// again, the Halloween event opening and the daily quests left. Nothing is sent from a server.
    /// </summary>
    public static class Notifications
    {
        public static INotificationProvider Provider;

        const string AskedKey = "notif_asked";
        const int QuestReminderDays = 3;

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
            if (s.NotifyQuests && app.Quests.Available) PlanQuests(app.Quests.Book, now);
        }

        /// <summary>
        /// 18:00 (phone time) on the next days the game is not opened: the daily quests left, or on Sunday the last day
        /// for the weekly quest when it needs only today. Today is skipped once every daily quest is done.
        /// </summary>
        static void PlanQuests(QuestBook book, DateTime now)
        {
            for (int d = 0; d < QuestReminderDays; d++)
            {
                var at = DateTime.Now.Date.AddDays(d).AddHours(18).ToUniversalTime();
                if (at <= now) continue;
                bool today = Quests.DayOf(at) == book.Day;
                int left = 0;
                foreach (var q in Quests.Today(Quests.DayOf(at))) if (!today || !Quests.IsDone(book, q)) left++;
                if (left == 0) continue;
                bool sameWeek = Quests.WeekOf(at) == book.Week;
                int days = sameWeek ? Quests.CountOf(book, Quests.Weekly) : 0;
                if (at.ToLocalTime().DayOfWeek == DayOfWeek.Sunday && days == Quests.WeeklyDays - 1)
                    Provider.Schedule(Loc.T("Dernier jour"), Loc.F("Termine tes quêtes du jour aujourd'hui pour gagner {0} scarabées dorés.", Quests.Weekly.Amount), at);
                else
                    Provider.Schedule(Loc.T("Quêtes du jour"), Loc.P(left, "Il te reste {0} quête du jour à terminer.", "Il te reste {0} quêtes du jour à terminer."), at);
            }
        }
    }
}
