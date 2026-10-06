using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>What the player agreed to. Stored on the device only.</summary>
    [Serializable]
    public sealed class PrivacyData
    {
        /// <summary>Version of the privacy policy / terms the player accepted (0 = never answered).</summary>
        public int AcceptedPolicyVersion;
        /// <summary>UTC date of that answer, ISO 8601 (proof of the choice, GDPR art. 7.1).</summary>
        public string AnsweredAtUtc = "";
        /// <summary>The player chose to use online features (rankings, friends, account).</summary>
        public bool OnlineEnabled;
        /// <summary>Below the age of digital consent of their country when they answered. The birth year itself is not kept.</summary>
        public bool IsMinor;
        /// <summary>The age question was answered (only asked when the player wants to play online).</summary>
        public bool AgeChecked;
        /// <summary>A parent or guardian confirmed the online features for a minor.</summary>
        public bool ParentalConsent;
        /// <summary>Friends may see the player's progression (off by default: privacy by default, art. 25).</summary>
        public bool ShareProgress;
    }

    /// <summary>
    /// GDPR / ePrivacy state of the player. Nothing leaves the device before the player has been informed and has
    /// chosen to play online; minors under their country's age of digital consent (art. 8) need a parent's approval.
    /// The game uses no advertising, no analytics and no tracking identifier.
    /// </summary>
    public sealed class PrivacyService
    {
        /// <summary>Bump when the privacy policy or the terms change materially: players are asked again.</summary>
        public const int PolicyVersion = 2;

        /// <summary>Age of digital consent per EU/EEA country (GDPR art. 8, national choices between 13 and 16).</summary>
        static readonly Dictionary<string, int> ConsentAge = new Dictionary<string, int>
        {
            { "AT", 14 }, { "BE", 13 }, { "BG", 14 }, { "CY", 14 }, { "CZ", 15 }, { "DE", 16 }, { "DK", 13 },
            { "EE", 13 }, { "ES", 14 }, { "FI", 13 }, { "FR", 15 }, { "GR", 15 }, { "HR", 16 }, { "HU", 16 },
            { "IE", 16 }, { "IT", 14 }, { "LT", 14 }, { "LU", 16 }, { "LV", 13 }, { "MT", 13 }, { "NL", 16 },
            { "PL", 16 }, { "PT", 13 }, { "RO", 16 }, { "SE", 13 }, { "SI", 15 }, { "SK", 16 },
            { "IS", 13 }, { "LI", 16 }, { "NO", 13 }, { "GB", 13 }, { "CH", 13 }, { "US", 13 },
        };

        /// <summary>Strictest value, used when the country is unknown.</summary>
        public const int DefaultConsentAge = 16;

        public PrivacyData Data { get; private set; } = new PrivacyData();
        public event Action Changed;

        static string FilePath => Path.Combine(Application.persistentDataPath, "privacy.json");

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Data = JsonUtility.FromJson<PrivacyData>(File.ReadAllText(FilePath)) ?? new PrivacyData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Privacy] Unreadable choices, asking again: {e.Message}");
                Data = new PrivacyData();
            }
        }

        void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Data)); }
            catch (Exception e) { Debug.LogError($"[Privacy] Could not write choices: {e.Message}"); }
            Changed?.Invoke();
        }

        /// <summary>First launch, or the policy changed since the player answered.</summary>
        public bool NeedsAnswer => Data.AcceptedPolicyVersion < PolicyVersion;

        /// <summary>Online features may run (informed choice, and a parent's approval for a minor).</summary>
        public bool OnlineAllowed => !NeedsAnswer && Data.OnlineEnabled && (!Data.IsMinor || Data.ParentalConsent);

        public static int ConsentAgeFor(string country) =>
            !string.IsNullOrEmpty(country) && ConsentAge.TryGetValue(country.ToUpperInvariant(), out int age) ? age : DefaultConsentAge;

        /// <summary>
        /// True when someone born in <paramref name="birthYear"/> may be under the age of digital consent this year
        /// (the birthday may not have passed yet: the doubtful year counts as minor).
        /// </summary>
        public static bool IsUnderConsentAge(int birthYear, string country) => DateTime.UtcNow.Year - birthYear <= ConsentAgeFor(country);

        /// <summary>
        /// Records the answer of the welcome screen. <paramref name="isMinor"/> is null when the age was not asked
        /// (offline play needs no age: nothing leaves the device).
        /// </summary>
        public void Answer(bool online, bool? isMinor, bool parentalConsent)
        {
            Data.AcceptedPolicyVersion = PolicyVersion;
            Data.AnsweredAtUtc = DateTime.UtcNow.ToString("o");
            if (isMinor.HasValue)
            {
                Data.AgeChecked = true;
                Data.IsMinor = isMinor.Value;
                Data.ParentalConsent = isMinor.Value && parentalConsent;
            }
            Data.OnlineEnabled = online && Data.AgeChecked && (!Data.IsMinor || Data.ParentalConsent);
            Save();
        }

        public void SetOnline(bool on)
        {
            Data.OnlineEnabled = on && Data.AgeChecked && (!Data.IsMinor || Data.ParentalConsent);
            Data.AnsweredAtUtc = DateTime.UtcNow.ToString("o");
            Save();
        }

        public void SetParentalConsent(bool on)
        {
            Data.ParentalConsent = Data.IsMinor && on;
            if (!Data.ParentalConsent && Data.IsMinor) Data.OnlineEnabled = false;
            Data.AnsweredAtUtc = DateTime.UtcNow.ToString("o");
            Save();
        }

        public void SetShareProgress(bool on)
        {
            Data.ShareProgress = on;
            Save();
        }

        /// <summary>Forgets every choice (the welcome screen shows again).</summary>
        public void Reset()
        {
            Data = new PrivacyData();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[Privacy] {e.Message}"); }
            Changed?.Invoke();
        }
    }
}
