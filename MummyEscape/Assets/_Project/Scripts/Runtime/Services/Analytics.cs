using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>Where the analytics SDK plugs in (its own assembly, compiled only when the package is there).</summary>
    public interface IAnalyticsProvider
    {
        /// <summary>Starts (true) or stops (false) the collection; the services must be initialised first.</summary>
        void SetEnabled(bool on);
        void Event(string name, Dictionary<string, object> fields);
        /// <summary>Asks the provider to erase what it holds about this player.</summary>
        Task DeleteAsync();
    }

    /// <summary>
    /// Usage statistics, only with the player's consent (Confidentialité › Statistiques d'usage) and only in online mode:
    /// which tombs are played, won or lost, what is bought, which ads are watched. No advertising identifier, nothing
    /// before the consent; the events are dropped when the player has not agreed.
    /// </summary>
    public static class Analytics
    {
        public static IAnalyticsProvider Provider;
        static bool _on;

        /// <summary>Follows the consent (called once the online services are up, and whenever the choice changes).</summary>
        public static void Apply(bool consent)
        {
            if (Provider == null || _on == consent) return;
            _on = consent;
            try { Provider.SetEnabled(consent); }
            catch (System.Exception e) { Debug.LogWarning("[Analytics] " + e.Message); }
        }

        public static void Track(string name, params (string key, object value)[] fields)
        {
            if (!_on || Provider == null) return;
            var dict = new Dictionary<string, object>(fields.Length);
            foreach (var (key, value) in fields) dict[key] = value;
            try { Provider.Event(name, dict); }
            catch (System.Exception e) { Debug.LogWarning("[Analytics] " + e.Message); }
        }

        public static Task DeleteAsync() => Provider?.DeleteAsync() ?? Task.CompletedTask;
    }
}
