using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MummyEscape.Services
{
    public enum Sfx { Step, Bump, Button, Door, Spikes, Darkness, Disarm, Teleport, Curse, Fall, Climb, Win, Death, Click, Coin, Splash, Crumble, Laser, Fire }

    /// <summary>
    /// Music + sound effects. Ships with synthesized placeholder sounds so the game is audible from day one;
    /// drop real clips into the serialized overrides on <see cref="App.GameBootstrap"/> to replace them.
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        const int Rate = 22050;

        AudioSource _music;      // the theme playing (or fading in)
        AudioSource _musicOut;   // the previous theme, fading out
        AudioSource _sfx;
        SettingsService _settings;
        readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();

        /// <summary>Theme 0 = menus, 1..5 = acts.</summary>
        int _theme = -1;
        float _mix = 1f;
        const float CrossfadeSeconds = 1.8f;
        AudioClip _menuOverride;
        readonly Dictionary<int, AudioClip> _themes = new Dictionary<int, AudioClip>();
        readonly Dictionary<int, Task<float[]>> _rendering = new Dictionary<int, Task<float[]>>();

        public void Init(SettingsService settings, AudioClip musicOverride)
        {
            _settings = settings;
            _music = NewMusicSource();
            _musicOut = NewMusicSource();
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _menuOverride = musicOverride;

            foreach (Sfx s in Enum.GetValues(typeof(Sfx))) _clips[s] = Synth.Make(s);

            settings.Changed += ApplyVolumes;
            PlayMusic(0);
            ApplyVolumes();
        }

        AudioSource NewMusicSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            return src;
        }

        void ApplyVolumes()
        {
            float music = _settings.MusicVolume * 0.5f;
            _music.volume = music * _mix;
            _musicOut.volume = music * (1f - _mix);
            _sfx.volume = _settings.SfxVolume;
        }

        void Update()
        {
            if (_mix >= 1f) return;
            _mix = Mathf.Min(1f, _mix + Time.unscaledDeltaTime / CrossfadeSeconds);
            ApplyVolumes();
            if (_mix >= 1f) _musicOut.Stop();
        }

        /// <summary>
        /// Switches to the theme of an act (0 = menus), crossfading. A track placed in <c>Resources/Music/act{n}</c>
        /// (or <c>menu</c>) — e.g. made with Suno — replaces the coded one; otherwise <see cref="MusicComposer"/>
        /// renders it on a worker thread the first time (the previous theme keeps playing meanwhile).
        /// </summary>
        public void PlayMusic(int theme)
        {
            if (theme == _theme) return;
            _theme = theme;
            var clip = ThemeClip(theme);
            if (clip != null) { CrossfadeTo(clip); return; }
            StartCoroutine(RenderThenPlay(theme));
        }

        /// <summary>Starts rendering a theme in the background so it is ready when needed.</summary>
        public void PrefetchMusic(int theme)
        {
            if (ThemeClip(theme) == null) Render(theme);
        }

        AudioClip ThemeClip(int theme)
        {
            if (_themes.TryGetValue(theme, out var cached)) return cached;
            var file = Resources.Load<AudioClip>(theme == 0 ? "Music/menu" : $"Music/act{theme}");
            if (file == null && theme == 0) file = _menuOverride != null ? _menuOverride : Synth.AmbientLoop();
            if (file != null) _themes[theme] = file;
            return file;
        }

        Task<float[]> Render(int theme)
        {
            if (!_rendering.TryGetValue(theme, out var task))
                _rendering[theme] = task = Task.Run(() => MusicComposer.Render(theme));
            return task;
        }

        IEnumerator RenderThenPlay(int theme)
        {
            var task = Render(theme);
            while (!task.IsCompleted) yield return null;
            _rendering.Remove(theme);
            if (task.IsFaulted) { Debug.LogError($"[Audio] Theme {theme} failed: {task.Exception}"); yield break; }
            var data = task.Result;
            var clip = AudioClip.Create($"theme_act{theme}", data.Length, 1, MusicComposer.Rate, false);
            clip.SetData(data, 0);
            // Keep the menu theme and the current act only (each loop is a few MB).
            foreach (var key in new List<int>(_themes.Keys))
                if (key != 0 && key != theme && _themes[key].name.StartsWith("theme_")) { Destroy(_themes[key]); _themes.Remove(key); }
            _themes[theme] = clip;
            if (_theme == theme) CrossfadeTo(clip);
        }

        void CrossfadeTo(AudioClip clip)
        {
            if (_music.clip == clip && _music.isPlaying) return;
            (_music, _musicOut) = (_musicOut, _music);
            _music.Stop();
            _mix = 0f; // the new theme fades in while the old one fades out
            _music.clip = clip;
            _music.Play();
            ApplyVolumes();
        }

        public void Play(Sfx s, float pitchJitter = 0.05f)
        {
            if (_settings.SfxVolume <= 0f) return;
            _sfx.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            _sfx.PlayOneShot(_clips[s]);
        }

        public void Override(Sfx s, AudioClip clip)
        {
            if (clip != null) _clips[s] = clip;
        }

        /// <summary>Tiny additive synth for placeholder audio.</summary>
        static class Synth
        {
            // Phrygian dominant on D: the "Egyptian" sounding scale.
            static readonly float[] Scale = { 146.83f, 155.56f, 185.00f, 196.00f, 220.00f, 233.08f, 261.63f, 293.66f };

            public static AudioClip Make(Sfx s)
            {
                switch (s)
                {
                    case Sfx.Step: return Build("step", 0.09f, (t, i) => Noise(i) * Env(t, 0.002f, 0.08f) * 0.35f + Sine(90, t) * Env(t, 0.001f, 0.06f) * 0.4f);
                    case Sfx.Bump: return Build("bump", 0.15f, (t, i) => Sine(60 - t * 100, t) * Env(t, 0.001f, 0.14f) * 0.8f);
                    case Sfx.Button: return Build("button", 0.5f, (t, i) => Noise(i) * Env(t, 0.001f, 0.03f) * 0.4f + Pluck(587.3f, t - 0.05f) * 0.5f + Pluck(880f, t - 0.15f) * 0.35f);
                    case Sfx.Door: return Build("door", 0.9f, (t, i) => (Noise(i / 6) * 0.6f + Sine(45, t) * 0.5f) * Env(t, 0.05f, 0.85f) * 0.7f);
                    case Sfx.Spikes: return Build("spikes", 0.45f, (t, i) => Sine(1800 + 900 * t, t) * Env(t, 0.001f, 0.25f) * 0.25f + Noise(i) * Env(t, 0.001f, 0.1f) * 0.5f + Sine(70, t) * Env(t, 0.01f, 0.3f) * 0.6f);
                    case Sfx.Darkness: return Build("darkness", 1.0f, (t, i) => Noise(i / 3) * Env(t, 0.3f, 0.7f) * 0.35f + Sine(110 - 50 * t, t) * Env(t, 0.1f, 0.9f) * 0.4f);
                    case Sfx.Disarm: return Build("disarm", 0.35f, (t, i) => Pluck(392f, t) * 0.4f + Pluck(311f, t - 0.08f) * 0.4f + Noise(i) * Env(t, 0.001f, 0.02f) * 0.3f);
                    case Sfx.Teleport: return Build("teleport", 0.8f, (t, i) => Sine(300 + 900 * t * t, t) * Env(t, 0.05f, 0.75f) * 0.35f + Sine(450 + 1350 * t * t, t) * Env(t, 0.05f, 0.75f) * 0.2f);
                    case Sfx.Curse: return Build("curse", 1.2f, (t, i) => (Sine(155.56f, t) + Sine(146.83f, t) + Sine(77.78f, t)) * Env(t, 0.1f, 1.1f) * 0.25f);
                    case Sfx.Fall: return Build("fall", 0.8f, (t, i) => Sine(500 - 450 * t, t) * Env(t, 0.01f, 0.6f) * 0.3f + Noise(i) * Env(t - 0.6f, 0.001f, 0.2f) * 0.6f);
                    case Sfx.Climb: return Build("climb", 0.5f, (t, i) => (Pluck(220f, t) + Pluck(293.66f, t - 0.12f) + Pluck(392f, t - 0.24f)) * 0.3f);
                    case Sfx.Win: return Build("win", 1.6f, (t, i) => { float v = 0; for (int k = 0; k < 6; k++) v += Pluck(Scale[(k * 2) % Scale.Length] * 2, t - k * 0.11f); return v * 0.25f; });
                    case Sfx.Death: return Build("death", 1.5f, (t, i) => (Sine(110 - 40 * t, t) + Sine(116.54f - 40 * t, t)) * Env(t, 0.02f, 1.4f) * 0.35f);
                    case Sfx.Click: return Build("click", 0.06f, (t, i) => Sine(1200, t) * Env(t, 0.001f, 0.05f) * 0.3f);
                    case Sfx.Coin: return Build("coin", 0.4f, (t, i) => Pluck(1318.5f, t) * 0.3f + Pluck(1760f, t - 0.07f) * 0.3f);
                    case Sfx.Splash: return Build("splash", 0.7f, (t, i) => (Noise(i) * 0.5f + Noise(i / 2) * 0.5f) * Env(t, 0.01f, 0.6f) * (0.5f + 0.5f * Mathf.Sin(t * 60f)) * 0.5f + Sine(220 - 150 * t, t) * Env(t, 0.005f, 0.2f) * 0.3f);
                    case Sfx.Crumble: return Build("crumble", 0.9f, (t, i) => Noise(i / 4) * Env(t, 0.02f, 0.85f) * (0.6f + 0.4f * Mathf.Sin(t * 47f)) * 0.6f + Sine(50, t) * Env(t, 0.01f, 0.5f) * 0.5f);
                    case Sfx.Laser: return Build("laser", 0.45f, (t, i) => (Sine(880 - 500 * t, t) * 0.5f + Sine(1320 - 750 * t, t) * 0.25f) * Env(t, 0.003f, 0.4f) * 0.35f + Noise(i) * Env(t, 0.001f, 0.04f) * 0.2f);
                    case Sfx.Fire: return Build("fire", 0.8f, (t, i) => Noise(i / 2) * Env(t, 0.04f, 0.75f) * (0.7f + 0.3f * Noise(i / 700)) * 0.6f + Sine(80, t) * Env(t, 0.02f, 0.5f) * 0.35f);
                }
                return Build("silence", 0.05f, (t, i) => 0f);
            }

            /// <summary>16 s ambient loop: low drone, wind and sparse oud-like plucks on the scale.</summary>
            public static AudioClip AmbientLoop()
            {
                const float length = 16f;
                var rng = new System.Random(7);
                var notes = new List<(float time, float freq)>();
                for (float t = 0.5f; t < length - 1.5f; t += 0.5f + (float)rng.NextDouble() * 1.5f)
                    notes.Add((t, Scale[rng.Next(Scale.Length)] * (rng.NextDouble() < 0.3 ? 2f : 1f)));

                return Build("ambient", length, (t, i) =>
                {
                    float loopFade = Mathf.Min(1f, Mathf.Min(t, length - t) * 2f);
                    float drone = (Sine(73.42f, t) * 0.5f + Sine(110f, t) * 0.25f + Sine(146.83f, t) * 0.12f) * (0.8f + 0.2f * Mathf.Sin(t * 0.8f));
                    float wind = Noise(i / 8) * (0.06f + 0.04f * Mathf.Sin(t * 0.37f));
                    float melody = 0f;
                    foreach (var n in notes) melody += Pluck(n.freq, t - n.time) * 0.18f;
                    return (drone * 0.35f + wind + melody) * loopFade;
                });
            }

            static AudioClip Build(string name, float seconds, Func<float, int, float> f)
            {
                int n = Mathf.CeilToInt(seconds * Rate);
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, i), -1f, 1f);
                var clip = AudioClip.Create(name, n, 1, Rate, false);
                clip.SetData(data, 0);
                return clip;
            }

            static float Sine(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t);

            static float Env(float t, float attack, float release)
            {
                if (t < 0f) return 0f;
                if (t < attack) return t / attack;
                return Mathf.Max(0f, 1f - (t - attack) / release);
            }

            /// <summary>Plucked string: a few decaying harmonics.</summary>
            static float Pluck(float freq, float t)
            {
                if (t < 0f) return 0f;
                float decay = Mathf.Exp(-t * 4f);
                return (Sine(freq, t) + 0.5f * Sine(freq * 2, t) * Mathf.Exp(-t * 6f) + 0.25f * Sine(freq * 3, t) * Mathf.Exp(-t * 9f)) * decay * Mathf.Min(1f, t * 400f);
            }

            static float Noise(int i)
            {
                unchecked
                {
                    uint x = (uint)i * 747796405u + 2891336453u;
                    x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                    return ((x >> 22) ^ x) / (float)uint.MaxValue * 2f - 1f;
                }
            }
        }
    }
}
