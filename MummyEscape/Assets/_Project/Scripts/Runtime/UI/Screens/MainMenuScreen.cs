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
        Image _mummy, _mate, _chatDot;
        Text _mateName, _mateState, _myName, _myState, _inviteLabel, _badgeText;
        Image _myShadow, _mateShadow, _badge;
        Button _chat, _plus;
        RectTransform _invite;
        int _invites, _teamsRequest;
        float _nextTeams;
        string _shownPending;
        const float TeamsSeconds = 10f, PlusSize = 120f;
        HomeScene _scene;
        RectTransform _stage;
        Text _online, _goal;
        System.Action _goalAction;

        // Vertical column that adapts to any portrait height (tall 20:9 phones down to 4:3 tablets):
        // the title shrinks between its minimum and preferred size, the mummy takes whatever space is left.
        protected override void Build()
        {
            var backdrop = UIKit.Backdrop(Root, new Color(0, 0, 0, 0));
            backdrop.raycastTarget = false;
            Destroy(backdrop.transform.Find("Glow").gameObject); // no warm halo over the title and the tomb // noloc
            // Inside the chosen act's tomb, the mummy standing in it facing us.
            _scene = HomeScene.Create(backdrop.transform);

            var column = UIKit.Rect("Column", Root);
            UIKit.Stretch(column, 0, 24, 0, 100);
            var layout = UIKit.Column(column, 14);
            layout.padding = new RectOffset(40, 40, 0, 0);

            // Top bar: wallet chips on the left, chat, home backdrop and settings on the right.
            var top = UIKit.Row(column, 96, 14);
            top.childAlignment = TextAnchor.MiddleLeft;
            _stars = UIKit.Chip(top.transform, UIKit.Art.Star, "0");
            _coins = UIKit.Chip(top.transform, UIKit.Art.Scarab, "0");
            // Golden scarabs: a tap opens the Treasure.
            _gold = UIKit.Chip(top.transform, UIKit.Art.GoldScarab, "0", TreasureScreen.GoldColor);
            UIKit.SameWidth(_stars, _coins, _gold);
            // Three icon buttons leave each chip ~215 px: big numbers shrink instead of spilling over.
            foreach (var chip in new[] { _stars, _coins, _gold }) UIKit.FitText(chip, 20);
            var goldPlate = _gold.transform.parent.gameObject;
            goldPlate.GetComponent<Image>().raycastTarget = true;
            goldPlate.AddComponent<Button>().onClick.AddListener(() => Router.Open<TreasureScreen>());
            // The chat, with a dot when a friend or the guild wrote.
            _chat = UIKit.IconButton(top.transform, UISprites.Chat, () => Router.Open<ChatScreen>(), 92);
            var chat = _chat;
            _chatDot = UIKit.Image(chat.transform, UISprites.Circle, UIKit.Danger, false, "Dot"); // noloc
            UIKit.Place(_chatDot.rectTransform, 1, 1, 28, 28, -10, -10);
            _chatDot.enabled = false;
            Online.ChatState.Changed += () => { if (_chatDot != null) _chatDot.enabled = Online.ChatState.AnyUnread(App.Online.PlayerId); };
            // The home backdrop, picked among the open acts.
            UIKit.IconButton(top.transform, UISprites.Palette, () => Router.Open<HomeThemeDialog>().OnPicked(ApplyTheme), 92);
            UIKit.IconButton(top.transform, UISprites.Gear, () => Router.Open<SettingsScreen>(), 92);

            var title = UIKit.Title(column, "MUMMY\nRUSH", 150);
            var black = Resources.Load<Font>("Fonts/Cinzel-Black");
            if (black != null) title.font = black;
            title.lineSpacing = 0.8f;
            UIKit.FitText(title, 64);
            var tle = UIKit.Size(title, 360);
            tle.minHeight = 190;
            Destroy(title.GetComponent<Shadow>()); // a clean title, no halo around the letters

            var subtitle = UIKit.Label(column, Loc.T("Échappe-toi du tombeau… à l'aveugle."), 36, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.FitText(subtitle, 24);
            UIKit.Size(subtitle, 56);

            _stage = UIKit.Rect("MummyStage", column);
            // Reserved room for the mummy; extra height goes to it too (flexible), the title shrinks first.
            var sle = UIKit.Size(_stage, 150, -1, -1, 1);
            sle.minHeight = 150;
            _stage.gameObject.AddComponent<ResizeNotifier>().Resized = () => _fitPending = true; // fitted in Update: toggling objects inside a layout rebuild is not allowed

            _mummy = UIKit.Image(_stage, null, Color.white);
            _mummy.preserveAspect = true; // the outfit sprite is 32x40
            Center(_mummy.rectTransform);
            // The party lobby: the player in the middle, his name (and, in a group, his readiness) over his head;
            // on his right a fixed slot, the teammate's mummy or a "+" to invite one.
            _myShadow = FootShadow();
            _mateShadow = FootShadow();
            _myName = StageLabel(28, UIKit.Sand);
            _myState = StageLabel(24, UIKit.Dim);
            _mate = UIKit.Image(_stage, null, Color.white, true);
            _mate.preserveAspect = true;
            Center(_mate.rectTransform);
            _mate.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Open<GroupScreen>());
            _mateName = StageLabel(26, UIKit.Sand);
            _mateState = StageLabel(24, UIKit.Dim);
            _invite = UIKit.Rect("Invite", _stage); // noloc
            Center(_invite);
            _plus = UIKit.IconButton(_invite, UISprites.Plus, () => Router.Open<InviteSheet>(), PlusSize);
            Center((RectTransform)_plus.transform);
            // A round "+" like a lobby's empty spot, not a square tool button.
            _plus.image.sprite = UISprites.Circle;
            _plus.image.type = Image.Type.Simple;
            _plus.image.pixelsPerUnitMultiplier = 1f;
            _inviteLabel = UIKit.Label(_invite, "", 26, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            Center(_inviteLabel.rectTransform);
            UIKit.FitText(_inviteLabel, 16);
            _badge = UIKit.Plate(_invite, UIKit.Gold, 16, null, false, "Badge"); // noloc
            Center(_badge.rectTransform);
            _badgeText = UIKit.Label(_badge.transform, "", 22, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_badgeText.rectTransform, 6, 0, 6, 0);
            UIKit.FitText(_badgeText, 14);

            // The dock: the pass, the next goal, the play button and the account line, framed together over the floor.
            var dock = UIKit.Panel(column, "Dock").transform; // noloc
            var dockLayout = UIKit.Column(dock, 12, 0);
            dockLayout.padding = new RectOffset(22, 22, 22, 18);

            // The season pass, with what is waiting to be collected.
            _pass = new PassBanner(dock, 150, () => Router.Open<PassScreen>());

            // What to aim for next, one tap away.
            var goal = UIKit.ListItem(dock, 116, () => _goalAction?.Invoke(), out var gh);
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

            var play = UIKit.Rect("PlayRow", dock);
            UIKit.Size(play, 124);
            var playBtn = UIKit.Button(play, Loc.T("JOUER"), () => { if (IntroPending(App.Save)) StartIntro(); else Router.Open<LevelSelectScreen>(); }, 50, ButtonStyle.Primary);
            UIKit.Place((RectTransform)playBtn.transform, 0.5f, 0.5f, 620, 124);

            // Duel, solo, rankings, friends and the shop are in the bottom bar (NavBar).

            _online = UIKit.Label(dock, "", 22, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.Size(_online, 40);
            UIKit.FitText(_online, 16);
            // A guest's progress lives on this phone only: the line leads to the account screen.
            _online.raycastTarget = true;
            _online.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (App.Online.Account == Online.AccountState.Guest) Router.Open<AccountScreen>();
            });
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            Services.Notifications.MaybeAsk(App);
            ApplyTheme();
            FitMummy();
            _stars.text = App.Save.TotalStars.ToString();
            _coins.text = App.Save.Data.Coins.ToString();
            _gold.text = App.Save.Gold.ToString();
            _pass.Refresh(App);
            RefreshGoal();
            MummyAnimator.Show(_mummy, App.Art, App.Save.Loadout);
            // The rooms open along the campaign: hidden until then, celebrated once when they open.
            _chat.gameObject.SetActive(Unlocks.IsUnlocked(Feature.Chat, App.Save.IsCompleted));
            int fresh = App.Save.TakeNewUnlocks() & (int)Feature.Duel; // the casino opens without fanfare: no popup toward a game of chance
            if (fresh != 0 && !IntroPending(App.Save)) Celebrate(fresh);
            StartCoroutine(App.Art.Warm(App.Save.FurthestUnlocked().Act)); // the next tomb's sprites, painted now
            var online = App.Online;
            _online.text = !online.IsAvailable ? Loc.T(online.Status)
                : online.Account == Online.AccountState.Account ? Loc.F("Compte {0} · {1}", online.Username, online.PlayerName)
                : Loc.T("Invité : ta progression n'est que sur ce téléphone. Crée un compte pour ne rien perdre ›");
            _online.color = online.IsAvailable && online.Account == Online.AccountState.Guest ? UIKit.Gold : UIKit.Dim;
            _chatDot.enabled = Online.ChatState.AnyUnread(App.Online.PlayerId);
            _ = Online.ChatState.RefreshAsync(App.Pvp);
            _ = Online.ChatState.SyncProfileAsync(App);
            ReloadTeams();
        }

        /// <summary>The group and the invites received, as the server has them (the lobby slot follows).</summary>
        async void ReloadTeams()
        {
            _nextTeams = Time.unscaledTime + TeamsSeconds;
            if (App.Pvp == null) return;
            int request = ++_teamsRequest;
            var teams = await App.Pvp.GetTeamsAsync();
            if (request != _teamsRequest || this == null) return;
            if (teams == null || teams.Error != null) return; // keep what is shown, the next tick tries again
            var duo = teams.Duos.Count > 0 ? teams.Duos[0] : null;
            App.Session.SetGroup(duo);
            _invites = teams.Invites.Count;
            if (duo != null) InviteSheet.PendingInvite = null;
            ShowMate();
        }

        /// <summary>" · ouvre les duels" when escaping this tomb opens a room (the goal card teases it).</summary>
        string Opens(LevelId next)
        {
            if (Unlocks.Gate(Feature.Duel).Value.Equals(next)) return "  ·  " + Loc.T("ouvre les duels");
            if (Unlocks.Gate(Feature.Casino).Value.Equals(next)) return "  ·  " + Loc.T("ouvre le casino");
            return "";
        }

        /// <summary>Newly open rooms (a <see cref="Feature"/> bitmask): a word for each, and a way in to the first.</summary>
        void Celebrate(int fresh)
        {
            var lines = new System.Collections.Generic.List<string>();
            System.Action first = null;
            foreach (var f in Unlocks.All)
            {
                if ((fresh & (int)f) == 0) continue;
                switch (f)
                {
                    case Feature.Duel: lines.Add(Loc.T("Les duels et le groupe")); first ??= () => Router.Open<PvpScreen>(); break;
                }
            }
            App.Audio.Play(Services.Sfx.Win);
            Router.Open<OfferDialog>().Configure("Nouvelles salles ouvertes !", Loc.T("Ta progression ouvre :") + "\n\n• " + string.Join("\n• ", lines),
                ("Voir", ButtonStyle.Primary, first), ("Plus tard", ButtonStyle.Ghost, null));
        }

        /// <summary>The next tomb to escape; once they are all escaped, the first one short of three stars; then the duels.</summary>
        void RefreshGoal()
        {
            var save = App.Save;
            var next = save.FurthestUnlocked();
            var rec = save.GetRecord(next);
            // Each act's traps are met in its intro corridor first (JOUER goes there too).
            if (IntroPending(save))
            {
                _goal.text = Loc.F("Apprends les pièges de l'acte {0} : intro", next.Act);
                _goalAction = StartIntro;
                return;
            }
            if (rec == null || rec.Completions == 0)
            {
                _goal.text = Loc.F("Évade-toi du tombeau {0}", LevelNames.Of(next)) + Opens(next);
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

        /// <summary>Draws the chosen act's tomb behind the menu (the default act when none is chosen or it is no longer open).</summary>
        void ApplyTheme()
        {
            var entry = HomeThemes.Get(App.Settings.HomeTheme);
            if (entry == null || !entry.IsUnlocked(App.Save)) entry = HomeThemes.Get(HomeThemes.Default);
            _scene.Show(App.Art, entry.Act);
        }

        /// <summary>The next tomb opens an Easy act whose intro is neither walked nor skipped.</summary>
        static bool IntroPending(Services.SaveService save) => save.NeedsIntro(save.FurthestUnlocked());

        void StartIntro()
        {
            Router.Open<HudScreen>();
            App.Game.StartIntro(App.Save.FurthestUnlocked().Act);
        }

        void PlayLevel(LevelId id) =>
            PlayGate.Solo(App, id, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartLevel(id);
            });

        bool _fitPending;

        void Update()
        {
            if (_mummy == null) return;
            if (_fitPending) { _fitPending = false; FitMummy(); }
            if (App.Pvp != null && Time.unscaledTime >= _nextTeams) ReloadTeams();
            if (InviteSheet.PendingInvite != _shownPending) ShowMate(); // an invite just sent from the sheet
            if (!_mummy.enabled) return;
            float size = _mummy.rectTransform.sizeDelta.y;
            _mummy.rectTransform.anchoredPosition = new Vector2(0, Mathf.Sin(Time.time * 1.6f) * 12f * size / 260f);
            if (!_mate.enabled) return;
            float mate = _mate.rectTransform.sizeDelta.y;
            _mate.rectTransform.anchoredPosition = new Vector2(SlotX(size), SlotY(size) + Mathf.Sin(Time.time * 1.6f + 1.2f) * 12f * mate / 260f);
        }

        // The teammate's slot: right of the player, a step behind him.
        static float SlotX(float size) => size * 0.78f;
        static float SlotY(float size) => -size * 0.1f;

        /// <summary>The mummy fits the space left between the subtitle and the buttons (hidden when there is none).</summary>
        void FitMummy()
        {
            if (_mummy == null) return;
            // Grows with the room left (tall phones), never past a size where the pixel art turns into a poster.
            // Room is kept over the head for the name and the readiness, and on the right for the teammate's slot.
            float size = Mathf.Min(620f, _stage.rect.height * 0.8f, _stage.rect.height - 190f, _stage.rect.width * 0.46f);
            bool show = size >= 100f;
            _mummy.enabled = _myShadow.enabled = _myName.enabled = show;
            _mummy.rectTransform.sizeDelta = new Vector2(size, size);
            float mate = size * 0.72f, x = SlotX(size), y = SlotY(size), plus = size * 0.36f;
            _mate.rectTransform.sizeDelta = new Vector2(mate, mate);
            Put(_myShadow.rectTransform, 0, -size * 0.47f, size * 1.7f, size * 0.2f);
            Put(_mateShadow.rectTransform, x, y - mate * 0.47f, mate * 1.7f, mate * 0.2f);
            _plus.transform.localScale = Vector3.one * (plus / PlusSize);
            ((RectTransform)_plus.transform).anchoredPosition = new Vector2(x, y);
            Put(_inviteLabel.rectTransform, x, y + plus * 0.5f + 28, size * 0.6f, 40);
            Put(_badge.rectTransform, x, y - plus * 0.5f - 30, size * 0.6f, 38);
            ShowMate();
        }

        /// <summary>
        /// The lobby: the player's name (and readiness in a group) over his head, then on his right the teammate as he
        /// dresses with his name and readiness, or the "+" to invite one. Everything hides with the mummy.
        /// </summary>
        void ShowMate()
        {
            _shownPending = InviteSheet.PendingInvite;
            var g = App.Session.Group;
            bool shown = _mummy.enabled;
            float size = _mummy.rectTransform.sizeDelta.y;
            // Name on top, readiness under it, both just over the head (clear of the bobbing).
            float head = HeadTop(App.Save.Loadout, size) + 36;
            _myName.text = App.Online.PlayerName;
            Put(_myName.rectTransform, 0, head + (g != null ? 34 : 0), size * 0.9f, 36);
            _myState.enabled = shown && g != null;
            Put(_myState.rectTransform, 0, head, size * 0.9f, 32);
            if (g != null) _myState.text = State(g.IsReady(App.Online.PlayerId));
            // The slot opens with the duels and the group.
            bool slot = shown && Unlocks.IsUnlocked(Feature.Duel, App.Save.IsCompleted);
            string mate = g?.Members.Find(m => m != App.Online.PlayerId);
            bool duo = slot && mate != null;
            _mate.enabled = _mateName.enabled = _mateState.enabled = _mateShadow.enabled = duo;
            _invite.gameObject.SetActive(slot && !duo);
            if (duo)
            {
                var look = PvpSkins.Loadout(g.LookOf(mate));
                MummyAnimator.Show(_mate, App.Art, look); // the classic outfit until his look is known
                int i = g.Members.IndexOf(mate);
                _mateName.text = i < g.Names.Count ? g.Names[i] : "";
                _mateState.text = State(g.IsReady(mate) || (App.Pvp != null && App.Pvp.IsDemo));
                float top = SlotY(size) + HeadTop(look, size * 0.72f) + 36;
                Put(_mateName.rectTransform, SlotX(size), top + 34, size * 0.6f, 36);
                Put(_mateState.rectTransform, SlotX(size), top, size * 0.6f, 32);
            }
            else if (slot)
            {
                _inviteLabel.text = Loc.T(_shownPending != null ? "Invitation envoyée…" : "Inviter");
                _badge.gameObject.SetActive(_invites > 0);
                _badgeText.text = Loc.P(_invites, "{0} invitation", "{0} invitations");
            }
        }

        /// <summary>Top of the head over the sprite's centre: the 32x40 outfit keeps its top quarter for a hat.</summary>
        static float HeadTop(Loadout look, float size) =>
            size * (look.Hat != null && look.Hat.Id != SkinCatalog.NoHatId ? 0.5f : 0.25f);

        static string State(bool ready) => ready ? "<color=#E8C35A>" + Loc.T("Prêt") + "</color>" : Loc.T("Pas prêt");

        static void Center(RectTransform rt) => rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        static void Put(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        Text StageLabel(int size, Color color)
        {
            var t = UIKit.Label(_stage, "", size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            Center(t.rectTransform);
            UIKit.FitText(t, 16);
            return t;
        }

        /// <summary>A soft dark ellipse at a mummy's feet: sets it apart from the (blurred) tomb behind.</summary>
        Image FootShadow()
        {
            var g = UIKit.Image(_stage, UISprites.RadialGlow, Color.black, false, "Shadow"); // noloc
            Center(g.rectTransform);
            g.transform.SetAsFirstSibling();
            return g;
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
        /// <summary>Act drawn when nothing is chosen or the chosen act is locked.</summary>
        public const int Default = 1;
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

    /// <summary>Home screen backdrop picker: every act (locked ones shown with a padlock).</summary>
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
            UIKit.Size(UIKit.Button(panel, "Fermer", () => Router.Close(this), UIKit.TextSize, ButtonStyle.Leave), UIKit.ButtonHeight);
        }

        public override void OnShow()
        {
            UIKit.ClearChildren(_list);
            var save = App.Save;
            int chosen = App.Settings.HomeTheme;
            if (HomeThemes.Get(chosen)?.IsUnlocked(save) != true) chosen = HomeThemes.Default;
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
