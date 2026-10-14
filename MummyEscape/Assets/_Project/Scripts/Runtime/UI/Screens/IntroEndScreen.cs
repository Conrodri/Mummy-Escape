using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>End of an act intro: out of the corridor, on to the act's first tomb; fallen in it, once more.</summary>
    public sealed class IntroEndScreen : UIScreen
    {
        public override bool IsModal => true;

        Text _title, _text;
        Button _main;
        bool _won;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Card(Root, 44, 22);
            UIKit.FitInParent(UIKit.Place(panel, 0.5f, 0.5f, 860, 0));
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel, "", 60);
            UIKit.Size(_title, 100);
            _text = UIKit.Label(panel, "", 32, UIKit.Sand);
            UIKit.Size(_text, 150);
            _main = UIKit.Button(panel, "Réessayer", Main, 38, ButtonStyle.Primary); // a label now: an empty one has no Text
            UIKit.Size(_main, UIKit.ButtonHeight + 8);
            UIKit.Size(UIKit.Button(panel, "Menu principal", Menu, UIKit.TextSize, ButtonStyle.Leave), 72);
        }

        public void Show(bool won)
        {
            _won = won;
            _title.text = Loc.T(won ? "Intro terminée !" : "La momie est tombée…");
            _text.text = Loc.T(won
                ? "Tu connais les pièges de cet acte.\nÀ toi de t'évader pour de vrai !"
                : "Pas de panique : l'intro ne coûte rien.\nRecommence et lis bien chaque panneau.");
            UIKit.SetLabel(_main, won ? Loc.F("Jouer le tombeau {0}-1", App.Game.IntroAct) : "Réessayer");
        }

        void Main()
        {
            Router.Close(this);
            if (!_won) { App.Game.Restart(); return; }
            PlayAct(App, Router, App.Game.IntroAct);
        }

        /// <summary>Leaves the intro (done by now) for the act's first Easy tomb: the end screen's button and Pause › Passer.</summary>
        public static void PlayAct(GameApp app, UIRouter router, int act)
        {
            var first = new LevelId(Difficulty.Easy, act, 1);
            app.Game.Abandon();
            router.Reset<MainMenuScreen>();
            PlayGate.Solo(app, first, () =>
            {
                router.Open<HudScreen>();
                _ = app.Game.StartLevel(first);
            });
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }

    /// <summary>
    /// Start of an act intro, before its first step (the clock waits): the act's new elements (act 1: the basics), each
    /// with its tile as drawn in the tomb, what it does and how to get past it. The corridor's signs then recall each one.
    /// </summary>
    public sealed class ActIntroCardScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        Text _title, _subtitle;
        RectTransform _list;
        ScrollRect _scroll;
        System.Action _then;

        protected override void Build()
        {
            var panel = ModalKit.Frame(Root, 940);
            _title = ModalKit.Header(panel, "", 56, out _subtitle);
            _list = UIKit.Scroll(panel, out _scroll);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 10;
            UIKit.Size(UIKit.Button(panel, "C'est parti !", OnBack, 38, ButtonStyle.Primary), UIKit.ButtonHeight + 8);
        }

        /// <summary>Fills the card for <paramref name="act"/>; <paramref name="then"/> runs once the player is ready.</summary>
        public void Show(int act, System.Action then)
        {
            _then = then;
            _title.text = Loc.F("Acte {0} — {1}", act, Loc.T(DifficultyTable.GetAct(act).Name));
            _subtitle.text = Loc.T(act == 1 ? "Les bases du tombeau" : "Les nouveautés de cet acte");
            UIKit.ClearChildren(_list);
            var lines = Lines(act);
            foreach (var (icon, tinted, name, text) in lines) Line(icon, tinted, name, text);
            UIKit.Size(_scroll, Mathf.Min(lines.Length * 158 + 16, 1150)); // past that, the list scrolls
            _scroll.verticalNormalizedPosition = 1f;
        }

        public override void OnShow() => App.Game.SetPaused(true);
        public override void OnHide() => App.Game.SetPaused(false);

        /// <summary>"C'est parti !" and the back key alike: the intro starts.</summary>
        public void OnBack()
        {
            Router.Close(this);
            var then = _then;
            _then = null;
            then?.Invoke();
        }

        void Line(Sprite icon, bool tinted, string name, string text)
        {
            var row = UIKit.Row(_list, 148, 24);
            row.padding = new RectOffset(12, 12, 0, 0);
            var img = UIKit.Image(row.transform, icon, tinted ? UIKit.Gold : Color.white);
            img.preserveAspect = true;
            UIKit.Size(img, 96, 96);
            var label = UIKit.Label(row.transform, "", 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            label.text = "<color=#E8C35A>" + Loc.T(name) + "</color>\n" + Loc.T(text);
            UIKit.FitText(label, 20);
            UIKit.Size(label, -1, -1, 1);
        }

        /// <summary>What the act brings, drawn with the tile sprites of the act's theme (set by the intro level).</summary>
        (Sprite icon, bool tinted, string name, string text)[] Lines(int act)
        {
            var art = App.Art;
            Sprite Of(TileType type, TileLook look = default) => art.ForTile(new Tile { Type = type }, look);
            Sprite Trap(TrapKind kind, byte param = 0) => art.ForTile(new Tile { Type = TileType.Trap, Trap = kind, Param = param }, new TileLook { Armed = true });
            Sprite Portal(TeleporterKind kind) => art.ForTile(new Tile { Type = TileType.Teleporter, Teleporter = kind }, default);
            switch (act)
            {
                case 1:
                    return new[]
                    {
                        (Of(TileType.Exit), false, "Le but", "Mémorise la carte du tombeau, puis rejoins la sortie dans le noir."),
                        (UISprites.Torch, true, "Ta torche", "Elle n'éclaire que les cases voisines : le reste, c'est ta mémoire."),
                        (Of(TileType.Door), false, "Porte", "Fermée : marche sur sa dalle (le bouton) pour l'ouvrir."),
                        (Portal(TeleporterKind.Visible), false, "Portail", "Marche dessus : il t'envoie au portail de la même couleur."),
                        (Trap(TrapKind.Spikes), false, "Pics", "Chacun coûte un ankh. Juste à côté, appuie sur « Désamorcer »."),
                        (Trap(TrapKind.Darkness), false, "Sable maudit", "Il t'aveugle 3 pas et ne se désamorce pas : avance de mémoire."),
                    };
                case 2:
                    return new[]
                    {
                        (Of(TileType.Dust), false, "Poussière", "Elle éteint ta torche : tu avances sans lumière."),
                        (Of(TileType.WallTorch), false, "Torche murale", "Passe juste à côté pour rallumer ta torche."),
                        (Of(TileType.Wrappings), false, "Bandelettes", "Elles barrent le passage : avance dedans torche allumée pour les brûler. Torche éteinte, elles tiennent."),
                        (art.ForTile(new Tile { Type = TileType.Current, Param = (byte)Dir.Right }, default), false, "Courant", "Il t'emporte jusqu'au bout et ne se remonte pas."),
                    };
                case 3:
                    return new[]
                    {
                        (Portal(TeleporterKind.Locked), false, "Portail scellé", "Il ne marche qu'une fois son levier actionné : va d'abord au levier."),
                        (Of(TileType.Crumbling), false, "Dalle fissurée", "Elle s'effondre dès que tu la quittes : un seul passage."),
                        (Of(TileType.FalseWall, new TileLook { WallFace = true }), false, "Faux mur (Normal, Extrême)", "Ce mur miroite : il se traverse. Toujours un raccourci, jamais obligatoire."),
                        (Trap(TrapKind.Reverse), false, "Miroir de Seth", "Il inverse tes commandes pendant 10 pas."),
                    };
                case 4:
                    return new[]
                    {
                        (Of(TileType.Barrier), false, "Barrières laser", "L'interrupteur ouvre les rouges et ferme les bleues ; chaque passage dessus inverse tout."),
                        (Portal(TeleporterKind.Cursed), false, "Portail maudit", "Il fonctionne, mais efface ta mémoire de la carte."),
                        (Of(TileType.Idol, new TileLook { Fire = 1 }), false, "Idole", "Ses yeux se chargent, puis son regard frappe sa ligne chaque seconde. Passe juste après."),
                        (Trap(TrapKind.Rotate, 1), false, "Dalle tournante", "Le tombeau pivote : déplace-toi selon ce que tu vois à l'écran. Une autre le remet droit."),
                        (Of(TileType.LadderUp), false, "Deux étages (Normal, Extrême)", "Une échelle mène à l'autre étage. À l'aperçu, glisse pour mémoriser les deux."),
                    };
                default:
                    return new[]
                    {
                        (Of(TileType.FireJet, new TileLook { Fire = 2 }), false, "Jet de flammes", "Il crache chaque seconde. Passe juste après une flamme, sans t'arrêter."),
                        (UISprites.Clock, true, "Temps réel", "Les flammes n'attendent pas tes pas : elles partent même si tu restes immobile. Hésiter sur leur ligne coûte un ankh."),
                        (Of(TileType.Crumbling), false, "Dalle fissurée", "De retour : elle s'effondre dès que tu la quittes. Prévois ton chemin avant d'y passer."),
                    };
            }
        }
    }
}
