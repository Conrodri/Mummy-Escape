using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class MainMenuScreen : UIScreen
    {
        Text _wallet;
        Image _mummy;
        Text _online;

        protected override void Build()
        {
            // Wallet (stars + scarabs).
            _wallet = UIKit.Label(Root, "", 40, UIKit.Gold, TextAnchor.MiddleRight);
            UIKit.TopBand(_wallet.rectTransform, 80, 30);
            _wallet.rectTransform.offsetMax = new Vector2(-40, _wallet.rectTransform.offsetMax.y);

            var title = UIKit.Label(Root, "MUMMY\nESCAPE", 150, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.TopBand(title.rectTransform, 380, 140);
            title.lineSpacing = 0.85f;
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.2f, 0.05f);
            outline.effectDistance = new Vector2(4, -4);

            var subtitle = UIKit.Label(Root, "Échappe-toi du tombeau… à l'aveugle.", 40, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.TopBand(subtitle.rectTransform, 60, 520);

            var glow = UIKit.Image(Root, UIKit.Art.Glow, new Color(1f, 0.75f, 0.4f, 0.55f));
            var grt = glow.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1);
            grt.sizeDelta = new Vector2(520, 520);
            grt.anchoredPosition = new Vector2(0, -800);

            _mummy = UIKit.Image(Root, null, Color.white);
            var mrt = _mummy.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 1);
            mrt.sizeDelta = new Vector2(260, 260);
            mrt.anchoredPosition = new Vector2(0, -800);

            var menu = UIKit.Rect("Menu", Root);
            menu.anchorMin = new Vector2(0.5f, 0);
            menu.anchorMax = new Vector2(0.5f, 0);
            menu.pivot = new Vector2(0.5f, 0);
            menu.sizeDelta = new Vector2(760, 880);
            menu.anchoredPosition = new Vector2(0, 110);
            UIKit.Column(menu, 26);

            UIKit.Size(UIKit.Button(menu, "JOUER", () => Router.Open<LevelSelectScreen>(), 64), 150);
            UIKit.Size(UIKit.Button(menu, "Classement", () => Router.Open<LeaderboardScreen>()), 115);
            UIKit.Size(UIKit.Button(menu, "Amis", () => Router.Open<FriendsScreen>()), 115);
            UIKit.Size(UIKit.Button(menu, "Boutique", () => Router.Open<ShopScreen>()), 115);
            UIKit.Size(UIKit.Button(menu, "Paramètres", () => Router.Open<SettingsScreen>()), 115);
#if !UNITY_IOS
            // Apple's guidelines discourage quit buttons; on iOS the home gesture closes the app.
            UIKit.Size(UIKit.Button(menu, "Quitter", Quit), 115);
#endif

            _online = UIKit.Label(Root, "", 26, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.BottomBand(_online.rectTransform, 50, 40);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            _wallet.text = $"Étoiles : {App.Save.TotalStars}    Scarabées : {App.Save.Data.Coins}";
            _mummy.sprite = App.Art.MummyPortrait(SkinCatalog.Get(App.Save.Data.SelectedSkin));
            _online.text = App.Online.IsAvailable ? $"Connecté : {App.Online.PlayerName}" : App.Online.Status;
        }

        void Update()
        {
            if (_mummy == null) return;
            _mummy.rectTransform.anchoredPosition = new Vector2(0, -800 + Mathf.Sin(Time.time * 1.6f) * 12f);
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
