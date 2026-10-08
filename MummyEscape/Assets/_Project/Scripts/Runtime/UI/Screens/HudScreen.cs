using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>In-game overlay: level, moves (the par stays hidden), HP (ankhs), floor, status effects and the map peek button.</summary>
    public sealed class HudScreen : UIScreen, IBackHandler
    {
        Text _level;
        Text _moves;
        Text _floor;
        Text _status;
        Text _hint;
        int _rotation, _turnedAt;
        RectTransform _hearts;
        readonly List<Image> _ankhs = new List<Image>();
        RectTransform _bottomBar;
        Button _disarm;
        MoveControls _controls;
        // Tutorial: what the stretch the mummy walks in teaches.
        RectTransform _lesson;
        Text _lessonText;
        TutorialLesson _shownLesson;
        CanvasGroup _preview;
        Text _previewTitle;
        Text _previewCount;
        Button _ready;
        CanvasGroup _intro;
        Text _introText;
        float _introTimer;
        // Duel: both runs' progress and the time left.
        RectTransform _duel;
        Image _myBar, _rivalBar;
        Text _myName, _rivalName, _timeLeft;
        // 2v2: one dot per leg for each duo, lit at each hand-off, the clock, and whose turn it is.
        RectTransform _relay;
        readonly List<Image> _myDots = new List<Image>(), _rivalDots = new List<Image>();
        RectTransform _myDotRow, _rivalDotRow;
        Text _myDuo, _rivalDuo, _relayTime, _turn;

        protected override void Build()
        {
            var top = UIKit.Rect("TopBar", Root);
            UIKit.TopBand(top, 200, 16);
            top.offsetMin = new Vector2(24, top.offsetMin.y);
            top.offsetMax = new Vector2(-24, top.offsetMax.y);
            var bg = UIKit.Plate(top, new Color(0.05f, 0.035f, 0.02f, 0.72f), 40, UIKit.Rim);
            UIKit.Stretch(bg.rectTransform);

            var pause = UIKit.IconButton(top, UISprites.Pause, OnBack, 96);
            UIKit.Place((RectTransform)pause.transform, 0, 0.5f, 96, 96, 22, 0);

            _level = UIKit.Title(top, "", 44);
            UIKit.Stretch(_level.rectTransform, 150, 22, 150, 110);
            _moves = UIKit.Label(top, "", 34, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_moves.rectTransform, 150, 92, 150, 52);
            _floor = UIKit.Label(top, "", 26, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_floor.rectTransform, 150, 146, 150, 14);

            _hearts = UIKit.Rect("Hearts", top);
            _hearts.anchorMin = _hearts.anchorMax = new Vector2(1, 0.5f);
            _hearts.pivot = new Vector2(1, 0.5f);
            _hearts.sizeDelta = new Vector2(200, 80);
            _hearts.anchoredPosition = new Vector2(-26, 0);
            var h = _hearts.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight;
            h.spacing = 8;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            _status = UIKit.Label(Root, "", 40, new Color(0.75f, 0.55f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.TopBand(_status.rectTransform, 90, 240);

            _lesson = UIKit.Rect("Lesson", Root); // noloc
            UIKit.TopBand(_lesson, 170, 340);
            _lesson.offsetMin = new Vector2(40, _lesson.offsetMin.y);
            _lesson.offsetMax = new Vector2(-40, _lesson.offsetMax.y);
            var lbg = UIKit.Plate(_lesson, new Color(0.05f, 0.035f, 0.02f, 0.82f), 32, UIKit.Gold);
            UIKit.Stretch(lbg.rectTransform);
            _lessonText = UIKit.Label(_lesson, "", 34, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_lessonText.rectTransform, 28, 12, 28, 12);
            UIKit.FitText(_lessonText, 22);
            _lesson.gameObject.SetActive(false);

            // Duel panel under the top bar: how far each mummy got, and the clock running down.
            _duel = UIKit.Rect("Duel", Root);
            UIKit.TopBand(_duel, 132, 226);
            _duel.offsetMin = new Vector2(24, _duel.offsetMin.y);
            _duel.offsetMax = new Vector2(-24, _duel.offsetMax.y);
            var dbg = UIKit.Plate(_duel, new Color(0.05f, 0.035f, 0.02f, 0.72f), 32, UIKit.Rim);
            UIKit.Stretch(dbg.rectTransform);
            _myBar = ProgressBar(_duel, 0, UIKit.Gold, out _myName);
            _rivalBar = ProgressBar(_duel, 1, UIKit.Turquoise, out _rivalName);
            _timeLeft = UIKit.Label(_duel, "", 54, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_timeLeft.rectTransform, 1, 0.5f, 170, 110, -16, 0);
            _duel.gameObject.SetActive(false);

            _relay = UIKit.Rect("Relay", Root); // noloc
            UIKit.TopBand(_relay, 132, 226);
            _relay.offsetMin = new Vector2(24, _relay.offsetMin.y);
            _relay.offsetMax = new Vector2(-24, _relay.offsetMax.y);
            var rbg = UIKit.Plate(_relay, new Color(0.05f, 0.035f, 0.02f, 0.72f), 32, UIKit.Rim);
            UIKit.Stretch(rbg.rectTransform);
            _myDotRow = DotRow(_relay, 0, UIKit.Gold, out _myDuo);
            _rivalDotRow = DotRow(_relay, 1, UIKit.Turquoise, out _rivalDuo);
            _relayTime = UIKit.Label(_relay, "", 54, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_relayTime.rectTransform, 1, 0.5f, 170, 110, -16, 0);
            _relay.gameObject.SetActive(false);
            _turn = UIKit.Label(Root, "", 36, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_turn, 22);
            UIKit.TopBand(_turn.rectTransform, 70, 368);
            UIKit.DropShadow(_turn, 4, 0.7f);

            // Bottom: map peek (hold) + hint.
            var bottom = UIKit.Rect("BottomBar", Root);
            UIKit.BottomBand(bottom, 150, 24);
            var map = UIKit.Button(bottom, "", null);
            UIKit.Place((RectTransform)map.transform, 1, 0.5f, 230, 96, -30, 0);
            var mapRow = map.gameObject.AddComponent<HorizontalLayoutGroup>();
            mapRow.childAlignment = TextAnchor.MiddleCenter;
            mapRow.spacing = 12;
            mapRow.childControlWidth = mapRow.childControlHeight = true;
            mapRow.childForceExpandWidth = mapRow.childForceExpandHeight = false;
            UIKit.Size(UIKit.Image(map.transform, UISprites.Map, UIKit.Gold), 44, 44);
            var mapLabel = UIKit.Label(map.transform, "Carte", UIKit.TextSize, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            mapLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(mapLabel, 50);
            var trigger = map.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerDown, () => App.Game.SetMapView(true));
            AddTrigger(trigger, EventTriggerType.PointerUp, () => App.Game.SetMapView(false));

            // Disarm: lights up only next to spikes the torch shows (not in the dark, not blinded, not cursed sand).
            _disarm = UIKit.Button(bottom, "Désamorcer", () => App.Game.DisarmAdjacent(), 34, ButtonStyle.Primary);
            UIKit.FitText(_disarm.GetComponentInChildren<Text>(), 22);
            UIKit.Place((RectTransform)_disarm.transform, 1, 1, 330, 100, -30, 110);

            _hint = UIKit.Label(bottom, "", 30, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIKit.Stretch(_hint.rectTransform, 44, 0, 290, 0);
            _bottomBar = bottom;

            // On-screen pad or joystick, when the player chose one over swipes.
            _controls = MoveControls.Create(Root, App.Game);

            // Start-of-run map preview: countdown + "ready" to start early. Replaces the bottom bar meanwhile.
            var pv = UIKit.Rect("Preview", Root);
            UIKit.BottomBand(pv, 180, 24);
            pv.offsetMin = new Vector2(24, pv.offsetMin.y);
            pv.offsetMax = new Vector2(-24, pv.offsetMax.y);
            var pbg = UIKit.Plate(pv, new Color(0.05f, 0.035f, 0.02f, 0.8f), 40, UIKit.Rim);
            UIKit.Stretch(pbg.rectTransform);
            _previewTitle = UIKit.Label(pv, "", 36, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Stretch(_previewTitle.rectTransform, 40, 0, 440, 0);
            _previewCount = UIKit.Label(pv, "", 84, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_previewCount.rectTransform, 1, 0.5f, 130, 150, -290, 0);
            var ready = _ready = UIKit.Button(pv, "Prêt", () => App.Game.SkipPreview(), 34, ButtonStyle.Primary);
            UIKit.FitText(ready.GetComponentInChildren<Text>(), 22);
            UIKit.Place((RectTransform)ready.transform, 1, 0.5f, 250, 104, -28, 0);
            _preview = pv.gameObject.AddComponent<CanvasGroup>();
            SetPreviewVisible(false);

            // Level intro card.
            var intro = UIKit.Rect("Intro", Root);
            UIKit.FitInParent(UIKit.Place(intro, 0.5f, 0.5f, 900, 300));
            var ibg = UIKit.Plate(intro, new Color(0.05f, 0.035f, 0.02f, 0.85f), 40, UIKit.Rim);
            UIKit.Stretch(ibg.rectTransform);
            _introText = UIKit.Title(intro, "", 54);
            UIKit.Stretch(_introText.rectTransform, 40, 20, 40, 20);
            _intro = intro.gameObject.AddComponent<CanvasGroup>();
            _intro.blocksRaycasts = false;
            _intro.alpha = 0;
        }

        /// <summary>One row of the duel panel (0 = top): a name, then a track filling with the progress.</summary>
        static Image ProgressBar(RectTransform parent, int index, Color color, out Text label)
        {
            var row = UIKit.Rect("Row" + index, parent); // noloc
            row.anchorMin = new Vector2(0, index == 0 ? 0.5f : 0f);
            row.anchorMax = new Vector2(1, index == 0 ? 1f : 0.5f);
            row.offsetMin = new Vector2(26, index == 0 ? 2 : 10);
            row.offsetMax = new Vector2(-196, index == 0 ? -10 : -2);
            label = UIKit.Label(row, "", 26, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(label, 16);
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0, 0);
            lr.anchorMax = new Vector2(0.45f, 1);
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var track = UIKit.Image(row, UISprites.Round, new Color(1, 1, 1, 0.12f), false, "Track"); // noloc
            UIKit.Rounded(track, 12);
            track.rectTransform.anchorMin = new Vector2(0.47f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.offsetMin = new Vector2(0, -12);
            track.rectTransform.offsetMax = new Vector2(0, 12);
            var fill = UIKit.Image(track.transform, UISprites.Round, color, false, "Fill"); // noloc
            UIKit.Rounded(fill, 12);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0.04f, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return fill;
        }

        /// <summary>One duo of the 2v2 panel (0 = top): its name, then a row for the leg dots.</summary>
        static RectTransform DotRow(RectTransform parent, int index, Color color, out Text label)
        {
            var row = UIKit.Rect("Row" + index, parent); // noloc
            row.anchorMin = new Vector2(0, index == 0 ? 0.5f : 0f);
            row.anchorMax = new Vector2(1, index == 0 ? 1f : 0.5f);
            row.offsetMin = new Vector2(26, index == 0 ? 2 : 10);
            row.offsetMax = new Vector2(-196, index == 0 ? -10 : -2);
            label = UIKit.Label(row, "", 26, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(label, 16);
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0, 0);
            lr.anchorMax = new Vector2(0.45f, 1);
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var dots = UIKit.Rect("Dots", row); // noloc
            dots.anchorMin = new Vector2(0.47f, 0);
            dots.anchorMax = new Vector2(1, 1);
            dots.offsetMin = dots.offsetMax = Vector2.zero;
            var h = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 14;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return dots;
        }

        static void SetDots(RectTransform row, List<Image> dots, int count, int lit, Color color)
        {
            while (dots.Count < count)
            {
                var d = UIKit.Image(row, UISprites.Round, Color.white, false, "Dot"); // noloc
                UIKit.Size(d, 34, 34);
                dots.Add(d);
            }
            for (int i = 0; i < dots.Count; i++)
            {
                dots[i].gameObject.SetActive(i < count);
                // The last dot is the finish line.
                bool finish = i == count - 1;
                dots[i].color = i < lit ? color : new Color(1, 1, 1, finish ? 0.28f : 0.14f);
                dots[i].rectTransform.localScale = Vector3.one * (finish ? 1.2f : 1f);
            }
        }

        void UpdateRelay()
        {
            var game = App.Game;
            bool relay = game.InRelay && game.RelayMap != null;
            if (_relay.gameObject.activeSelf != relay)
            {
                _relay.gameObject.SetActive(relay);
                UIKit.TopBand(_status.rectTransform, 90, relay ? 440 : 240);
            }
            if (!relay)
            {
                if (_turn.text != "") _turn.text = "";
                return;
            }
            int n = game.RelayMap.Segments;
            SetDots(_myDotRow, _myDots, n, game.MyRace.Segment, UIKit.Gold);
            SetDots(_rivalDotRow, _rivalDots, n, game.RivalRace.Segment, UIKit.Turquoise);
            _myDuo.text = game.MySide.Name;
            _rivalDuo.text = game.RivalSide.Name;
            int left = game.RelayTimeLeftMs, sec = (left + 999) / 1000;
            _relayTime.text = $"{sec / 60}:{sec % 60:00}";
            _relayTime.color = sec <= 30 ? UIKit.Danger : UIKit.Sand;
            _turn.text = TurnText(game);
        }

        static string TurnText(Game.GameController game)
        {
            if (game.Phase != Game.RelayPhase.Racing || game.MyRace.IsOver) return "";
            var race = game.MyRace;
            if (game.MyTurn)
            {
                bool last = race.Segment == game.RelayMap.Segments - 1;
                return last ? Loc.T("À toi ! Trouve la sortie !") : Loc.T("À toi ! Cours jusqu'à ta dalle");
            }
            var runner = game.MySide.RunnerOf(race.ActiveMaze);
            string where = race.Segment == 0 ? Loc.T("tu attends sur le départ") : Loc.T("tu attends sur ta dalle");
            return Loc.F("{0} court · {1}", runner?.Name ?? "?", where);
        }

        void OnRelayChanged()
        {
            if (App.Game.Phase == Game.RelayPhase.Vote && !(Router.Current is RelayVoteScreen) && !_voteShown)
            {
                _voteShown = true;
                Router.Open<RelayVoteScreen>();
            }
            if (App.Game.Phase != Game.RelayPhase.Vote) _voteShown = false;
            Refresh();
        }

        bool _voteShown;

        static void SetProgress(Image fill, float p) =>
            fill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(fill.rectTransform.anchorMax.x, Mathf.Clamp(p, 0.04f, 1f), 0.25f), 1);

        void UpdateDuel()
        {
            var match = App.Game.Match;
            bool duel = match != null;
            if (_duel.gameObject.activeSelf != duel)
            {
                _duel.gameObject.SetActive(duel);
                UIKit.TopBand(_status.rectTransform, 90, duel ? 372 : 240);
            }
            if (!duel) return;
            int left = App.Game.DuelTimeLeftMs, sec = (left + 999) / 1000;
            _timeLeft.text = $"{sec / 60}:{sec % 60:00}";
            _timeLeft.color = sec <= 30 ? UIKit.Danger : UIKit.Sand;
            _myName.text = Loc.T("Toi");
            SetProgress(_myBar, match.MyProgress);
            _rivalBar.transform.parent.gameObject.SetActive(match.HasGhost);
            if (!match.HasGhost)
            {
                _rivalName.text = Loc.T("Pas de rival : tu ouvres la voie");
                return;
            }
            int elapsed = App.Game.Session?.ElapsedMs ?? 0;
            string state = !match.GhostDone(elapsed) ? ""
                         : match.Ghost.Outcome == Pvp.RunOutcome.Finished ? "  · " + Loc.F("sorti en {0}", LevelResult.FormatTime(match.Ghost.TimeMs))
                         : "  · " + Loc.T("éliminé");
            string title = TitleBook.Line(match.Ghost.Look?.Title);
            _rivalName.text = $"{match.Ghost.PlayerName}{(title == "" ? "" : " " + title)} · {match.Ghost.Elo}{state}";
            SetProgress(_rivalBar, match.GhostProgress);
        }

        static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        public override void OnShow()
        {
            App.Game.Changed += Refresh;
            App.Game.PreviewChanged += Refresh;
            App.Game.LevelLoading += ShowLoading;
            App.Game.LevelStarted += ShowIntro;
            App.Game.RelayChanged += OnRelayChanged;
            App.Game.SetPaused(false);
            App.Settings.Changed += ApplyControls;
            App.Game.TutorialEnded += OnTutorialEnded;
            ApplyControls();
            Refresh();
        }

        public override void OnHide()
        {
            App.Game.Changed -= Refresh;
            App.Game.PreviewChanged -= Refresh;
            App.Game.LevelLoading -= ShowLoading;
            App.Game.LevelStarted -= ShowIntro;
            App.Game.RelayChanged -= OnRelayChanged;
            App.Settings.Changed -= ApplyControls;
            App.Game.TutorialEnded -= OnTutorialEnded;
        }

        void ApplyControls() => _controls.Apply(App.Settings.Controls);

        void OnTutorialEnded(bool won) => Router.Open<TutorialEndScreen>().Show(won);

        /// <summary>Tutorial: the lesson of the stretch the mummy walks in, popping in as each new one starts.</summary>
        void UpdateLesson(GameSession s)
        {
            if (!App.Game.InTutorial) { _shownLesson = null; _lesson.gameObject.SetActive(false); return; }
            var lesson = Tutorial.LessonAt(s.Position) ?? _shownLesson;
            if (lesson != _shownLesson && lesson != null)
            {
                _lessonText.text = Loc.T(lesson.Text);
                if (_lesson.gameObject.activeInHierarchy) UIFx.PopIn(_lesson);
            }
            _shownLesson = lesson;
            _lesson.gameObject.SetActive(lesson != null && s.Status == SessionStatus.Playing);
        }

        public void OnBack()
        {
            if (App.Game.Session == null) { Router.Reset<MainMenuScreen>(); return; }
            Router.Open<PauseScreen>();
        }

        void ShowLoading(LevelId id)
        {
            var match = App.Game.Match;
            string title = App.Game.InTutorial ? Loc.T("Tutoriel")
                         : match == null ? Loc.F("Niveau {0}", id)
                         : match.HasGhost ? Loc.F("Duel contre {0}", match.Ghost.PlayerName)
                         : Loc.T("Duel");
            _introText.text = title + "\n<size=36>" + Loc.T("Les dieux scellent un nouveau tombeau…") + "</size>";
            UpdateDuel();
            _intro.alpha = 1f;
            _introTimer = float.MaxValue;
        }

        void ShowIntro()
        {
            // The map preview starts right away: no card over it, the title lives in the preview bar.
            _intro.alpha = 0f;
            _introTimer = -1f;
            Refresh();
        }

        void SetPreviewVisible(bool on)
        {
            _preview.alpha = on ? 1f : 0f;
            _preview.blocksRaycasts = on;
            _preview.interactable = on;
            _bottomBar.gameObject.SetActive(!on);
        }

        void Update()
        {
            var session = App.Game.Session;
            if (session != null) UpdateCounter(session);
            UpdateDuel();
            UpdateRelay();
            if (App.Game.Previewing)
                _previewCount.text = Mathf.Max(1, Mathf.CeilToInt(App.Game.PreviewLeft)).ToString();
            if (_introTimer == float.MaxValue || _introTimer < 0f) return;
            _introTimer -= Time.deltaTime;
            if (_introTimer < 0.6f) _intro.alpha = Mathf.Clamp01(_introTimer / 0.6f);
        }

        /// <summary>"Coups : 12 · 0:42" — the clock starts once the tomb is hidden.</summary>
        void UpdateCounter(GameSession s)
        {
            if (App.Game.InRelay)
            {
                int rs = App.Game.RelayClockMs / 1000, legs = App.Game.RelayMap?.Segments ?? 0;
                string relay = Loc.F("Étape {0} / {1}", Mathf.Min(legs, (App.Game.MyRace?.Segment ?? 0) + 1), legs) + $"  ·  {rs / 60}:{rs % 60:00}";
                if (_moves.text != relay) _moves.text = relay;
                return;
            }
            int sec = s.ElapsedMs / 1000;
            string text = Loc.F("Coups : {0}", s.Moves) + $"  ·  {sec / 60}:{sec % 60:00}";
            // Solo: the expert mummy's time on this maze, red once the clock has gone past it.
            var pace = App.Game.Pace;
            int target = pace != null && pace.IsCompletedSuccessfully ? pace.Result.TargetMs ?? 0 : 0;
            if (target > 0)
            {
                string toBeat = Loc.F("à battre {0}", LevelResult.FormatTime(target));
                text += $"  <size=26><color=#{(s.ElapsedMs > target ? "E06A4A" : "9C8B70")}>· {toBeat}</color></size>"; // noloc
            }
            if (_moves.text != text) _moves.text = text;
        }

        void Refresh()
        {
            var s = App.Game.Session;
            if (s == null) return;
            var level = s.Level;
            _level.text = App.Game.InRelay ? "2v2" : App.Game.InTutorial ? Loc.T("Tutoriel") : App.Game.InDuel ? Loc.T("Duel") : Loc.F("Niveau {0}", level.Id); // noloc
            UpdateCounter(s);
            UpdateLesson(s);
            _floor.text = level.Floors > 1 && !App.Game.Previewing ? Loc.F("Étage {0} / {1}", s.Position.Floor + 1, level.Floors) : "";

            bool preview = App.Game.Previewing;
            SetPreviewVisible(preview);
            if (preview)
            {
                var act = DifficultyTable.GetAct(level.Id.Act);
                string sub = level.Floors > 1 ? Loc.F("Étage {0} / {1}", App.Game.PreviewFloor + 1, level.Floors) + "  ·  " + Loc.T("glisse pour changer d'étage") : Loc.F("Acte {0} — {1}", level.Id.Act, Loc.T(act.Name));
                if (App.Game.InRelay)
                {
                    // Both mazes, every floor, nothing to skip: the four phones keep in step.
                    string maze = App.Game.PreviewMaze == 0 ? Loc.T("Labyrinthe 1 · départ") : Loc.T("Labyrinthe 2 · arrivée");
                    if (level.Floors > 1) maze += "  ·  " + Loc.F("Étage {0} / {1}", App.Game.PreviewFloor + 1, level.Floors);
                    sub = maze + (App.Game.PreviewPages > 1 ? "  ·  " + Loc.T("glisse pour changer") : "");
                }
                _previewTitle.text = Loc.T("Mémorise le tombeau !") + $"\n<size=30>{sub}</size>";
                _ready.gameObject.SetActive(!App.Game.InRelay && !App.Game.InLiveDuel); // live: same time for everyone
            }


            _disarm.interactable = !preview && s.Status == SessionStatus.Playing && s.CanDisarm(out _);

            while (_ankhs.Count < level.MaxHp)
            {
                var img = UIKit.Image(_hearts, UIKit.Art.Ankh, Color.white);
                UIKit.Size(img, 70, 70);
                _ankhs.Add(img);
            }
            for (int i = 0; i < _ankhs.Count; i++)
            {
                _ankhs[i].gameObject.SetActive(i < level.MaxHp);
                _ankhs[i].sprite = i < s.Hp ? UIKit.Art.Ankh : UIKit.Art.AnkhEmpty;
            }

            // The tomb just turned: say so for a few steps (the turn itself lasts the whole run).
            if (s.State.Rotation != _rotation) { _rotation = s.State.Rotation; _turnedAt = s.Moves; }
            bool justTurned = _rotation != 0 && s.Moves - _turnedAt < 4;
            int reversed = s.State.Reversed;
            _status.color = preview ? new Color(1f, 0.45f, 0.35f) : reversed > 0 ? new Color(1f, 0.45f, 0.85f)
                          : justTurned ? new Color(1f, 0.82f, 0.4f) : s.IsBlind ? new Color(0.75f, 0.55f, 1f) : new Color(1f, 0.62f, 0.3f);
            _status.text = preview
                    ? App.Game.ScreenCaptured ? Loc.T("Enregistrement d'écran détecté :\nle tombeau reste dans l'ombre")
                    : App.Game.ScreenshotRedraw ? Loc.T("Capture d'écran : les dieux ont scellé\nun autre tombeau !")
                    : ""
                : App.Game.DuelWaiting ? Loc.F("En attente de {0}…", App.Game.Match.Ghost?.PlayerName ?? "?")
                : reversed > 0 ? Loc.F("Commandes inversées ! ({0})", reversed)
                : justTurned ? Loc.T("Le tombeau a pivoté !\nTes gestes suivent l'écran.")
                : s.CanDisarm(out _) && (s.IsBlind || !s.TorchLit) ? Loc.T("Ces pics, tu les as sentis :\ndésarme-les à tâtons")
                : s.IsBlind ? Loc.F("Aveuglé ! ({0})", s.BlindTurnsLeft)
                : !s.TorchLit ? Loc.T("Torche éteinte : longe une torche murale")
                : "";

            // Act 1 teaches the controls; later acts recall their own rule first, then the map button.
            string Controls = Loc.T("Glisse pour avancer d'une case.\nTa torche éclaire les cases voisines.");
            string Map = Loc.T("Maintiens « Carte » pour revoir ce que tu as exploré.");
            string mechanic = ActHint(level.Id.Act);
            _hint.text = preview || App.Game.InTutorial ? ""
                : mechanic == null ? (s.Moves == 0 ? Controls : s.Moves < 4 ? Map : "")
                : s.Moves < 4 ? mechanic
                : s.Moves < 8 ? Map
                : "";
        }

        /// <summary>The rule each act adds, recalled at the start of its tombs.</summary>
        static string ActHint(int act)
        {
            switch (act)
            {
                case 2: return Loc.T("Les courants t'emportent jusqu'au bout\net ne se remontent pas.");
                case 3: return Loc.T("Les dalles fissurées s'effondrent\ndès que tu les quittes.");
                case 4: return Loc.T("Un levier inverse les barrières :\nrouges ouvertes, bleues fermées.");
                case 5: return Loc.T("Les jets de flammes crachent un pas sur trois :\nobserve leur rythme.");
            }
            return null;
        }
    }
}
