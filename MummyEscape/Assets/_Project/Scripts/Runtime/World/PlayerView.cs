using System.Collections;
using MummyEscape.Core;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>The mummy sprite, its torch and the little animations that sell each rule.</summary>
    public sealed class PlayerView : MonoBehaviour
    {
        SpriteRenderer _sprite;
        SpriteRenderer _flame;
        Loadout _look;
        bool _dressed;
        Transform _body;
        Light2D _torch;
        ParticleSystem _dust;
        ArtLibrary _art;
        SettingsService _settings;
        Material _spriteMaterial;
        bool _blind;
        float _torchRadius = 2.6f;
        Color _torchColor = new Color(1f, 0.72f, 0.42f);

        /// <summary>The torch in the mummy's hand. Visual state only for now: dust will put it out, wall torches relight it.</summary>
        public bool TorchLit { get; private set; } = true;

        public void Init(ArtLibrary art, Material spriteMaterial, SettingsService settings)
        {
            _art = art;
            _settings = settings;
            _spriteMaterial = spriteMaterial;

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _sprite = _body.gameObject.AddComponent<SpriteRenderer>();
            if (spriteMaterial != null) _sprite.sharedMaterial = spriteMaterial;
            _sprite.sortingOrder = 10;

            _flame = new GameObject("Flame").AddComponent<SpriteRenderer>();
            _flame.transform.SetParent(_body, false);
            if (spriteMaterial != null) _flame.sharedMaterial = spriteMaterial;
            _flame.sortingOrder = 11;

            var torchGo = new GameObject("Torch");
            torchGo.transform.SetParent(_body, false);
            torchGo.transform.localPosition = ArtLibrary.TorchFlameOffset;
            _torch = torchGo.AddComponent<Light2D>();
            _torch.lightType = Light2D.LightType.Point;
            _torch.pointLightInnerRadius = 0.3f;
            _torch.pointLightOuterRadius = _torchRadius;
            _torch.falloffIntensity = 0.55f;
            _torch.intensity = 1.35f;

            _dust = CreateDust(spriteMaterial);
            settings.Changed += ApplySettings;
            ApplySettings();
        }

        public void SetSkin(Loadout look)
        {
            _look = look;
            _dressed = true;
            _sprite.sprite = _art.Mummy(look);
            _torchColor = look.Light;
            _torch.color = _torchColor;
        }

        /// <summary>The motes floating in the torch light follow the act: dust, dripping water, embers, data sparks...</summary>
        public void SetTheme(TombTheme theme, Material unlit)
        {
            var main = _dust.main;
            main.startColor = theme.Motes;
            var vel = _dust.velocityOverLifetime;
            vel.enabled = Mathf.Abs(theme.MotesFall) > 0.001f;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 0f;
            vel.y = -theme.MotesFall;
            vel.z = 0f;
            var r = _dust.GetComponent<ParticleSystemRenderer>();
            if (theme.MotesGlow && unlit != null) r.sharedMaterial = unlit;
            else if (_spriteMaterial != null) r.sharedMaterial = _spriteMaterial;
            _dust.Clear();
        }

        public void SetBlind(bool blind) => _blind = blind;

        public void SetTorchLit(bool lit)
        {
            TorchLit = lit;
            if (!lit) _flame.sprite = _art.TorchEmber();
        }

        public void Place(Cell c)
        {
            transform.position = MazeView.CellToWorld(c);
            _body.localScale = Vector3.one;
            _sprite.color = Color.white;
        }

        public IEnumerator WalkTo(Cell c, float duration = 0.13f)
        {
            Vector3 from = transform.position, to = MazeView.CellToWorld(c);
            if (to.x < from.x - 0.01f) _sprite.flipX = true;
            else if (to.x > from.x + 0.01f) _sprite.flipX = false;
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float e = 1f - (1f - t) * (1f - t);
                transform.position = Vector3.Lerp(from, to, e);
                _body.localPosition = new Vector3(0, Mathf.Sin(t * Mathf.PI) * 0.12f, 0);
                yield return null;
            }
            transform.position = to;
            _body.localPosition = Vector3.zero;
        }

        /// <summary>Carried by a current: glides without walking, slightly tilted.</summary>
        public IEnumerator Slide(Cell c, float duration = 0.09f)
        {
            Vector3 from = transform.position, to = MazeView.CellToWorld(c);
            float tilt = to.x < from.x - 0.01f ? 8f : to.x > from.x + 0.01f ? -8f : 0f;
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                transform.position = Vector3.Lerp(from, to, t);
                _body.localRotation = Quaternion.Euler(0f, 0f, tilt + Mathf.Sin(Time.time * 30f) * 3f);
                yield return null;
            }
            transform.position = to;
            _body.localRotation = Quaternion.identity;
        }

        public IEnumerator Bump(Dir d)
        {
            var offset = new Vector3(d.Dx(), d.Dy(), 0) * 0.18f;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.14f)
            {
                _body.localPosition = offset * Mathf.Sin(t * Mathf.PI);
                yield return null;
            }
            _body.localPosition = Vector3.zero;
        }

        public IEnumerator Vanish(float duration = 0.25f)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                _body.localScale = new Vector3(1f - t * 0.7f, 1f + t * 0.6f, 1f);
                _sprite.color = new Color(1, 1, 1, 1f - t);
                yield return null;
            }
            _sprite.color = new Color(1, 1, 1, 0);
        }

        public IEnumerator Appear(float duration = 0.25f)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                _body.localScale = Vector3.Lerp(new Vector3(0.3f, 1.6f, 1f), Vector3.one, t);
                _sprite.color = new Color(1, 1, 1, t);
                yield return null;
            }
            _body.localScale = Vector3.one;
            _sprite.color = Color.white;
        }

        public IEnumerator FallThrough()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                _body.localScale = Vector3.one * (1f - t * 0.8f);
                _body.localRotation = Quaternion.Euler(0, 0, t * 200f);
                yield return null;
            }
            _body.localRotation = Quaternion.identity;
        }

        public IEnumerator Hurt()
        {
            for (int i = 0; i < 3; i++)
            {
                _sprite.color = new Color(1f, 0.3f, 0.25f);
                yield return new WaitForSeconds(0.06f);
                _sprite.color = Color.white;
                yield return new WaitForSeconds(0.06f);
            }
        }

        public IEnumerator Die()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.8f)
            {
                _body.localScale = new Vector3(1f + t * 0.3f, 1f - t * 0.9f, 1f);
                _sprite.color = Color.Lerp(Color.white, new Color(0.3f, 0.25f, 0.2f, 0.6f), t);
                _torch.intensity = Mathf.Lerp(1.35f, 0f, t);
                yield return null;
            }
        }

        public void ResetVisual()
        {
            _body.localScale = Vector3.one;
            _body.localRotation = Quaternion.identity;
            _body.localPosition = Vector3.zero;
            _sprite.color = Color.white;
            _torch.intensity = 1.35f;
            TorchLit = true;
        }

        void ApplySettings()
        {
            var emission = _dust.emission;
            emission.enabled = _settings.AdvancedLighting;
        }

        void Update()
        {
            // Idle breathing.
            if (_body.localPosition == Vector3.zero)
                _body.localScale = new Vector3(_body.localScale.x, Mathf.Lerp(_body.localScale.y, 1f + Mathf.Sin(Time.time * 3f) * 0.025f, 0.5f), 1f);

            UpdateFlame();
            float targetRadius = _blind || !TorchLit ? 0.85f : _torchRadius;
            _torch.pointLightOuterRadius = Mathf.Lerp(_torch.pointLightOuterRadius, targetRadius, Time.deltaTime * 6f);
            if (_settings.AdvancedLighting && _torch.intensity > 0.01f)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 0.25f + Mathf.PerlinNoise(Time.time * 19f, 0.9f) * 0.1f;
                _torch.intensity = Mathf.Lerp(_torch.intensity, 1.2f + flicker, Time.deltaTime * 10f);
            }
        }

        void UpdateFlame()
        {
            // Mirror with the body and fade with it (vanish, teleport, death).
            _flame.flipX = _sprite.flipX;
            _flame.color = new Color(1f, 1f, 1f, _sprite.color.a);
            var offset = ArtLibrary.TorchFlameOffset;
            _torch.transform.localPosition = new Vector3(_sprite.flipX ? -offset.x : offset.x, offset.y, 0f);
            if (TorchLit && _dressed)
                _flame.sprite = _art.TorchFlame(_look.Light, (int)(Time.time * 9f) % ArtLibrary.TorchFlameFrames);
        }

        ParticleSystem CreateDust(Material spriteMaterial)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 3f;
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new Color(1f, 0.9f, 0.7f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.6f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 20;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;
            ps.Play();
            return ps;
        }
    }
}
