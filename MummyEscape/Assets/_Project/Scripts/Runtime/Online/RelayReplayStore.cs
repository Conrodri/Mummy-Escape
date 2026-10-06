using System;
using System.Collections.Generic;
using System.IO;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>A 2v2 match as the phone keeps it to watch again: both relays, and the verdict once the server gave it.</summary>
    [Serializable]
    public sealed class RelayRecord
    {
        public string Me;
        public RelayMatch Match;
        public long PlayedAtUnixMs;
        /// <summary>The server's verdict is in (otherwise <see cref="Result"/> is the phone's).</summary>
        public bool Resolved;
        /// <summary>For the player's duo.</summary>
        public DuelResult Result;
        public int EloDelta;
        public int NewElo;

        public string Id => Match?.Id;
        public RelaySide Mine => Match?.SideOf(Me);
        public RelaySide Rival => Match?.OtherSide(Me);
    }

    /// <summary>
    /// The last 2v2 matches kept on the device (<see cref="PvpConfig.HistorySize"/>, the oldest replaced by new ones), and
    /// up to <see cref="SavedSize"/> kept apart until the player removes them. Recorded when a match ends, completed
    /// with the server's copy (both relays as it replayed them) when the verdict arrives.
    /// </summary>
    public static class RelayReplayStore
    {
        public const int SavedSize = 10;

        [Serializable]
        sealed class Stored
        {
            public List<RelayRecord> Recent = new List<RelayRecord>();
            public List<RelayRecord> Saved = new List<RelayRecord>();
        }

        static List<RelayRecord> _recent;
        static List<RelayRecord> _saved;

        static string FilePath => Path.Combine(Application.persistentDataPath, "relay_replays.json"); // noloc

        /// <summary>From the most recent to the oldest.</summary>
        public static IReadOnlyList<RelayRecord> Recent
        {
            get
            {
                Load();
                return _recent;
            }
        }

        /// <summary>The matches kept apart, from the most recent to the oldest.</summary>
        public static IReadOnlyList<RelayRecord> Saved
        {
            get
            {
                Load();
                return _saved;
            }
        }

        /// <summary>A match as it just ended on the phone: the duo's relay, and the rival duo's as it was received.</summary>
        public static RelayRecord Of(RelayMatch match, string me, string starter, List<RelayInput> mine, List<RelayInput> rival, DuelResult result)
        {
            // A copy: the live match object stays as the game left it.
            var copy = JsonUtility.FromJson<RelayMatch>(JsonUtility.ToJson(match));
            var mySide = copy.SideOf(me);
            var rivalSide = copy.OtherSide(me);
            if (mySide != null)
            {
                mySide.Inputs = mine ?? new List<RelayInput>();
                if (!string.IsNullOrEmpty(starter)) mySide.Starter = starter;
            }
            if (rivalSide != null && (rivalSide.Inputs == null || rivalSide.Inputs.Count == 0)) rivalSide.Inputs = rival ?? new List<RelayInput>();
            return new RelayRecord
            {
                Me = me, Match = copy, PlayedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Result = result,
            };
        }

        public static bool IsSaved(string id)
        {
            Load();
            return _saved.Exists(r => r.Id == id);
        }

        /// <summary>Adds a match, or replaces it (by id) in both lists, and keeps the latest ones.</summary>
        public static void Put(RelayRecord record)
        {
            if (record?.Match == null || string.IsNullOrEmpty(record.Id)) return;
            Load();
            _recent.RemoveAll(r => r.Id == record.Id);
            _recent.Insert(0, record);
            _recent.Sort((a, b) => b.PlayedAtUnixMs.CompareTo(a.PlayedAtUnixMs));
            if (_recent.Count > PvpConfig.HistorySize) _recent.RemoveRange(PvpConfig.HistorySize, _recent.Count - PvpConfig.HistorySize);
            int kept = _saved.FindIndex(r => r.Id == record.Id);
            if (kept >= 0) _saved[kept] = record;
            Save();
        }

        /// <summary>Keeps a match apart; false when the <see cref="SavedSize"/> places are taken.</summary>
        public static bool Keep(RelayRecord record)
        {
            if (record?.Match == null) return false;
            Load();
            if (_saved.Exists(r => r.Id == record.Id)) return true;
            if (_saved.Count >= SavedSize) return false;
            _saved.Add(record);
            _saved.Sort((a, b) => b.PlayedAtUnixMs.CompareTo(a.PlayedAtUnixMs));
            Save();
            return true;
        }

        public static void Forget(string id)
        {
            Load();
            if (_saved.RemoveAll(r => r.Id == id) > 0) Save();
        }

        static void Load()
        {
            if (_recent != null) return;
            _recent = new List<RelayRecord>();
            _saved = new List<RelayRecord>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var stored = JsonUtility.FromJson<Stored>(File.ReadAllText(FilePath));
                if (stored == null) return;
                Read(stored.Recent, _recent);
                Read(stored.Saved, _saved);
            }
            catch (Exception e) { Debug.LogWarning("[Pvp] Unreadable 2v2 replays: " + e.Message); }
        }

        static void Read(List<RelayRecord> from, List<RelayRecord> into)
        {
            if (from == null) return;
            foreach (var r in from)
                if (r?.Match?.A != null && r.Match.B != null && !string.IsNullOrEmpty(r.Id) && !into.Exists(x => x.Id == r.Id))
                    into.Add(r);
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new Stored { Recent = _recent, Saved = _saved })); }
            catch (Exception e) { Debug.LogWarning("[Pvp] 2v2 replays not saved: " + e.Message); }
        }
    }
}
