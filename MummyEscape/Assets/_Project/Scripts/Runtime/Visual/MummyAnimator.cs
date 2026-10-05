using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.Visual
{
    /// <summary>
    /// Shows a mummy outfit on a sprite renderer (in game) or a UI image (menus, shop), and plays the loop of a legendary
    /// colour. A plain outfit gets its single sprite and the component stays asleep.
    /// </summary>
    public sealed class MummyAnimator : MonoBehaviour
    {
        public const float Fps = 8f;

        ArtLibrary _art;
        Loadout _look;
        bool _portrait;
        SpriteRenderer _renderer;
        Image _image;
        int _frame = -1;

        /// <summary>The in-game mummy (no flame: the torch is a separate sprite).</summary>
        public static void Show(SpriteRenderer target, ArtLibrary art, Loadout look) => Attach(target.gameObject, art, look, false, target, null);

        /// <summary>A portrait with its torch lit.</summary>
        public static void Show(Image target, ArtLibrary art, Loadout look) => Attach(target.gameObject, art, look, true, null, target);

        static void Attach(GameObject go, ArtLibrary art, Loadout look, bool portrait, SpriteRenderer renderer, Image image)
        {
            var a = go.GetComponent<MummyAnimator>();
            if (a == null)
            {
                if (!look.Animated)
                {
                    // The common case: one sprite, no component.
                    var sprite = portrait ? art.MummyPortrait(look) : art.Mummy(look);
                    if (renderer != null) renderer.sprite = sprite; else image.sprite = sprite;
                    return;
                }
                a = go.AddComponent<MummyAnimator>();
            }
            a._art = art;
            a._look = look;
            a._portrait = portrait;
            a._renderer = renderer;
            a._image = image;
            a._frame = -1;
            a.enabled = look.Animated;
            a.Apply(look.Animated ? CurrentFrame : 0);
        }

        static int CurrentFrame => (int)(Time.unscaledTime * Fps) % ArtLibrary.LegendaryFrames;

        void Update() => Apply(CurrentFrame);

        void Apply(int frame)
        {
            if (frame == _frame || _art == null) return;
            _frame = frame;
            var sprite = _portrait ? _art.MummyPortrait(_look, frame) : _art.Mummy(_look, frame);
            if (_renderer != null) _renderer.sprite = sprite;
            if (_image != null) _image.sprite = sprite;
        }
    }
}
