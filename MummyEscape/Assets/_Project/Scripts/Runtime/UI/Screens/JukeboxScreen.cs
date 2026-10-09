using MummyEscape.Core;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Every piece of music, by theme then by floor, exactly as the game would pick it (a floor's own tracks, else the
    /// act's). Tapping a track plays it; leaving goes back to the menu music. Meant for trying out new themes.
    /// </summary>
    public sealed class JukeboxScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        RectTransform _list;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Juke-box");
            var body = Body(180, 40, 40);
            UIKit.Column(body, 16);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 8;
            var help = UIKit.Label(body, "Musiques dans Resources/Music : act2 (tout l'acte), act2_f2 (étage 2), act2_b (variante, tirée au hasard).", 24, UIKit.Dim);
            UIKit.Size(help, 76);
        }

        public override void OnShow()
        {
            App.Audio.TrackChanged += Fill;
            Fill();
            _scroll.verticalNormalizedPosition = 1f;
        }

        public override void OnHide()
        {
            App.Audio.TrackChanged -= Fill;
            App.Audio.PlayMusic(0);
        }

        void Fill()
        {
            UIKit.ClearChildren(_list);
            Section(Loc.T("Menus"));
            foreach (var t in MusicCatalog.For(0, 0)) Item(t, null);
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var def = DifficultyTable.GetAct(act);
                Section(Loc.F("Acte {0} — {1}", act, Loc.T(def.Name)));
                int floors = def.Floors;
                for (int f = 1; f <= floors; f++)
                {
                    if (floors > 1) Floor(Loc.F("Étage {0}", f));
                    foreach (var t in MusicCatalog.For(act, f)) Item(t, t.Floor > 0 ? Loc.T("propre à l'étage") : Loc.T("tout l'acte"));
                }
            }
        }

        void Section(string title)
        {
            var t = UIKit.SectionTitle(_list, "");
            t.text = title.ToUpperInvariant();
            UIKit.Size(t, 64);
        }

        void Floor(string title)
        {
            var t = UIKit.Label(_list, "", 28, UIKit.Dim, TextAnchor.LowerLeft, FontStyle.Bold);
            t.text = "   " + title;
            UIKit.Size(t, 44);
        }

        void Item(MusicTrack track, string scope)
        {
            bool playing = App.Audio.Current == track;
            UIKit.ListItem(_list, 100, () => App.Audio.PlayTrack(track), out var h, playing);
            UIKit.Size(UIKit.Image(h.transform, playing ? UISprites.Note : UISprites.Play, playing ? UIKit.Turquoise : UIKit.Gold), 40, 40);
            var name = UIKit.Label(h.transform, "", 32, UIKit.Sand, TextAnchor.MiddleLeft, playing ? FontStyle.Bold : FontStyle.Normal);
            name.text = !track.Composed ? track.Resource
                : track.Theme == 0 ? Loc.T("Boucle d'ambiance intégrée")
                : Loc.T("Thème composé par le jeu");
            UIKit.FitText(name, 22);
            UIKit.Size(name, -1, -1, 1);
            if (scope != null)
            {
                var s = UIKit.Label(h.transform, "", 24, UIKit.Dim, TextAnchor.MiddleRight);
                s.text = scope;
                UIKit.Size(s, -1, 230, 0);
            }
        }
    }
}
