using MummyEscape.Game;
using MummyEscape.Pvp;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// After the preview: "who starts?". Each teammate taps a mummy (himself or the other); if they agree, that one runs
    /// maze 1 and the other finishes in maze 2, otherwise the gods draw lots. Closes itself when the race starts.
    /// </summary>
    public sealed class RelayVoteScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        Text _count, _status;
        readonly Button[] _choices = new Button[2];
        readonly Image[] _portraits = new Image[2];
        readonly Text[] _names = new Text[2];
        readonly Image[] _marks = new Image[2];
        string[] _ids = new string[2];

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Panel(Root);
            UIKit.FitInParent(UIKit.Place(panel.rectTransform, 0.5f, 0.5f, 960, 0));
            var col = UIKit.Column(panel.transform, 18, 40);
            col.padding = new RectOffset(40, 40, 36, 40);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Size(UIKit.Title(panel.transform, "Qui commence ?", 70), 96);
            var rule = UIKit.Label(panel.transform, "Le premier court le labyrinthe 1 jusqu'à sa dalle, l'autre finit par la sortie du labyrinthe 2. D'accord : c'est fait. Pas d'accord : tirage au sort.", 26, UIKit.Dim);
            UIKit.FitText(rule, 18);
            UIKit.Size(rule, 110);

            var row = UIKit.Row(panel.transform, 420, 24);
            for (int k = 0; k < 2; k++)
            {
                int index = k;
                var b = _choices[k] = UIKit.Button(row.transform, "", () => Vote(index), 30);
                UIKit.Size(b, 420, -1, 1);
                var portrait = _portraits[k] = UIKit.Image(b.transform, null, Color.white, false, "Portrait"); // noloc
                UIKit.Place(portrait.rectTransform, 0.5f, 1f, 250, 250, 0, -20);
                portrait.preserveAspect = true;
                var name = _names[k] = UIKit.Title(b.transform, "", 34, UIKit.Sand);
                UIKit.FitText(name, 20);
                UIKit.Place(name.rectTransform, 0.5f, 0f, 380, 60, 0, 40);
                var mark = _marks[k] = UIKit.Image(b.transform, UIKit.Art.White, UIKit.Gold, false, "Mark"); // noloc
                UIKit.Rounded(mark, 10);
                UIKit.Place(mark.rectTransform, 0.5f, 0f, 200, 10, 0, 18);
            }

            _status = UIKit.Label(panel.transform, "", 30, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_status, 18);
            UIKit.Size(_status, 90);
            _count = UIKit.Title(panel.transform, "", 64, UIKit.Gold);
            UIKit.Size(_count, 80);
        }

        public override void OnShow()
        {
            var game = App.Game;
            var side = game.MySide;
            if (side == null) return;
            for (int k = 0; k < 2; k++)
            {
                var runner = side.Runners[k];
                _ids[k] = runner.PlayerId;
                bool me = runner.PlayerId == game.Relay.Me;
                MummyAnimator.Show(_portraits[k], App.Art, me ? App.Save.Loadout : PvpSkins.Loadout(runner.Look));
                _names[k].text = me ? Loc.T("Moi") : runner.Name;
            }
            game.RelayChanged += Refresh;
            Refresh();
        }

        public override void OnHide() => App.Game.RelayChanged -= Refresh;

        void Vote(int index)
        {
            App.Game.VoteStarter(_ids[index]);
            App.Audio.Play(Services.Sfx.Click);
        }

        void Refresh()
        {
            var game = App.Game;
            if (game.Phase != RelayPhase.Vote)
            {
                Router.Close(this);
                return;
            }
            game.Votes.TryGetValue(game.Relay.Me, out var mine);
            string mate = _ids[0] == game.Relay.Me ? _ids[1] : _ids[0];
            game.Votes.TryGetValue(mate, out var theirs);
            for (int k = 0; k < 2; k++)
            {
                _choices[k].interactable = mine == null;
                _marks[k].gameObject.SetActive(mine == _ids[k] || theirs == _ids[k]);
                _marks[k].color = mine == _ids[k] && theirs == _ids[k] ? UIKit.Success : mine == _ids[k] ? UIKit.Gold : UIKit.Turquoise;
            }
            string mateName = game.MySide.Runner(mate)?.Name ?? "?";
            string Who(string id) => id == game.Relay.Me ? Loc.T("toi") : mateName;
            _status.text = theirs == null ? Loc.F("{0} réfléchit…", mateName)
                         : Loc.F("{0} veut que {1} commence", mateName, Who(theirs));
        }

        void Update()
        {
            if (App.Game.Phase == RelayPhase.Vote) _count.text = Mathf.Max(0, Mathf.CeilToInt(App.Game.VoteLeft)).ToString();
            else Router.Close(this);
        }

        public void OnBack() { }
    }
}
