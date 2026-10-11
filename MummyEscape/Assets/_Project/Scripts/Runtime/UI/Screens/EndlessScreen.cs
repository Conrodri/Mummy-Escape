using MummyEscape.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The endless ladders (<see cref="Endless"/>), one per mode, each opened by finishing that mode's campaign and played
    /// at the player's tomb. Locked ones stay in sight, greyed, with what opens them. A run costs a point of endless energy.
    /// </summary>
    public sealed class EndlessScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        readonly Button[] _ladders = new Button[3];
        Text _energy;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Sans fin");
            var body = Body(180, 40, 40);
            UIKit.Column(body, 20);

            var intro = UIKit.Label(body, "Des tombeaux sans fin, de plus en plus durs : chaque tombeau franchi ouvre le suivant. Le classement compte les tombeaux, puis le temps.", 28, UIKit.Sand);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 120);
            foreach (var m in DifficultyExt.All)
            {
                var mode = m;
                var b = UIKit.Button(body, "Sans fin", () => Play(mode), 34, ButtonStyle.Primary);
                UIKit.FitText(b.GetComponentInChildren<Text>(), 20);
                UIKit.Size(b, 150);
                _ladders[(int)m] = b;
            }
            _energy = UIKit.Label(body, "", 26, UIKit.Dim);
            UIKit.Size(_energy, 40);
            UIKit.Size(UIKit.Button(body, "Classement", Ranking), UIKit.ButtonHeight);
            UIKit.Size(UIKit.Rect("Spacer", body), -1, -1, -1, 1); // noloc
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            foreach (var m in DifficultyExt.All)
            {
                bool open = App.Save.IsEndlessUnlocked(m);
                var b = _ladders[(int)m];
                b.interactable = open;
                UIKit.SetLabel(b, Loc.F("Sans fin · {0}", LevelNames.Mode(m)) + "\n"
                                  + (open ? Loc.F("Tombeau {0}", App.Save.EndlessOf(m).Reached) : Loc.F("Finis {0}", LevelNames.Of(Progression.Last(m)))));
            }
            _energy.text = PlayGate.Status(App, Monetization.PlayMode.Endless);
        }

        void Play(Difficulty mode)
        {
            if (App.Save.EndlessOf(mode).Cleared >= MummyEscape.Game.GameController.EndlessTombCount(mode))
            {
                Router.Open<OfferDialog>().Configure("Sans fin", Loc.T("Tu as franchi tous les tombeaux : d'autres arrivent avec les mises à jour."),
                    ("OK", ButtonStyle.Primary, null)); // noloc
                return;
            }
            PlayGate.EndlessRun(App, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartEndless(mode);
            });
        }

        void Ranking()
        {
            var mode = Difficulty.Easy;
            foreach (var m in DifficultyExt.All) if (App.Save.IsEndlessUnlocked(m)) mode = m;
            Router.Get<LeaderboardScreen>().FocusEndless(mode); // before it opens: it loads the board it shows
            Router.Open<LeaderboardScreen>();
        }
    }
}
