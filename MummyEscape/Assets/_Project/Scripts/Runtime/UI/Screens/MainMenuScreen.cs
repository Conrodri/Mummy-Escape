using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class MainMenuScreen : UIScreen
    {
        Text _wallet;
        Image _mummy, _glow;
        RectTransform _stage;
        Text _online;

        // Vertical column that adapts to any portrait height (tall 20:9 phones down to 4:3 tablets):
        // the title shrinks between its minimum and preferred size, the mummy takes whatever space is left.
        protected override void Build()
        {
            var column = UIKit.Rect("Column", Root);
            UIKit.Stretch(column, 0, 30, 0, 110);
            var layout = UIKit.Column(column, 12);

            // Wallet (stars + scarabs).
            _wallet = UIKit.Label(column, "", 40, UIKit.Gold, TextAnchor.MiddleRight);
            UIKit.FitText(_wallet, 26);
            UIKit.Size(_wallet, 80);
            layout.padding = new RectOffset(40, 40, 0, 0);

            var title = UIKit.Label(column, "MUMMY\nESCAPE", 150, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.lineSpacing = 0.85f;
            UIKit.FitText(title, 64);
            var tle = UIKit.Size(title, 380);
            tle.minHeight = 190;
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.2f, 0.05f);
            outline.effectDistance = new Vector2(4, -4);

            var subtitle = UIKit.Label(column, "Échappe-toi du tombeau… à l'aveugle.", 40, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.FitText(subtitle, 26);
            UIKit.Size(subtitle, 60);

            _stage = UIKit.Rect("MummyStage", column);
            // Reserved room for the mummy; extra height goes to it too (flexible), the title shrinks first.
            var sle = UIKit.Size(_stage, 150, -1, -1, 1);
            sle.minHeight = 150;
            _stage.gameObject.AddComponent<ResizeNotifier>().Resized = FitMummy;

            _glow = UIKit.Image(_stage, UIKit.Art.Glow, new Color(1f, 0.75f, 0.4f, 0.55f));
            _mummy = UIKit.Image(_stage, null, Color.white);
            foreach (var rt in new[] { _glow.rectTransform, _mummy.rectTransform })
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var menu = UIKit.Rect("Menu", column);
            var mcol = UIKit.Column(menu, 26);
            mcol.childForceExpandWidth = false;
            mcol.childControlWidth = true;

            void Add(string label, System.Action onClick, int size, float height) =>
                UIKit.Size(UIKit.Button(menu, label, onClick, size), height, 760);
            Add("JOUER", () => Router.Open<LevelSelectScreen>(), 64, 150);
            Add("Classement", () => Router.Open<LeaderboardScreen>(), 44, 115);
            Add("Amis", () => Router.Open<FriendsScreen>(), 44, 115);
            Add("Boutique", () => Router.Open<ShopScreen>(), 44, 115);
            Add("Paramètres", () => Router.Open<SettingsScreen>(), 44, 115);
#if !UNITY_IOS
            // Apple's guidelines discourage quit buttons; on iOS the home gesture closes the app.
            Add("Quitter", Quit, 44, 115);
#endif

            _online = UIKit.Label(Root, "", 26, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.BottomBand(_online.rectTransform, 50, 40);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            FitMummy();
            _wallet.text = $"Étoiles : {App.Save.TotalStars}    Scarabées : {App.Save.Data.Coins}";
            _mummy.sprite = App.Art.MummyPortrait(SkinCatalog.Get(App.Save.Data.SelectedSkin));
            var online = App.Online;
            _online.text = !online.IsAvailable ? online.Status
                : online.Account == Online.AccountState.Account ? $"Compte {online.Username} · {online.PlayerName}" : $"Invité : {online.PlayerName}";
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
