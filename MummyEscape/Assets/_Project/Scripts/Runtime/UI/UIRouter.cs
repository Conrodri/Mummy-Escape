using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    public abstract class UIScreen : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        protected UIRouter Router { get; private set; }
        protected App.GameApp App => MummyEscape.App.GameApp.I;

        /// <summary>Modal screens overlay the current one instead of replacing it.</summary>
        public virtual bool IsModal => false;

        public void Setup(UIRouter router, RectTransform root)
        {
            Router = router;
            Root = root;
            Build();
        }

        protected abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }

        /// <summary>Standard header: back button + title.</summary>
        protected Text Header(string title, Action onBack = null)
        {
            var bar = UIKit.Rect("Header", Root);
            UIKit.TopBand(bar, 140, 20);
            var back = UIKit.IconButton(bar, UISprites.Back, onBack ?? (() => Router.Back()), 92);
            UIKit.Place((RectTransform)back.transform, 0, 0.5f, 92, 92, 36, 0);
            var t = UIKit.Title(bar, title, 54);
            UIKit.FitText(t, 34);
            UIKit.Stretch(t.rectTransform, 150, 0, 150, 0);
            return t;
        }

        /// <summary>Main content area below the header.</summary>
        protected RectTransform Body(float top = 190, float bottom = 40, float side = 50)
        {
            var body = UIKit.Rect("Body", Root);
            UIKit.Stretch(body, side, top, side, bottom);
            return body;
        }
    }

    /// <summary>Owns the canvas and the screen stack.</summary>
    public sealed class UIRouter : MonoBehaviour
    {
        readonly Dictionary<Type, UIScreen> _screens = new Dictionary<Type, UIScreen>();
        readonly Stack<UIScreen> _history = new Stack<UIScreen>();
        readonly List<UIScreen> _modals = new List<UIScreen>();
        RectTransform _safeArea;
        Rect _lastSafeArea;

        public UIScreen Current => _history.Count > 0 ? _history.Peek() : null;

        public void Init()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _safeArea = UIKit.Rect("SafeArea", canvasGo.transform);
            ApplySafeArea();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        void Update()
        {
            if (Screen.safeArea != _lastSafeArea) ApplySafeArea();
            if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) Back();
        }

        void ApplySafeArea()
        {
            _lastSafeArea = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var min = _lastSafeArea.position;
            var max = _lastSafeArea.position + _lastSafeArea.size;
            _safeArea.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
            _safeArea.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
            _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
        }

        public T Get<T>() where T : UIScreen
        {
            if (_screens.TryGetValue(typeof(T), out var s)) return (T)s;
            var root = UIKit.Stretch(UIKit.Rect(typeof(T).Name, _safeArea));
            var screen = root.gameObject.AddComponent<T>();
            screen.Setup(this, root);
            root.gameObject.SetActive(false);
            _screens[typeof(T)] = screen;
            return screen;
        }

        /// <summary>Shows a screen. Full screens replace the current one (kept in history); modals overlay it.</summary>
        public T Open<T>() where T : UIScreen
        {
            var screen = Get<T>();
            if (screen.IsModal)
            {
                if (!_modals.Contains(screen)) _modals.Add(screen);
                screen.transform.SetAsLastSibling();
                Activate(screen);
                return screen;
            }
            CloseAllModals();
            if (Current != null && Current != screen) Deactivate(Current);
            if (Current != screen) _history.Push(screen);
            screen.transform.SetAsFirstSibling();
            Activate(screen);
            return screen;
        }

        /// <summary>Replaces the whole history (e.g. going back to the main menu).</summary>
        public T Reset<T>() where T : UIScreen
        {
            CloseAllModals();
            while (_history.Count > 0) Deactivate(_history.Pop());
            return Open<T>();
        }

        /// <summary>Destroys every screen (rebuilt on next use), e.g. after a language change. Open the wanted screens again afterwards.</summary>
        public void RebuildAll()
        {
            CloseAllModals();
            while (_history.Count > 0) Deactivate(_history.Pop());
            foreach (var s in _screens.Values) if (s != null) Destroy(s.gameObject);
            _screens.Clear();
        }

        public void Close(UIScreen modal)
        {
            if (!_modals.Remove(modal)) return;
            Deactivate(modal);
        }

        public void Back()
        {
            if (_modals.Count > 0)
            {
                var top = _modals[_modals.Count - 1];
                if (top is IBackHandler h) h.OnBack(); else Close(top);
                return;
            }
            if (Current is IBackHandler handler) { handler.OnBack(); return; }
            if (_history.Count <= 1) return;
            Deactivate(_history.Pop());
            Activate(Current);
        }

        void CloseAllModals()
        {
            foreach (var m in _modals) Deactivate(m);
            _modals.Clear();
        }

        static void Activate(UIScreen s)
        {
            bool wasActive = s.gameObject.activeSelf;
            s.gameObject.SetActive(true);
            s.OnShow();
            if (!wasActive) s.StartCoroutine(FadeIn(s));
        }

        /// <summary>Screens fade in; modals also settle from a slightly smaller size.</summary>
        static System.Collections.IEnumerator FadeIn(UIScreen s)
        {
            var group = s.GetComponent<CanvasGroup>() ?? s.gameObject.AddComponent<CanvasGroup>();
            const float duration = 0.18f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                k = 1f - (1f - k) * (1f - k);
                group.alpha = k;
                if (s.IsModal) s.Root.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
                yield return null;
            }
            group.alpha = 1f;
            s.Root.localScale = Vector3.one;
        }

        static void Deactivate(UIScreen s)
        {
            if (!s.gameObject.activeSelf) return;
            s.OnHide();
            s.gameObject.SetActive(false);
        }
    }

    /// <summary>Screens that want custom behaviour on Back / Escape (e.g. HUD opens pause).</summary>
    public interface IBackHandler
    {
        void OnBack();
    }
}
