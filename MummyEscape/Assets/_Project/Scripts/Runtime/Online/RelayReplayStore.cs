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
            Normalize(copy);
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

        /// <summary>
        /// The server's history (<see cref="IPvpService.GetRelayHistoryAsync"/>): each judged match replaces the phone's copy
        /// (both relays as the server replayed them), or comes in if the phone never had it (played on another device).
        /// </summary>
        public static void Merge(IEnumerable<RelayMatch> matches, string me)
        {
            if (matches == null || string.IsNullOrEmpty(me)) return;
            Load();
            bool changed = false;
            foreach (var m in matches)
            {
                var mine = m?.SideOf(me);
                if (mine == null || !m.Settled || m.A == null || m.B == null) continue;
                var known = _recent.Find(r => r.Id == m.Id) ?? _saved.Find(r => r.Id == m.Id);
                var record = known ?? new RelayRecord { Me = me, PlayedAtUnixMs = m.CreatedAtUnixMs };
                if (known != null && known.Resolved && known.Match?.Settled == true && known.Match.A?.Inputs != null && known.Match.B?.Inputs != null) continue;
                record.Match = m;
                record.Resolved = true;
                record.Result = mine == m.A ? m.Result : DuelResolver.Invert(m.Result);
                record.EloDelta = mine == m.A ? m.EloDeltaA : m.EloDeltaB;
                _recent.RemoveAll(r => r.Id == m.Id);
                _recent.Add(record);
                int kept = _saved.FindIndex(r => r.Id == m.Id);
                if (kept >= 0) _saved[kept] = record;
                changed = true;
            }
            if (!changed) return;
            _recent.Sort((a, b) => b.PlayedAtUnixMs.CompareTo(a.PlayedAtUnixMs));
            if (_recent.Count > PvpConfig.HistorySize) _recent.RemoveRange(PvpConfig.HistorySize, _recent.Count - PvpConfig.HistorySize);
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
                {
                    Normalize(r.Match);
                    into.Add(r);
                }
        }

        /// <summary>JsonUtility reads a missing object as an empty one and a missing list as null: puts things back.</summary>
        static void Normalize(RelayMatch m)
        {
            foreach (var side in new[] { m.A, m.B })
            {
                side.Runners ??= new List<RelayRunner>();
                side.Runners.RemoveAll(r => r == null || string.IsNullOrEmpty(r.PlayerId));
                foreach (var r in side.Runners)
                    if (r.Look != null && string.IsNullOrEmpty(r.Look.Mummy)) r.Look = null;
                side.Inputs ??= new List<RelayInput>();
                side.Quitters ??= new List<string>();
                if (string.IsNullOrEmpty(side.Starter)) side.Starter = null;
            }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new Stored { Recent = _recent, Saved = _saved })); }
            catch (Exception e) { Debug.LogWarning("[Pvp] 2v2 replays not saved: " + e.Message); }
        }
    }
}
