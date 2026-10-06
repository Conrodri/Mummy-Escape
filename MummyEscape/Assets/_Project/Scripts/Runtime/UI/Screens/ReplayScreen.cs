using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using MummyEscape.World;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// A duel watched again from both sides at once: the player's run on top ("TOI"), the rival's below ("RIVAL"),
    /// each with its own fog and the swipes played, on one shared clock (play, pause, x2, timeline). The rival can be
    /// reported for cheating from here: the whole duel goes to a file reviewed by a person.
    /// </summary>
    public sealed class ReplayScreen : UIScreen
    {
        const int StripSlots = 9;
        const float SeekEvery = 0.12f;

        static ReplayRig _rig;

        RectTransform _topArea, _bottomArea;
        RawImage _topView, _bottomView;
        Side _me, _rival;
        Text _status, _waiting, _title;
        Button _play, _speed, _report;
        Image _playIcon;
        Slider _timeline;
        DuelRecord _duel;
        RelayRecord _relay;
        int _limitMs = PvpConfig.TimeLimitMs;
        bool _spectator;
        string _verdict;
        int _load;
        float _pendingSeek = -1f, _lastSeek;
        int _savedMask;

        sealed class Side
        {
            public Text Tag, Name, State, Count;
            public Image[] Slots = new Image[StripSlots];
            public int Shown = -1;
            public float ChangedAt;
            public DuelRun Run;
        }

        protected override void Build()
        {
            // Header: back, title, report.
            var header = UIKit.Image(Root, UIKit.Art.White, new Color(0.04f, 0.03f, 0.02f, 0.94f), true, "Header"); // noloc
            UIKit.TopBand(header.rectTransform, 150);
            var back = UIKit.IconButton(header.transform, UISprites.Back, () => Router.Back(), 92);
            UIKit.Place((RectTransform)back.transform, 0, 0.5f, 92, 92, 36, 0);
            var title = _title = UIKit.Title(header.transform, "Revoir le duel", 50);
            UIKit.FitText(title, 30);
            UIKit.Stretch(title.rectTransform, 150, 0, 150, 0);
            _report = UIKit.IconButton(header.transform, UISprites.Flag, Report, 92, ButtonStyle.Danger);
            UIKit.Place((RectTransform)_report.transform, 1, 0.5f, 92, 92, -36, 0);

            // The two views (drawn by the replay cameras behind the canvas) and their overlays.
            var views = UIKit.Rect("Views", Root); // noloc
            UIKit.Stretch(views, 0, 150, 0, 250);
            _topArea = UIKit.Rect("Top", views); // noloc
            _topArea.anchorMin = new Vector2(0, 0.5f);
            _topArea.anchorMax = Vector2.one;
            _topArea.offsetMin = new Vector2(0, 5);
            _topArea.offsetMax = Vector2.zero;
            _bottomArea = UIKit.Rect("Bottom", views); // noloc
            _bottomArea.anchorMin = Vector2.zero;
            _bottomArea.anchorMax = new Vector2(1, 0.5f);
            _bottomArea.offsetMin = Vector2.zero;
            _bottomArea.offsetMax = new Vector2(0, -5);
            _topView = View(_topArea);
            _bottomView = View(_bottomArea);
            _me = MakeSide(_topArea, "TOI", UIKit.Gold);
            _rival = MakeSide(_bottomArea, "RIVAL", UIKit.Turquoise);

            var divider = UIKit.Image(views, UIKit.Art.White, UIKit.Gold, false, "Divider"); // noloc
            UIKit.Place(divider.rectTransform, 0.5f, 0.5f, 2000, 10);
            var vs = UIKit.Plate(views, UIKit.Ink, 34, UIKit.Gold);
            UIKit.Place(vs.rectTransform, 0.5f, 0.5f, 120, 68);
            var vsText = UIKit.Title(vs.transform, "VS", 40); // noloc
            UIKit.Stretch(vsText.rectTransform);

            _waiting = UIKit.Label(_bottomArea, "", 30, UIKit.Sand);
            UIKit.FitText(_waiting, 20);
            UIKit.Stretch(_waiting.rectTransform, 60, 60, 60, 60);

            // Controls.
            var bar = UIKit.Image(Root, UIKit.Art.White, new Color(0.04f, 0.03f, 0.02f, 0.94f), true, "Controls"); // noloc
            UIKit.BottomBand(bar.rectTransform, 250);
            _status = UIKit.Label(bar.transform, "", 26, UIKit.Dim);
            UIKit.FitText(_status, 18);
            UIKit.TopBand(_status.rectTransform, 44, 8);
            _play = UIKit.IconButton(bar.transform, UISprites.Play, TogglePlay, 104, ButtonStyle.Primary);
            UIKit.Place((RectTransform)_play.transform, 0, 0, 104, 104, 40, 40);
            _playIcon = _play.transform.Find("Icon")?.GetComponent<Image>(); // noloc
            _speed = UIKit.Button(bar.transform, "x1", ToggleSpeed, 34); // noloc
            UIKit.Place((RectTransform)_speed.transform, 0, 0, 120, 92, 164, 46);
            _timeline = UIKit.Slider(bar.transform, "", 0f, v => _pendingSeek = v, 0f, 1f, v => Clock(v));
            var row = (RectTransform)_timeline.transform.parent;
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(0.5f, 0);
            row.offsetMin = new Vector2(310, 36);
            row.offsetMax = new Vector2(-40, 148);
        }

        Side MakeSide(RectTransform area, string tag, Color accent)
        {
            var side = new Side();
            var plate = UIKit.Plate(area, new Color(0, 0, 0, 0.6f), 30, new Color(accent.r, accent.g, accent.b, 0.6f));
            UIKit.Place(plate.rectTransform, 0, 1, 620, 112, 24, -18);
            side.Tag = UIKit.Title(plate.transform, tag, 34, accent, TextAnchor.MiddleLeft);
            UIKit.FitText(side.Tag, 20);
            UIKit.Place(side.Tag.rectTransform, 0, 1, 570, 52, 26, -6);
            side.Name = UIKit.Label(plate.transform, "", 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(side.Name, 18);
            UIKit.Place(side.Name.rectTransform, 0, 0, 570, 52, 26, 6);
            side.State = UIKit.Label(area, "", 30, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.FitText(side.State, 18);
            UIKit.Place(side.State.rectTransform, 1, 1, 380, 60, -28, -30);
            UIKit.DropShadow(side.State, 4, 0.7f);

            // The swipes, the latest on the right.
            var strip = UIKit.Plate(area, new Color(0, 0, 0, 0.55f), 40, new Color(accent.r, accent.g, accent.b, 0.4f));
            strip.rectTransform.anchorMin = new Vector2(0, 0);
            strip.rectTransform.anchorMax = new Vector2(1, 0);
            strip.rectTransform.pivot = new Vector2(0.5f, 0);
            strip.rectTransform.offsetMin = new Vector2(24, 18);
            strip.rectTransform.offsetMax = new Vector2(-24, 114);
            for (int i = 0; i < StripSlots; i++)
            {
                var img = UIKit.Image(strip.transform, UISprites.Arrow, Color.clear, false, "Input"); // noloc
                // Evenly spread over the strip, whatever the screen width.
                UIKit.Place(img.rectTransform, (i + 0.5f) / StripSlots, 0.5f, 60, 60);
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                img.rectTransform.anchoredPosition = Vector2.zero;
                side.Slots[i] = img;
            }
            side.Count = UIKit.Label(area, "", 24, UIKit.Dim, TextAnchor.MiddleRight);
            UIKit.Place(side.Count.rectTransform, 1, 0, 300, 40, -50, 120);
            return side;
        }

        /// <summary>Opens a duel of the history.</summary>
        public void Show(DuelRecord duel) => Show(duel, false, "TOI", "RIVAL", null);

        /// <summary>
        /// Opens a round of a team battle as a spectator: traps and points of interest are hidden, the two sides are
        /// named by their team, and there is nobody to report from here.
        /// </summary>
        public void ShowRound(DuelRecord round, string topTeam, string bottomTeam, string verdict) =>
            Show(round, true, topTeam, bottomTeam, verdict);

        /// <summary>
        /// Opens a 2v2 match: the player's duo on top, the rival duo below, each view following its duo's runner of the
        /// moment (over to the teammate's maze at each relay plate).
        /// </summary>
        public void ShowRelay(RelayRecord record)
        {
            _duel = null;
            _relay = record;
            _spectator = false;
            _verdict = null;
            _limitMs = RelayConfig.TimeLimitMs;
            _title.text = Loc.T("Revoir le match");
            var mine = record.Mine;
            var rival = record.Rival;
            _me.Tag.text = Loc.T("TON DUO");
            _rival.Tag.text = Loc.T("DUO ADVERSE");
            FillRelay(_me, mine);
            FillRelay(_rival, rival);
            bool rivalRun = rival?.Inputs != null && rival.Inputs.Count > 0;
            _waiting.text = rivalRun ? "" : Loc.T("Le relais du duo adverse n'est pas encore arrivé.");
            _report.gameObject.SetActive(false);
            _status.text = Loc.T("Chargement…");
            SetPlaying(false);
            LoadRelay(record);
        }

        static void FillRelay(Side side, RelaySide relay)
        {
            var s = relay?.Verified;
            var run = relay == null ? null : new DuelRun
            {
                PlayerName = relay.Name,
                Outcome = s == null ? RunOutcome.TimedOut : s.Finished ? RunOutcome.Finished : s.Lost ? RunOutcome.Died : RunOutcome.TimedOut,
                TimeMs = s?.TimeMs ?? 0,
                Inputs = new System.Collections.Generic.List<RunInput>(),
            };
            if (relay?.Inputs != null)
                foreach (var i in relay.Inputs) run.Inputs.Add(new RunInput { Tick = i.Tick, Direction = i.Direction });
            Fill(side, run, null);
        }

        async void LoadRelay(RelayRecord record)
        {
            int load = ++_load;
            var match = record.Match;
            if (match.GeneratorVersion != 0 && match.GeneratorVersion != DifficultyTable.GeneratorVersion)
            {
                _status.text = Loc.T("Match joué sur une ancienne version du jeu : il ne peut plus être rejoué.");
                return;
            }
            RelayMap map;
            try { map = await Task.Run(() => PvpServer.RelayArenaFor(match.Seed)); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Pvp] Replay relay not generated: " + e.Message);
                _status.text = Loc.T("Ce match ne peut pas être rejoué.");
                return;
            }
            if (load != _load || this == null || !isActiveAndEnabled) return;

            var theme = TombTheme.ForAct(RelayArena.LevelFor(match.Seed).Act);
            App.Art.SetTheme(theme);
            App.Lighting.SetTheme(theme);
            App.Lighting.SetMood(true);
            if (_rig == null) _rig = ReplayRig.Create(App.transform, App);
            _rig.OpenRelay(map, record.Mine, record.Rival, theme);
            _rig.Speed = 1f;
            _speed.GetComponentInChildren<Text>().text = "x1"; // noloc
            _status.text = RelayVerdict(record);
            SetPlaying(true);
        }

        static string RelayVerdict(RelayRecord r)
        {
            string result = Loc.T(r.Result == DuelResult.Win ? "Victoire" : r.Result == DuelResult.Draw ? "Match nul" : "Défaite");
            if (!r.Resolved) return result + " · " + Loc.T("verdict du serveur en attente");
            return result + " · " + Loc.F("Elo 2v2 {0}", r.NewElo) + " (" + (r.EloDelta > 0 ? "+" : "") + r.EloDelta + ")"; // noloc
        }

        /// <summary>A relay's step on screen ("2/4 · "), null for a duel.</summary>
        static string Steps(ReplayView view)
        {
            if (!(view.Track is RelayTrack relay)) return null;
            int total = relay.Race.Map.Segments;
            return Loc.F("Étape {0} / {1}", Mathf.Min(relay.Race.Segment + 1, total), total) + " · ";
        }

        void Show(DuelRecord duel, bool spectator, string topTag, string bottomTag, string verdict)
        {
            _duel = duel;
            _relay = null;
            _limitMs = PvpConfig.TimeLimitMs;
            _title.text = Loc.T("Revoir le duel");
            _spectator = spectator;
            _verdict = verdict;
            _me.Tag.text = Loc.T(topTag);
            _rival.Tag.text = Loc.T(bottomTag);
            Fill(_me, duel.Me, spectator ? null : Loc.T("Toi"));
            Fill(_rival, duel.Rival, null);
            _waiting.text = duel.Rival == null ? Loc.T("Personne n'a encore couru contre ton fantôme : le duel se complétera quand un adversaire l'aura affronté.") : "";
            _report.gameObject.SetActive(duel.Rival != null && !spectator);
            _report.interactable = !duel.Reported;
            _status.text = Loc.T("Chargement…");
            SetPlaying(false);
            Load(duel);
        }

        static void Fill(Side side, DuelRun run, string fallback)
        {
            side.Run = run;
            side.Name.text = run == null ? "—" : (!string.IsNullOrEmpty(run.PlayerName) ? run.PlayerName : fallback ?? Loc.T("Momie anonyme"))
                           + (Titles.Get(run.Look?.Title) != null ? " " + TitleBook.Line(run.Look.Title) : "");
            side.State.text = "";
            side.Count.text = "";
            side.Shown = -1;
            foreach (var s in side.Slots) s.color = Color.clear;
        }

        async void Load(DuelRecord duel)
        {
            int load = ++_load;
            if (duel.GeneratorVersion != 0 && duel.GeneratorVersion != DifficultyTable.GeneratorVersion)
            {
                _status.text = Loc.T("Duel joué sur une ancienne version du jeu : il ne peut plus être rejoué.");
                return;
            }
            Level level;
            try { level = await Task.Run(() => PvpServer.Arena(duel.Seed)); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Pvp] Replay tomb not generated: " + e.Message);
                _status.text = Loc.T("Ce duel ne peut pas être rejoué.");
                return;
            }
            if (load != _load || this == null || !isActiveAndEnabled) return;

            var theme = TombTheme.ForAct(level.Id.Act);
            App.Art.SetTheme(theme);
            App.Lighting.SetTheme(theme);
            App.Lighting.SetMood(true);
            if (_rig == null) _rig = ReplayRig.Create(App.transform, App);
            _rig.Open(duel, level, theme, _spectator);
            _rig.Speed = 1f;
            _speed.GetComponentInChildren<Text>().text = "x1"; // noloc
            _status.text = _verdict ?? Verdict(duel);
            SetPlaying(true);
        }

        static string Verdict(DuelRecord d)
        {
            if (!d.Resolved) return Loc.T("Duel en attente d'un adversaire");
            string result = Loc.T(d.Result == DuelResult.Win ? "Victoire" : d.Result == DuelResult.Draw ? "Match nul" : "Défaite");
            int delta = d.EloAfter - d.EloBefore;
            return result + " · Elo " + d.EloBefore + " → " + d.EloAfter + " (" + (delta > 0 ? "+" : "") + delta + ")"; // noloc
        }

        public override void OnShow()
        {
            // The game's own camera keeps clearing the screen (header, safe area) but draws nothing.
            if (App.Camera.Cam.cullingMask != 0) _savedMask = App.Camera.Cam.cullingMask;
            App.Camera.Cam.cullingMask = 0;
        }

        public override void OnHide()
        {
            _load++;
            if (_savedMask != 0) App.Camera.Cam.cullingMask = _savedMask;
            if (_rig != null) _rig.Close();
            _topView.enabled = _bottomView.enabled = false;
            App.Lighting.SetMood(false);
        }

        void TogglePlay()
        {
            if (_rig == null || !_rig.gameObject.activeSelf) return;
            if (!_rig.Playing && _rig.TimeMs >= _rig.EndMs) _rig.Seek(0); // from the start again
            SetPlaying(!_rig.Playing);
        }

        void SetPlaying(bool on)
        {
            if (_rig != null) _rig.Playing = on && _rig.gameObject.activeSelf;
            if (_playIcon != null) _playIcon.sprite = on ? UISprites.Pause : UISprites.Play;
        }

        void ToggleSpeed()
        {
            if (_rig == null) return;
            _rig.Speed = _rig.Speed > 1.5f ? 1f : 2f;
            _speed.GetComponentInChildren<Text>().text = _rig.Speed > 1.5f ? "x2" : "x1"; // noloc
        }

        string Clock(float v)
        {
            int end = _rig != null && _rig.gameObject.activeSelf ? _rig.EndMs : 0;
            return LevelResult.FormatTime((int)(v * end)) + " / " + LevelResult.FormatTime(end); // noloc
        }

        void Update()
        {
            if (_rig == null || !_rig.gameObject.activeSelf || _duel == null && _relay == null) return;

            if (_pendingSeek >= 0f && Time.unscaledTime - _lastSeek >= SeekEvery)
            {
                _rig.Seek((int)(_pendingSeek * _rig.EndMs));
                _pendingSeek = -1f;
                _lastSeek = Time.unscaledTime;
            }
            if (_playIcon != null) _playIcon.sprite = _rig.Playing ? UISprites.Pause : UISprites.Play;
            float t = _rig.EndMs > 0 ? _rig.TimeMs / (float)_rig.EndMs : 0f;
            _timeline.SetValueWithoutNotify(t);
            var valueText = _timeline.transform.parent.GetComponentsInChildren<Text>();
            if (valueText.Length > 1) valueText[1].text = Clock(t);

            UpdateSide(_me, _rig.Top);
            UpdateSide(_rival, _rig.Bottom);
        }

        void LateUpdate()
        {
            if (_rig == null || !_rig.gameObject.activeSelf) return;
            Draw(_rig.Top, _topView, _topArea, true);
            Draw(_rig.Bottom, _bottomView, _bottomArea, _relay != null || _duel?.Rival != null);
        }

        /// <summary>The picture of one replay view, under that side's overlays.</summary>
        static RawImage View(RectTransform area)
        {
            var go = UIKit.Rect("View", area); // noloc
            go.SetAsFirstSibling();
            UIKit.Stretch(go);
            var raw = go.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            return raw;
        }

        /// <summary>Renders a view at the pixel size of its area.</summary>
        static void Draw(ReplayView view, RawImage image, RectTransform area, bool on)
        {
            var r = ScreenRect(area);
            var texture = on ? view.Render(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height)) : view.Render(0, 0);
            image.texture = texture;
            image.enabled = texture != null;
        }

        static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>A UI rect in screen pixels (the canvas is screen-space overlay).</summary>
        static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return new Rect(Corners[0].x, Corners[0].y, Corners[2].x - Corners[0].x, Corners[2].y - Corners[0].y);
        }

        void UpdateSide(Side side, ReplayView view)
        {
            var run = side.Run;
            if (run == null || view.Session == null) return;
            int played = view.Played;
            if (played != side.Shown)
            {
                side.Shown = played;
                side.ChangedAt = Time.unscaledTime;
                for (int i = 0; i < StripSlots; i++)
                {
                    int index = played - StripSlots + i;
                    var img = side.Slots[i];
                    if (index < 0 || index >= run.Inputs.Count) { img.color = Color.clear; continue; }
                    int code = run.Inputs[index].Direction;
                    bool disarm = code > 4;
                    int dir = (code - 1) % 4; // 0 up, 1 right, 2 down, 3 left (screen swipe)
                    img.rectTransform.localRotation = Quaternion.Euler(0, 0, -90f * dir);
                    float age = (StripSlots - 1 - i) / (float)StripSlots;
                    var c = disarm ? UIKit.Danger : i == StripSlots - 1 ? UIKit.Gold : UIKit.Sand;
                    img.color = new Color(c.r, c.g, c.b, i == StripSlots - 1 ? 1f : 0.85f - age * 0.6f);
                }
                side.Count.text = Loc.F("{0} coups", played);
            }
            // The latest swipe pops in.
            var last = side.Slots[StripSlots - 1];
            float k = Mathf.Clamp01((Time.unscaledTime - side.ChangedAt) / 0.18f);
            last.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.45f, 1.1f, k);

            var s = view.Session;
            side.State.text = s.Status == SessionStatus.Won ? "<color=#40E0D0>" + Loc.F("SORTI · {0}", LevelResult.FormatTime(run.TimeMs)) + "</color>" // noloc
                : s.Status == SessionStatus.Dead ? "<color=#D65440>" + Loc.T("MORT") + "</color>" // noloc
                : _rig.TimeMs >= _limitMs ? Loc.T("TEMPS ÉCOULÉ")
                : run.Outcome == RunOutcome.Abandoned && played >= run.Inputs.Count && _rig.TimeMs > EndOfInputs(run) ? Loc.T("ABANDON")
                : (Steps(view) ?? "") + LevelResult.FormatTime(Mathf.Min(_rig.TimeMs, _limitMs));
        }

        static int EndOfInputs(DuelRun run) => run.Inputs.Count == 0 ? 0 : RunActions.MsOf(run.Inputs[run.Inputs.Count - 1].Tick);

        void Report()
        {
            var duel = _duel;
            if (duel?.Rival == null || duel.Reported || App.Pvp == null) return;
            string name = string.IsNullOrEmpty(duel.Rival.PlayerName) ? Loc.T("Momie anonyme") : duel.Rival.PlayerName;
            Router.Open<ConfirmDialog>().Configure(Loc.F("Signaler {0} pour triche ?", name),
                "Le duel entier, vos deux courses, sera transmis à l'équipe qui l'examinera. Aucune sanction n'est automatique.",
                "Signaler", async () =>
                {
                    var r = await App.Pvp.ReportCheatAsync(duel.MatchId);
                    if (r == null || r.Error == PvpServiceFactory.NetworkError) return Loc.T("Envoi impossible : vérifie ta connexion.");
                    if (r.Error != null && r.Error != "ALREADY_REPORTED") return ReportError(r.Error); // noloc
                    duel.Reported = true;
                    ReplayStore.MarkReported(duel.MatchId);
                    if (this != null && _duel == duel)
                    {
                        _report.interactable = false;
                        _status.text = Loc.T("Merci : le duel sera examiné.");
                    }
                    return null;
                });
        }

        public static string ReportError(string code)
        {
            switch (code)
            {
                case "LIMIT": return Loc.F("Tu as déjà envoyé {0} signalements aujourd'hui.", PvpConfig.MaxReportsPerDay); // noloc
                case "NO_RIVAL": return Loc.T("Personne à signaler sur ce duel."); // noloc
                default: return Loc.T("Ce duel n'est plus sur le serveur."); // noloc
            }
        }
    }
}
