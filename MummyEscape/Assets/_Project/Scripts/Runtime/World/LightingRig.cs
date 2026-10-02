using MummyEscape.Services;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>
    /// Ambient light + post processing. The tomb is lit by a faint cold ambient (the player's memory of the map)
    /// and the warm flickering torch of the mummy; bloom makes gold, portals and the exit glow.
    /// </summary>
    public sealed class LightingRig : MonoBehaviour
    {
        Light2D _ambient;
        Volume _volume;
        Bloom _bloom;
        Vignette _vignette;
        ColorAdjustments _color;
        FilmGrain _grain;
        SettingsService _settings;

        float _baseAmbient = 0.55f;
        Color _gameAmbient = new Color(0.5f, 0.58f, 0.85f);
        float _gameIntensity = 0.5f;
        float _flash;
        Color _flashColor = Color.red;

        public void Init(SettingsService settings)
        {
            _settings = settings;

            _ambient = gameObject.AddComponent<Light2D>();
            _ambient.lightType = Light2D.LightType.Global;
            _ambient.color = new Color(0.55f, 0.62f, 0.85f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.sharedProfile = profile;

            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(0.9f);
            _bloom.intensity.Override(1.1f);
            _bloom.scatter.Override(0.65f);

            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.38f);
            _vignette.smoothness.Override(0.5f);
            _vignette.color.Override(Color.black);

            _color = profile.Add<ColorAdjustments>(true);
            _color.postExposure.Override(0f);
            _color.contrast.Override(12f);
            _color.saturation.Override(-5f);
            _color.colorFilter.Override(new Color(1f, 0.95f, 0.88f));

            _grain = profile.Add<FilmGrain>(true);
            _grain.type.Override(FilmGrainLookup.Thin1);
            _grain.intensity.Override(0.18f);

            settings.Changed += Apply;
            Apply();
        }

        /// <summary>Each act has its own darkness: blue-green under the Nile, dusty grey in the ruins, red in the sanctuary...</summary>
        public void SetTheme(Visual.TombTheme theme)
        {
            _gameAmbient = theme.Ambient;
            _gameIntensity = theme.AmbientIntensity;
            // Wet stone and neon glow more; ruins are dusty and dull.
            _bloom.intensity.Override(theme.Kind == Visual.TombTheme.Style.Tech ? 1.5f : theme.Kind == Visual.TombTheme.Style.Inferno ? 1.35f : 1.1f);
            _color.saturation.Override(theme.Kind == Visual.TombTheme.Style.Ruins ? -18f : theme.Kind == Visual.TombTheme.Style.Tech ? 5f : -5f);
            _color.colorFilter.Override(theme.Kind == Visual.TombTheme.Style.Flooded ? new Color(0.9f, 0.98f, 1f)
                : theme.Kind == Visual.TombTheme.Style.Inferno ? new Color(1f, 0.9f, 0.82f)
                : theme.Kind == Visual.TombTheme.Style.Tech ? new Color(0.92f, 0.95f, 1f)
                : new Color(1f, 0.95f, 0.88f));
        }

        /// <summary>Menu = brighter warm tomb, Game = dark exploration.</summary>
        public void SetMood(bool inGame)
        {
            _baseAmbient = inGame ? _gameIntensity : 0.9f;
            _ambient.color = inGame ? _gameAmbient : new Color(1f, 0.85f, 0.65f);
            Apply();
        }

        /// <summary>Start-of-run map preview: the tomb is bathed in light so the whole layout reads at a glance.</summary>
        public void SetPreview(bool on)
        {
            _baseAmbient = on ? 1.05f : _gameIntensity;
            _ambient.color = on ? Color.Lerp(new Color(1f, 0.9f, 0.75f), _gameAmbient, 0.2f) : _gameAmbient;
            Apply();
        }

        /// <summary>Coloured vignette pulse (damage, curse...).</summary>
        public void Flash(Color color, float strength = 1f)
        {
            _flashColor = color;
            _flash = Mathf.Max(_flash, strength);
        }

        void Apply()
        {
            float b = _settings.Brightness;
            _ambient.intensity = _baseAmbient * (1f + 0.5f * b);
            _color.postExposure.value = b * 0.7f;
            bool fx = _settings.AdvancedLighting;
            _bloom.active = fx;
            _grain.active = fx;
        }

        void Update()
        {
            if (_flash <= 0f) return;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 2.5f);
            _vignette.color.value = Color.Lerp(Color.black, _flashColor, _flash);
            _vignette.intensity.value = Mathf.Lerp(0.38f, 0.55f, _flash);
        }
    }
}
