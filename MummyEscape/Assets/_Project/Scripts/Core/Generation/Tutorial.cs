using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>One stretch of the tutorial corridor and what it teaches (French text, a Loc key).</summary>
    public sealed class TutorialLesson
    {
        public int Floor;
        public int FromX;
        public int ToX;
        public string Text;

        public bool Contains(Cell c) => c.Floor == Floor && c.X >= FromX && c.X <= ToX;
    }

    /// <summary>
    /// The tutorial: one long hand-made corridor over two floors that meets every mechanic of the game in turn, each one
    /// announced on screen when the mummy walks into its stretch. Doors, spikes, cursed sand, the four kinds of portals, a
    /// ladder, dust and wall torches, a current, a fragile slab, a barrier and its switch, a flame jet, a cracked floor,
    /// the mirror and two turning slabs (the second one sets the tomb straight again), then the exit.
    /// </summary>
    public static class Tutorial
    {
        public static readonly LevelId Id = new LevelId(1, 0);
        public const int Width = 106;
        public const int Height = 5;
        const int Row = 2;

        public static readonly IReadOnlyList<TutorialLesson> Lessons = new[]
        {
            L(0, 0, 5, "Glisse vers la droite pour avancer d'une case.\nTa torche n'éclaire que les cases voisines."),
            L(0, 6, 10, "Porte fermée : marche sur la dalle\ndu renfoncement pour l'ouvrir."),
            L(0, 11, 15, "Des pics ! Chacun coûte un ankh.\nJuste à côté, appuie sur « Désamorcer »."),
            L(0, 16, 19, "Sable maudit : il t'aveugle 3 pas\net ne se désamorce pas. Continue tout droit."),
            L(0, 20, 24, "Un téléporteur : marche dessus.\nDeux portails de même couleur sont reliés."),
            L(0, 25, 30, "Portail scellé : active d'abord\nle levier du renfoncement."),
            L(0, 31, 36, "Un cul-de-sac ? Certains portails sont cachés :\nva jusqu'au bout du couloir."),
            L(0, 37, 41, "Portail maudit : il fonctionne,\nmais efface ta mémoire de la carte."),
            L(0, 42, 44, "Une échelle : marche dessus\npour monter d'un étage."),
            L(1, 44, 53, "La poussière éteint ta torche.\nLonge une torche murale pour la rallumer."),
            L(1, 54, 59, "Un courant t'emporte jusqu'au bout\net ne se remonte pas."),
            L(1, 60, 63, "Dalle fissurée : elle s'effondre\ndès que tu la quittes. Un seul passage."),
            L(1, 64, 68, "Barrière rouge : l'interrupteur l'ouvre.\nChaque passage dessus l'inverse."),
            L(1, 69, 72, "Jet de flammes : il crache un pas sur trois.\nAttends qu'il s'éteigne pour passer."),
            L(1, 73, 75, "Sol fragile : il cède sous tes pieds\net tu tombes à l'étage du dessous."),
            L(0, 75, 90, "Miroir de Seth : tes commandes\nsont inversées pendant 10 pas."),
            L(0, 91, 96, "Dalle tournante : le tombeau pivote.\nGlisse selon ce que tu vois à l'écran."),
            L(0, 97, 100, "Une autre dalle le remet droit."),
            L(0, 101, Width, "La sortie ! Avant chaque vrai tombeau, mémorise\nsa carte : ensuite, tout est dans le noir."),
        };

        static TutorialLesson L(int floor, int from, int to, string text) => new TutorialLesson { Floor = floor, FromX = from, ToX = to, Text = text };

        /// <summary>The lesson of the stretch the mummy stands in, null between two.</summary>
        public static TutorialLesson LessonAt(Cell c)
        {
            foreach (var l in Lessons) if (l.Contains(c)) return l;
            return null;
        }

        public static Level Build()
        {
            var level = new Level(Width, Height, 2)
            {
                Id = Id, Start = new Cell(0, 1, Row), Exit = new Cell(0, 104, Row), MaxHp = 3, ChannelCount = 3, TrapCount = 5,
            };
            foreach (var c in level.AllCells()) level[c] = Tile.Wall;
            void Set(int f, int x, int y, Tile t) => level[new Cell(f, x, y)] = t;
            void Corridor(int f, int from, int to) { for (int x = from; x <= to; x++) Set(f, x, Row, Tile.Floor); }
            void Portal(int f, int a, int b, TeleporterKind kind, byte channel = 0)
            {
                var t = new Tile { Type = TileType.Teleporter, Teleporter = kind, Channel = channel };
                Set(f, a, Row, t);
                Set(f, b, Row, t);
                level.LinkTeleporters(new Cell(f, a, Row), new Cell(f, b, Row));
            }
            Tile Trap(TrapKind kind, byte index, byte param = 0) => new Tile { Type = TileType.Trap, Trap = kind, TrapIndex = index, Param = param };

            // Ground floor, first half: doors, traps and portals.
            Corridor(0, 1, 22);
            Corridor(0, 24, 28);
            Corridor(0, 30, 34);
            Corridor(0, 36, 39);
            Corridor(0, 41, 43);
            Set(0, 8, Row + 1, new Tile { Type = TileType.Button, Channel = 0 });
            Set(0, 10, Row, new Tile { Type = TileType.Door, Channel = 0 });
            Set(0, 14, Row, Trap(TrapKind.Spikes, 0));
            Set(0, 18, Row, Trap(TrapKind.Darkness, 1));
            Portal(0, 22, 24, TeleporterKind.Visible);
            Set(0, 26, Row + 1, new Tile { Type = TileType.Button, Channel = 1 });
            Portal(0, 28, 30, TeleporterKind.Locked, 1);
            Portal(0, 34, 36, TeleporterKind.Hidden);
            Portal(0, 39, 41, TeleporterKind.Cursed);
            Set(0, 44, Row, new Tile { Type = TileType.LadderUp });

            // Upstairs: torch, current, fragile slab, barrier, flames, and a floor that gives way.
            Corridor(1, 45, 74);
            Set(1, 44, Row, new Tile { Type = TileType.LadderDown });
            Set(1, 47, Row, new Tile { Type = TileType.Dust });
            Set(1, 48, Row, new Tile { Type = TileType.Dust });
            Set(1, 53, Row + 1, new Tile { Type = TileType.WallTorch });
            for (int x = 56; x <= 58; x++) Set(1, x, Row, new Tile { Type = TileType.Current, Param = (byte)Dir.Right });
            Set(1, 62, Row, new Tile { Type = TileType.Crumbling, Param = 0 });
            Set(1, 65, Row + 1, new Tile { Type = TileType.Switch, Channel = 2 });
            Set(1, 66, Row, new Tile { Type = TileType.Barrier, Channel = 2, Param = 0 });
            Set(1, 71, Row, new Tile { Type = TileType.FireJet, Param = 0 });
            Set(1, 75, Row, new Tile { Type = TileType.BreakableFloor });

            // Ground floor again: the mirror, the turning slabs, the exit.
            Corridor(0, 75, 103);
            Set(0, 78, Row, Trap(TrapKind.Reverse, 2));
            Set(0, 93, Row, Trap(TrapKind.Rotate, 3, 1));
            Set(0, 97, Row, Trap(TrapKind.Rotate, 4, 3));
            Set(0, 104, Row, new Tile { Type = TileType.Exit });
            return level;
        }
    }
}
