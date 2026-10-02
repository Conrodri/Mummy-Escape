using System;
using System.Collections.Generic;
using System.IO;
using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;

namespace MummyEscape.Services
{
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>2 = records compare runs by moves above the optimal route (every run is a new maze).</summary>
        public int Version = 2;
        public List<LevelRecord> Records = new List<LevelRecord>();
        public int Coins;
        public List<string> OwnedSkins = new List<string> { SkinCatalog.DefaultSkinId };
        public string SelectedSkin = SkinCatalog.DefaultSkinId;
        /// <summary>Country shown in the rankings (ISO alpha-2). Empty = detect from the device.</summary>
        public string Country = "";
    }

    public struct RecordOutcome
    {
        public bool NewBest;
        public int CoinsEarned;
        /// <summary>Previous best gap to the optimal route, -1 when there was none.</summary>
        public int PreviousBestOverPar;
        /// <summary>Play time of that previous best (0 = unknown).</summary>
        public int PreviousBestTimeMs;
    }

    /// <summary>Local progression (JSON in persistentDataPath). The online layer mirrors it but is never required.</summary>
    public sealed class SaveService
    {
        public SaveData Data { get; private set; } = new SaveData();
        public event Action Changed;

        static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath)) ?? new SaveData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Corrupted save, starting fresh: {e.Message}");
                Data = new SaveData();
            }
            if (Data.OwnedSkins.Count == 0) Data.OwnedSkins.Add(SkinCatalog.DefaultSkinId);
            if (Data.Version < 2)
            {
                // v1 records counted raw moves on a fixed maze: only a 3-star run is known to be perfect.
                foreach (var r in Data.Records) r.BestOverPar = r.BestStars >= 3 ? 0 : -1;
                Data.Version = 2;
            }
        }

        public void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not write save: {e.Message}");
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Folds in the save of another device (account cloud copy): best record per level, owned skins united,
        /// the larger wallet (never the sum: syncing twice must not mint scarabs). Returns true when something changed.
        /// </summary>
        public bool MergeFrom(SaveData other)
        {
            if (other == null) return false;
            string before = JsonUtility.ToJson(Data);
            foreach (var theirs in other.Records)
            {
                if (theirs == null || string.IsNullOrEmpty(theirs.Key)) continue;
                var mine = Data.Records.Find(r => r.Key == theirs.Key);
                if (mine == null) Data.Records.Add(JsonUtility.FromJson<LevelRecord>(JsonUtility.ToJson(theirs)));
                else mine.MergeWith(theirs);
            }
            Data.Coins = Math.Max(Data.Coins, other.Coins);
            foreach (var s in other.OwnedSkins) if (!Data.OwnedSkins.Contains(s)) Data.OwnedSkins.Add(s);
            if (string.IsNullOrEmpty(Data.Country)) Data.Country = other.Country ?? "";
            bool changed = JsonUtility.ToJson(Data) != before;
            if (changed) Save();
            return changed;
        }

        /// <summary>Erases the local progression (the file and the memory copy).</summary>
        public void Wipe()
        {
            Data = new SaveData();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[Save] {e.Message}"); }
            Changed?.Invoke();
        }

        public LevelRecord GetRecord(LevelId id) => Data.Records.Find(r => r.Key == id.Key);

        LevelRecord GetOrCreateRecord(LevelId id)
        {
            var rec = GetRecord(id);
            if (rec == null)
            {
                rec = new LevelRecord { Key = id.Key };
                Data.Records.Add(rec);
            }
            return rec;
        }

        /// <summary>Maze number the next run of this level will play (without consuming it).</summary>
        public int PeekVariant(LevelId id) => GetRecord(id)?.Runs ?? 0;

        /// <summary>Consumes a maze number: every run (first try, replay, retry after death) gets a new tomb.</summary>
        public int NextVariant(LevelId id)
        {
            var rec = GetOrCreateRecord(id);
            int variant = rec.Runs++;
            Save();
            return variant;
        }

        public bool IsCompleted(LevelId id) => GetRecord(id)?.Completions > 0;

        public bool IsUnlocked(LevelId id) => Progression.IsUnlocked(id, IsCompleted);

        public int TotalStars => Progression.TotalStars(Data.Records);

        public RecordOutcome Apply(LevelResult result)
        {
            var rec = GetRecord(result.Level);
            if (rec == null)
            {
                rec = new LevelRecord { Key = result.Level.Key };
                Data.Records.Add(rec);
            }
            int previousStars = rec.BestStars;
            int previousBest = rec.HasBest ? rec.BestOverPar : -1;
            int previousTime = rec.HasBest ? rec.BestTimeMs : 0;
            bool improved = rec.Merge(result);
            int coins = Progression.CoinsFor(result, previousStars);
            Data.Coins += coins;
            Save();
            return new RecordOutcome { NewBest = improved && result.Won, CoinsEarned = coins, PreviousBestOverPar = previousBest, PreviousBestTimeMs = previousTime };
        }

        public bool TryBuySkin(string id, int price)
        {
            if (Data.OwnedSkins.Contains(id) || Data.Coins < price) return false;
            Data.Coins -= price;
            Data.OwnedSkins.Add(id);
            Save();
            return true;
        }

        public void SelectSkin(string id)
        {
            if (!Data.OwnedSkins.Contains(id)) return;
            Data.SelectedSkin = id;
            Save();
        }

        /// <summary>Stored in <see cref="SaveData.Country"/> when the player asks to use the device region.</summary>
        public const string AutoCountry = "auto";

        /// <summary>
        /// Country attached to the player's public scores. Empty unless the player chose one (privacy by default):
        /// a code, or <see cref="AutoCountry"/> for the device region.
        /// </summary>
        public string Country => Data.Country == AutoCountry ? CountryService.Detect() : Data.Country ?? "";

        public void SetCountry(string code)
        {
            Data.Country = code ?? "";
            Save();
        }

        /// <summary>Highest unlocked level, used to resume quickly from the main menu.</summary>
        public LevelId FurthestUnlocked()
        {
            var last = new LevelId(1, 1);
            foreach (var id in DifficultyTable.AllLevels())
            {
                if (!IsUnlocked(id)) break;
                last = id;
            }
            return last;
        }
    }
}
