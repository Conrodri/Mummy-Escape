using System;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>Player preferences: audio, comfort and visual effects. Persisted in PlayerPrefs.</summary>
    public sealed class SettingsService
    {
        public float MusicVolume { get; private set; }
        public float SfxVolume { get; private set; }
        public bool ScreenShake { get; private set; }
        /// <summary>-1 (darker) .. +1 (brighter), applied as post exposure and ambient light.</summary>
        public float Brightness { get; private set; }
        /// <summary>Bloom, film grain, dust particles and flickering torch. Off = better battery life.</summary>
        public bool AdvancedLighting { get; private set; }
        public bool Haptics { get; private set; }
        /// <summary>Start-of-run tomb preview (memorise, then play in the dark). Off = every run starts in the dark at once.</summary>
        public bool ShowPreview { get; private set; }
        /// <summary>Language code ("fr", "en"), or "" to follow the device language.</summary>
        public string Language { get; private set; }

        public event Action Changed;

        public void Load()
        {
            MusicVolume = PlayerPrefs.GetFloat("music", 0.6f);
            SfxVolume = PlayerPrefs.GetFloat("sfx", 0.8f);
            ScreenShake = PlayerPrefs.GetInt("shake", 1) == 1;
            Brightness = PlayerPrefs.GetFloat("brightness", 0f);
            AdvancedLighting = PlayerPrefs.GetInt("fx", 1) == 1;
            Haptics = PlayerPrefs.GetInt("haptics", 1) == 1;
            ShowPreview = PlayerPrefs.GetInt("preview", 1) == 1;
            Language = PlayerPrefs.GetString("lang", "");
        }

        public void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("music", MusicVolume); Commit(); }
        public void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("sfx", SfxVolume); Commit(); }
        public void SetScreenShake(bool on) { ScreenShake = on; PlayerPrefs.SetInt("shake", on ? 1 : 0); Commit(); }
        public void SetBrightness(float v) { Brightness = Mathf.Clamp(v, -1f, 1f); PlayerPrefs.SetFloat("brightness", Brightness); Commit(); }
        public void SetAdvancedLighting(bool on) { AdvancedLighting = on; PlayerPrefs.SetInt("fx", on ? 1 : 0); Commit(); }
        public void SetHaptics(bool on) { Haptics = on; PlayerPrefs.SetInt("haptics", on ? 1 : 0); Commit(); }
        public void SetShowPreview(bool on) { ShowPreview = on; PlayerPrefs.SetInt("preview", on ? 1 : 0); Commit(); }
        public void SetLanguage(string code) { Language = code ?? ""; PlayerPrefs.SetString("lang", Language); Commit(); Loc.Apply(Language); }

        void Commit()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
