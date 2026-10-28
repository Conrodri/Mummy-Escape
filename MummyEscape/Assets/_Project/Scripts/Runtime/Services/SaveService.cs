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
        /// <summary>
        /// 2 = records compare runs by moves above the optimal route (every run is a new maze). 3 = the campaign in three
        /// modes (Facile, Normal, Extrême) of 5 levels per act: the progress of the old campaign was reset. 4 = generator
        /// v13, 10 levels per act and lighter modes: the same keys hold other tombs, progress was reset again.
        /// </summary>
        public const int CurrentVersion = 4;
        public int Version = CurrentVersion;
        public List<LevelRecord> Records = new List<LevelRecord>();
        public int Coins;
        /// <summary>The server holds the scarabs (carried over once): <see cref="Coins"/> is then its balance plus the runs it has not replayed.</summary>
        public bool ScarabsHeld;
        /// <summary>Every shop item owned, whatever its slot (mummy, colour, torch, hat, shoes).</summary>
        public List<string> OwnedSkins = new List<string>(SkinCatalog.Defaults);
        /// <summary>Selected colour (the name predates the other slots, kept for old saves).</summary>
        public string SelectedSkin = SkinCatalog.DefaultSkinId;
        public string SelectedMummy = SkinCatalog.DefaultMummyId;
        public string SelectedTorch = SkinCatalog.DefaultTorchId;
        public string SelectedHat = SkinCatalog.NoHatId;
        public string SelectedShoes = SkinCatalog.NoShoesId;
        /// <summary>Title shown under the player's name (<see cref="Pvp.Titles"/>), empty for none.</summary>
        public string SelectedTitle = "";
        /// <summary>Turns of the casino's scarab wheel (statistics).</summary>
        public int WheelSpins;
        /// <summary>Country shown in the rankings (ISO alpha-2). Empty = detect from the device.</summary>
        public string Country = "";
        /// <summary>The name and country were chosen at the first online connection (<see cref="UI.Screens.ProfileSetupScreen"/>).</summary>
        public bool ProfileDone;
        /// <summary>Act intros (Easy) walked or skipped, bit (act - 1).</summary>
        public int IntrosDone;
        /// <summary>Features (Core.Feature bitmask) already celebrated on the home screen.</summary>
        public int SeenUnlocks;
        /// <summary>Best scores the leaderboards confirmed ("level_f1_1:score"): any other record is sent again when online.</summary>
        public List<string> SubmittedScores = new List<string>();

        // ---- Real-money economy (see Monetization): golden scarabs, solo energy, season pass.
        /// <summary>Golden scarabs: the copy of the wallet the PvP server keeps (bought in the store, earned on the pass).</summary>
        public int GoldScarabs;
        /// <summary>Changes of name paid for: the wallet's copy (the next one is free while it is 0).</summary>
        public int Renames;
        /// <summary>Solo energy (from act 2), kept by the device; the online energy is kept by the PvP server.</summary>
        public MummyEscape.Pvp.EnergyMeter SoloEnergy = new MummyEscape.Pvp.EnergyMeter();
        /// <summary>Season of the pass progress below; another season starts it over.</summary>
        public string PassSeason = "";
        public int PassXp;
        /// <summary>Seasons whose paid pass the player bought.</summary>
        public List<string> PassesOwned = new List<string>();
        public List<int> PassFreeClaimed = new List<int>();
        public List<int> PassPremiumClaimed = new List<int>();
        /// <summary>Golden-scarab spends the device delivers itself (scarabs, tiers), until the server's wallet confirms them.</summary>
        public List<GoldSpend> GoldSpends = new List<GoldSpend>();
        /// <summary>Solo runs not yet replayed by the server (it grants their pass XP), with the XP the device expects.</summary>
        public List<PendingRun> PendingRuns = new List<PendingRun>();

        // ---- Endless mode (solo): the place on each ladder (Facile, Normal, Extrême, in that order) and its own energy.
        public List<EndlessProgress> Endless = new List<EndlessProgress> { new EndlessProgress(), new EndlessProgress(), new EndlessProgress() };
        public MummyEscape.Pvp.EnergyMeter EndlessEnergy = new MummyEscape.Pvp.EnergyMeter();

        // ---- Halloween event: tombs escaped in a row (the act is finished at Core.Halloween.TombCount), kept between events.
        public int HalloweenCleared;

        // ---- Challenge tower (solo): the run in progress (when TowerActive) and the best one.
        public bool TowerActive;
        public TowerRun Tower = new TowerRun();
        public TowerRecord TowerBest = new TowerRecord();
    }

    /// <summary>A solo run waiting for the server's replay, and the pass XP the device expects from it meanwhile.</summary>
    [Serializable]
    public class PendingRun
    {
        public MummyEscape.Pvp.SoloRunReport Run;
        public int Xp;
        /// <summary>The scarabs the phone counted for it (shown until the server's replay pays its own).</summary>
        public int Scarabs;
    }

    /// <summary>A spend asked to the server: <see cref="Offer"/> is a scarab offer id or <see cref="SaveService.TiersSpend"/>.</summary>
    [Serializable]
    public class GoldSpend
    {
        public string Id;
        public string Offer;
        public long AtMs;
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
    public sealed partial class SaveService
    {
        public SaveData Data { get; private set; } = new SaveData();
        public event Action Changed;

        static string FilePath => Path.Combine(Application.persistentDataPath, TestProfile.File("save.json"));

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
            Sanitize(Data);
            // Saves from before the torches, hats and shoes: own every slot's free item, wear the defaults.
            foreach (var id in SkinCatalog.Defaults) if (!Data.OwnedSkins.Contains(id)) Data.OwnedSkins.Add(id);
            Data.SelectedMummy = SkinCatalog.Get(Data.SelectedMummy, CosmeticSlot.Mummy).Id;
            Data.SelectedSkin = SkinCatalog.Get(Data.SelectedSkin, CosmeticSlot.Color).Id;
            Data.SelectedTorch = SkinCatalog.Get(Data.SelectedTorch, CosmeticSlot.Torch).Id;
            Data.SelectedHat = SkinCatalog.Get(Data.SelectedHat, CosmeticSlot.Hat).Id;
            Data.SelectedShoes = SkinCatalog.Get(Data.SelectedShoes, CosmeticSlot.Shoes).Id;
            if (Data.Version < SaveData.CurrentVersion)
            {
                // v3: the campaign became three modes of 5 levels per act; v4: generator v13, 10 levels per act. The old
                // tombs are gone, progress starts over (scarabs, skins and the intros are kept), and the scores the old
                // leaderboards confirmed say nothing of the new ones.
                Data.Records.Clear();
                Data.SubmittedScores.Clear();
                Data.Version = SaveData.CurrentVersion;
                Save();
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
        /// <summary>
        /// A hand-edited, truncated or very old file can hold missing lists or holes: JsonUtility then gives null lists, and
        /// writes a null entry back as an empty object (a record without a level). Those are dropped.
        /// </summary>
        static void Sanitize(SaveData d)
        {
            d.Records ??= new List<LevelRecord>();
            // Records of the old campaign (keys without a mode, from a cloud copy or another device) are dropped too.
            d.Records.RemoveAll(r => r == null || !LevelId.TryParseKey(r.Key, out _));
            d.OwnedSkins ??= new List<string>();
            d.OwnedSkins.RemoveAll(string.IsNullOrEmpty);
            d.SubmittedScores ??= new List<string>();
            d.PassesOwned ??= new List<string>();
            d.PassesOwned.RemoveAll(string.IsNullOrEmpty);
            d.PassFreeClaimed ??= new List<int>();
            d.PassPremiumClaimed ??= new List<int>();
            d.GoldSpends ??= new List<GoldSpend>();
            d.GoldSpends.RemoveAll(s => s == null || string.IsNullOrEmpty(s.Id));
            d.PendingRuns ??= new List<PendingRun>();
            d.PendingRuns.RemoveAll(p => p?.Run == null || string.IsNullOrEmpty(p.Run.Id));
            d.SoloEnergy ??= new MummyEscape.Pvp.EnergyMeter();
            d.Endless ??= new List<EndlessProgress>();
            for (int i = 0; i < d.Endless.Count; i++) d.Endless[i] ??= new EndlessProgress();
            while (d.Endless.Count < DifficultyExt.All.Length) d.Endless.Add(new EndlessProgress());
            d.EndlessEnergy ??= new MummyEscape.Pvp.EnergyMeter();
            d.Tower ??= new TowerRun();
            d.TowerBest ??= new TowerRecord();
        }

        public bool MergeFrom(SaveData other)
        {
            if (other == null) return false;
            Sanitize(other);
            string before = JsonUtility.ToJson(Data);
            // A copy saved before the last reset holds the old campaign's records under the same keys: those stay out.
            if (other.Version < SaveData.CurrentVersion) other.Records.Clear();
            foreach (var theirs in other.Records)
            {
                if (theirs == null || string.IsNullOrEmpty(theirs.Key)) continue;
                var mine = Data.Records.Find(r => r.Key == theirs.Key);
                if (mine == null) Data.Records.Add(JsonUtility.FromJson<LevelRecord>(JsonUtility.ToJson(theirs)));
                else mine.MergeWith(theirs);
            }
            if (!Data.ScarabsHeld) Data.Coins = Math.Max(Data.Coins, other.Coins); // held: the server's balance, not a copy's
            // Paid skins come from the server's wallet only (ApplyWallet), never from a copy.
            foreach (var s in other.OwnedSkins) if (!Data.OwnedSkins.Contains(s) && !Monetization.GoldShop.IsPaid(s)) Data.OwnedSkins.Add(s);
            if (string.IsNullOrEmpty(Data.Country)) Data.Country = other.Country ?? "";
            Data.ProfileDone |= other.ProfileDone;
            Data.IntrosDone |= other.IntrosDone;
            MergeEconomy(other);
            Data.HalloweenCleared = Math.Max(Data.HalloweenCleared, other.HalloweenCleared);
            Data.TowerBest.MergeWith(other.TowerBest); // the run in progress stays on its device
            // The endless ladders never go back: the further copy wins (the version reset above leaves them alone).
            for (int i = 0; i < Data.Endless.Count; i++) Data.Endless[i].MergeWith(other.Endless[i]);
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

        /// <summary>
        /// Starting this level plays its act's intro first: Easy F{a}-1 while that intro is neither walked nor skipped.
        /// A player who already escaped F{a}-1 (before the intros existed) is never sent back to it.
        /// </summary>
        public bool NeedsIntro(LevelId id) =>
            id.Mode == Difficulty.Easy && id.Index == 1 && (Data.IntrosDone & (1 << (id.Act - 1))) == 0 && !IsCompleted(id);

        public bool IsIntroDone(int act) => (Data.IntrosDone & (1 << (act - 1))) != 0;

        /// <summary>Marks an act's intro as walked or skipped.</summary>
        public void MarkIntroDone(int act)
        {
            int bit = 1 << (act - 1);
            if ((Data.IntrosDone & bit) != 0) return;
            Data.IntrosDone |= bit;
            Save();
        }

        /// <summary>Features opened since the last call (a <see cref="Feature"/> bitmask), so each is celebrated once.</summary>
        public int TakeNewUnlocks()
        {
            int now = Unlocks.Mask(IsCompleted);
            int fresh = now & ~Data.SeenUnlocks;
            if (fresh != 0) { Data.SeenUnlocks = now; Save(); }
            return fresh;
        }

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

        /// <summary>Adds duel rewards the server granted (season skins, seal shop); returns true when one was new.</summary>
        public bool GrantSkins(IEnumerable<string> ids)
        {
            if (ids == null) return false;
            bool added = false;
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id) && !Data.OwnedSkins.Contains(id) && SkinCatalog.Get(id).Id == id)
                {
                    Data.OwnedSkins.Add(id);
                    added = true;
                }
            if (added) Save();
            return added;
        }

        /// <summary>Scarabs won outside a level (first clear of a 2v2 campaign tomb).</summary>
        public void EarnCoins(int amount)
        {
            Data.Coins += Math.Max(0, amount);
            Save();
        }

        /// <summary>Wears an owned item in its slot.</summary>
        public void SelectSkin(string id)
        {
            if (!Data.OwnedSkins.Contains(id)) return;
            var item = SkinCatalog.Get(id);
            switch (item.Slot)
            {
                case CosmeticSlot.Mummy: Data.SelectedMummy = id; break;
                case CosmeticSlot.Color: Data.SelectedSkin = id; break;
                case CosmeticSlot.Torch: Data.SelectedTorch = id; break;
                case CosmeticSlot.Hat: Data.SelectedHat = id; break;
                default: Data.SelectedShoes = id; break;
            }
            Save();
        }

        /// <summary>Shows a title under the player's name ("" or null: none). Earning it is checked by <see cref="TitleBook"/>.</summary>
        public void SelectTitle(string id)
        {
            Data.SelectedTitle = Pvp.Titles.Get(id)?.Id ?? "";
            Save();
        }

        public bool IsWorn(string id) =>
            id == Data.SelectedMummy || id == Data.SelectedSkin || id == Data.SelectedTorch || id == Data.SelectedHat || id == Data.SelectedShoes;

        /// <summary>Everything the player wears.</summary>
        public Loadout Loadout => new Loadout(
            SkinCatalog.Get(Data.SelectedMummy, CosmeticSlot.Mummy), SkinCatalog.Get(Data.SelectedSkin, CosmeticSlot.Color),
            SkinCatalog.Get(Data.SelectedTorch, CosmeticSlot.Torch), SkinCatalog.Get(Data.SelectedHat, CosmeticSlot.Hat),
            SkinCatalog.Get(Data.SelectedShoes, CosmeticSlot.Shoes));

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

        /// <summary>Highest unlocked level (modes in order), used to resume quickly from the main menu.</summary>
        public LevelId FurthestUnlocked()
        {
            var last = Progression.First(Difficulty.Easy);
            foreach (var id in DifficultyTable.AllLevels())
            {
                if (!IsUnlocked(id)) break;
                last = id;
            }
            return last;
        }

        /// <summary>Highest unlocked level of one mode (its first level while the mode is still locked).</summary>
        public LevelId FurthestUnlocked(Difficulty mode)
        {
            var last = Progression.First(mode);
            foreach (var id in DifficultyTable.Levels(mode))
            {
                if (!IsUnlocked(id)) break;
                last = id;
            }
            return last;
        }
    }
}
