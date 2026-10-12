using System;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>How the mummy is moved on a touch screen.</summary>
    public enum MoveControl { Swipe = 0, Pad = 1, Joystick = 2 }

    /// <summary>Player preferences: audio, comfort and visual effects. Persisted in PlayerPrefs.</summary>
    public sealed class SettingsService
    {
        public float MusicVolume { get; private set; }
        public float SfxVolume { get; private set; }
        public bool ScreenShake { get; private set; }
        /// <summary>Bloom, film grain, dust particles and flickering torch. Off = better battery life.</summary>
        public bool AdvancedLighting { get; private set; }
        public bool Haptics { get; private set; }
        /// <summary>Start-of-run tomb preview (memorise, then play in the dark). Off = every run starts in the dark at once.</summary>
        public bool ShowPreview { get; private set; }
        /// <summary>Language code ("fr", "en"), or "" to follow the device language.</summary>
        public string Language { get; private set; }
        /// <summary>2v2: when no real duo is found within a minute, play against a duo of bots rather than keep waiting.</summary>
        /// <summary>Duels and 2v2: a bot of the league when nobody of it shows up within a minute.</summary>
        public bool PvpBots { get; private set; }
        /// <summary>Swipes, an on-screen directional pad, or a joystick that keeps moving while held.</summary>
        public MoveControl Controls { get; private set; }
        /// <summary>Home screen backdrop: the act whose tomb is drawn behind it (see HomeThemes); act 1 by default.</summary>
        public int HomeTheme { get; private set; }
        /// <summary>The pad or the joystick under the right thumb instead of the left.</summary>
        public bool PadRight { get; private set; }
        /// <summary>30 frames a second instead of 60.</summary>
        public bool BatterySaver { get; private set; }
        /// <summary>A notification when the solo energy is full again.</summary>
        public bool NotifyEnergy { get; private set; }
        /// <summary>A notification when a seasonal event opens.</summary>
        public bool NotifyEvents { get; private set; }

        public event Action Changed;

        public void Load()
        {
            MusicVolume = PlayerPrefs.GetFloat("music", 0.6f);
            SfxVolume = PlayerPrefs.GetFloat("sfx", 0.8f);
            ScreenShake = PlayerPrefs.GetInt("shake", 1) == 1;
            AdvancedLighting = PlayerPrefs.GetInt("fx", 1) == 1;
            Haptics = PlayerPrefs.GetInt("haptics", 1) == 1;
            ShowPreview = PlayerPrefs.GetInt("preview", 1) == 1;
            Language = PlayerPrefs.GetString("lang", "");
            PvpBots = PlayerPrefs.GetInt("pvp_bots", 1) == 1;
            Controls = (MoveControl)Mathf.Clamp(PlayerPrefs.GetInt("controls", 0), 0, 2);
            HomeTheme = PlayerPrefs.GetInt("home_theme", UI.Screens.HomeThemes.Default);
            PadRight = PlayerPrefs.GetInt("pad_right", 0) == 1;
            BatterySaver = PlayerPrefs.GetInt("battery", 0) == 1;
            NotifyEnergy = PlayerPrefs.GetInt("notif_energy", 1) == 1;
            NotifyEvents = PlayerPrefs.GetInt("notif_events", 1) == 1;
        }

        public void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("music", MusicVolume); Commit(); }
        public void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("sfx", SfxVolume); Commit(); }
        public void SetScreenShake(bool on) { ScreenShake = on; PlayerPrefs.SetInt("shake", on ? 1 : 0); Commit(); }
        public void SetAdvancedLighting(bool on) { AdvancedLighting = on; PlayerPrefs.SetInt("fx", on ? 1 : 0); Commit(); }
        public void SetHaptics(bool on) { Haptics = on; PlayerPrefs.SetInt("haptics", on ? 1 : 0); Commit(); }
        public void SetPvpBots(bool on) { PvpBots = on; PlayerPrefs.SetInt("pvp_bots", on ? 1 : 0); Commit(); }
        public void SetControls(MoveControl c) { Controls = c; PlayerPrefs.SetInt("controls", (int)c); Commit(); }
        public void SetShowPreview(bool on) { ShowPreview = on; PlayerPrefs.SetInt("preview", on ? 1 : 0); Commit(); }
        public void SetHomeTheme(int act) { HomeTheme = act; PlayerPrefs.SetInt("home_theme", act); Commit(); }
        public void SetPadRight(bool on) { PadRight = on; PlayerPrefs.SetInt("pad_right", on ? 1 : 0); Commit(); }
        public void SetBatterySaver(bool on) { BatterySaver = on; PlayerPrefs.SetInt("battery", on ? 1 : 0); Commit(); }
        public void SetNotifyEnergy(bool on) { NotifyEnergy = on; PlayerPrefs.SetInt("notif_energy", on ? 1 : 0); Commit(); }
        public void SetNotifyEvents(bool on) { NotifyEvents = on; PlayerPrefs.SetInt("notif_events", on ? 1 : 0); Commit(); }
        public void SetLanguage(string code) { Language = code ?? ""; PlayerPrefs.SetString("lang", Language); Commit(); Loc.Apply(Language); }

        void Commit()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
