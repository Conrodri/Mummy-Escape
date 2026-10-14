using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>One stretch of an intro corridor and what it teaches (French text, a Loc key).</summary>
    public sealed class IntroLesson
    {
        public int Floor;
        public int FromX;
        public int ToX;
        public string Text;

        public bool Contains(Cell c) => c.Floor == Floor && c.X >= FromX && c.X <= ToX;
    }

    /// <summary>
    /// The act intros (Easy mode): one short hand-made corridor per act that meets the act's new mechanics in turn,
    /// each one announced on screen when the mummy walks into its stretch, then the exit.
    /// </summary>
    public static class ActIntro
    {
        const int Height = 5;
        const int Row = 2;
        const string ExitA = "La sortie ! Avant chaque vrai tombeau, mémorise\nsa carte : ensuite, tout est dans le noir.";
        const string ExitB = "La sortie ! Les tombeaux de cet acte\nt'attendent, mémorise bien leur carte.";
        const string CrumblingText = "Dalle fissurée : elle s'effondre\ndès que tu la quittes. Un seul passage.";

        static readonly IntroLesson[][] AllLessons =
        {
            new[]
            {
                L(0, 2, "Avance vers la droite, une case à la fois.\nLa sortie est au bout du couloir."),
                L(3, 5, "Ta torche n'éclaire que les cases voisines :\nretiens bien ce que tu as vu."),
                L(6, 10, "Porte fermée : marche sur la dalle\ndu renfoncement pour l'ouvrir."),
                L(11, 15, "Des pics ! Chacun coûte un ankh.\nJuste à côté, appuie sur « Désamorcer »."),
                L(16, 19, "Sable maudit : il t'aveugle 3 pas\net ne se désamorce pas. Continue tout droit."),
                L(20, 24, "Un téléporteur : marche dessus.\nDeux portails de même couleur sont reliés."),
                L(25, 29, ExitA),
            },
            new[]
            {
                L(0, 9, "La poussière éteint ta torche.\nLonge une torche murale pour la rallumer."),
                L(10, 13, "Des bandelettes barrent le passage :\navance dessus torche allumée pour les brûler."),
                L(14, 19, "Un courant t'emporte jusqu'au bout\net ne se remonte pas."),
                L(20, 24, ExitB),
            },
            new[]
            {
                L(0, 6, "Portail scellé : active d'abord\nle levier du renfoncement."),
                L(7, 10, CrumblingText),
                L(11, 15, "Ce mur miroite : c'est un mirage.\nTraverse-le, il raccourcit le chemin."),
                L(16, 21, "Miroir de Seth : tes commandes\nsont inversées pendant 10 pas."),
                L(22, 26, ExitB),
            },
            new[]
            {
                L(0, 5, "Barrière rouge : l'interrupteur l'ouvre.\nChaque passage dessus l'inverse."),
                L(6, 9, "Portail maudit : il fonctionne,\nmais efface ta mémoire de la carte."),
                L(10, 14, "Idole : ses yeux se chargent, puis son regard\nfrappe sa ligne chaque seconde. Passe juste après."),
                L(15, 19, "Dalle tournante : le tombeau pivote.\nDéplace-toi selon ce que tu vois à l'écran."),
                L(20, 23, "Une autre dalle le remet droit."),
                L(24, 28, ExitB),
            },
            new[]
            {
                L(0, 5, "Jet de flammes : il crache chaque seconde.\nPasse juste après une flamme, sans t'arrêter."),
                L(6, 9, CrumblingText),
                L(10, 14, ExitB),
            },
        };

        static IntroLesson L(int from, int to, string text) => new IntroLesson { Floor = 0, FromX = from, ToX = to, Text = text };

        public static LevelId Id(int act) => new LevelId(Difficulty.Easy, act, 0);

        /// <summary>The stretches of the act's intro, in order (act 1..DifficultyTable.ActCount).</summary>
        public static IReadOnlyList<IntroLesson> Lessons(int act) => AllLessons[act - 1];

        /// <summary>The lesson of the stretch the mummy stands in, null between two.</summary>
        public static IntroLesson LessonAt(int act, Cell c)
        {
            foreach (var l in Lessons(act)) if (l.Contains(c)) return l;
            return null;
        }

        public static Level Build(int act)
        {
            var lessons = Lessons(act);
            int width = lessons[lessons.Count - 1].ToX + 1;
            var level = new Level(width, Height, 1) { Id = Id(act), Start = new Cell(0, 1, Row), MaxHp = 3 };
            foreach (var c in level.AllCells()) level[c] = Tile.Wall;
            void Set(int x, int y, Tile t) => level[new Cell(0, x, y)] = t;
            void Corridor(int from, int to) { for (int x = from; x <= to; x++) Set(x, Row, Tile.Floor); }
            void Portal(int a, int b, TeleporterKind kind, byte channel = 0)
            {
                var t = new Tile { Type = TileType.Teleporter, Teleporter = kind, Channel = channel };
                Set(a, Row, t);
                Set(b, Row, t);
                level.LinkTeleporters(new Cell(0, a, Row), new Cell(0, b, Row));
            }
            Tile Trap(TrapKind kind, byte index, byte param = 0) => new Tile { Type = TileType.Trap, Trap = kind, TrapIndex = index, Param = param };
            void Exit(int x) { Set(x, Row, new Tile { Type = TileType.Exit }); level.Exit = new Cell(0, x, Row); }

            switch (act)
            {
                case 1: // door, spikes, cursed sand, a portal
                    level.ChannelCount = 1;
                    level.TrapCount = 2;
                    Corridor(1, 22);
                    Corridor(24, 27);
                    Set(9, Row + 1, new Tile { Type = TileType.Button, Channel = 0 }); // the torch shows both from x = 9
                    Set(10, Row, new Tile { Type = TileType.Door, Channel = 0 });
                    Set(14, Row, Trap(TrapKind.Spikes, 0));
                    Set(18, Row, Trap(TrapKind.Darkness, 1));
                    Portal(22, 24, TeleporterKind.Visible);
                    Exit(28);
                    break;
                case 2: // dust and a wall torch, wrappings to burn, a current
                    Corridor(1, 22);
                    Set(3, Row, new Tile { Type = TileType.Dust });
                    Set(4, Row, new Tile { Type = TileType.Dust });
                    Set(9, Row + 1, new Tile { Type = TileType.WallTorch });
                    Set(12, Row, new Tile { Type = TileType.Wrappings, Param = 0 });
                    for (int x = 16; x <= 18; x++) Set(x, Row, new Tile { Type = TileType.Current, Param = (byte)Dir.Right });
                    Exit(23);
                    break;
                case 3: // sealed portal and its lever, fragile slab, a mirage (shortcut past a bend), mirror
                    level.ChannelCount = 1;
                    level.TrapCount = 1;
                    Corridor(1, 4);
                    Corridor(6, 24);
                    Set(3, Row + 1, new Tile { Type = TileType.Button, Channel = 0 }); // the lever and the sealed portal seen from the same tile
                    Portal(4, 6, TeleporterKind.Locked, 0);
                    Set(9, Row, new Tile { Type = TileType.Crumbling, Param = 0 });
                    for (int x = 12; x <= 14; x++) Set(x, Row - 1, Tile.Floor); // the way round
                    Set(13, Row, new Tile { Type = TileType.FalseWall });
                    Set(18, Row, Trap(TrapKind.Reverse, 0));
                    Exit(25);
                    break;
                case 4: // barrier and switch, cursed portal, an idol, two turning slabs (the second sets the tomb straight)
                    level.ChannelCount = 1;
                    level.TrapCount = 2;
                    Corridor(1, 7);
                    Corridor(9, 26);
                    Set(3, Row + 1, new Tile { Type = TileType.Switch, Channel = 0 });
                    Set(4, Row, new Tile { Type = TileType.Barrier, Channel = 0, Param = 0 });
                    Portal(7, 9, TeleporterKind.Cursed);
                    Set(12, Row - 1, Tile.Floor);
                    Set(12, Row + 1, Tile.Floor);
                    Set(12, 0, new Tile { Type = TileType.Idol, Param = (byte)Dir.Up }); // its gaze crosses the corridor
                    Set(17, Row, Trap(TrapKind.Rotate, 0, 1));
                    Set(21, Row, Trap(TrapKind.Rotate, 1, 3));
                    Exit(27);
                    break;
                case 5: // flame jet (real time), fragile slab
                    Corridor(1, 12);
                    Set(4, Row, new Tile { Type = TileType.FireJet });
                    Set(8, Row, new Tile { Type = TileType.Crumbling, Param = 0 });
                    Exit(13);
                    break;
            }
            return level;
        }
    }
}
