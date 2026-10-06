using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// Small motion effects for the menus: cards popping in one after the other, a slow pulse on what wants a tap, a
    /// shine sweeping a banner, a turning glow, blinking lights. All run on unscaled time (menus open over a paused game).
    /// </summary>
    public static class UIFx
    {
        /// <summary>Fades and scales the element in when it appears, after <paramref name="delay"/> seconds.</summary>
        public static void PopIn(Component c, float delay = 0f, float from = 0.86f)
        {
            var pop = c.gameObject.GetComponent<PopIn>() ?? c.gameObject.AddComponent<PopIn>();
            pop.Delay = delay;
            pop.From = from;
            // Already played once (a reused screen): play it again.
            if (pop.isActiveAndEnabled) { pop.enabled = false; pop.enabled = true; }
            else pop.enabled = true;
        }

        /// <summary>Breathes the element's scale (1 ± amount) with the given period.</summary>
        public static Pulse Pulse(Component c, float amount = 0.035f, float period = 1.6f)
        {
            var p = c.gameObject.GetComponent<Pulse>() ?? c.gameObject.AddComponent<Pulse>();
            p.Amount = amount;
            p.Period = period;
            return p;
        }

        /// <summary>Turns the element around its pivot (degrees per second, negative = clockwise).</summary>
        public static void Spin(Component c, float speed) =>
            (c.gameObject.GetComponent<Spin>() ?? c.gameObject.AddComponent<Spin>()).Speed = speed;

        /// <summary>Makes the element float up and down (it must not be placed by a layout group).</summary>
        public static void Float(Component c, float height = 10f, float period = 2.6f)
        {
            var f = c.gameObject.GetComponent<Float>() ?? c.gameObject.AddComponent<Float>();
            f.Height = height;
            f.Period = period;
        }

        /// <summary>Alternates the graphic's alpha between low and full (phase in turns, 0-1).</summary>
        public static void Blink(Graphic g, float period, float phase, float low = 0.25f)
        {
            var b = g.gameObject.GetComponent<Blink>() ?? g.gameObject.AddComponent<Blink>();
            b.Period = period;
            b.Phase = phase;
            b.Low = low;
        }

        /// <summary>A bright diagonal band crossing the element every <paramref name="period"/> seconds (clipped to it).</summary>
        public static void Shine(RectTransform target, float period = 3.5f, float alpha = 0.22f)
        {
            if (target.GetComponent<RectMask2D>() == null) target.gameObject.AddComponent<RectMask2D>();
            var band = UIKit.Image(target, UIKit.Art.White, new Color(1f, 1f, 1f, alpha), false, "Shine"); // noloc
            band.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = band.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(90, 1400);
            rt.localRotation = Quaternion.Euler(0, 0, -24);
            var g = band.gameObject.AddComponent<UIGradient>();
            g.Top = g.Bottom = Color.white;
            band.gameObject.AddComponent<Sweep>().Period = period;
        }

        /// <summary>Vertical gradient over a graphic (multiplied with its colour).</summary>
        public static UIGradient Gradient(Graphic g, Color top, Color bottom)
        {
            var grad = g.GetComponent<UIGradient>() ?? g.gameObject.AddComponent<UIGradient>();
            grad.Top = top;
            grad.Bottom = bottom;
            g.SetVerticesDirty();
            return grad;
        }

        /// <summary>Rounded progress track; returns the fill (set its fillAmount).</summary>
        public static Image Bar(Transform parent, float height, Color color, string name = "Bar")
        {
            var track = UIKit.Plate(parent, new Color(0, 0, 0, 0.5f), height / 2f, UIKit.Rim, false, name);
            UIKit.Size(track, height);
            var fill = UIKit.Image(track.transform, UIKit.Art.White, color, false, "Fill"); // noloc
            UIKit.Stretch(fill.rectTransform, 4, 4, 4, 4);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            return fill;
        }

        /// <summary>A soft coloured halo behind an element (not laid out), optionally turning.</summary>
        public static Image Halo(Transform parent, Color color, float size, float spin = 0f)
        {
            var glow = UIKit.Image(parent, UISprites.RadialGlow, color, false, "Halo"); // noloc
            glow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(glow.rectTransform, 0.5f, 0.5f, size, size);
            glow.transform.SetAsFirstSibling();
            if (spin != 0f) Spin(glow, spin);
            return glow;
        }
    }

    public sealed class PopIn : MonoBehaviour
    {
        public float Delay;
        public float From = 0.86f;
        const float Duration = 0.28f;
        float _t;
        CanvasGroup _group;

        void OnEnable()
        {
            _t = -Delay;
            _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            Apply(0f);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Duration);
            Apply(k);
            if (k >= 1f) enabled = false;
        }

        void Apply(float k)
        {
            // Ease out with a small overshoot.
            float e = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
            float s = Mathf.LerpUnclamped(From, 1f, k <= 0f ? 0f : e);
            transform.localScale = new Vector3(s, s, 1f);
            if (_group != null) _group.alpha = Mathf.Clamp01(k * 1.6f);
        }

        void OnDisable()
        {
            transform.localScale = Vector3.one;
            if (_group != null) _group.alpha = 1f;
        }
    }

    public sealed class Pulse : MonoBehaviour
    {
        public float Amount = 0.035f;
        public float Period = 1.6f;

        void Update()
        {
            float s = 1f + Amount * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Period);
            transform.localScale = new Vector3(s, s, 1f);
        }

        void OnDisable() => transform.localScale = Vector3.one;
    }

    public sealed class Spin : MonoBehaviour
    {
        public float Speed = 20f;
        void Update() => transform.Rotate(0, 0, Speed * Time.unscaledDeltaTime);
    }

    public sealed class Float : MonoBehaviour
    {
        public float Height = 10f;
        public float Period = 2.6f;
        Vector2 _base;
        bool _set;

        void OnEnable()
        {
            if (!_set) { _base = ((RectTransform)transform).anchoredPosition; _set = true; }
        }

        void Update() =>
            ((RectTransform)transform).anchoredPosition = _base + new Vector2(0, Height * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Period));
    }

    public sealed class Blink : MonoBehaviour
    {
        public float Period = 1f;
        public float Phase;
        public float Low = 0.25f;
        Graphic _g;
        float _alpha = -1f;

        void Update()
        {
            if (_g == null) { _g = GetComponent<Graphic>(); _alpha = _g.color.a; }
            float k = Mathf.Repeat(Time.unscaledTime / Period + Phase, 1f) < 0.5f ? 1f : Low;
            var c = _g.color;
            c.a = _alpha * k;
            _g.color = c;
        }
    }

    /// <summary>Moves a shine band across its parent's width, then waits for the next pass.</summary>
    public sealed class Sweep : MonoBehaviour
    {
        public float Period = 3.5f;
        const float Pass = 0.9f;

        void Update()
        {
            var rt = (RectTransform)transform;
            float width = ((RectTransform)rt.parent).rect.width;
            float t = Mathf.Repeat(Time.unscaledTime, Period);
            float k = Mathf.Clamp01(t / Pass);
            rt.anchoredPosition = new Vector2(Mathf.Lerp(-200f, width + 200f, k * k * (3f - 2f * k)), 0);
        }
    }
}
