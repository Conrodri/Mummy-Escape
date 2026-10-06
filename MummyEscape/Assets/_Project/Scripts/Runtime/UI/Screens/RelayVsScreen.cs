using System;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Before a 2v2, like a fighting game: the player's duo slams in from the left at the top, the rival duo from the right
    /// at the bottom, four mummies in their outfits with their names and titles, the duos' names and 2v2 Elo, then a big
    /// "VS". Not skippable: the four phones start the preview together when it ends.
    /// </summary>
    public sealed class RelayVsScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        const float Duration = 3f;
        const float Slide = 0.34f;
        const float Clash = 0.38f;
        const float FadeOut = 0.25f;
        const float Tilt = -7f;

        static readonly Color MeColor = new Color32(150, 104, 30, 255);
        static readonly Color RivalColor = new Color32(26, 120, 118, 255);

        CanvasGroup _group;
        Band _mine, _rivals;
        RectTransform _vs;
        Image _flash;
        float _t;
        bool _done;
        Action _then;

        sealed class Band
        {
            public RectTransform Holder;
            public readonly Image[] Portraits = new Image[2];
            public readonly Text[] Names = new Text[2];
            public readonly Text[] Titles = new Text[2];
            public Text Duo;
            public float From, Delay, Rest;
        }

        protected override void Build()
        {
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            var bg = UIKit.Image(Root, UIKit.Art.White, new Color(0.05f, 0.035f, 0.025f, 1f), true, "Back"); // noloc
            UIKit.Stretch(bg.rectTransform, -600, -600, -600, -600);

            _mine = MakeBand(true, 440);
            _rivals = MakeBand(false, -440);

            _flash = UIKit.Image(Root, UIKit.Art.White, new Color(1f, 0.96f, 0.85f, 0f), false, "Flash"); // noloc
            UIKit.Stretch(_flash.rectTransform, -600, -600, -600, -600);

            _vs = UIKit.Place(UIKit.Rect("Vs", Root), 0.5f, 0.5f, 560, 360); // noloc
            var vs = UIKit.Title(_vs, "VS", 260, UIKit.Gold); // noloc
            UIKit.Stretch(vs.rectTransform);
            UIKit.DropShadow(vs, 14, 0.7f);

            var hint = UIKit.Label(Root, "2v2 · le premier duo à la sortie gagne", 28, UIKit.Dim);
            UIKit.BottomBand(hint.rectTransform, 60, 40);
        }

        /// <summary>A duo's band: slanted stripe, two mummies side by side with name and title, the duo's name and Elo.</summary>
        Band MakeBand(bool mine, float y)
        {
            var band = new Band { Rest = y, From = (mine ? -1f : 1f) * 1500f, Delay = mine ? 0f : 0.1f };
            band.Holder = UIKit.Rect(mine ? "Mine" : "Rivals", Root); // noloc
            band.Holder.anchorMin = new Vector2(0, 0.5f);
            band.Holder.anchorMax = new Vector2(1, 0.5f);
            band.Holder.sizeDelta = new Vector2(0, 640);
            band.Holder.anchoredPosition = new Vector2(0, y);

            var stripe = UIKit.Image(band.Holder, UIKit.Art.White, mine ? MeColor : RivalColor, false, "Stripe"); // noloc
            UIKit.Place(stripe.rectTransform, 0.5f, 0.5f, 1900, 420);
            stripe.rectTransform.localRotation = Quaternion.Euler(0, 0, Tilt);
            var edge = UIKit.Image(stripe.transform, UIKit.Art.White, new Color(1f, 0.92f, 0.7f, 0.85f), false, "Edge"); // noloc
            edge.rectTransform.anchorMin = new Vector2(0, mine ? 0 : 1);
            edge.rectTransform.anchorMax = new Vector2(1, mine ? 0 : 1);
            edge.rectTransform.sizeDelta = new Vector2(0, 10);
            edge.rectTransform.anchoredPosition = Vector2.zero;

            band.Duo = UIKit.Title(band.Holder, "", 46, mine ? UIKit.Gold : UIKit.Turquoise);
            UIKit.FitText(band.Duo, 24);
            var duoRt = band.Duo.rectTransform;
            duoRt.anchorMin = new Vector2(0.05f, mine ? 1 : 0);
            duoRt.anchorMax = new Vector2(0.95f, mine ? 1 : 0);
            duoRt.pivot = new Vector2(0.5f, mine ? 1 : 0);
            duoRt.sizeDelta = new Vector2(0, 80);
            duoRt.anchoredPosition = Vector2.zero;
            UIKit.DropShadow(band.Duo, 6, 0.6f);

            for (int k = 0; k < 2; k++)
            {
                float ax = k == 0 ? 0.27f : 0.73f;
                var glow = UIKit.Image(band.Holder, UISprites.RadialGlow, new Color(1f, 0.85f, 0.5f, 0.3f), false, "Glow"); // noloc
                At(glow.rectTransform, ax, 400, 40);
                var portrait = band.Portraits[k] = UIKit.Image(band.Holder, null, Color.white, false, "Portrait"); // noloc
                At(portrait.rectTransform, ax, 300, 50);
                portrait.preserveAspect = true;
                // The rivals face the player's duo.
                if (!mine) portrait.rectTransform.localScale = new Vector3(-1, 1, 1);

                var name = band.Names[k] = UIKit.Title(band.Holder, "", 44, UIKit.Sand);
                UIKit.FitText(name, 22);
                Under(name.rectTransform, ax, -126, 70);
                UIKit.DropShadow(name, 5, 0.6f);
                var title = band.Titles[k] = UIKit.Label(band.Holder, "", 26, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.FitText(title, 16);
                Under(title.rectTransform, ax, -176, 40);
                UIKit.DropShadow(title, 4, 0.6f);
            }
            return band;
        }

        static void At(RectTransform rt, float ax, float size, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(0, y);
        }

        static void Under(RectTransform rt, float ax, float y, float height)
        {
            rt.anchorMin = new Vector2(ax - 0.22f, 0.5f);
            rt.anchorMax = new Vector2(ax + 0.22f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>Plays the screen for this match (the player's duo on top), then runs <paramref name="then"/>.</summary>
        public void Show(RelayMatch match, string me, Action then)
        {
            _then = then;
            _t = 0f;
            _done = false;
            _group.alpha = 1f;
            _vs.localScale = Vector3.zero;
            Fill(_mine, match.SideOf(me), me);
            Fill(_rivals, match.OtherSide(me), me);
            Place(0f);
            App.Audio.Play(Sfx.Versus);
        }

        void Fill(Band band, RelaySide side, string me)
        {
            band.Duo.text = (side.Name ?? "") + "  ·  " + Loc.F("Elo 2v2 {0}", side.Elo);
            for (int k = 0; k < 2; k++)
            {
                var runner = k < side.Runners.Count ? side.Runners[k] : null;
                bool isMe = runner != null && runner.PlayerId == me;
                var look = isMe ? App.Save.Loadout : PvpSkins.Loadout(runner?.Look);
                MummyAnimator.Show(band.Portraits[k], App.Art, look);
                band.Names[k].text = runner == null ? "?" : isMe ? (string.IsNullOrEmpty(App.Online.PlayerName) ? Loc.T("Toi") : App.Online.PlayerName) : runner.Name;
                band.Titles[k].text = TitleBook.Line(isMe ? TitleBook.Equipped(App) : runner?.Look?.Title);
            }
        }

        void Update()
        {
            if (_done) return;
            _t += Time.unscaledDeltaTime;
            Place(_t);
            if (_t >= Duration) Finish();
        }

        void Place(float t)
        {
            foreach (var band in new[] { _mine, _rivals })
            {
                float k = Mathf.Clamp01((t - band.Delay) / Slide);
                float x = Mathf.LerpUnclamped(band.From, 0f, BackOut(k));
                float drift = Mathf.Max(0f, t - Slide) * 12f * -Mathf.Sign(band.From);
                float shake = t > Clash ? Mathf.Max(0f, 1f - (t - Clash) / 0.3f) * 16f : 0f;
                band.Holder.anchoredPosition = new Vector2(x + drift + Mathf.Sin(t * 70f) * shake, band.Rest + Mathf.Cos(t * 61f) * shake);
                for (int p = 0; p < 2; p++)
                {
                    var rt = band.Portraits[p].rectTransform;
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 50 + Mathf.Sin(t * 2.4f + p * 1.7f + band.Delay * 20f) * 10f);
                }
            }
            float v = Mathf.Clamp01((t - Clash + 0.12f) / 0.12f);
            float scale = t < Clash - 0.12f ? 0f : Mathf.Lerp(3.2f, 1f, v * v) * (1f + 0.04f * Mathf.Sin(Mathf.Max(0f, t - Clash) * 9f));
            _vs.localScale = Vector3.one * scale;
            _vs.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18f, Tilt * 0.5f, v));
            float flash = t < Clash ? 0f : Mathf.Max(0f, 0.85f - (t - Clash) / 0.35f * 0.85f);
            _flash.color = new Color(_flash.color.r, _flash.color.g, _flash.color.b, flash);
            _group.alpha = Mathf.Clamp01((Duration - t) / FadeOut);
        }

        static float BackOut(float k)
        {
            const float s = 1.6f;
            k -= 1f;
            return k * k * ((s + 1f) * k + s) + 1f;
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            Router.Close(this);
            var then = _then;
            _then = null;
            then?.Invoke();
        }

        /// <summary>Back does nothing: the four phones go on together.</summary>
        public void OnBack() { }
    }
}
