using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The solo map: one act at a time (chevrons, dots or a swipe), its banner in the act's colours with the stars won
    /// in it, then its tombs as cards (the next one to play glows), and a big button that
    /// resumes at the furthest tomb. The daily games left sit under it.
    /// </summary>
    public sealed class LevelSelectScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        int _act = 1;
        Image _glow, _banner, _bannerRim, _bar;
        Text _actNumber, _actTitle, _actInfo, _actStars;
        Button _prev, _next, _continue;
        RectTransform _grid, _dots;
        Text _plays;

        protected override void Build()
        {
            var back = UIKit.Backdrop(Root);
            // The act's colour lights the top of the screen.
            _glow = UIKit.Image(back.transform, UISprites.RadialGlow, Color.clear, false, "ActGlow"); // noloc
            _glow.preserveAspect = false;
            UIKit.Place(_glow.rectTransform, 0.5f, 1f, 2200, 1600, 0, -250);

            var title = Header("Solo");
            // The music of the tombs, with the solo game it belongs to.
            var jukebox = UIKit.IconButton(title.transform.parent, UISprites.Note, () => Router.Open<JukeboxScreen>(), 92);
            UIKit.Place((RectTransform)jukebox.transform, 1, 0.5f, 92, 92, -36, 0);
            var body = Body(190, 40);
            UIKit.Column(body, 22);
            // A horizontal swipe anywhere on the page changes the act.
            var swipeArea = body.gameObject.AddComponent<Image>();
            swipeArea.color = Color.clear;
            body.gameObject.AddComponent<SwipePager>().Swiped = dir => SetAct(_act + dir, true);

            BuildBanner(body);

            _dots = UIKit.Row(body, 24, 14).GetComponent<RectTransform>();
            _dots.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            for (int i = 1; i <= DifficultyTable.ActCount; i++)
            {
                var dot = UIKit.Image(_dots, UISprites.Circle, UIKit.Dim, false, "Dot"); // noloc
                UIKit.Size(dot, 20, 20);
            }

            _grid = (RectTransform)UIKit.FittedGrid(body, 3, new Vector2(290, 250), new Vector2(26, 26), out var slot).transform;
            UIKit.Size(slot, -1, -1, -1, 1);

            var row = UIKit.Rect("Continue", body); // noloc
            UIKit.Size(row, 120);
            _continue = UIKit.Button(row, "JOUER", ContinueRun, 42, ButtonStyle.Primary); // a label now: an empty one has no Text
            UIKit.Place((RectTransform)_continue.transform, 0.5f, 0.5f, 700, 120);
            UIKit.FitText(_continue.GetComponentInChildren<Text>(), 24);
            _plays = UIKit.Label(body, "", 26, UIKit.Dim);
            UIKit.Size(_plays, 36);
        }

        /// <summary>The act banner: chevrons around the act's name, its floors and its star bar.</summary>
        void BuildBanner(Transform body)
        {
            _banner = UIKit.Plate(body, Color.white, 36, null, false, "Act"); // noloc
            _bannerRim = UIKit.AddRim(_banner, UIKit.Rim, 36);
            UIKit.DropShadow(_banner, 10, 0.5f);
            UIKit.Size(_banner, 330);
            var inner = UIKit.Stretch(UIKit.Rect("Inner", _banner.transform)); // noloc
            UIFx.Shine(inner, 6f, 0.07f);

            _prev = UIKit.IconButton(inner, UISprites.Back, () => SetAct(_act - 1, true), 84);
            UIKit.Place((RectTransform)_prev.transform, 0f, 0.5f, 84, 84, 66, 20);
            _next = UIKit.IconButton(inner, UISprites.Next, () => SetAct(_act + 1, true), 84);
            UIKit.Place((RectTransform)_next.transform, 1f, 0.5f, 84, 84, -66, 20);

            var col = UIKit.Rect("Texts", inner); // noloc
            UIKit.Stretch(col, 160, 26, 160, 24);
            UIKit.Column(col, 4, 0, TextAnchor.UpperCenter);
            _actNumber = UIKit.Label(col, "", 28, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_actNumber, 40);
            _actTitle = UIKit.Title(col, "", 54, UIKit.Sand);
            UIKit.FitText(_actTitle, 30);
            UIKit.Size(_actTitle, 80);
            _actInfo = UIKit.Label(col, "", 28, UIKit.Sand);
            UIKit.FitText(_actInfo, 18);
            UIKit.Size(_actInfo, 40);
            UIKit.Size(UIKit.Rect("Gap", col), 10); // noloc
            var stars = UIKit.Row(col, 52, 12);
            _bar = UIFx.Bar(stars.transform, 26, UIKit.Gold);
            UIKit.Size(_bar.transform.parent, 26, -1, 1);
            _actStars = UIKit.Chip(stars.transform, UIKit.Art.Star, "", null, 52);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            var furthest = App.Save.FurthestUnlocked();
            SetAct(furthest.Act, false);
            _plays.text = PlayGate.Status(App, Monetization.PlayMode.Solo);
            UIKit.SetLabel(_continue, Loc.F("JOUER · {0}", furthest.ToString()));
            App.Audio.PrefetchMusic(furthest.Act); // its theme is ready when the level starts
        }

        void SetAct(int act, bool animate)
        {
            act = Mathf.Clamp(act, 1, DifficultyTable.ActCount);
            if (animate && act == _act) return;
            _act = act;
            var def = DifficultyTable.GetAct(_act);
            var theme = TombTheme.ForAct(_act);
            var accent = Color.Lerp(theme.Accent, UIKit.Sand, 0.2f);

            _glow.color = new Color(accent.r, accent.g, accent.b, 0.22f);
            UIFx.Gradient(_banner, Color.Lerp(theme.Wall, Color.black, 0.15f), Color.Lerp(theme.FloorDark, Color.black, 0.6f));
            _bannerRim.color = new Color(accent.r, accent.g, accent.b, 0.6f);
            _actNumber.text = Loc.F("Acte {0}", _act).ToUpperInvariant();
            _actNumber.color = accent;
            _actTitle.text = Loc.T(def.Name);
            _prev.gameObject.SetActive(_act > 1);
            _next.gameObject.SetActive(_act < DifficultyTable.ActCount);
            var first = DifficultyTable.Spec(new LevelId(_act, 1));
            int lastFloors = DifficultyTable.Spec(new LevelId(_act, def.Levels)).Floors;
            string floors = first.Floors == lastFloors
                ? Loc.P(first.Floors, "{0} étage", "{0} étages")
                : Loc.F("{0} à {1} étages", first.Floors, lastFloors);
            _actInfo.text = floors + "  ·  " + Loc.P(def.Levels, "{0} crypte", "{0} cryptes");
            for (int i = 0; i < _dots.childCount; i++)
            {
                var dot = _dots.GetChild(i).GetComponent<Image>();
                bool on = i + 1 == _act;
                dot.color = on ? accent : new Color(1f, 1f, 1f, 0.18f);
                dot.rectTransform.localScale = Vector3.one * (on ? 1.3f : 1f);
            }

            var furthest = App.Save.FurthestUnlocked();
            int stars = 0;
            UIKit.ClearChildren(_grid);
            for (int i = 1; i <= def.Levels; i++)
            {
                var id = new LevelId(_act, i);
                var rec = App.Save.GetRecord(id);
                stars += rec?.BestStars ?? 0;
                LevelCard(id, rec, id.Equals(furthest) && (rec == null || rec.Completions == 0), theme, accent, i - 1);
            }
            _actStars.text = $"{stars} / {def.Levels * 3}";
            _bar.fillAmount = stars / (float)(def.Levels * 3);
        }

        void LevelCard(LevelId id, LevelRecord rec, bool current, TombTheme theme, Color accent, int index)
        {
            bool unlocked = App.Save.IsUnlocked(id);
            bool done = rec != null && rec.Completions > 0;
            // The card in a holder: the grid places the holder, the pulse and the pop-in scale the card.
            var holder = UIKit.Rect("Tomb " + id, _grid); // noloc
            var card = UIKit.Plate(holder, Color.white, 30, current ? UIKit.Gold : done ? new Color(accent.r, accent.g, accent.b, 0.55f) : UIKit.Rim, current, "Card"); // noloc
            UIKit.Stretch(card.rectTransform);
            if (current) UIFx.Gradient(card, new Color32(250, 214, 110, 255), new Color32(200, 140, 40, 255));
            else if (unlocked) UIFx.Gradient(card, Color.Lerp(theme.Wall, Color.black, done ? 0.2f : 0.45f), Color.Lerp(theme.FloorDark, Color.black, 0.65f));
            else card.color = new Color(0, 0, 0, 0.4f);
            card.raycastTarget = unlocked;
            if (unlocked)
            {
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                btn.onClick.AddListener(() => { App.Audio.Play(Services.Sfx.Click, 0f); Play(id); });
                card.gameObject.AddComponent<PressScale>();
            }
            if (current)
            {
                UIFx.Halo(holder, new Color(1f, 0.75f, 0.3f, 0.5f), 420);
                UIFx.Pulse(card, 0.03f, 1.5f);
            }
            UIFx.PopIn(holder, index * 0.03f);

            var col = UIKit.Rect("Content", card.transform); // noloc
            UIKit.Stretch(col, 10, 16, 10, 16);
            UIKit.Column(col, 4, 0, TextAnchor.MiddleCenter);
            var ink = current ? UIKit.Ink : unlocked ? UIKit.Sand : new Color(1f, 0.9f, 0.75f, 0.3f);
            var num = UIKit.Label(col, id.ToString(), 60, ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (current) num.GetComponent<Shadow>().enabled = false;
            UIKit.Size(num, 82);
            if (!unlocked)
            {
                var lockImg = UIKit.Image(col, UIKit.Art.Lock, new Color(1, 1, 1, 0.45f));
                UIKit.Size(lockImg, 56);
                return;
            }
            if (current)
            {
                var play = UIKit.Plate(col, UIKit.Ink, 22, null, false, "Play"); // noloc
                UIKit.Size(play, 50, 180, 0);
                var t = UIKit.Label(play.transform, "À toi !", 26, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Stretch(t.rectTransform);
                var hint = UIKit.Label(col, "Inexploré", 22, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                hint.GetComponent<Shadow>().enabled = false;
                UIKit.Size(hint, 32);
                return;
            }
            UIKit.Stars(col, rec?.BestStars ?? 0, 48);
            string sub = rec != null && rec.HasBest ? LevelResult.FormatScore(rec.BestOverPar, rec.BestTimeMs)
                       : done ? Loc.T("Évadé") : Loc.T("Inexploré");
            var best = UIKit.Label(col, "", 24, done ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            best.text = sub;
            UIKit.Size(best, 34);
        }

        void ContinueRun() => Play(App.Save.FurthestUnlocked());

        void Play(LevelId id)
        {
            PlayGate.Solo(App, id, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartLevel(id);
            });
        }


        /// <summary>Turns a horizontal drag over the page into "next act" / "previous act".</summary>
        sealed class SwipePager : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public System.Action<int> Swiped;
            Vector2 _start;

            public void OnBeginDrag(PointerEventData e) => _start = e.position;
            public void OnDrag(PointerEventData e) { }

            public void OnEndDrag(PointerEventData e)
            {
                var d = e.position - _start;
                if (Mathf.Abs(d.x) > Screen.width * 0.12f && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) Swiped?.Invoke(d.x < 0 ? 1 : -1);
            }
        }
    }
}
