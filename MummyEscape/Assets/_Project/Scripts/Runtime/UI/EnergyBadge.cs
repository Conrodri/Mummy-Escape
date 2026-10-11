using MummyEscape.App;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using UnityEngine;
using UnityEngine.UI;
using PlayMode = MummyEscape.Monetization.PlayMode;

namespace MummyEscape.UI
{
    /// <summary>
    /// The energy of a mode, the same plate on every screen that spends it: a bolt, "ÉNERGIE" over the points left, and
    /// the time before the next one (or "Pass actif" when the pass lifts the limit). Keeps itself up to date.
    /// </summary>
    public sealed class EnergyBadge : MonoBehaviour
    {
        PlayMode _mode;
        Text _caption, _value, _next;
        Image _bolt;
        float _nextRefresh;

        public static EnergyBadge Create(Transform parent, PlayMode mode, float height = 84)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.55f), false, "Energy"); // noloc
            UIKit.Size(plate, height);
            var row = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(16, 26, 8, 8);
            row.spacing = 16;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var disc = UIKit.Image(plate.transform, UISprites.Circle, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.22f), false, "Disc"); // noloc
            UIKit.Size(disc, height - 16, height - 16);
            var badge = plate.gameObject.AddComponent<EnergyBadge>();
            badge._mode = mode;
            badge._bolt = UIKit.Image(disc.transform, UISprites.Bolt, UIKit.Gold, false, "Bolt"); // noloc
            UIKit.Stretch(badge._bolt.rectTransform, 10, 10, 10, 10);

            var texts = UIKit.Rect("Texts", plate.transform); // noloc
            UIKit.Size(texts, height - 16, -1, 0);
            UIKit.Column(texts, 0, 0, TextAnchor.MiddleLeft);
            var title = badge._caption = UIKit.Label(texts, "", Mathf.RoundToInt(height * 0.24f), UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(title, height * 0.3f);
            badge._value = UIKit.Label(texts, "", Mathf.RoundToInt(height * 0.42f), UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            badge._value.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(badge._value, height * 0.48f);

            badge._next = UIKit.Label(plate.transform, "", Mathf.RoundToInt(height * 0.3f), UIKit.Dim, TextAnchor.MiddleRight);
            UIKit.FitText(badge._next, 14);
            UIKit.Size(badge._next, height - 16, -1, 1);
            badge.Refresh();
            return badge;
        }

        /// <summary>Which energy it shows (a result screen serves several modes).</summary>
        public PlayMode Mode
        {
            get => _mode;
            set { _mode = value; Refresh(); }
        }

        void OnEnable() => _nextRefresh = 0;

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            Refresh();
        }

        public void Refresh()
        {
            var app = GameApp.I;
            if (app == null || _value == null) return;
            _caption.text = Loc.T(_mode == PlayMode.Endless ? "Énergie sans fin" : _mode == PlayMode.Solo ? "Énergie" : "Énergie de combat").ToUpperInvariant();
            if (app.Save.Unlimited)
            {
                _value.text = Loc.T("Illimitée");
                _value.color = UIKit.Gold;
                _next.text = Loc.T("Pass actif");
                _next.color = UIKit.Gold;
                return;
            }
            var (left, max, next) = PlayGate.Meter(app, _mode);
            _value.text = left + " / " + max;
            _value.color = left > 0 ? UIKit.Sand : UIKit.Danger;
            _next.text = next > 0 ? Loc.F("+1 dans {0}", Energy.Clock(next)) : Loc.T("Pleine");
            _next.color = UIKit.Dim;
        }
    }
}
