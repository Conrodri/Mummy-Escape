using System;
using System.Collections.Generic;
using System.IO;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// The last duels kept on the device (<see cref="PvpConfig.HistorySize"/>, the oldest replaced by new games), so
    /// they can be watched again without a connection. A duel is saved as soon as it is played; the server's copy
    /// (<see cref="IPvpService.GetHistoryAsync"/>) then replaces it, which completes a duel run first on its tomb once
    /// someone has raced its ghost.
    /// </summary>
    public static class ReplayStore
    {
        [Serializable]
        sealed class Stored
        {
            public List<DuelRecord> Duels = new List<DuelRecord>();
        }

        static List<DuelRecord> _duels;

        static string FilePath => Path.Combine(Application.persistentDataPath, "duel_replays.json"); // noloc

        /// <summary>From the most recent to the oldest.</summary>
        public static IReadOnlyList<DuelRecord> Duels
        {
            get
            {
                Load();
                return _duels;
            }
        }

        public static DuelRecord Find(string matchId)
        {
            Load();
            return _duels.Find(d => d.MatchId == matchId);
        }

        /// <summary>Adds or replaces duels (by match id) and keeps the latest ones.</summary>
        public static void Merge(IEnumerable<DuelRecord> duels)
        {
            if (duels == null) return;
            Load();
            foreach (var d in duels)
            {
                if (d == null || string.IsNullOrEmpty(d.MatchId)) continue;
                Normalize(d);
                var known = _duels.Find(x => x.MatchId == d.MatchId);
                if (known != null)
                {
                    d.Reported |= known.Reported;
                    // A local copy may be complete while the server's answer was read before the duel resolved.
                    if (known.Resolved && !d.Resolved) continue;
                }
                PvpServer.Remember(_duels, d);
            }
            Save();
        }

        public static void Add(DuelRecord duel) => Merge(new[] { duel });

        public static void MarkReported(string matchId)
        {
            var d = Find(matchId);
            if (d == null) return;
            d.Reported = true;
            Save();
        }

        /// <summary>JsonUtility reads a missing object as an empty one: puts the nulls back.</summary>
        public static void Normalize(DuelRecord d)
        {
            if (d.Rival != null && string.IsNullOrEmpty(d.Rival.PlayerId) && (d.Rival.Inputs == null || d.Rival.Inputs.Count == 0)) d.Rival = null;
            Fix(d.Me);
            Fix(d.Rival);
        }

        static void Fix(DuelRun run)
        {
            if (run == null) return;
            if (run.Inputs == null) run.Inputs = new List<RunInput>();
            if (run.Look != null && string.IsNullOrEmpty(run.Look.Mummy)) run.Look = null;
        }

        static void Load()
        {
            if (_duels != null) return;
            _duels = new List<DuelRecord>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var stored = JsonUtility.FromJson<Stored>(File.ReadAllText(FilePath));
                if (stored?.Duels == null) return;
                foreach (var d in stored.Duels)
                    if (d != null && !string.IsNullOrEmpty(d.MatchId))
                    {
                        Normalize(d);
                        _duels.Add(d);
                    }
            }
            catch (Exception e) { Debug.LogWarning("[Pvp] Unreadable replays: " + e.Message); }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new Stored { Duels = _duels })); }
            catch (Exception e) { Debug.LogWarning("[Pvp] Replays not saved: " + e.Message); }
        }
    }
}
