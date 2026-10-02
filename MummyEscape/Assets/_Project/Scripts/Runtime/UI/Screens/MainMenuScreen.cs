using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class MainMenuScreen : UIScreen
    {
        Text _stars, _coins;
        Image _mummy, _glow;
        RectTransform _stage;
        Text _online;

        // Vertical column that adapts to any portrait height (tall 20:9 phones down to 4:3 tablets):
        // the title shrinks between its minimum and preferred size, the mummy takes whatever space is left.
        protected override void Build()
        {
            UIKit.Backdrop(Root, new Color(0, 0, 0, 0)).raycastTarget = false;

            var column = UIKit.Rect("Column", Root);
            UIKit.Stretch(column, 0, 24, 0, 100);
            var layout = UIKit.Column(column, 14);
            layout.padding = new RectOffset(40, 40, 0, 0);

            // Top bar: wallet chips on the left, settings on the right.
            var top = UIKit.Row(column, 96, 14);
            top.childAlignment = TextAnchor.MiddleLeft;
            _stars = UIKit.Chip(top.transform, UIKit.Art.Star, "0");
            _coins = UIKit.Chip(top.transform, UIKit.Art.Scarab, "0");
            UIKit.Size(UIKit.Rect("Spacer", top.transform), -1, -1, 1);
            UIKit.IconButton(top.transform, UISprites.Gear, () => Router.Open<SettingsScreen>(), 92);

            var title = UIKit.Title(column, "MUMMY\nESCAPE", 150);
            var black = Resources.Load<Font>("Fonts/Cinzel-Black");
            if (black != null) title.font = black;
            title.lineSpacing = 0.8f;
            UIKit.FitText(title, 64);
            var tle = UIKit.Size(title, 360);
            tle.minHeight = 190;
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.12f, 0.03f, 0.9f);
            outline.effectDistance = new Vector2(3, -3);

            var subtitle = UIKit.Label(column, Loc.T("Échappe-toi du tombeau… à l'aveugle."), 36, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.FitText(subtitle, 24);
            UIKit.Size(subtitle, 56);

            _stage = UIKit.Rect("MummyStage", column);
            // Reserved room for the mummy; extra height goes to it too (flexible), the title shrinks first.
            var sle = UIKit.Size(_stage, 150, -1, -1, 1);
            sle.minHeight = 150;
            _stage.gameObject.AddComponent<ResizeNotifier>().Resized = FitMummy;

            _glow = UIKit.Image(_stage, UIKit.Art.Glow, new Color(1f, 0.75f, 0.4f, 0.55f));
            _mummy = UIKit.Image(_stage, null, Color.white);
            foreach (var rt in new[] { _glow.rectTransform, _mummy.rectTransform })
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var play = UIKit.Rect("PlayRow", column);
            UIKit.Size(play, 124);
            var playBtn = UIKit.Button(play, Loc.T("JOUER"), () => Router.Open<LevelSelectScreen>(), 50, ButtonStyle.Primary);
            UIKit.Place((RectTransform)playBtn.transform, 0.5f, 0.5f, 620, 124);
            UIKit.Rounded(playBtn.image, 62);

            // Secondary destinations as captioned icons.
            UIKit.Size(UIKit.Rect("Gap", column), 18);
            var actions = UIKit.Row(column, 170, 44);
            UIKit.IconAction(actions.transform, UISprites.Podium, Loc.T("Classement"), () => Router.Open<LeaderboardScreen>());
            UIKit.IconAction(actions.transform, UISprites.Friends, Loc.T("Amis"), () => Router.Open<FriendsScreen>());
            UIKit.IconAction(actions.transform, UISprites.Bag, Loc.T("Boutique"), () => Router.Open<ShopScreen>());
            UIKit.IconAction(actions.transform, UISprites.Note, Loc.T("Juke-box"), () => Router.Open<JukeboxScreen>());
#if !UNITY_IOS
            // Apple's guidelines discourage quit buttons; on iOS the home gesture closes the app.
            UIKit.Size(UIKit.Button(column, Loc.T("Quitter"), Quit, 28, ButtonStyle.Ghost), 64);
#endif

            _online = UIKit.Label(Root, "", 24, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.BottomBand(_online.rectTransform, 50, 36);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            FitMummy();
            _stars.text = App.Save.TotalStars.ToString();
            _coins.text = App.Save.Data.Coins.ToString();
            _mummy.sprite = App.Art.MummyPortrait(SkinCatalog.Get(App.Save.Data.SelectedSkin));
            var online = App.Online;
            _online.text = !online.IsAvailable ? Loc.T(online.Status)
                : online.Account == Online.AccountState.Account ? Loc.F("Compte {0} · {1}", online.Username, online.PlayerName) : Loc.F("Invité : {0}", online.PlayerName);
        }

        void Update()
        {
            if (_mummy == null || !_mummy.enabled) return;
            float size = _mummy.rectTransform.sizeDelta.y;
            _mummy.rectTransform.anchoredPosition = new Vector2(0, Mathf.Sin(Time.time * 1.6f) * 12f * size / 260f);
        }

        /// <summary>The mummy fits the space left between the subtitle and the buttons (hidden when there is none).</summary>
        void FitMummy()
        {
            if (_mummy == null) return;
            float size = Mathf.Min(260f, _stage.rect.height - 30f);
            bool show = size >= 100f;
            _mummy.enabled = _glow.enabled = show;
            if (!show) return;
            _mummy.rectTransform.sizeDelta = new Vector2(size, size);
            _glow.rectTransform.sizeDelta = new Vector2(size * 2f, size * 2f);
        }

        /// <summary>Calls back when the layout resizes the rect it sits on.</summary>
        sealed class ResizeNotifier : MonoBehaviour
        {
            public System.Action Resized;
            void OnRectTransformDimensionsChange() => Resized?.Invoke();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
