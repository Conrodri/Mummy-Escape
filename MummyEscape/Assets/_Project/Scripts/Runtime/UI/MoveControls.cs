using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// On-screen movement, for the players who prefer it to swiping (Settings › Déplacements): a directional pad (one press,
    /// one move) or a joystick (held in a direction, the mummy keeps going). Swipes still work beside them.
    /// </summary>
    public sealed class MoveControls : MonoBehaviour
    {
        const float Size = 340f;
        static readonly Color Base = new Color(0.05f, 0.035f, 0.02f, 0.55f);

        GameController _game;
        RectTransform _root;
        GameObject _pad, _stick;
        MoveControl _mode = (MoveControl)(-1);

        public static MoveControls Create(Transform parent, GameController game)
        {
            var rt = UIKit.Rect("MoveControls", parent); // noloc
            // Bottom left, above the bottom bar (the map and disarm buttons stay on the right).
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(Size, Size);
            rt.anchoredPosition = new Vector2(40, 230);
            var c = rt.gameObject.AddComponent<MoveControls>();
            c._game = game;
            c._root = rt;
            return c;
        }

        /// <summary>Shows the control the player chose (nothing for swipes).</summary>
        public void Apply(MoveControl mode)
        {
            if (mode == _mode) return;
            _mode = mode;
            _game.HoldMove(null);
            if (mode == MoveControl.Pad && _pad == null) _pad = BuildPad();
            if (mode == MoveControl.Joystick && _stick == null) _stick = BuildStick();
            if (_pad != null) _pad.SetActive(mode == MoveControl.Pad);
            if (_stick != null) _stick.SetActive(mode == MoveControl.Joystick);
        }

        void OnDisable()
        {
            if (_game != null) _game.HoldMove(null);
        }

        GameObject BuildPad()
        {
            var pad = UIKit.Rect("Pad", _root); // noloc
            UIKit.Stretch(pad);
            float b = Size * 0.36f;
            for (int i = 0; i < 4; i++)
            {
                var dir = (Dir)i;
                var key = UIKit.Image(pad, UISprites.PixelButton, Base, true, "Key"); // noloc
                UIKit.Pixelated(key, UISprites.PixelButton);
                key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                key.rectTransform.sizeDelta = new Vector2(b, b);
                key.rectTransform.anchoredPosition = new Vector2(dir.Dx(), dir.Dy()) * (Size * 0.5f - b * 0.5f);
                var arrow = UIKit.Image(key.transform, UISprites.Arrow, UIKit.Sand, false, "Arrow"); // noloc
                arrow.rectTransform.sizeDelta = new Vector2(b * 0.55f, b * 0.55f);
                arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -90f * i);
                var press = key.gameObject.AddComponent<PadKey>();
                press.Pressed = () => _game.PressMove(dir);
                press.Image = key;
            }
            return pad.gameObject;
        }

        GameObject BuildStick()
        {
            var ring = UIKit.Image(_root, UISprites.Circle, Base, true, "Stick"); // noloc
            UIKit.Stretch(ring.rectTransform);
            var rim = UIKit.Image(ring.transform, UISprites.Ring, new Color(UIKit.Sand.r, UIKit.Sand.g, UIKit.Sand.b, 0.5f), false, "Rim"); // noloc
            UIKit.Stretch(rim.rectTransform);
            var knob = UIKit.Image(ring.transform, UISprites.Circle, UIKit.Gold, false, "Knob"); // noloc
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            knob.rectTransform.sizeDelta = new Vector2(Size * 0.38f, Size * 0.38f);
            var stick = ring.gameObject.AddComponent<Stick>();
            stick.Init(_game, ring.rectTransform, knob.rectTransform, Size * 0.5f);
            return ring.gameObject;
        }

        /// <summary>A pad key: moves on press (not on release, for speed), lit while held.</summary>
        sealed class PadKey : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public System.Action Pressed;
            public Image Image;

            public void OnPointerDown(PointerEventData e)
            {
                Image.color = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.8f);
                Pressed?.Invoke();
            }

            public void OnPointerUp(PointerEventData e) => Image.color = Base;
        }

        /// <summary>
        /// The joystick: past a dead zone, the knob's main axis is the direction. The first move goes at once, then it
        /// repeats while held (<see cref="GameController.HoldMove"/>); turning the knob turns the mummy.
        /// </summary>
        sealed class Stick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            const float DeadZone = 0.3f;

            GameController _game;
            RectTransform _area, _knob;
            float _radius;
            Dir? _dir;

            public void Init(GameController game, RectTransform area, RectTransform knob, float radius)
            {
                _game = game;
                _area = area;
                _knob = knob;
                _radius = radius;
            }

            public void OnPointerDown(PointerEventData e) => Track(e);
            public void OnDrag(PointerEventData e) => Track(e);

            public void OnPointerUp(PointerEventData e)
            {
                _knob.anchoredPosition = Vector2.zero;
                _dir = null;
                _game.HoldMove(null);
            }

            void OnDisable()
            {
                if (_knob != null) _knob.anchoredPosition = Vector2.zero;
                _dir = null;
            }

            void Track(PointerEventData e)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, e.pressEventCamera, out var local)) return;
                local -= _area.rect.center;
                var offset = Vector2.ClampMagnitude(local, _radius * 0.62f);
                _knob.anchoredPosition = offset;
                Dir? dir = null;
                if (local.magnitude >= _radius * DeadZone)
                    dir = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (local.x > 0 ? Dir.Right : Dir.Left) : (local.y > 0 ? Dir.Up : Dir.Down);
                if (dir == _dir) return;
                _dir = dir;
                _game.HoldMove(dir);
                if (dir.HasValue) _game.PressMove(dir.Value);
            }
        }
    }
}
