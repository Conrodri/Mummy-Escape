using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>A hand-made tomb of the two-player campaign.</summary>
    public sealed class CoopTomb
    {
        /// <summary>1, 2, 3… across the whole campaign: what saves, the server and the network use.</summary>
        public int Number;
        public int Act;
        public int Index;
        /// <summary>French name (a Loc key).</summary>
        public string Name;
        /// <summary>What this tomb introduces (French, a Loc key), shown before the preview.</summary>
        public string Lesson;
        /// <summary>One mummy alone cannot reach the exit (checked by the tests).</summary>
        public bool NeedsBoth;
        public Level Level;
        /// <summary>Where mummy 0 and mummy 1 start (mummy i: the duo's member i).</summary>
        public Cell[] Starts;

        Lazy<int> _par;
        /// <summary>The fewest actions (both mummies together) that get the pair out.</summary>
        public int Par => _par.Value;

        /// <summary>The time the stars are measured against: what a good pair needs per action of the par.</summary>
        public const int TargetMsPerMove = 700;
        public int TargetMs => Par * TargetMsPerMove;

        internal void Seal() => _par = new Lazy<int>(() => CoopSolver.Solve(new CoopRules(Level), new CoopRules(Level).Initial(Starts[0], Starts[1]))?.Moves ?? 0);

        /// <summary>The try as a recap, stars like solo: by moves against the par and by time against <see cref="TargetMs"/>.</summary>
        public LevelResult Result(bool won, int moves, int timeMs) => new LevelResult
        {
            Level = Level.Id,
            Won = won,
            Moves = moves,
            Par = Par,
            TimeMs = timeMs,
            TargetMs = TargetMs,
            MaxHp = Level.MaxHp,
        };
    }

    /// <summary>
    /// The two-player campaign: hand-made tombs over three acts, each act bringing one way of needing the other mummy.
    /// Act 1, the shared tomb (a button opens the other's door, the exit waits for both); act 2, held plates (one stands
    /// on it while the other passes); act 3, switches and barriers (one switch opens a way and closes another) and slabs
    /// that bear only one crossing. Tombs are drawn in text, top row first:
    /// <code>
    /// # wall   . floor   1 2 starts of mummy 0 and 1   E exit (both must stand on it)
    /// A-D doors, a-d their buttons (channels 0-3, a press stays)
    /// P-S doors, p-s their held plates (channels 4-7, open only while a mummy stands on the plate)
    /// k l switches (channels 8, 9); K L red barriers, open while the channel is on; M N blue ones, open while it is off
    /// ^ spikes   x fragile slab   5 6 7 pairs of portals
    /// </code>
    /// </summary>
    public static class CoopCampaign
    {
        public const int Acts = 3;

        static readonly object Gate = new object();
        static List<CoopTomb> _tombs;

        public static IReadOnlyList<CoopTomb> Tombs
        {
            get
            {
                lock (Gate) return _tombs ?? (_tombs = Build());
            }
        }

        public static int Count => Tombs.Count;

        /// <summary>Tomb by its <see cref="CoopTomb.Number"/>, null when there is none.</summary>
        public static CoopTomb Get(int number) => number >= 1 && number <= Count ? Tombs[number - 1] : null;

        /// <summary>Act names (French, Loc keys).</summary>
        public static string ActName(int act) => act == 1 ? "Le tombeau partagé" : act == 2 ? "Les dalles de pression" : "Leviers et barrières";

        static List<CoopTomb> Build()
        {
            var list = new List<CoopTomb>();
            void Add(int act, string name, string lesson, bool needsBoth, params string[] rows)
            {
                int index = 1;
                foreach (var t in list) if (t.Act == act) index++;
                var tomb = Parse(rows);
                tomb.Number = list.Count + 1;
                tomb.Act = act;
                tomb.Index = index;
                tomb.Name = name;
                tomb.Lesson = lesson;
                tomb.NeedsBoth = needsBoth;
                tomb.Level.Id = new LevelId(Difficulty.Easy, act, index);
                tomb.Seal();
                list.Add(tomb);
            }

            // ------------------------------------------------------------------ act 1: the shared tomb

            Add(1, "Ensemble", "Vous sortez à deux : la sortie ne s'ouvre que quand vos deux momies s'y tiennent.", false,
                "###########",
                "#1...#...2#",
                "#.#.###.#.#",
                "#.#.....#.#",
                "#.###.###.#",
                "#.....E...#",
                "###########");

            Add(1, "Le levier de l'autre", "Le bouton de ta porte est chez ton coéquipier : un bouton pressé l'est pour vous deux.", true,
                "###########",
                "#1.b#a..2.#",
                "#.###.#####",
                "#.A.#.B...#",
                "###.#####.#",
                "#...E.....#",
                "###########");

            Add(1, "Chacun son tour", "Chaque porte s'ouvre chez l'autre : avancez l'un après l'autre.", true,
                "##############",
                "#1..A..b..C..#",
                "############.#",
                "#2..a..B..c.E#",
                "##############");

            Add(1, "Deux torches", "Ce que la torche de ton coéquipier éclaire, tu le vois aussi.", true,
                "###############",
                "#1....#.....a.#",
                "#.###.#.###.#.#",
                "#.#b#.#.#2#...#",
                "#.#.#.#.#.#.###",
                "#...#.#...#...#",
                "#####A#######B#",
                "#.............#",
                "#.###.#####.#.#",
                "#...#...E...#.#",
                "###############");

            Add(1, "Portails jumeaux", "Un portail mène chacun chez l'autre : passez, et ouvrez-vous la route.", true,
                "###############",
                "#1..5#5..b...A#",
                "#############.#",
                "#2..6#6..a..#.#",
                "########B####.#",
                "#.............#",
                "#.###########.#",
                "#......E......#",
                "###############");

            Add(1, "Pics partagés", "Des pics désamorcés le sont pour vous deux : le premier arrivé dégage la route.", true,
                "#############",
                "#1.b#a....2.#",
                "#.###.#######",
                "#.A.#.B.....#",
                "###.#######.#",
                "#...........#",
                "#.#########.#",
                "#.^.^.E.^.^.#",
                "#############");

            Add(1, "Trois portes", "Une porte chacun, puis une troisième : avancez au rythme de l'autre.", true,
                "#############",
                "#1..b#..2...#",
                "#A#####B#####",
                "#...c#...a..#",
                "#.#####C#####",
                "#.....E.....#",
                "#############");

            // ------------------------------------------------------------------ act 2: held plates

            Add(2, "Tiens la dalle", "Une dalle de pression ne tient sa porte ouverte que tant qu'une momie reste dessus.", true,
                "###########",
                "#1..p#2...#",
                "#.####P####",
                "#.A....a..#",
                "#####.#####",
                "#...E.....#",
                "###########");

            Add(2, "Le sas", "Toi sur la dalle, lui dans le passage, puis l'inverse.", true,
                "#############",
                "#1..p#2.....#",
                "#Q####P######",
                "#.#...q.....#",
                "#.#########.#",
                "#.....E.....#",
                "#############");

            Add(2, "Mémoire de pierre", "Ta porte est loin de ta dalle : souviens-toi du chemin de ton coéquipier.", true,
                "###############",
                "#1......#2....#",
                "#######.#####.#",
                "#p......#.....#",
                "#.#######.#####",
                "#......A#.....#",
                "#######.#####P#",
                "#.......#..a..#",
                "#.#######.#####",
                "#......E......#",
                "###############");

            Add(2, "Relais de dalles", "Deux dalles, deux portes : tenez-les chacun votre tour.", true,
                "#############",
                "#1..p#2.....#",
                "#Q####P######",
                "#.#...q..A..#",
                "#.#########.#",
                "#a........E.#",
                "#############");

            Add(2, "Dalle et pics", "Pendant que l'un tient la dalle, l'autre désamorce les pics de la route commune.", true,
                "###############",
                "#1..p#....^...#",
                "#.####.######.#",
                "#.#..P.#2...#.#",
                "#.#.####.##.#.#",
                "#.#.^.....b.#.#",
                "#.###########.#",
                "#..B.......E..#",
                "###############");

            Add(2, "Les deux dalles", "Ta dalle est derrière une porte que ton coéquipier doit t'ouvrir.", true,
                "###############",
                "#1...A..p#2..a#",
                "#B##########P##",
                "#.#########b..#",
                "#.###########.#",
                "#......E......#",
                "###############");

            Add(2, "Le gardien", "L'un garde la dalle jusqu'au bout : l'autre doit aller et revenir.", true,
                "###############",
                "#1..p#2..b..A.#",
                "#B#####P#####.#",
                "#.#a......###.#",
                "#.###########.#",
                "#......E......#",
                "###############");

            // ------------------------------------------------------------------ act 3: switches, barriers, fragile slabs

            Add(3, "L'interrupteur", "Un interrupteur ouvre les barrières rouges et ferme les bleues, pour vous deux.", true,
                "#############",
                "#1.kA.M.....#",
                "###########.#",
                "#2.K..k..a.E#",
                "#############");

            Add(3, "Rouge et bleu", "Ce qui t'ouvre la route la ferme à ton coéquipier : choisissez le bon moment.", true,
                "###############",
                "#1.k.M.L......#",
                "#############.#",
                "#2..K.N..l....#",
                "#############E#",
                "###############");

            Add(3, "Un seul passage", "La dalle fragile ne porte qu'une traversée : qui passe, et comment l'autre suit ?", true,
                "#############",
                "#1.2.px...a.#",
                "#A#########P#",
                "#.#########.#",
                "#.....E.....#",
                "#############");

            Add(3, "Dalle et barrière", "Ta dalle tient la porte, son interrupteur tient ta barrière : à chacun son moment.", true,
                "###############",
                "#1..p#2.......#",
                "#K####P########",
                "#.#..k...M....#",
                "#.###########.#",
                "#......E......#",
                "###############");

            Add(3, "Le grand passage", "Tout ce que vous avez appris : dalles, boutons et dalle fragile.", true,
                "#################",
                "#1...p#2........#",
                "#A#####P#########",
                "#.#...a.........#",
                "#.#############.#",
                "#..q............#",
                "#B###########x###",
                "#.#########b....#",
                "#.#############Q#",
                "#.......E.......#",
                "#################");

            Add(3, "La dernière chambre", "La dernière chambre : leviers, barrières, pics et dalles, ensemble.", true,
                "#################",
                "#1.k.M.L........#",
                "###############.#",
                "#2..K.N..l......#",
                "###############.#",
                "#..p..^...^.....#",
                "#############P#Q#",
                "#..E....q.....#.#",
                "#...............#",
                "#################");
            return list;
        }

        /// <summary>Builds a one-floor tomb from its rows (see the legend above).</summary>
        internal static CoopTomb Parse(string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            var level = new Level(w, h, 1) { MaxHp = 2 };
            var starts = new Cell[2];
            var found = new bool[2];
            var portals = new Dictionary<char, Cell>();
            int traps = 0, crumbling = 0, channels = 0;
            for (int row = 0; row < h; row++)
            {
                if (rows[row].Length != w) throw new ArgumentException($"row {row} is {rows[row].Length} wide, not {w}");
                for (int x = 0; x < w; x++)
                {
                    var c = new Cell(0, x, h - 1 - row);
                    char ch = rows[row][x];
                    var t = Tile.Floor;
                    if (ch == '#') t = Tile.Wall;
                    else if (ch == '1' || ch == '2') { starts[ch - '1'] = c; found[ch - '1'] = true; }
                    else if (ch == 'E') { t = new Tile { Type = TileType.Exit }; level.Exit = c; }
                    else if (ch >= 'A' && ch <= 'D') t = new Tile { Type = TileType.Door, Channel = (byte)(ch - 'A') };
                    else if (ch >= 'a' && ch <= 'd') t = new Tile { Type = TileType.Button, Channel = (byte)(ch - 'a') };
                    else if (ch >= 'P' && ch <= 'S') t = new Tile { Type = TileType.Door, Channel = (byte)(4 + ch - 'P') };
                    else if (ch >= 'p' && ch <= 's') t = new Tile { Type = TileType.Button, Channel = (byte)(4 + ch - 'p'), Param = CoopRules.HeldPlate };
                    else if (ch == 'k' || ch == 'l') t = new Tile { Type = TileType.Switch, Channel = (byte)(8 + ch - 'k') };
                    else if (ch == 'K' || ch == 'L') t = new Tile { Type = TileType.Barrier, Channel = (byte)(8 + ch - 'K'), Param = 0 };
                    else if (ch == 'M' || ch == 'N') t = new Tile { Type = TileType.Barrier, Channel = (byte)(8 + ch - 'M'), Param = 1 };
                    else if (ch == '^') t = new Tile { Type = TileType.Trap, Trap = TrapKind.Spikes, TrapIndex = (byte)traps++ };
                    else if (ch == 'x') t = new Tile { Type = TileType.Crumbling, Param = (byte)crumbling++ };
                    else if (ch >= '5' && ch <= '7')
                    {
                        t = new Tile { Type = TileType.Teleporter, Teleporter = TeleporterKind.Visible };
                        if (portals.TryGetValue(ch, out var other)) level.LinkTeleporters(other, c);
                        else portals[ch] = c;
                    }
                    else if (ch != '.') throw new ArgumentException($"unknown tile '{ch}'");
                    if (t.Type == TileType.Door || t.Type == TileType.Button || t.Type == TileType.Switch || t.Type == TileType.Barrier)
                        channels = Math.Max(channels, t.Channel + 1);
                    level[c] = t;
                }
            }
            if (!found[0] || !found[1]) throw new ArgumentException("a tomb needs both starts");
            level.Start = starts[0];
            level.TrapCount = traps;
            level.ChannelCount = channels;
            return new CoopTomb { Level = level, Starts = starts };
        }
    }
}
