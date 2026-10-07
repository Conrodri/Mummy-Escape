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
    /// The player can also keep up to <see cref="SavedSize"/> duels apart: those stay until the player removes or
    /// replaces them, whatever is played since.
    /// </summary>
    public static class ReplayStore
    {
        public const int SavedSize = 10;

        [Serializable]
        sealed class Stored
        {
            public List<DuelRecord> Duels = new List<DuelRecord>();
            public List<DuelRecord> Saved = new List<DuelRecord>();
        }

        static List<DuelRecord> _duels;
        static List<DuelRecord> _saved;

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

        /// <summary>The duels kept apart, from the most recent to the oldest.</summary>
        public static IReadOnlyList<DuelRecord> Saved
        {
            get
            {
                Load();
                return _saved;
            }
        }

        public static bool IsSaved(string matchId)
        {
            Load();
            return _saved.Exists(d => d.MatchId == matchId);
        }

        /// <summary>
        /// Keeps <paramref name="duel"/> apart. Once <see cref="SavedSize"/> are kept, one must make room:
        /// <paramref name="replaceMatchId"/> names it, and without it nothing is saved (false).
        /// </summary>
        public static bool Keep(DuelRecord duel, string replaceMatchId = null)
        {
            if (duel == null || string.IsNullOrEmpty(duel.MatchId)) return false;
            Load();
            if (_saved.Exists(d => d.MatchId == duel.MatchId)) return true;
            if (replaceMatchId != null) _saved.RemoveAll(d => d.MatchId == replaceMatchId);
            if (_saved.Count >= SavedSize) return false;
            _saved.Add(duel);
            SortSaved();
            Save();
            return true;
        }

        public static void Forget(string matchId)
        {
            Load();
            if (_saved.RemoveAll(d => d.MatchId == matchId) > 0) Save();
        }

        public static DuelRecord Find(string matchId)
        {
            Load();
            return _duels.Find(d => d.MatchId == matchId) ?? _saved.Find(d => d.MatchId == matchId);
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
                var known = Find(d.MatchId);
                if (known != null)
                {
                    d.Reported |= known.Reported;
                    // A local copy may be complete while the server's answer was read before the duel resolved.
                    if (known.Resolved && !d.Resolved) continue;
                }
                PvpServer.Remember(_duels, d);
                // A kept duel follows the server too (the rival arrives once someone has raced its ghost).
                int kept = _saved.FindIndex(x => x.MatchId == d.MatchId);
                if (kept >= 0) _saved[kept] = d;
            }
            Save();
        }

        public static void Add(DuelRecord duel) => Merge(new[] { duel });

        public static void MarkReported(string matchId)
        {
            Load();
            foreach (var d in _duels) if (d.MatchId == matchId) d.Reported = true;
            foreach (var d in _saved) if (d.MatchId == matchId) d.Reported = true;
            Save();
        }

        /// <summary>JsonUtility reads a missing object as an empty one: puts the nulls back.</summary>
        /// <summary>Duels have been live since this day: the records saved before the flag existed get it back.</summary>
        const long LiveSinceUnixMs = 1791244800000L; // 2026-10-06 00:00 UTC

        public static void Normalize(DuelRecord d)
        {
            if (!d.Live && d.PlayedAtUnixMs >= LiveSinceUnixMs) d.Live = true;
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

        static void SortSaved() => _saved.Sort((a, b) => b.PlayedAtUnixMs.CompareTo(a.PlayedAtUnixMs));

        static void Load()
        {
            if (_duels != null) return;
            _duels = new List<DuelRecord>();
            _saved = new List<DuelRecord>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var stored = JsonUtility.FromJson<Stored>(File.ReadAllText(FilePath));
                if (stored == null) return;
                Read(stored.Duels, _duels);
                Read(stored.Saved, _saved);
                SortSaved();
            }
            catch (Exception e) { Debug.LogWarning("[Pvp] Unreadable replays: " + e.Message); }
        }

        static void Read(List<DuelRecord> from, List<DuelRecord> into)
        {
            if (from == null) return;
            foreach (var d in from)
                if (d != null && !string.IsNullOrEmpty(d.MatchId) && !into.Exists(x => x.MatchId == d.MatchId))
                {
                    Normalize(d);
                    into.Add(d);
                }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new Stored { Duels = _duels, Saved = _saved })); }
            catch (Exception e) { Debug.LogWarning("[Pvp] Replays not saved: " + e.Message); }
        }
    }
}
