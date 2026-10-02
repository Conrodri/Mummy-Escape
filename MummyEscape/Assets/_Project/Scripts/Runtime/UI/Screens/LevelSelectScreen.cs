using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    public sealed class LevelSelectScreen : UIScreen
    {
        int _act = 1;
        Text _actTitle;
        Text _actInfo;
        RectTransform _grid;

        protected override void Build()
        {
            Header("Choisis ta crypte");
            var body = Body(200, 60);
            UIKit.Column(body, 30);

            var pager = UIKit.Row(body, 130);
            UIKit.Size(UIKit.Button(pager.transform, "◄", () => SetAct(_act - 1), 56), -1, 130, 0);
            _actTitle = UIKit.Label(pager.transform, "", 50, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_actTitle, -1, -1, 1);
            UIKit.Size(UIKit.Button(pager.transform, "►", () => SetAct(_act + 1), 56), -1, 130, 0);

            _actInfo = UIKit.Label(body, "", 34, UIKit.Sand);
            UIKit.Size(_actInfo, 110);

            _grid = UIKit.Rect("Grid", body);
            UIKit.Size(_grid, 900);
            var grid = _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280, 260);
            grid.spacing = new Vector2(36, 36);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            var furthest = App.Save.FurthestUnlocked();
            SetAct(furthest.Act);
        }

        void SetAct(int act)
        {
            _act = Mathf.Clamp(act, 1, DifficultyTable.ActCount);
            var def = DifficultyTable.GetAct(_act);
            _actTitle.text = $"Acte {_act}\n{def.Name}";
            var first = DifficultyTable.Spec(new LevelId(_act, 1));
            int lastFloors = DifficultyTable.Spec(new LevelId(_act, def.Levels)).Floors;
            _actInfo.text = first.Floors == lastFloors
                ? $"{first.Floors} étage{(first.Floors > 1 ? "s" : "")}"
                : $"{first.Floors} à {lastFloors} étages";

            UIKit.ClearChildren(_grid);
            for (int i = 1; i <= def.Levels; i++)
            {
                var id = new LevelId(_act, i);
                bool unlocked = App.Save.IsUnlocked(id);
                var rec = App.Save.GetRecord(id);
                var btn = UIKit.Button(_grid, "", () => Play(id), 40);
                btn.interactable = unlocked;
                var col = UIKit.Rect("Content", btn.transform);
                UIKit.Stretch(col, 10, 20, 10, 20);
                UIKit.Column(col, 6, 0, TextAnchor.MiddleCenter);
                var num = UIKit.Label(col, id.ToString(), 60, unlocked ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Size(num, 90);
                if (!unlocked)
                {
                    var lockImg = UIKit.Image(col, UIKit.Art.Lock, Color.white);
                    UIKit.Size(lockImg, 70);
                }
                else
                {
                    UIKit.Stars(col, rec?.BestStars ?? 0, 56);
                    var best = UIKit.Label(col, rec != null && rec.HasBest ? LevelResult.FormatScore(rec.BestOverPar, rec.BestTimeMs) : rec != null && rec.Completions > 0 ? "Évadé" : "Inexploré", 28, UIKit.Dim);
                    UIKit.Size(best, 40);
                }
            }
        }

        void Play(LevelId id)
        {
            Router.Open<HudScreen>();
            _ = App.Game.StartLevel(id);
        }
    }
}
