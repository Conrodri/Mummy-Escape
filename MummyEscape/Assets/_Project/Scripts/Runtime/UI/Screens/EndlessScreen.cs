using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The endless ladders (<see cref="Endless"/>): what the mode is (three rules, each with its icon) and the energy,
    /// then one card per difficulty: the tomb the player stands at, the tombs cleared on a bar, the time added up, and
    /// its play button. A ladder opens with its mode's campaign; locked ones stay in sight with what opens them.
    /// </summary>
    public sealed class EndlessScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        static readonly Color[] ModeColors = { new Color32(110, 220, 200, 255), new Color32(240, 196, 90, 255), new Color32(240, 100, 80, 255) };

        RectTransform _content;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Sans fin");
            var body = Body(180, 40, 40);
            UIKit.Column(body, 16);
            EnergyBadge.Create(body, Monetization.PlayMode.Endless); // above the list: always in sight
            _content = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _content.GetComponent<VerticalLayoutGroup>().spacing = 22;
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            UIKit.ClearChildren(_content);
            Rules();
            int index = 0;
            foreach (var m in DifficultyExt.All) Ladder(m, index++);
            var ranking = UIKit.Button(_content, "Classement", Ranking, 32, ButtonStyle.Secondary);
            UIKit.Size(ranking, UIKit.ButtonHeight);
        }

        /// <summary>The mode in three lines, under its sign, and the energy left.</summary>
        void Rules()
        {
            var card = UIKit.Plate(_content, Color.white, 30, new Color(1f, 0.85f, 0.4f, 0.45f), false, "Rules"); // noloc
            UIFx.Gradient(card, new Color32(66, 50, 32, 255), new Color32(30, 22, 14, 255));
            UIKit.Size(card, 290);
            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(28, 28, 22, 22);
            row.spacing = 26;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var sign = UIKit.Rect("Sign", card.transform); // noloc
            UIKit.Size(sign, 180, 180);
            UIFx.Halo(sign, new Color(1f, 0.8f, 0.35f, 0.35f), 260);
            var icon = UIKit.Image(sign, UISprites.Infinity, UIKit.Gold, false, "Icon"); // noloc
            UIKit.Stretch(icon.rectTransform, 14, 14, 14, 14);

            var lines = UIKit.Rect("Lines", card.transform); // noloc
            UIKit.Size(lines, -1, 0, 1);
            UIKit.Column(lines, 10, 0, TextAnchor.MiddleLeft);
            Line(lines, UISprites.Steps, Loc.T("Chaque tombeau franchi ouvre le suivant, plus dur."));
            Line(lines, UISprites.Retry, Loc.T("Une chute : le même tombeau, nouvel essai."));
            Line(lines, UISprites.Podium, Loc.T("Classement : les tombeaux, puis le temps."));
        }

        static void Line(Transform parent, Sprite sprite, string text)
        {
            var row = UIKit.Row(parent, 56, 14);
            row.childAlignment = TextAnchor.MiddleLeft;
            var icon = UIKit.Image(row.transform, sprite, UIKit.Sand, false, "Icon"); // noloc
            UIKit.Size(icon, 40, 40, 0);
            var label = UIKit.Label(row.transform, text, 26, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(label, 16);
            UIKit.Size(label, -1, -1, 1);
        }

        /// <summary>One difficulty's ladder: the tomb reached in a badge, the progress, and its button.</summary>
        void Ladder(Difficulty mode, int index)
        {
            var save = App.Save;
            bool open = save.IsEndlessUnlocked(mode);
            var progress = save.EndlessOf(mode);
            int count = MummyEscape.Game.GameController.EndlessTombCount(mode);
            bool done = progress.Cleared >= count;
            var accent = ModeColors[(int)mode];
            var faded = open ? accent : Color.Lerp(accent, new Color(0.4f, 0.37f, 0.34f), 0.7f);

            var card = UIKit.Plate(_content, Color.white, 30, new Color(faded.r, faded.g, faded.b, 0.6f), false, mode.ToString());
            UIFx.Gradient(card, Color.Lerp(faded, Color.black, 0.72f), Color.Lerp(faded, Color.black, 0.88f));
            UIKit.Size(card, 250);
            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(22, 24, 20, 20);
            row.spacing = 22;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            // The tomb the player stands at, big, in the mode's colour (a padlock while locked).
            var badge = UIKit.Plate(card.transform, new Color(0, 0, 0, 0.35f), 26, faded, false, "Badge"); // noloc
            UIKit.Size(badge, 190, 170, 0);
            if (open)
            {
                UIKit.Column(badge.transform, 0, 8, TextAnchor.MiddleCenter);
                var number = UIKit.Title(badge.transform, (done ? progress.Cleared : progress.Reached).ToString(), 76, accent);
                UIKit.FitText(number, 30);
                UIKit.Size(number, 96);
                var caption = UIKit.Label(badge.transform, done ? Loc.T("tous franchis") : Loc.T("tombeau"), 24, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Size(caption, 34);
            }
            else
            {
                var padlock = UIKit.Image(badge.transform, UIKit.Art.Lock, Color.white, false, "Lock"); // noloc
                padlock.preserveAspect = true;
                UIKit.Place(padlock.rectTransform, 0.5f, 0.5f, 90, 90);
            }

            var info = UIKit.Rect("Info", card.transform); // noloc
            UIKit.Size(info, -1, 0, 1);
            UIKit.Column(info, 8, 0, TextAnchor.MiddleLeft);
            var name = UIKit.Label(info, LevelNames.Mode(mode), 40, faded, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 24);
            UIKit.Size(name, 52);
            if (open)
            {
                var bar = UIFx.Bar(info, 22, accent);
                bar.fillAmount = count > 0 ? progress.Cleared / (float)count : 0f;
                var cleared = UIKit.Label(info, Loc.F("{0} / {1} tombeaux franchis", progress.Cleared, count), 24, UIKit.Sand, TextAnchor.MiddleLeft);
                UIKit.FitText(cleared, 16);
                UIKit.Size(cleared, 34);
                var time = UIKit.Label(info, progress.Cleared > 0 ? Loc.F("Temps cumulé : {0}", LevelResult.FormatTime(progress.TimeMs)) : Loc.T("Aucun tombeau franchi"), 22, UIKit.Dim, TextAnchor.MiddleLeft);
                UIKit.FitText(time, 15);
                UIKit.Size(time, 30);
            }
            else
            {
                var unlock = UIKit.Label(info, Loc.F("Finis {0} pour l'ouvrir.", LevelNames.Of(Progression.Last(mode))), 26, UIKit.Dim, TextAnchor.MiddleLeft);
                UIKit.FitText(unlock, 16);
                UIKit.Size(unlock, 70);
            }

            if (open)
            {
                var play = UIKit.Button(card.transform, done ? "Terminé" : "Jouer", () => Play(mode), 32, ButtonStyle.Primary);
                play.interactable = !done;
                UIKit.FitText(play.GetComponentInChildren<Text>(), 18);
                UIKit.Size(play, 120, 180, 0);
            }
            UIFx.PopIn(card, index * 0.06f, 0.94f);
        }

        void Play(Difficulty mode) =>
            PlayGate.EndlessRun(App, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartEndless(mode);
            });

        void Ranking() => App.RequireOnline(() =>
        {
            var mode = Difficulty.Easy;
            foreach (var m in DifficultyExt.All) if (App.Save.IsEndlessUnlocked(m)) mode = m;
            Router.Get<LeaderboardScreen>().FocusEndless(mode); // before it opens: it loads the board it shows
            Router.Open<LeaderboardScreen>();
        });
    }
}
