using MummyEscape.Services;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MummyEscape.World
{
    /// <summary>Top-down orthographic camera: smooth follow, map peek (zoom out) and optional screen shake.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>Tiles visible across the screen width in normal play.</summary>
        public float TilesAcross = 7.5f;

        public Camera Cam { get; private set; }

        SettingsService _settings;
        Vector3 _target;
        float _mapSize;
        bool _mapView;
        float _trauma;

        public void Init(SettingsService settings)
        {
            _settings = settings;
            Cam = gameObject.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.02f, 0.015f, 0.01f);
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 50f;
            Cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            gameObject.AddComponent<AudioListener>();
            _base = _target = transform.position = new Vector3(0, 0, -10);
            Cam.orthographicSize = NormalSize;
        }

        float NormalSize => TilesAcross / Mathf.Max(0.1f, Cam.aspect) * 0.5f;

        public void Follow(Vector3 worldPos) => _target = new Vector3(worldPos.x, worldPos.y, -10f);

        public void SnapTo(Vector3 worldPos)
        {
            Follow(worldPos);
            _base = _target;
            transform.position = _target;
        }

        /// <summary>Zoom out to see the whole explored floor (hold the map button).</summary>
        public void SetMapView(bool on, Vector3 center, float halfExtent)
        {
            _mapView = on;
            _mapSize = Mathf.Max(halfExtent / Mathf.Max(0.1f, Cam.aspect), halfExtent) + 1f;
            if (on) _target = new Vector3(center.x, center.y, -10f);
        }

        /// <summary>
        /// Frames a whole area (the start-of-run map preview). The HUD bands cover the top and bottom of the screen,
        /// so only ~78% of the height is usable.
        /// </summary>
        public void ShowArea(Bounds area)
        {
            const float usableHeight = 0.78f;
            float halfH = (area.extents.y + 0.6f) / usableHeight;
            float halfW = (area.extents.x + 0.6f) / Mathf.Max(0.1f, Cam.aspect);
            _mapView = true;
            _mapSize = Mathf.Max(halfH, halfW);
            _target = new Vector3(area.center.x, area.center.y + _mapSize * 0.02f, -10f);
        }

        public void EndArea(Vector3 follow)
        {
            _mapView = false;
            Follow(follow);
        }

        public void Shake(float amount)
        {
            if (_settings.ScreenShake) _trauma = Mathf.Clamp01(_trauma + amount);
        }

        Vector3 _base;

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime * 10f);
            _base = Vector3.Lerp(_base, _target, k);
            Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, _mapView ? _mapSize : NormalSize, k);

            _trauma = Mathf.Max(0f, _trauma - Time.deltaTime * 1.8f);
            float s = _trauma * _trauma * 0.35f;
            float t = Time.time * 30f;
            var shake = new Vector3((Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * s, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * s, 0f);
            transform.position = _base + shake;
        }
    }
}
