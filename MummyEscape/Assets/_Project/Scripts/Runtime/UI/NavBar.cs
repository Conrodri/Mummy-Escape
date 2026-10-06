using MummyEscape.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>Where a menu screen sits in the bottom bar.</summary>
    public enum NavTab { None, Home, Duel, Solo, Mummy, Casino, Ranking, Friends, Shop }

    /// <summary>
    /// The bar at the bottom of every menu screen, five tabs reachable from anywhere outside a run: Accueil, Jouer (solo, duel,
    /// leaderboards), Momie, Social (friends, guild, chat) and Boutique (shop, casino, pass). A tab grouping several screens
    /// opens a small pixel-art menu above itself. Each destination opens over the main menu, so Back always leads home.
    /// </summary>
    public sealed class NavBar : MonoBehaviour
    {
        public const float Height = 150f;
        const float MenuWidth = 440f;

        struct Entry
        {
            public string Caption;
            public Sprite Icon;
            public System.Func<UIScreen, bool> IsCurrent;
            public System.Action Open;
        }

        sealed class Group
        {
            public NavTab[] Tabs;
            public Entry[] Entries;
            public Image Icon, Pill;
            public Text Caption;
            public RectTransform Menu, Choices;
        }

        UIRouter _router;
        readonly System.Collections.Generic.List<Group> _groups = new System.Collections.Generic.List<Group>();

        public static NavBar Create(RectTransform parent, UIRouter router)
        {
            var bg = UIKit.Image(parent, UIKit.Art.White, new Color(0.06f, 0.045f, 0.03f, 0.97f), true, "NavBar"); // noloc
            UIKit.BottomBand(bg.rectTransform, Height);
            // Reaches under the home indicator: the bar's colour fills the bottom of the screen.
            bg.rectTransform.offsetMin = new Vector2(0, -200);
            bg.rectTransform.offsetMax = new Vector2(0, Height);
            // Pixel trim: a gold line over a dark one.
            var shadow = UIKit.Image(bg.transform, UIKit.Art.White, new Color(0.02f, 0.01f, 0f, 1f), false, "Trim"); // noloc
            UIKit.TopBand(shadow.rectTransform, UISprites.PixelScale * 2);
            var rim = UIKit.Image(bg.transform, UIKit.Art.White, new Color32(200, 160, 70, 255), false, "Rim"); // noloc
            UIKit.TopBand(rim.rectTransform, UISprites.PixelScale);

            var bar = bg.gameObject.AddComponent<NavBar>();
            bar._router = router;
            var row = UIKit.Rect("Items", bg.transform); // noloc
            UIKit.TopBand(row, Height);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 8, 10, 12);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;

            bar.Add(row, UISprites.Home, "Accueil", new[] { NavTab.Home },
                E("Accueil", UISprites.Home, s => s is MainMenuScreen, () => router.Reset<MainMenuScreen>()));
            bar.Add(row, UISprites.Play, "Jouer", new[] { NavTab.Solo, NavTab.Duel, NavTab.Ranking },
                E("Solo", UISprites.Map, s => s.Tab == NavTab.Solo, bar.Go<LevelSelectScreen>),
                E("Duel", UISprites.Swords, s => s.Tab == NavTab.Duel, bar.Go<PvpScreen>),
                E("Classements", UISprites.Podium, s => s.Tab == NavTab.Ranking, bar.Go<LeaderboardScreen>));
            bar.Add(row, UISprites.User, "Momie", new[] { NavTab.Mummy },
                E("Momie", UISprites.User, s => s is MummyScreen, bar.Go<MummyScreen>));
            bar.Add(row, UISprites.Friends, "Social", new[] { NavTab.Friends },
                E("Amis", UISprites.Friends, s => s is FriendsScreen, bar.Go<FriendsScreen>),
                E("Guilde", UISprites.Crown, s => s is GuildScreen, bar.Go<GuildScreen>),
                E("Tchat", UISprites.Chat, s => s is ChatScreen, bar.Go<ChatScreen>));
            bar.Add(row, UISprites.Bag, "Boutique", new[] { NavTab.Shop, NavTab.Casino },
                E("Boutique", UISprites.Bag, s => s is ShopScreen || s is TreasureScreen, bar.Go<ShopScreen>),
                E("Casino", UISprites.Wheel, s => s is CasinoScreen, bar.Go<CasinoScreen>),
                E("Pass", UISprites.Seal, s => s is PassScreen, bar.Go<PassScreen>));
            bg.gameObject.SetActive(false);
            return bar;
        }

        static Entry E(string caption, Sprite icon, System.Func<UIScreen, bool> isCurrent, System.Action open) =>
            new Entry { Caption = caption, Icon = icon, IsCurrent = isCurrent, Open = open };

        void Add(RectTransform row, Sprite sprite, string caption, NavTab[] tabs, params Entry[] entries)
        {
            var group = new Group { Tabs = tabs, Entries = entries };
            int index = _groups.Count;
            _groups.Add(group);

            var cell = UIKit.Image(row, UIKit.Art.White, new Color(0, 0, 0, 0), true, "Tab " + caption); // noloc
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f);
                if (entries.Length == 1) { CloseMenus(); entries[0].Open(); }
                else Toggle(index);
            });
            cell.gameObject.AddComponent<PressScale>();

            group.Pill = UIKit.Pixelated(UIKit.Image(cell.transform, UISprites.PixelPlate, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.3f), false, "Lit"), UISprites.PixelPlate); // noloc
            UIKit.Place(group.Pill.rectTransform, 0.5f, 1f, 120, 66, 0, -6);
            group.Icon = UIKit.Image(cell.transform, sprite, UIKit.Dim, false, "Icon"); // noloc
            UIKit.Place(group.Icon.rectTransform, 0.5f, 1f, 52, 52, 0, -13);
            group.Caption = UIKit.Label(cell.transform, caption, 24, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(group.Caption, 14);
            group.Caption.rectTransform.anchorMin = new Vector2(0, 0);
            group.Caption.rectTransform.anchorMax = new Vector2(1, 0);
            group.Caption.rectTransform.pivot = new Vector2(0.5f, 0f);
            group.Caption.rectTransform.anchoredPosition = new Vector2(0, 6);
            group.Caption.rectTransform.sizeDelta = new Vector2(0, 40);
            // A small sparkle tells the tab holds several screens.
            if (entries.Length > 1)
            {
                var more = UIKit.Image(cell.transform, UISprites.PixelSparkle, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.7f), false, "More"); // noloc
                UIKit.Place(more.rectTransform, 0.5f, 1f, 21, 21, 44, -10);
            }
        }

        void Toggle(int index)
        {
            var group = _groups[index];
            bool open = group.Menu != null && group.Menu.gameObject.activeSelf;
            CloseMenus();
            if (open) return;
            if (group.Menu == null) BuildMenu(group, index);
            UIKit.ClearChildren(group.Choices);
            FillMenu(group);
            group.Menu.gameObject.SetActive(true);
            group.Menu.SetAsLastSibling();
            UIFx.PopIn(group.Choices, 0f, 0.9f);
        }

        void BuildMenu(Group group, int index)
        {
            var root = UIKit.Rect("Menu", transform.parent); // noloc
            UIKit.Stretch(root);
            // Tapping anywhere else closes it.
            var catcher = UIKit.Image(root, UIKit.Art.White, new Color(0, 0, 0, 0.55f), true, "Catcher"); // noloc
            UIKit.Stretch(catcher.rectTransform, -600, -600, -600, -600);
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(CloseMenus);

            var panel = UIKit.Panel(root, "Choices"); // noloc
            var rt = panel.rectTransform;
            // Right above its tab, kept inside the screen.
            float share = (index + 0.5f) / 5f;
            rt.anchorMin = rt.anchorMax = new Vector2(share, 0f);
            rt.pivot = new Vector2(Mathf.Clamp(share, 0.25f, 0.75f) * 2f - 0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, Height + 16);
            rt.sizeDelta = new Vector2(MenuWidth, 0);
            var col = UIKit.Column(panel.transform, 14, 34);
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            group.Menu = root;
            group.Choices = rt;
            root.gameObject.SetActive(false);
        }

        void FillMenu(Group group)
        {
            var current = _router.Current;
            foreach (var entry in group.Entries)
            {
                var e = entry;
                bool here = current != null && e.IsCurrent(current);
                var b = UIKit.Button(group.Choices, e.Caption, () => { CloseMenus(); e.Open(); }, 34, here ? ButtonStyle.Primary : ButtonStyle.Secondary);
                UIKit.Size(b, 108);
                var label = b.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(104, label.rectTransform.offsetMin.y);
                var icon = UIKit.Image(b.transform, e.Icon, here ? UIKit.Ink : UIKit.Sand, false, "Icon"); // noloc
                UIKit.Place(icon.rectTransform, 0f, 0.5f, 54, 54, 30, UISprites.PixelScale);
            }
        }

        void CloseMenus()
        {
            foreach (var g in _groups)
                if (g.Menu != null) g.Menu.gameObject.SetActive(false);
        }

        void OnDisable() => CloseMenus();

        /// <summary>Shown with <paramref name="tab"/> lit; hidden for <see cref="NavTab.None"/> (runs, replays, dialogs).</summary>
        public void Show(NavTab tab)
        {
            CloseMenus();
            gameObject.SetActive(tab != NavTab.None);
            foreach (var g in _groups)
            {
                bool lit = tab != NavTab.None && System.Array.IndexOf(g.Tabs, tab) >= 0;
                g.Icon.color = lit ? UIKit.Gold : UIKit.Dim;
                g.Caption.color = lit ? UIKit.Gold : UIKit.Dim;
                g.Pill.gameObject.SetActive(lit);
            }
        }

        void Go<T>() where T : UIScreen
        {
            if (_router.Current is T) return;
            _router.Reset<MainMenuScreen>();
            _router.Open<T>();
        }
    }
}
