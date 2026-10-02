using System.Collections.Generic;
using MummyEscape.Core;
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
        RectTransform _hearts;
        readonly List<Image> _ankhs = new List<Image>();
        RectTransform _bottomBar;
        CanvasGroup _preview;
        Text _previewTitle;
        Text _previewCount;
        CanvasGroup _intro;
        Text _introText;
        float _introTimer;

        protected override void Build()
        {
            var top = UIKit.Rect("TopBar", Root);
            UIKit.TopBand(top, 230, 10);
            var bg = UIKit.Image(top, UIKit.Art.White, new Color(0, 0, 0, 0.45f));
            UIKit.Stretch(bg.rectTransform);

            var pause = UIKit.Button(top, "II", OnBack, 50);
            var prt = (RectTransform)pause.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0, 0.5f);
            prt.pivot = new Vector2(0, 0.5f);
            prt.sizeDelta = new Vector2(120, 120);
            prt.anchoredPosition = new Vector2(25, 10);

            _level = UIKit.Label(top, "", 52, UIKit.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            UIKit.Stretch(_level.rectTransform, 170, 22, 170, 120);
            _moves = UIKit.Label(top, "", 42, UIKit.Sand, TextAnchor.MiddleCenter);
            UIKit.Stretch(_moves.rectTransform, 170, 95, 170, 70);
            _floor = UIKit.Label(top, "", 32, UIKit.Dim, TextAnchor.LowerCenter);
            UIKit.Stretch(_floor.rectTransform, 170, 150, 170, 18);

            _hearts = UIKit.Rect("Hearts", top);
            _hearts.anchorMin = _hearts.anchorMax = new Vector2(1, 0.5f);
            _hearts.pivot = new Vector2(1, 0.5f);
            _hearts.sizeDelta = new Vector2(200, 90);
            _hearts.anchoredPosition = new Vector2(-25, 10);
            var h = _hearts.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight;
            h.spacing = 8;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            _status = UIKit.Label(Root, "", 40, new Color(0.75f, 0.55f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.TopBand(_status.rectTransform, 60, 250);

            // Bottom: map peek (hold) + hint.
            var bottom = UIKit.Rect("BottomBar", Root);
            UIKit.BottomBand(bottom, 170, 20);
            var map = UIKit.Button(bottom, "Carte", null, 40);
            var mrt = (RectTransform)map.transform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(1, 0.5f);
            mrt.pivot = new Vector2(1, 0.5f);
            mrt.sizeDelta = new Vector2(220, 130);
            mrt.anchoredPosition = new Vector2(-30, 0);
            var trigger = map.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerDown, () => App.Game.SetMapView(true));
            AddTrigger(trigger, EventTriggerType.PointerUp, () => App.Game.SetMapView(false));

            _hint = UIKit.Label(bottom, "", 32, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIKit.Stretch(_hint.rectTransform, 40, 0, 280, 0);
            _bottomBar = bottom;

            // Start-of-run map preview: countdown + "ready" to start early. Replaces the bottom bar meanwhile.
            var pv = UIKit.Rect("Preview", Root);
            UIKit.BottomBand(pv, 210, 20);
            var pbg = UIKit.Image(pv, UIKit.Art.White, new Color(0, 0, 0, 0.62f));
            UIKit.Stretch(pbg.rectTransform);
            _previewTitle = UIKit.Label(pv, "", 40, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Stretch(_previewTitle.rectTransform, 40, 0, 420, 0);
            _previewCount = UIKit.Label(pv, "", 110, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            var crt = _previewCount.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1, 0.5f);
            crt.pivot = new Vector2(1, 0.5f);
            crt.sizeDelta = new Vector2(150, 190);
            crt.anchoredPosition = new Vector2(-250, 0);
            var ready = UIKit.Button(pv, "Prêt", () => App.Game.SkipPreview(), 42);
            var rrt = (RectTransform)ready.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1, 0.5f);
            rrt.pivot = new Vector2(1, 0.5f);
            rrt.sizeDelta = new Vector2(200, 130);
            rrt.anchoredPosition = new Vector2(-30, 0);
            _preview = pv.gameObject.AddComponent<CanvasGroup>();
            SetPreviewVisible(false);

            // Level intro card.
            var intro = UIKit.Rect("Intro", Root);
            intro.anchorMin = new Vector2(0, 0.5f);
            intro.anchorMax = new Vector2(1, 0.5f);
            intro.sizeDelta = new Vector2(0, 360);
            var ibg = UIKit.Image(intro, UIKit.Art.White, new Color(0, 0, 0, 0.7f));
            UIKit.Stretch(ibg.rectTransform);
            _introText = UIKit.Label(intro, "", 54, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_introText.rectTransform, 40, 20, 40, 20);
            _intro = intro.gameObject.AddComponent<CanvasGroup>();
            _intro.blocksRaycasts = false;
            _intro.alpha = 0;
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
            App.Game.SetPaused(false);
            Refresh();
        }

        public override void OnHide()
        {
            App.Game.Changed -= Refresh;
            App.Game.PreviewChanged -= Refresh;
            App.Game.LevelLoading -= ShowLoading;
            App.Game.LevelStarted -= ShowIntro;
        }

        public void OnBack()
        {
            if (App.Game.Session == null) { Router.Reset<MainMenuScreen>(); return; }
            Router.Open<PauseScreen>();
        }

        void ShowLoading(LevelId id)
        {
            _introText.text = $"Niveau {id}\n<size=36>Les dieux scellent un nouveau tombeau…</size>";
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
            if (App.Game.Previewing)
                _previewCount.text = Mathf.Max(1, Mathf.CeilToInt(App.Game.PreviewLeft)).ToString();
            if (_introTimer == float.MaxValue || _introTimer < 0f) return;
            _introTimer -= Time.deltaTime;
            if (_introTimer < 0.6f) _intro.alpha = Mathf.Clamp01(_introTimer / 0.6f);
        }

        void Refresh()
        {
            var s = App.Game.Session;
            if (s == null) return;
            var level = s.Level;
            _level.text = $"Niveau {level.Id}";
            _moves.text = $"Coups : {s.Moves}";
            _floor.text = level.Floors > 1 && !App.Game.Previewing ? $"Étage {s.Position.Floor + 1} / {level.Floors}" : "";

            bool preview = App.Game.Previewing;
            SetPreviewVisible(preview);
            if (preview)
            {
                var act = DifficultyTable.GetAct(level.Id.Act);
                string sub = level.Floors > 1 ? $"{level.Floors} étages empilés, le 1er en bas" : $"Acte {level.Id.Act} — {act.Name}";
                _previewTitle.text = $"Mémorise le tombeau !\n<size=30>{sub}</size>";
            }

            while (_ankhs.Count < level.MaxHp)
            {
                var img = UIKit.Image(_hearts, UIKit.Art.Ankh, Color.white);
                UIKit.Size(img, 80, 80);
                _ankhs.Add(img);
            }
            for (int i = 0; i < _ankhs.Count; i++)
            {
                _ankhs[i].gameObject.SetActive(i < level.MaxHp);
                _ankhs[i].sprite = i < s.Hp ? UIKit.Art.Ankh : UIKit.Art.AnkhEmpty;
            }

            _status.color = s.IsBlind ? new Color(0.75f, 0.55f, 1f) : new Color(1f, 0.62f, 0.3f);
            _status.text = preview ? ""
                : s.IsBlind ? $"Aveuglé ! ({s.BlindTurnsLeft})"
                : !s.TorchLit ? "Torche éteinte : longe une torche murale"
                : "";
            _hint.text = preview ? ""
                : s.Moves == 0 ? "Glisse pour avancer d'une case.\nTa torche éclaire les cases voisines."
                : s.Moves < 4 ? "Maintiens « Carte » pour revoir ce que tu as exploré." : "";
        }
    }
}
