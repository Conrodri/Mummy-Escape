using System.Threading.Tasks;
using MummyEscape.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>Where a menu screen sits in the bottom bar.</summary>
    public enum NavTab { None, Home, Duel, Solo, Ranking, Friends, Shop }

    /// <summary>
    /// The bar at the bottom of every menu screen: Duel, Solo, Classement, Amis, Boutique and Quitter, reachable from
    /// anywhere outside a run. Each destination opens over the main menu, so Back always leads home.
    /// </summary>
    public sealed class NavBar : MonoBehaviour
    {
        public const float Height = 150f;

        UIRouter _router;
        readonly System.Collections.Generic.List<(NavTab tab, Image icon, Text caption, Image pill)> _items =
            new System.Collections.Generic.List<(NavTab, Image, Text, Image)>();

        public static NavBar Create(RectTransform parent, UIRouter router)
        {
            var bg = UIKit.Image(parent, UIKit.Art.White, new Color(0.06f, 0.045f, 0.03f, 0.97f), true, "NavBar"); // noloc
            UIKit.BottomBand(bg.rectTransform, Height);
            // Reaches under the home indicator: the bar's colour fills the bottom of the screen.
            bg.rectTransform.offsetMin = new Vector2(0, -200);
            bg.rectTransform.offsetMax = new Vector2(0, Height);
            var rim = UIKit.Image(bg.transform, UIKit.Art.White, UIKit.Rim, false, "Rim"); // noloc
            UIKit.TopBand(rim.rectTransform, 3);

            var bar = bg.gameObject.AddComponent<NavBar>();
            bar._router = router;
            var row = UIKit.Rect("Items", bg.transform); // noloc
            UIKit.TopBand(row, Height);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 8, 10, 12);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;

            bar.Add(row, NavTab.Duel, UISprites.Swords, "Duel", () => bar.Go<PvpScreen>());
            bar.Add(row, NavTab.Solo, UISprites.Map, "Solo", () => bar.Go<LevelSelectScreen>());
            bar.Add(row, NavTab.Ranking, UISprites.Podium, "Classement", () => bar.Go<LeaderboardScreen>());
            bar.Add(row, NavTab.Friends, UISprites.Friends, "Amis", () => bar.Go<FriendsScreen>());
            bar.Add(row, NavTab.Shop, UISprites.Bag, "Boutique", () => bar.Go<ShopScreen>());
#if !UNITY_IOS
            // Apple's guidelines discourage quit buttons; on iOS the home gesture closes the app.
            bar.Add(row, NavTab.None, UISprites.Close, "Quitter", bar.AskQuit);
#endif
            bg.gameObject.SetActive(false);
            return bar;
        }

        void Add(RectTransform row, NavTab tab, Sprite sprite, string caption, System.Action onClick)
        {
            var cell = UIKit.Image(row, UIKit.Art.White, new Color(0, 0, 0, 0), true, "Tab " + caption); // noloc
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                onClick();
            });
            cell.gameObject.AddComponent<PressScale>();

            var pill = UIKit.Plate(cell.transform, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.16f), 30, null, false, "Lit"); // noloc
            UIKit.Place(pill.rectTransform, 0.5f, 1f, 112, 64, 0, -6);
            var icon = UIKit.Image(cell.transform, sprite, UIKit.Dim, false, "Icon"); // noloc
            UIKit.Place(icon.rectTransform, 0.5f, 1f, 52, 52, 0, -12);
            var text = UIKit.Label(cell.transform, caption, 22, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(text, 14);
            UIKit.Place(text.rectTransform, 0.5f, 0f, 170, 40, 0, 6);
            text.rectTransform.anchorMin = new Vector2(0, 0);
            text.rectTransform.anchorMax = new Vector2(1, 0);
            text.rectTransform.sizeDelta = new Vector2(0, 40);
            _items.Add((tab, icon, text, pill));
        }

        /// <summary>Shown with <paramref name="tab"/> lit; hidden for <see cref="NavTab.None"/> (runs, replays, dialogs).</summary>
        public void Show(NavTab tab)
        {
            gameObject.SetActive(tab != NavTab.None);
            foreach (var (t, icon, caption, pill) in _items)
            {
                bool lit = t != NavTab.None && t == tab;
                icon.color = lit ? UIKit.Gold : UIKit.Dim;
                caption.color = lit ? UIKit.Gold : UIKit.Dim;
                pill.gameObject.SetActive(lit);
            }
        }

        void Go<T>() where T : UIScreen
        {
            if (_router.Current is T) return;
            _router.Reset<MainMenuScreen>();
            _router.Open<T>();
        }

        void AskQuit() =>
            _router.Open<ConfirmDialog>().Configure("Quitter Mummy Escape ?", "Ta progression est enregistrée.", "Quitter", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return Task.FromResult<string>(null);
            });
    }
}
