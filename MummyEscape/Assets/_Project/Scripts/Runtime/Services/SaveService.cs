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
        public int Version = 1;
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
        public int PreviousBestMoves;
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

        public LevelRecord GetRecord(LevelId id) => Data.Records.Find(r => r.Key == id.Key);

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
            int previousBest = rec.BestMoves;
            bool improved = rec.Merge(result);
            int coins = Progression.CoinsFor(result, previousStars);
            Data.Coins += coins;
            Save();
            return new RecordOutcome { NewBest = improved && result.Won, CoinsEarned = coins, PreviousBestMoves = previousBest };
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

        /// <summary>Chosen country, or the device region when the player never picked one.</summary>
        public string Country => string.IsNullOrEmpty(Data.Country) ? CountryService.Detect() : Data.Country;

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
