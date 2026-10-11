using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Services;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Unity Analytics behind <see cref="Analytics"/>: collection starts only on consent (the services are initialised by
    /// the online service before <see cref="SetEnabled"/> is called with true) and every event is a custom event.
    /// </summary>
    public sealed class UgsAnalytics : IAnalyticsProvider
    {
        public void SetEnabled(bool on)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) return;
            if (on) AnalyticsService.Instance.StartDataCollection();
            else AnalyticsService.Instance.StopDataCollection();
        }

        public void Event(string name, Dictionary<string, object> fields)
        {
            var e = new CustomEvent(name);
            foreach (var kv in fields) e.Add(kv.Key, kv.Value);
            AnalyticsService.Instance.RecordEvent(e);
        }

        public Task DeleteAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) AnalyticsService.Instance.RequestDataDeletion();
            return Task.CompletedTask;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Services.Analytics.Provider = new UgsAnalytics();
    }
}
