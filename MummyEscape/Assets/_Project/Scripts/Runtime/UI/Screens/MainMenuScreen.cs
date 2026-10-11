using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class MainMenuScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Home;

        Text _stars, _coins, _gold;
        PassBanner _pass;
        Image _mummy, _glow, _chatDot, _floor;
        RectTransform _stage;
        Text _online, _goal;
        System.Action _goalAction;

        // Vertical column that adapts to any portrait height (tall 20:9 phones down to 4:3 tablets):
        // the title shrinks between its minimum and preferred size, the mummy takes whatever space is left.
        protected override void Build()
        {
            var backdrop = UIKit.Backdrop(Root, new Color(0, 0, 0, 0));
            backdrop.raycastTarget = false;
            // The chosen act's floor slabs, dimmed so the menu stays readable (under the warm glow).
            _floor = UIKit.Image(backdrop.transform, null, new Color(0.42f, 0.4f, 0.38f), false, "Floor"); // noloc
            UIKit.Stretch(_floor.rectTransform);
            _floor.transform.SetAsFirstSibling();
            _floor.type = Image.Type.Tiled;
            _floor.pixelsPerUnitMultiplier = 0.5f; // 32 px slabs drawn ~200 units wide: big pixels, like in the game

            var column = UIKit.Rect("Column", Root);
            UIKit.Stretch(column, 0, 24, 0, 100);
            var layout = UIKit.Column(column, 14);
            layout.padding = new RectOffset(40, 40, 0, 0);

            // Top bar: wallet chips on the left, settings on the right.
            var top = UIKit.Row(column, 96, 14);
            top.childAlignment = TextAnchor.MiddleLeft;
            _stars = UIKit.Chip(top.transform, UIKit.Art.Star, "0");
            _coins = UIKit.Chip(top.transform, UIKit.Art.Scarab, "0");
            // Golden scarabs: a tap opens the Treasure.
            _gold = UIKit.Chip(top.transform, UIKit.Art.GoldScarab, "0", TreasureScreen.GoldColor);
            var goldPlate = _gold.transform.parent.gameObject;
            goldPlate.GetComponent<Image>().raycastTarget = true;
            goldPlate.AddComponent<Button>().onClick.AddListener(() => Router.Open<TreasureScreen>());
            UIKit.Size(UIKit.Rect("Spacer", top.transform), -1, -1, 1);
            // The chat, with a dot when a friend or the guild wrote.
            var chat = UIKit.IconButton(top.transform, UISprites.Chat, () => Router.Open<ChatScreen>(), 92);
            _chatDot = UIKit.Image(chat.transform, UISprites.Circle, UIKit.Danger, false, "Dot"); // noloc
            UIKit.Place(_chatDot.rectTransform, 1, 1, 28, 28, -10, -10);
            _chatDot.enabled = false;
            Online.ChatState.Changed += () => { if (_chatDot != null) _chatDot.enabled = Online.ChatState.AnyUnread(App.Online.PlayerId); };
            UIKit.IconButton(top.transform, UISprites.Gear, () => Router.Open<SettingsScreen>(), 92);

            var title = UIKit.Title(column, "MUMMY\nRUSH", 150);
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
            _mummy.preserveAspect = true; // the outfit sprite is 32x40
            foreach (var rt in new[] { _glow.rectTransform, _mummy.rectTransform })
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            // The home backdrop, picked among the open acts (in the stage's corner: the top bar is full on narrow phones).
            var theme = UIKit.IconAction(_stage, UISprites.Palette, "Thème", () => Router.Open<HomeThemeDialog>().OnPicked(ApplyTheme), 84);
            UIKit.Place((RectTransform)theme.transform.parent.parent, 1f, 1f, 134, 144); // button → its slot → the action column

            // What to aim for next, one tap away.
            var goal = UIKit.ListItem(column, 116, () => _goalAction?.Invoke(), out var gh);
            var flag = UIKit.Image(gh.transform, UISprites.Flag, UIKit.Gold, false, "Icon"); // noloc
            flag.preserveAspect = true;
            UIKit.Size(flag, 56, 56);
            var gcol = UIKit.Rect("Text", gh.transform); // noloc
            UIKit.Column(gcol, 2, 0, TextAnchor.MiddleLeft).childForceExpandHeight = false;
            UIKit.Size(gcol, -1, -1, 1);
            UIKit.Size(UIKit.SectionTitle(gcol, "Prochain objectif"), 34);
            _goal = UIKit.Label(gcol, "", 32, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_goal, 20);
            UIKit.Size(_goal, 44);
            var arrow = UIKit.Image(gh.transform, UISprites.Next, UIKit.Dim, false, "Arrow"); // noloc
            UIKit.Size(arrow, 40, 40);

            var play = UIKit.Rect("PlayRow", column);
            UIKit.Size(play, 124);
            var playBtn = UIKit.Button(play, Loc.T("JOUER"), () => Router.Open<LevelSelectScreen>(), 50, ButtonStyle.Primary);
            UIKit.Place((RectTransform)playBtn.transform, 0.5f, 0.5f, 620, 124);

            // The season pass, with what is waiting to be collected.
            _pass = new PassBanner(column, 150, () => Router.Open<PassScreen>());

            // Duel, solo, rankings, friends and the shop are in the bottom bar (NavBar).

            _online = UIKit.Label(Root, "", 24, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.BottomBand(_online.rectTransform, 50, 36);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            ApplyTheme();
            FitMummy();
            _stars.text = App.Save.TotalStars.ToString();
            _coins.text = App.Save.Data.Coins.ToString();
            _gold.text = App.Save.Gold.ToString();
            _pass.Refresh(App);
            RefreshGoal();
            MummyAnimator.Show(_mummy, App.Art, App.Save.Loadout);
            var online = App.Online;
            _online.text = !online.IsAvailable ? Loc.T(online.Status)
                : online.Account == Online.AccountState.Account ? Loc.F("Compte {0} · {1}", online.Username, online.PlayerName) : Loc.F("Invité : {0}", online.PlayerName);
            _chatDot.enabled = Online.ChatState.AnyUnread(App.Online.PlayerId);
            _ = Online.ChatState.RefreshAsync(App.Pvp);
            _ = Online.ChatState.SyncProfileAsync(App);
        }

        /// <summary>The next tomb to escape; once they are all escaped, the first one short of three stars; then the duels.</summary>
        void RefreshGoal()
        {
            var save = App.Save;
            var next = save.FurthestUnlocked();
            var rec = save.GetRecord(next);
            // A new player meets the traps in the tutorial corridor first.
            if (!save.Data.TutorialDone && next.Equals(Progression.First(Difficulty.Easy)) && (rec == null || rec.Completions == 0))
            {
                _goal.text = Loc.T("Apprends à déjouer les pièges : tutoriel");
                _goalAction = () => { Router.Open<HudScreen>(); App.Game.StartTutorial(); };
                return;
            }
            if (rec == null || rec.Completions == 0)
            {
                _goal.text = Loc.F("Évade-toi du tombeau {0}", LevelNames.Of(next));
                _goalAction = () => PlayLevel(next);
                return;
            }
            foreach (var id in DifficultyTable.AllLevels())
            {
                var r = save.GetRecord(id);
                if (r == null || r.BestStars >= 3) continue;
                _goal.text = Loc.F("Trois étoiles au tombeau {0} ({1}/3)", LevelNames.Of(id), r.BestStars);
                _goalAction = () => PlayLevel(id);
                return;
            }
            _goal.text = Loc.T("Grimpe au classement des duels");
            _goalAction = () => Router.Open<PvpScreen>();
        }

        /// <summary>Draws the chosen act's tomb behind the menu (the plain dark backdrop when none, or when it is no longer open).</summary>
        void ApplyTheme()
        {
            var entry = HomeThemes.Get(App.Settings.HomeTheme);
            bool on = entry != null && entry.IsUnlocked(App.Save);
            _floor.enabled = on;
            if (on) _floor.sprite = App.Art.ActFloor(entry.Act);
        }

        void PlayLevel(LevelId id) =>
            PlayGate.Solo(App, id, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartLevel(id);
            });

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
            // Grows with the room left (tall phones), never past a size where the pixel art turns into a poster.
            float size = Mathf.Min(620f, _stage.rect.height * 0.8f, _stage.rect.width * 0.7f);
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
    }

    /// <summary>Backdrops of the home screen: an act's tomb, once the player has opened that act.</summary>
    public static class HomeThemes
    {
        public sealed class Entry
        {
            /// <summary>Tomb drawn (<see cref="TombTheme.ForAct"/>), also the id saved in the settings.</summary>
            public int Act;
            /// <summary>Shown in the picker (French key); null = "Acte N — its name".</summary>
            public string Name;
            public System.Func<Services.SaveService, bool> IsUnlocked;
        }

        /// <summary>The campaign acts, in order, then Halloween (once its act is finished; kept after the event).</summary>
        public static readonly System.Collections.Generic.List<Entry> All = Acts();

        static System.Collections.Generic.List<Entry> Acts()
        {
            var list = new System.Collections.Generic.List<Entry>();
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var first = new LevelId(Difficulty.Easy, act, 1); // modes open in order: an act open in any mode is open in Facile
                list.Add(new Entry { Act = act, IsUnlocked = s => s.IsUnlocked(first) });
            }
            list.Add(new Entry { Act = Halloween.Theme, Name = "Halloween", IsUnlocked = s => s.HalloweenDone });
            return list;
        }

        public static Entry Get(int act) => All.Find(e => e.Act == act);

        public static string NameOf(Entry e) =>
            e.Name != null ? Loc.T(e.Name) : Loc.F("Acte {0} — {1}", e.Act, Loc.T(DifficultyTable.GetAct(e.Act).Name));
    }

    /// <summary>Home screen backdrop picker: the plain one, then every act (locked ones shown with a padlock).</summary>
    public sealed class HomeThemeDialog : UIScreen
    {
        public override bool IsModal => true;

        RectTransform _list;
        System.Action _picked;

        /// <summary>Called once the player picked a backdrop (the home screen redraws its own).</summary>
        public HomeThemeDialog OnPicked(System.Action picked)
        {
            _picked = picked;
            return this;
        }

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.8f), true, "Shade");
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Close(this)); // tap outside = cancel

            var panel = UIKit.Card(Root, 44, 22);
            UIKit.Place(panel, 0.5f, 0.5f, 960, 0);
            UIKit.FitInParent(panel);
            panel.GetComponent<Image>().raycastTarget = true; // swallow taps inside the panel
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Size(UIKit.Title(panel, "Thème de l'accueil", 46), 80);
            _list = UIKit.Rect("List", panel); // noloc
            UIKit.Column(_list, 12);
            UIKit.Size(UIKit.Button(panel, "Fermer", () => Router.Close(this)), UIKit.ButtonHeight);
        }

        public override void OnShow()
        {
            UIKit.ClearChildren(_list);
            var save = App.Save;
            int chosen = App.Settings.HomeTheme;
            if (HomeThemes.Get(chosen)?.IsUnlocked(save) != true) chosen = 0;
            Item(0, null, Loc.T("Nuit du tombeau"), true, chosen == 0);
            foreach (var e in HomeThemes.All)
            {
                bool open = e.IsUnlocked(save);
                Item(e.Act, App.Art.ActFloor(e.Act), HomeThemes.NameOf(e), open, chosen == e.Act);
            }
        }

        void Item(int act, Sprite floor, string name, bool open, bool selected)
        {
            UIKit.ListItem(_list, 104, open ? () => Pick(act) : (System.Action)null, out var h, selected);
            var swatch = UIKit.Image(h.transform, floor ?? UIKit.Art.White, floor != null ? Color.white : UIKit.BackdropColor, false, "Swatch"); // noloc
            UIKit.Size(swatch, 80, 80);
            var label = UIKit.Label(h.transform, name, 34, open ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleLeft, selected ? FontStyle.Bold : FontStyle.Normal);
            UIKit.FitText(label, 22);
            UIKit.Size(label, -1, -1, 1);
            if (open) return;
            var padlock = UIKit.Image(h.transform, UIKit.Art.Lock, new Color(1, 1, 1, 0.45f), false, "Lock"); // noloc
            UIKit.Size(padlock, 56, 56);
            swatch.color = new Color(0.4f, 0.4f, 0.4f);
        }

        void Pick(int act)
        {
            App.Settings.SetHomeTheme(act);
            Router.Close(this);
            var picked = _picked;
            _picked = null;
            picked?.Invoke();
        }
    }
}
