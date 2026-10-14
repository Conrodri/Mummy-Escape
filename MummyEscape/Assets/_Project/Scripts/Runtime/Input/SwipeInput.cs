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
    /// the Input System maps physical keys) work in the editor and on desktop. A finger left down after its swipe holds
    /// that direction (<see cref="Held"/>, repeated by the game like the joystick); dragged a swipe's length another way
    /// it turns, lifted it stops.
    /// </summary>
    public sealed class SwipeInput : MonoBehaviour
    {
        public event Action<Dir> Swiped;
        public event Action<Vector2> Tapped;
        /// <summary>Raised just before the swipe it holds; null when the finger lifts or input is turned off.</summary>
        public event Action<Dir?> Held;

        /// <summary>
        /// Seconds a swipe's finger must stay down before its move repeats: a quick flick lifts well within this and stays
        /// one move, even when the step animation ends sooner.
        /// </summary>
        public const float HoldDelay = 0.25f;

        public bool Enabled { get; set; }

        const float SwipeInches = 0.22f;
        const float TapMaxSeconds = 0.3f;
        // A tap barely moves: a swipe too short to count is not a tap (it would step toward the tile under the finger,
        // often the opposite way when spamming swipes from below or beside the mummy).
        const float TapMaxInches = 0.06f;

        Vector2 _start;
        int _touchId = -1;
        float _startTime;
        float _travel;
        bool _tracking;
        bool _consumed;
        bool _fresh;
        Dir? _held;
        Vector2 _anchor; // where the held direction was last confirmed: a turn is measured from here
        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        static float Dpi => Screen.dpi > 0 ? Screen.dpi : 200f;
        float SwipePixels => SwipeInches * Dpi;

        void Update()
        {
            if (!Enabled) { _tracking = false; Release(); return; }

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

            if (!pressed) Release(); // released, cancelled, or a release frame missed
            if (down)
            {
                Release();
                _tracking = !IsOverUi(start);
                _consumed = false;
                _start = start;
                _startTime = Time.unscaledTime;
                _travel = 0f;
                // On the press frame of a quick next swipe, the start or the position can still be the previous finger's:
                // the gesture is only read from the next frame on.
                _fresh = true;
            }
            if (!_tracking) return;
            if (_fresh) { _fresh = false; if (!up) return; }
            if (touch != null && _touchId >= 0) _start = start; // the touch's own start, settled by now

            _travel = Mathf.Max(_travel, (pos - _start).magnitude);
            if (pressed && !_consumed)
            {
                Vector2 delta = pos - _start;
                if (delta.magnitude >= SwipePixels)
                {
                    _consumed = true;
                    Hold(ToDir(delta), pos);
                }
            }
            else if (pressed && _held.HasValue && (pos - _anchor).magnitude >= SwipePixels)
            {
                // Still down: dragging on the same way keeps going, a swipe's length another way turns (and moves at once).
                var dir = ToDir(pos - _anchor);
                _anchor = pos;
                if (dir != _held) Hold(dir, pos);
            }

            if (up)
            {
                if (!_consumed && Time.unscaledTime - _startTime <= TapMaxSeconds && _travel <= TapMaxInches * Dpi) Tapped?.Invoke(pos);
                _tracking = false;
                _touchId = -1;
            }
        }

        static Dir ToDir(Vector2 delta) => Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? (delta.x > 0 ? Dir.Right : Dir.Left)
            : (delta.y > 0 ? Dir.Up : Dir.Down);

        void Hold(Dir dir, Vector2 pos)
        {
            _held = dir;
            _anchor = pos;
            Held?.Invoke(dir); // before the move, so a bump into a wall stops the repeat
            Swiped?.Invoke(dir);
        }

        void Release()
        {
            if (!_held.HasValue) return;
            _held = null;
            Held?.Invoke(null);
        }

        void OnDisable() => Release();

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
