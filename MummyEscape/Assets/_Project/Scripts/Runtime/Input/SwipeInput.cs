using System;
using System.Collections.Generic;
using MummyEscape.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MummyEscape.Input
{
    /// <summary>
    /// One swipe = one move. A swipe fires as soon as the finger travels far enough (responsive), and a short tap
    /// is reported separately (used to disarm the trap you are looking at). Keyboard arrows / WASD (ZQSD on AZERTY,
    /// the Input System maps physical keys) work in the editor and on desktop.
    /// </summary>
    public sealed class SwipeInput : MonoBehaviour
    {
        public event Action<Dir> Swiped;
        public event Action<Vector2> Tapped;

        public bool Enabled { get; set; }

        const float SwipeInches = 0.22f;
        const float TapMaxSeconds = 0.3f;

        Vector2 _start;
        int _touchId = -1;
        float _startTime;
        bool _tracking;
        bool _consumed;
        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        float SwipePixels => SwipeInches * (Screen.dpi > 0 ? Screen.dpi : 200f);

        void Update()
        {
            if (!Enabled) { _tracking = false; return; }

            ReadKeyboard();

            bool pressed, down, up;
            Vector2 pos, start;
            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
            {
                var t = touch.primaryTouch;
                pressed = t.press.isPressed;
                down = t.press.wasPressedThisFrame;
                up = t.press.wasReleasedThisFrame;
                pos = t.position.ReadValue();
                // Measure from where this very touch began: on the press frame of a quick second swipe, the position
                // can still be where the previous finger left the glass, and the gesture then read backwards.
                start = t.startPosition.ReadValue();
                int id = t.touchId.ReadValue();
                if (pressed && id != _touchId) { down = true; _touchId = id; } // a new touch, even if its press was missed
            }
            else if (Mouse.current != null)
            {
                var m = Mouse.current;
                pressed = m.leftButton.isPressed;
                down = m.leftButton.wasPressedThisFrame;
                up = m.leftButton.wasReleasedThisFrame;
                pos = m.position.ReadValue();
                start = pos;
            }
            else return;

            if (down)
            {
                _tracking = !IsOverUi(start);
                _consumed = false;
                _start = start;
                _startTime = Time.unscaledTime;
            }
            if (!_tracking) return;

            if (pressed && !_consumed)
            {
                Vector2 delta = pos - _start;
                if (delta.magnitude >= SwipePixels)
                {
                    _consumed = true;
                    Swiped?.Invoke(Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                        ? (delta.x > 0 ? Dir.Right : Dir.Left)
                        : (delta.y > 0 ? Dir.Up : Dir.Down));
                }
            }

            if (up)
            {
                if (!_consumed && Time.unscaledTime - _startTime <= TapMaxSeconds) Tapped?.Invoke(pos);
                _tracking = false;
                _touchId = -1;
            }
        }

        void ReadKeyboard()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Swiped?.Invoke(Dir.Up);
            else if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Swiped?.Invoke(Dir.Down);
            else if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) Swiped?.Invoke(Dir.Left);
            else if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) Swiped?.Invoke(Dir.Right);
        }

        bool IsOverUi(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            _hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screenPos }, _hits);
            return _hits.Count > 0;
        }
    }
}
