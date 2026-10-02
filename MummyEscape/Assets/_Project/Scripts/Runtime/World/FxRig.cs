using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>
    /// Particles and light flashes for everything that happens in the tomb. A handful of world-space particle systems
    /// are shared by all effects (each burst is emitted by hand at a position), plus a small pool of 2D lights for
    /// flashes. Glowing effects (sparks, magic, fire) use an unlit material so they shine in the dark; dust, smoke and
    /// debris are lit, so they only show where a light falls.
    /// </summary>
    public sealed class FxRig : MonoBehaviour
    {
        ArtLibrary _art;
        Material _lit, _unlit;
        SettingsService _settings;

        ParticleSystem _dust, _smoke, _debris, _sparks, _magic, _fire, _water;
        readonly List<(Light2D light, float life, float duration, float peak)> _flashes = new List<(Light2D, float, float, float)>();

        public Material Unlit => _unlit;
        public Material Lit => _lit;

        public void Init(ArtLibrary art, Material lit, Material unlit, SettingsService settings)
        {
            _art = art;
            _lit = lit;
            _settings = settings;
            if (unlit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader != null) unlit = new Material(shader);
            }
            _unlit = unlit != null ? unlit : lit;

            _dust = Create("Dust", art.Puff, _lit, 5, 0f, 1.6f, grow: 1.9f);
            _smoke = Create("Smoke", art.Puff, _lit, 22, -0.03f, 1.2f, grow: 2.4f);
            _debris = Create("Debris", art.Shard, _lit, 6, 0.32f, 0.4f, grow: 1f, spin: true);
            _sparks = Create("Sparks", art.Spark, _unlit, 24, 0.22f, 0.8f, grow: 0.2f);
            _magic = Create("Magic", art.Spark, _unlit, 24, 0f, 2.2f, grow: 0.4f);
            _fire = Create("Fire", art.Puff, _unlit, 23, -0.08f, 1.4f, grow: 0.5f, fireColors: true);
            _water = Create("Water", art.Pixel, _unlit, 21, 0.35f, 0.3f, grow: 1f);
        }

        ParticleSystem Create(string name, Sprite sprite, Material material, int order, float gravity, float drag, float grow,
                              bool spin = false, bool fireColors = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = gravity;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = drag > 0f;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (fireColors)
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.35f), new GradientColorKey(new Color(0.6f, 0.12f, 0.05f), 1f) },
                          new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));

            if (spin)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            }

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Sprites;
            tsa.AddSprite(sprite);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = order;
            if (material != null) r.sharedMaterial = material;
            ps.Play();
            return ps;
        }

        /// <summary>Emits particles flying out from a point (optionally pushed one way and spread over a disc).</summary>
        void Burst(ParticleSystem ps, Vector3 pos, int count, Color color, float speedMin, float speedMax, float sizeMin, float sizeMax,
                   float lifeMin, float lifeMax, Vector2 bias = default, float radius = 0f, float colorJitter = 0.1f)
        {
            if (ps == null) return;
            // Glowing specks read better on a phone a bit bigger than physical scale.
            if (ps == _sparks || ps == _magic) { sizeMin *= 1.4f; sizeMax *= 1.4f; }
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                var offset = Random.insideUnitCircle * radius;
                ep.position = pos + new Vector3(offset.x, offset.y, 0f);
                ep.velocity = (Vector3)(dir * Random.Range(speedMin, speedMax) + bias);
                float j = 1f + Random.Range(-colorJitter, colorJitter);
                ep.startColor = new Color(color.r * j, color.g * j, color.b * j, color.a);
                ep.startSize = Random.Range(sizeMin, sizeMax);
                ep.startLifetime = Random.Range(lifeMin, lifeMax);
                ep.rotation = Random.Range(0f, 360f);
                ps.Emit(ep, 1);
            }
        }

        /// <summary>Particles pulled into a point (implosion).</summary>
        void Implode(ParticleSystem ps, Vector3 pos, int count, Color color, float radius, float life)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                float r = radius * Random.Range(0.6f, 1f);
                ep.position = pos + (Vector3)(dir * r);
                // Tangential + inward: a short spiral into the pad.
                var tangent = new Vector2(-dir.y, dir.x);
                ep.velocity = (Vector3)((-dir * r / life) * 1.6f + tangent * 1.2f);
                ep.startColor = color;
                ep.startSize = Random.Range(0.08f, 0.16f);
                ep.startLifetime = life;
                ps.Emit(ep, 1);
            }
        }

        /// <summary>Particles streaming from one point to another (fire leaping from a sconce to the torch...).</summary>
        void Stream(ParticleSystem ps, Vector3 from, Vector3 to, int count, Color color, float life)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var jitter = Random.insideUnitCircle * 0.15f;
                ep.position = from + (Vector3)jitter;
                ep.velocity = (to - from) / life * Random.Range(0.85f, 1.25f) + (Vector3)(Random.insideUnitCircle * 0.4f);
                ep.startColor = color;
                ep.startSize = Random.Range(0.1f, 0.22f);
                ep.startLifetime = life * Random.Range(0.8f, 1.1f);
                ps.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ light flashes

        public void Flash(Vector3 pos, Color color, float radius = 2.5f, float intensity = 1.6f, float duration = 0.45f)
        {
            Light2D light = null;
            for (int i = 0; i < _flashes.Count; i++)
                if (_flashes[i].life <= 0f) { light = _flashes[i].light; _flashes.RemoveAt(i); break; }
            if (light == null)
            {
                if (_flashes.Count >= 6) { light = _flashes[0].light; _flashes.RemoveAt(0); }
                else
                {
                    var go = new GameObject("Flash");
                    go.transform.SetParent(transform, false);
                    light = go.AddComponent<Light2D>();
                    light.lightType = Light2D.LightType.Point;
                    light.pointLightInnerRadius = 0.1f;
                    light.falloffIntensity = 0.6f;
                }
            }
            light.transform.position = pos;
            light.color = color;
            light.pointLightOuterRadius = radius;
            light.intensity = intensity;
            light.enabled = true;
            _flashes.Add((light, duration, duration, intensity));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _flashes.Count; i++)
            {
                var f = _flashes[i];
                if (f.life <= 0f) continue;
                f.life -= dt;
                float k = Mathf.Clamp01(f.life / f.duration);
                f.light.intensity = f.peak * k * k;
                if (f.life <= 0f) f.light.enabled = false;
                _flashes[i] = f;
            }
        }

        bool Rich => _settings == null || _settings.AdvancedLighting;

        // ------------------------------------------------------------------ gameplay effects

        Color DustColor => (Color)_art.Theme.FloorLight;

        public void Footstep(Vector3 pos)
        {
            if (!Rich) return;
            var c = DustColor; c.a = 0.45f;
            Burst(_dust, pos + new Vector3(0f, -0.3f, 0f), 3, c, 0.15f, 0.4f, 0.12f, 0.22f, 0.35f, 0.6f, radius: 0.12f);
        }

        public void Bump(Vector3 pos, Dir d)
        {
            var c = DustColor; c.a = 0.5f;
            var at = pos + new Vector3(d.Dx(), d.Dy(), 0f) * 0.45f;
            Burst(_dust, at, 5, c, 0.2f, 0.5f, 0.1f, 0.2f, 0.3f, 0.5f);
            Burst(_debris, at, 3, (Color)_art.Theme.WallLight, 0.6f, 1.2f, 0.05f, 0.09f, 0.3f, 0.5f);
        }

        public void ButtonPressed(Vector3 pos, Color glow)
        {
            Burst(_magic, pos, 26, glow, 1.6f, 2.6f, 0.08f, 0.18f, 0.5f, 0.8f);
            var c = DustColor; c.a = 0.55f;
            Burst(_dust, pos, 8, c, 0.4f, 0.9f, 0.18f, 0.3f, 0.5f, 0.8f);
            Flash(pos, glow, 2.6f, 2f, 0.6f);
        }

        /// <summary>A door or a barrier changes state somewhere (shown only where the player can see).</summary>
        public void GateMoved(Vector3 pos, bool laser, Color color)
        {
            if (laser)
            {
                Burst(_sparks, pos, 18, color, 1.2f, 2.4f, 0.05f, 0.1f, 0.25f, 0.5f);
                Flash(pos, color, 1.8f, 1.6f, 0.35f);
                return;
            }
            var c = (Color)_art.Theme.WallLight; c.a = 0.75f;
            Burst(_dust, pos, 16, c, 0.4f, 1.2f, 0.2f, 0.4f, 0.6f, 1.1f, radius: 0.3f);
            Burst(_debris, pos, 10, (Color)_art.Theme.WallLight, 0.8f, 2f, 0.05f, 0.1f, 0.4f, 0.7f);
        }

        public void TeleportOut(Vector3 pos, bool cursed)
        {
            var c = cursed ? new Color(0.6f, 1f, 0.3f) : new Color(0.35f, 0.95f, 1f);
            Implode(_magic, pos, 30, c, 1.1f, 0.35f);
            Flash(pos, c, 2.2f, 2f, 0.4f);
        }

        public void TeleportIn(Vector3 pos, bool cursed)
        {
            var c = cursed ? new Color(0.6f, 1f, 0.3f) : new Color(0.35f, 0.95f, 1f);
            Burst(_magic, pos, 34, c, 1.4f, 3f, 0.08f, 0.2f, 0.4f, 0.8f);
            Flash(pos, c, 3f, 2.4f, 0.6f);
        }

        public void Spikes(Vector3 pos)
        {
            Burst(_sparks, pos, 16, new Color(1f, 0.85f, 0.6f), 1.5f, 3f, 0.04f, 0.09f, 0.2f, 0.45f);
            Burst(_magic, pos, 8, new Color(0.9f, 0.1f, 0.05f), 0.6f, 1.4f, 0.08f, 0.16f, 0.3f, 0.6f);
        }

        public void Disarm(Vector3 pos)
        {
            Burst(_sparks, pos, 12, new Color(1f, 0.9f, 0.6f), 1f, 2.2f, 0.04f, 0.08f, 0.2f, 0.4f);
            Burst(_smoke, pos, 4, new Color(0.7f, 0.7f, 0.7f, 0.5f), 0.1f, 0.3f, 0.2f, 0.35f, 0.6f, 1f);
        }

        public void Darkness(Vector3 pos)
        {
            Burst(_magic, pos, 30, new Color(0.45f, 0.15f, 0.75f), 0.4f, 1.6f, 0.12f, 0.28f, 0.6f, 1.2f, radius: 0.3f);
            Flash(pos, new Color(0.5f, 0.2f, 0.9f), 2.4f, 1.6f, 0.6f);
        }

        public void TorchSmothered(Vector3 pos)
        {
            var theme = _art.Theme.Kind;
            if (theme == TombTheme.Style.Flooded)
            {
                Splash(pos, null);
                Burst(_smoke, pos + new Vector3(0.3f, 0.3f, 0f), 6, new Color(0.9f, 0.9f, 0.9f, 0.6f), 0.1f, 0.4f, 0.15f, 0.3f, 0.8f, 1.3f, new Vector2(0f, 0.5f));
                return;
            }
            var c = DustColor; c.a = 0.75f;
            Burst(_dust, pos, 18, c, 0.4f, 1.3f, 0.2f, 0.45f, 0.8f, 1.4f, radius: 0.2f);
            Burst(_smoke, pos + new Vector3(0.3f, 0.3f, 0f), 6, new Color(0.4f, 0.38f, 0.36f, 0.7f), 0.1f, 0.3f, 0.15f, 0.3f, 0.8f, 1.4f, new Vector2(0f, 0.5f));
        }

        public void TorchRelit(Vector3 sconce, Vector3 torch, Color color)
        {
            Stream(_fire, sconce, torch, 18, color, 0.3f);
            Burst(_sparks, torch, 14, color, 0.8f, 2f, 0.05f, 0.1f, 0.3f, 0.6f);
            Flash(torch, color, 2.6f, 1.8f, 0.5f);
        }

        /// <summary>Water thrown up by a current or a puddle.</summary>
        public void Splash(Vector3 pos, Dir? flow)
        {
            var bias = flow.HasValue ? new Vector2(flow.Value.Dx(), flow.Value.Dy()) * 1.2f : Vector2.zero;
            Burst(_water, pos, 16, new Color(0.75f, 0.92f, 1f, 0.9f), 0.8f, 2f, 0.05f, 0.09f, 0.3f, 0.6f, bias);
            Burst(_dust, pos, 5, new Color(0.85f, 0.95f, 1f, 0.45f), 0.2f, 0.6f, 0.2f, 0.35f, 0.4f, 0.7f, bias * 0.5f);
        }

        public void Collapse(Vector3 pos)
        {
            var c = (Color)_art.Theme.FloorLight; c.a = 0.8f;
            Burst(_dust, pos, 20, c, 0.3f, 1.2f, 0.25f, 0.5f, 0.7f, 1.3f, radius: 0.3f);
            Burst(_debris, pos, 16, (Color)_art.Theme.Floor, 0.6f, 1.8f, 0.06f, 0.12f, 0.4f, 0.8f, radius: 0.3f);
        }

        public void Switched(Vector3 pos, bool on)
        {
            var c = on ? new Color(0.3f, 1f, 0.9f) : new Color(1f, 0.35f, 0.25f);
            Burst(_sparks, pos, 20, c, 1.2f, 2.8f, 0.04f, 0.1f, 0.25f, 0.5f);
            Flash(pos, c, 2.4f, 2f, 0.45f);
        }

        /// <summary>A flame jet blasting (also when nobody stands in it: the rhythm is visible).</summary>
        public void FireBlast(Vector3 pos, bool strong)
        {
            if (!Rich && !strong) return;
            Burst(_fire, pos, strong ? 26 : 14, Color.white, 0.4f, 1.4f, 0.25f, 0.5f, 0.4f, 0.8f, new Vector2(0f, 1.4f), 0.25f);
            Burst(_sparks, pos, strong ? 10 : 4, new Color(1f, 0.7f, 0.3f), 0.6f, 1.6f, 0.03f, 0.07f, 0.4f, 0.8f, new Vector2(0f, 1f));
            if (strong) Flash(pos, new Color(1f, 0.55f, 0.15f), 2.8f, 2.2f, 0.45f);
        }

        public void Fall(Vector3 pos)
        {
            var c = DustColor; c.a = 0.8f;
            Burst(_dust, pos, 18, c, 0.3f, 1.1f, 0.2f, 0.45f, 0.7f, 1.2f, radius: 0.25f);
            Burst(_debris, pos, 14, (Color)_art.Theme.Floor, 0.4f, 1.4f, 0.06f, 0.11f, 0.4f, 0.8f, radius: 0.25f);
        }

        public void Climb(Vector3 pos)
        {
            var c = DustColor; c.a = 0.5f;
            Burst(_dust, pos, 8, c, 0.2f, 0.6f, 0.15f, 0.3f, 0.5f, 0.8f);
        }

        public void Win(Vector3 pos)
        {
            Burst(_magic, pos, 60, new Color(1f, 0.85f, 0.4f), 1.5f, 4f, 0.08f, 0.22f, 0.8f, 1.5f, new Vector2(0f, 1f));
            Burst(_sparks, pos, 30, new Color(1f, 0.95f, 0.7f), 2f, 4.5f, 0.04f, 0.1f, 0.5f, 1f);
            Flash(pos, new Color(1f, 0.85f, 0.45f), 4.5f, 2.6f, 1.2f);
        }

        public void Death(Vector3 pos)
        {
            var c = DustColor; c.a = 0.85f;
            Burst(_dust, pos, 30, c, 0.3f, 1.5f, 0.25f, 0.5f, 0.9f, 1.6f, radius: 0.2f);
            Burst(_smoke, pos, 10, new Color(0.3f, 0.28f, 0.26f, 0.7f), 0.1f, 0.5f, 0.3f, 0.5f, 1f, 1.8f, new Vector2(0f, 0.4f));
        }

        // ------------------------------------------------------------------ looping tile effects

        public enum Loop { TorchEmbers, ExitMotes, PortalSwirl, DarkWisps, CurrentFoam, FireSmoke, BarrierSparks, Motes }

        /// <summary>A small looping emitter attached to a tile (local space: it follows the tile in the preview).</summary>
        public ParticleSystem CreateLoop(Loop kind, Transform parent, Color color, Dir flow = Dir.Up)
        {
            var go = new GameObject("Fx " + kind);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 40;
            main.startColor = color;
            var emission = ps.emission;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            var vel = ps.velocityOverLifetime;
            Sprite sprite = _art.Spark;
            Material mat = _unlit;
            int order = 3;
            float grow = 0.3f;

            switch (kind)
            {
                case Loop.TorchEmbers:
                    go.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                    emission.rateOverTime = 7f;
                    shape.radius = 0.1f;
                    shape.arc = 60f;
                    shape.rotation = new Vector3(0f, 0f, 60f);
                    order = 12;
                    break;
                case Loop.ExitMotes:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
                    main.startSpeed = 0.05f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                    emission.rateOverTime = 6f;
                    shape.radius = 0.45f;
                    vel.enabled = true;
                    vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                    order = 12;
                    break;
                case Loop.PortalSwirl:
                    main.startLifetime = 1.2f;
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                    emission.rateOverTime = 12f;
                    shape.radius = 0.38f;
                    shape.radiusThickness = 0f;
                    vel.enabled = true;
                    vel.orbitalZ = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
                    vel.radial = -0.25f;
                    order = 4;
                    break;
                case Loop.DarkWisps:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
                    main.startSpeed = 0.05f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                    emission.rateOverTime = 5f;
                    shape.radius = 0.35f;
                    vel.enabled = true;
                    vel.y = 0.2f;
                    sprite = _art.Puff;
                    order = 4;
                    break;
                case Loop.CurrentFoam:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                    emission.rateOverTime = 8f;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.8f, 0.8f, 0f);
                    vel.enabled = true;
                    vel.x = flow.Dx() * 1.1f;
                    vel.y = flow.Dy() * 1.1f;
                    sprite = _art.Pixel;
                    order = 4;
                    break;
                case Loop.FireSmoke:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
                    main.startSpeed = 0.05f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
                    emission.rateOverTime = 3f;
                    shape.radius = 0.25f;
                    vel.enabled = true;
                    vel.y = 0.35f;
                    sprite = _art.Puff;
                    mat = _lit;
                    order = 22;
                    grow = 2.2f;
                    break;
                case Loop.BarrierSparks:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
                    emission.rateOverTime = 9f;
                    shape.radius = 0.4f;
                    order = 6;
                    break;
                case Loop.Motes:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
                    main.startSpeed = 0.03f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                    emission.rateOverTime = 4f;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.6f, 1.8f, 0f);
                    go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                    vel.enabled = true;
                    vel.y = -0.12f;
                    order = 12;
                    break;
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Sprites;
            tsa.AddSprite(sprite);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = order;
            if (mat != null) r.sharedMaterial = mat;
            ps.Play();
            return ps;
        }
    }
}
