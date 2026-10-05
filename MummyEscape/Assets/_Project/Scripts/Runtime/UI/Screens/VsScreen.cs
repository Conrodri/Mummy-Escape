using System;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Before a duel, like a fighting game: the two mummies in their outfits slide in from each side with their names,
    /// leagues and Elo, a big "VS" slams down in a flash, then the tomb preview starts. About 2.5 s; a tap skips it.
    /// The tomb is drawn meanwhile. With no ghost on this tomb yet, the rival is a dark silhouette.
    /// </summary>
    public sealed class VsScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        const float Duration = 2.5f;
        const float Slide = 0.32f;    // each band's way in
        const float Clash = 0.34f;    // the VS lands (the gong of Sfx.Versus)
        const float FadeOut = 0.25f;
        const float Tilt = -7f;       // the bands' slant, in degrees

        static readonly Color MeColor = new Color32(150, 104, 30, 255);
        static readonly Color RivalColor = new Color32(26, 120, 118, 255);
        static readonly Color Silhouette = new Color(0.06f, 0.045f, 0.035f, 1f);

        CanvasGroup _group;
        Side _me, _rival;
        RectTransform _vs;
        Text _vsText;
        Image _flash;
        float _t;
        bool _done;
        Action _then;

        sealed class Side
        {
            public RectTransform Holder;
            public Image Portrait;
            public Text Mystery, Name, Info;
            public float From;   // x where it starts, off screen
            public float Delay;
            public float Rest;   // y of the band
        }

        protected override void Build()
        {
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            var bg = UIKit.Image(Root, UIKit.Art.White, new Color(0.05f, 0.035f, 0.025f, 1f), true, "Tap"); // noloc
            UIKit.Stretch(bg.rectTransform, -600, -600, -600, -600);
            var tap = bg.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(Finish);

            _me = MakeSide(true, 330);
            _rival = MakeSide(false, -330);

            _flash = UIKit.Image(Root, UIKit.Art.White, new Color(1f, 0.96f, 0.85f, 0f), false, "Flash"); // noloc
            UIKit.Stretch(_flash.rectTransform, -600, -600, -600, -600);

            _vs = UIKit.Place(UIKit.Rect("Vs", Root), 0.5f, 0.5f, 640, 420); // noloc
            _vsText = UIKit.Title(_vs, "VS", 300, UIKit.Gold); // noloc
            UIKit.Stretch(_vsText.rectTransform);
            UIKit.DropShadow(_vsText, 14, 0.7f);

            var hint = UIKit.Label(Root, "Touche pour passer", 26, UIKit.Dim);
            UIKit.BottomBand(hint.rectTransform, 60, 40);
        }

        /// <summary>One player's band: slanted stripe, portrait facing the centre, name, league and Elo on the other side.</summary>
        Side MakeSide(bool me, float y)
        {
            float dir = me ? -1f : 1f; // where the portrait sits, and where the band comes from
            var side = new Side { Rest = y, From = dir * 1500f, Delay = me ? 0f : 0.08f };
            side.Holder = UIKit.Rect(me ? "Me" : "Rival", Root); // noloc
            Band(side.Holder, 0f, 1f, 560, y);
            float px = me ? 0.27f : 0.73f; // the portrait's place across the screen

            var stripe = UIKit.Image(side.Holder, UIKit.Art.White, me ? MeColor : RivalColor, false, "Stripe"); // noloc
            UIKit.Place(stripe.rectTransform, 0.5f, 0.5f, 1900, 380);
            stripe.rectTransform.localRotation = Quaternion.Euler(0, 0, Tilt);
            var edge = UIKit.Image(stripe.transform, UIKit.Art.White, new Color(1f, 0.92f, 0.7f, 0.85f), false, "Edge"); // noloc
            edge.rectTransform.anchorMin = new Vector2(0, me ? 0 : 1);
            edge.rectTransform.anchorMax = new Vector2(1, me ? 0 : 1);
            edge.rectTransform.sizeDelta = new Vector2(0, 10);
            edge.rectTransform.anchoredPosition = Vector2.zero;

            var glow = UIKit.Image(side.Holder, UISprites.RadialGlow, new Color(1f, 0.85f, 0.5f, 0.35f), false, "Glow"); // noloc
            At(glow.rectTransform, px, 560, 10);
            side.Portrait = UIKit.Image(side.Holder, null, Color.white, false, "Portrait"); // noloc
            At(side.Portrait.rectTransform, px, 420, 20);
            side.Portrait.preserveAspect = true;
            // Both mummies face the centre of the screen.
            if (!me) side.Portrait.rectTransform.localScale = new Vector3(-1, 1, 1);
            side.Mystery = UIKit.Title(side.Holder, "?", 220, UIKit.Turquoise);
            At(side.Mystery.rectTransform, px, 300, 10);

            var info = UIKit.Rect("Info", side.Holder); // noloc
            Band(info, me ? 0.52f : 0.03f, me ? 0.97f : 0.48f, 260, 0);
            var anchor = me ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            side.Name = UIKit.Title(info, "", 64, me ? UIKit.Gold : UIKit.Turquoise, anchor);
            UIKit.FitText(side.Name, 24);
            var nameRt = side.Name.rectTransform;
            nameRt.anchorMin = new Vector2(0, 1);
            nameRt.anchorMax = nameRt.pivot = new Vector2(1, 1);
            nameRt.sizeDelta = new Vector2(0, 110);
            UIKit.DropShadow(side.Name, 6, 0.6f);
            side.Info = UIKit.Label(info, "", 32, UIKit.Sand, anchor, FontStyle.Bold);
            UIKit.FitText(side.Info, 20);
            var infoRt = side.Info.rectTransform;
            infoRt.anchorMin = infoRt.pivot = Vector2.zero;
            infoRt.anchorMax = new Vector2(1, 0);
            infoRt.sizeDelta = new Vector2(0, 130);
            UIKit.DropShadow(side.Info, 4, 0.6f);
            return side;
        }

        /// <summary>A horizontal band from <paramref name="left"/> to <paramref name="right"/> (fractions of the width), centred at <paramref name="y"/>.</summary>
        static void Band(RectTransform rt, float left, float right, float height, float y)
        {
            rt.anchorMin = new Vector2(left, 0.5f);
            rt.anchorMax = new Vector2(right, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>A square centred at <paramref name="ax"/> of the width.</summary>
        static void At(RectTransform rt, float ax, float size, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>
        /// Plays the screen for this duel, then runs <paramref name="then"/> (open the HUD and start the run).
        /// <paramref name="ghost"/> null: nobody has run this tomb yet.
        /// </summary>
        public void Show(Loadout myLook, string myName, int myElo, GhostRun ghost, Action then)
        {
            _then = then;
            _t = 0f;
            _done = false;
            _group.alpha = 1f;
            _flash.color = new Color(_flash.color.r, _flash.color.g, _flash.color.b, 0f);
            _vs.localScale = Vector3.zero;

            Fill(_me, myLook, string.IsNullOrEmpty(myName) ? Loc.T("Toi") : myName,
                 TitleBook.Line(TitleBook.Equipped(App), true) + LeagueLine(myElo), false);
            if (ghost != null)
                Fill(_rival, PvpSkins.Loadout(ghost.Look), string.IsNullOrEmpty(ghost.PlayerName) ? Loc.T("Momie anonyme") : ghost.PlayerName,
                     TitleBook.Line(ghost.Look?.Title, true) + LeagueLine(ghost.Elo), false);
            else
                Fill(_rival, SkinCatalog.Classic, Loc.T("Personne… encore"),
                     Loc.T("Ta course deviendra le fantôme du prochain challenger"), true);
            Place(0f);
            App.Audio.Play(Sfx.Versus);
        }

        void Fill(Side side, Loadout look, string name, string info, bool unknown)
        {
            MummyAnimator.Show(side.Portrait, App.Art, look);
            side.Portrait.color = unknown ? Silhouette : Color.white;
            side.Mystery.gameObject.SetActive(unknown);
            side.Name.text = name;
            side.Info.text = info;
        }

        static string LeagueLine(int elo)
        {
            var league = Leagues.FromElo(elo);
            string hex = ColorUtility.ToHtmlStringRGB(PvpSkins.LeagueColor(league));
            return $"<color=#{hex}>" + Loc.F("Ligue {0}", Loc.T(PvpSkins.LeagueName(league))) + "</color>\nElo " + elo; // noloc
        }

        void Update()
        {
            if (_done) return;
            _t += Time.unscaledDeltaTime;
            Place(_t);
            if (_t >= Duration) Finish();
        }

        /// <summary>Everything at time <paramref name="t"/>: bands sliding in then drifting, the VS slam, the flash, the fade.</summary>
        void Place(float t)
        {
            foreach (var side in new[] { _me, _rival })
            {
                float k = Mathf.Clamp01((t - side.Delay) / Slide);
                float x = Mathf.LerpUnclamped(side.From, 0f, BackOut(k));
                // A slow drift toward the centre once in place, and a shake when the VS lands.
                float drift = Mathf.Max(0f, t - Slide) * 14f * -Mathf.Sign(side.From);
                float shake = t > Clash ? Mathf.Max(0f, 1f - (t - Clash) / 0.3f) * 16f : 0f;
                side.Holder.anchoredPosition = new Vector2(x + drift + Mathf.Sin(t * 70f) * shake, side.Rest + Mathf.Cos(t * 61f) * shake);
                side.Portrait.rectTransform.anchoredPosition = new Vector2(side.Portrait.rectTransform.anchoredPosition.x, 20 + Mathf.Sin(t * 2.4f + side.Delay * 20f) * 10f);
            }

            float v = Mathf.Clamp01((t - Clash + 0.12f) / 0.12f); // falls from above the screen in 0.12 s
            float scale = t < Clash - 0.12f ? 0f : Mathf.Lerp(3.2f, 1f, v * v) * (1f + 0.04f * Mathf.Sin(Mathf.Max(0f, t - Clash) * 9f));
            _vs.localScale = Vector3.one * scale;
            _vs.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18f, Tilt * 0.5f, v));
            float flash = t < Clash ? 0f : Mathf.Max(0f, 0.85f - (t - Clash) / 0.35f * 0.85f);
            _flash.color = new Color(_flash.color.r, _flash.color.g, _flash.color.b, flash);
            _group.alpha = Mathf.Clamp01((Duration - t) / FadeOut);
        }

        /// <summary>Ease out with a slight overshoot: the band slams in and settles.</summary>
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

        public void OnBack() => Finish();
    }
}
