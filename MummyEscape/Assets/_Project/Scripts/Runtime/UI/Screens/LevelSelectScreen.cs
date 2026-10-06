using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class LevelSelectScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        int _act = 1;
        Text _actNumber, _actTitle, _actInfo, _actStars;
        Button _prev, _next;
        RectTransform _grid;
        Text _plays;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            var title = Header("Choisis ta crypte");
            // The music of the tombs, with the solo game it belongs to.
            var jukebox = UIKit.IconButton(title.transform.parent, UISprites.Note, () => Router.Open<JukeboxScreen>(), 92);
            UIKit.Place((RectTransform)jukebox.transform, 1, 0.5f, 92, 92, -36, 0);
            var body = Body(190, 60);
            UIKit.Column(body, 28);

            // Act pager: chevrons around the act name, its floors and the stars won in it.
            var pager = UIKit.Row(body, 190, 16);
            pager.childForceExpandHeight = false;
            _prev = UIKit.IconButton(pager.transform, UISprites.Back, () => SetAct(_act - 1), 84);
            var info = UIKit.Rect("Act", pager.transform);
            UIKit.Size(info, -1, -1, 1);
            UIKit.Column(info, 2, 0, TextAnchor.MiddleCenter);
            _actNumber = UIKit.Label(info, "", 26, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_actNumber, 38);
            _actTitle = UIKit.Title(info, "", 46, UIKit.Sand);
            UIKit.FitText(_actTitle, 28);
            UIKit.Size(_actTitle, 70);
            var meta = UIKit.Row(info, 50, 14);
            _actInfo = UIKit.Label(meta.transform, "", 28, UIKit.Dim);
            UIKit.Size(_actInfo, -1, -1, 0);
            _actInfo.horizontalOverflow = HorizontalWrapMode.Overflow;
            _actStars = UIKit.Chip(meta.transform, UIKit.Art.Star, "", null, 48);
            _next = UIKit.IconButton(pager.transform, UISprites.Next, () => SetAct(_act + 1), 84);

            _grid = (RectTransform)UIKit.FittedGrid(body, 3, new Vector2(290, 236), new Vector2(30, 30), out var slot).transform;
            UIKit.Size(slot, -1, -1, -1, 1);
            _plays = UIKit.Label(body, "", 26, UIKit.Dim);
            UIKit.Size(_plays, 36);
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            App.Audio.PlayMusic(0);
            var furthest = App.Save.FurthestUnlocked();
            SetAct(furthest.Act);
            _plays.text = PlayGate.Status(App, Monetization.PlayMode.Solo);
            App.Audio.PrefetchMusic(furthest.Act); // its theme is ready when the level starts
        }

        void SetAct(int act)
        {
            _act = Mathf.Clamp(act, 1, DifficultyTable.ActCount);
            var def = DifficultyTable.GetAct(_act);
            _actNumber.text = Loc.F("Acte {0}", _act).ToUpperInvariant();
            _actTitle.text = Loc.T(def.Name);
            _prev.interactable = _act > 1;
            _next.interactable = _act < DifficultyTable.ActCount;
            var first = DifficultyTable.Spec(new LevelId(_act, 1));
            int lastFloors = DifficultyTable.Spec(new LevelId(_act, def.Levels)).Floors;
            _actInfo.text = first.Floors == lastFloors
                ? Loc.P(first.Floors, "{0} étage", "{0} étages")
                : Loc.F("{0} à {1} étages", first.Floors, lastFloors);

            var furthest = App.Save.FurthestUnlocked();
            int stars = 0;
            UIKit.ClearChildren(_grid);
            for (int i = 1; i <= def.Levels; i++)
            {
                var id = new LevelId(_act, i);
                bool unlocked = App.Save.IsUnlocked(id);
                var rec = App.Save.GetRecord(id);
                stars += rec?.BestStars ?? 0;
                bool current = id.Equals(furthest) && (rec == null || rec.Completions == 0);
                var btn = UIKit.Button(_grid, "", () => Play(id), UIKit.TextSize, current ? ButtonStyle.Primary : ButtonStyle.Secondary);
                btn.interactable = unlocked;
                if (!unlocked)
                {
                    btn.image.color = new Color(0, 0, 0, 0.35f);
                    var rim = btn.transform.Find("Rim")?.GetComponent<Image>();
                    if (rim != null) rim.color = new Color(1f, 0.9f, 0.7f, 0.08f);
                }
                var col = UIKit.Rect("Content", btn.transform);
                UIKit.Stretch(col, 10, 18, 10, 18);
                UIKit.Column(col, 4, 0, TextAnchor.MiddleCenter);
                var ink = current ? UIKit.Ink : unlocked ? UIKit.Sand : new Color(1f, 0.9f, 0.75f, 0.3f);
                var num = UIKit.Label(col, id.ToString(), 58, ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                if (current) num.GetComponent<Shadow>().enabled = false;
                UIKit.Size(num, 80);
                if (!unlocked)
                {
                    var lockImg = UIKit.Image(col, UIKit.Art.Lock, new Color(1, 1, 1, 0.45f));
                    UIKit.Size(lockImg, 52);
                }
                else
                {
                    UIKit.Stars(col, rec?.BestStars ?? 0, 46);
                    string sub = rec != null && rec.HasBest ? LevelResult.FormatScore(rec.BestOverPar, rec.BestTimeMs)
                               : rec != null && rec.Completions > 0 ? Loc.T("Évadé")
                               : current ? Loc.T("À toi de jouer") : Loc.T("Inexploré");
                    var best = UIKit.Label(col, "", 24, current ? UIKit.Ink : UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
                    best.text = sub;
                    if (current) best.GetComponent<Shadow>().enabled = false;
                    UIKit.Size(best, 34);
                }
            }
            _actStars.text = $"{stars} / {def.Levels * 3}";
        }

        void Play(LevelId id)
        {
            PlayGate.Play(App, Monetization.PlayMode.Solo, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartLevel(id);
            });
        }
    }
}
