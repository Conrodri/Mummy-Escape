using MummyEscape.Core;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>End of level: "Tu as fini en X coups, X interactions, il te reste X PV" + stars, record, share.</summary>
    public sealed class RecapScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        Text _title;
        RectTransform _starsSlot;
        Text _lines;
        Text _extra;
        Button _next;
        LevelResult _result;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform);

            var panel = UIKit.Panel(Root);
            var rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900, 1400);
            UIKit.Column(panel.transform, 22, 56);

            _title = UIKit.Label(panel.transform, "", 78, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_title, 130);
            _starsSlot = UIKit.Rect("StarsSlot", panel.transform);
            UIKit.Size(_starsSlot, 130);
            _lines = UIKit.Label(panel.transform, "", 44, UIKit.Sand);
            UIKit.Size(_lines, 260);
            _extra = UIKit.Label(panel.transform, "", 38, UIKit.Turquoise, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_extra, 110);

            UIKit.Size(UIKit.Button(panel.transform, "Partager", Share), 120);
            _next = UIKit.Button(panel.transform, "Niveau suivant", Next);
            UIKit.Size(_next, 120);
            var row = UIKit.Row(panel.transform, 120);
            UIKit.Size(UIKit.Button(row.transform, "Rejouer", Retry, 40), -1, -1, 1);
            UIKit.Size(UIKit.Button(row.transform, "Classement", Leaderboard, 40), -1, -1, 1);
            UIKit.Size(UIKit.Button(panel.transform, "Menu", Menu), 110);
        }

        public void Show(LevelResult result, RecordOutcome outcome)
        {
            _result = result;
            _title.text = result.Won ? "ÉVADÉ !" : "LA MOMIE A PÉRI";
            _title.color = result.Won ? UIKit.Gold : UIKit.Danger;

            UIKit.ClearChildren(_starsSlot);
            var stars = UIKit.Stars(_starsSlot, result.Stars, 110);
            UIKit.Stretch(stars);

            string over = result.OverPar == 0
                ? "<color=#E8C35A><b>Chemin parfait !</b></color>"
                : $"<b>+{result.OverPar}</b> coup{(result.OverPar > 1 ? "s" : "")} de plus que le chemin idéal";
            _lines.text = result.Won
                ? $"Évadé en <b>{result.Moves}</b> coups · <b>{LevelResult.FormatTime(result.TimeMs)}</b>\n{over}\n" +
                  $"<b>{result.Interactions}</b> interaction{(result.Interactions > 1 ? "s" : "")} · " +
                  $"<b>{result.HpLeft}</b>/{result.MaxHp} PV"
                : $"Les pièges ont eu raison de toi après {result.Moves} coups.\n{result.Interactions} interaction{(result.Interactions > 1 ? "s" : "")}\nRéessaie : un nouveau tombeau t'attend.";

            string extra = "";
            if (outcome.NewBest && outcome.PreviousBestOverPar >= 0)
                extra += $"Nouveau record ! ({LevelResult.FormatScore(outcome.PreviousBestOverPar, outcome.PreviousBestTimeMs)} → {LevelResult.FormatScore(result.OverPar, result.TimeMs)})\n";
            else if (outcome.NewBest) extra += "Premier passage !\n";
            if (outcome.CoinsEarned > 0) extra += $"+{outcome.CoinsEarned} scarabées";
            _extra.text = extra;

            var next = Progression.Next(result.Level);
            _next.gameObject.SetActive(result.Won && next.HasValue);
            // Get the next tombs generating while the player reads the recap.
            App.Game.Prefetch(result.Level);
            if (result.Won && next.HasValue) App.Game.Prefetch(next.Value);
        }

        public void OnBack() => Menu();

        void Share() => App.Share.ShareText(_result.ShareText(ShareService.GameUrl));

        void Next()
        {
            var next = Progression.Next(_result.Level);
            if (!next.HasValue) return;
            Router.Close(this);
            _ = App.Game.StartLevel(next.Value);
        }

        void Retry()
        {
            Router.Close(this);
            App.Game.Restart();
        }

        void Leaderboard()
        {
            var lb = Router.Get<LeaderboardScreen>();
            lb.Focus(_result.Level);
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            Router.Open<LeaderboardScreen>();
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
