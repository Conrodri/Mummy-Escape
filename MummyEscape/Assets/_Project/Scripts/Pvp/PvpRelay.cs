// Mummy Rush PvP — le relais 2v2 : deux labyrinthes séparés, un par coéquipier, joués en alternance. Le premier
// coureur va jusqu'à sa dalle de relais, ce qui libère son coéquipier sur le départ de l'autre labyrinthe ; celui-ci
// court jusqu'à sa propre dalle et rend la main, et ainsi de suite jusqu'à la sortie du second labyrinthe. Les deux duos
// courent en direct sur la même graine : le premier dont le dernier coureur sort gagne ; une momie morte fait perdre
// son duo. Même graine + mêmes actions = même relais, sur les quatre téléphones et sur le serveur.
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public static class RelayConfig
    {
        public const int Mazes = 2;
        public const int TimeLimitMs = 300_000;          // 5 minutes pour tout le relais
        /// <summary>Après la dalle : la caméra passe chez le coéquipier, qui ne peut pas partir avant.</summary>
        public const int HandoffMs = 600;
        public const int PreviewSecondsPerFloor = 7;     // aperçu de chaque étage des deux labyrinthes, sans passer
        public const int VoteSeconds = 15;               // « qui commence ? », après l'aperçu
        /// <summary>Recherche d'un duo réel avant de proposer des bots (pour ceux qui les acceptent).</summary>
        public const int BotFallbackSeconds = 60;
    }

    /// <summary>Une action du relais : l'instant (sur l'horloge commune de la course), le labyrinthe, l'action (<see cref="RunActions"/>).</summary>
    [Serializable]
    public class RelayInput
    {
        public int Tick;
        public int Maze;
        public int Direction;
    }

    /// <summary>
    /// Les deux labyrinthes d'un relais. Le labyrinthe 0 est celui du coureur qui part ; le 1 celui qui finit. Chacun a ses
    /// dalles de relais, dans l'ordre où son coureur les atteint ; sa dernière étape mène à sa case de sortie (pour le
    /// labyrinthe 0 c'est une dernière dalle, pour le 1 la vraie sortie).
    /// </summary>
    public sealed class RelayMap
    {
        public int Seed;
        public LevelId Id;
        public Level[] Mazes;
        public IReadOnlyList<Cell>[] Relays;

        /// <summary>Étapes de chaque coureur : 2 sur les petits tombeaux, 3 sur les grands.</summary>
        public int LegsPerRunner => Relays[0].Count + 1;
        /// <summary>Étapes du relais (= points de la barre de progression : chaque libération, puis l'arrivée).</summary>
        public int Segments => LegsPerRunner * RelayConfig.Mazes;

        /// <summary>Les étapes alternent : paires dans le labyrinthe 0, impaires dans le 1.</summary>
        public static int MazeOf(int segment) => segment % RelayConfig.Mazes;

        /// <summary>La case qui termine cette étape : une dalle de relais, ou la case de sortie du labyrinthe pour sa dernière.</summary>
        public Cell GoalOf(int segment)
        {
            int maze = MazeOf(segment), leg = segment / RelayConfig.Mazes;
            return leg < Relays[maze].Count ? Relays[maze][leg] : Mazes[maze].Exit;
        }

        /// <summary>Étages montrés avant la course, labyrinthe 0 puis 1 (<see cref="RelayConfig.PreviewSecondsPerFloor"/> par étage, mis en commun : on passe de l'un à l'autre en glissant).</summary>
        public int PreviewFloors => Mazes[0].Floors + Mazes[1].Floors;

        public int PreviewSeconds => PreviewFloors * RelayConfig.PreviewSecondsPerFloor;

        public ulong ComputeHash()
        {
            ulong h = 14695981039346656037UL;
            foreach (var m in Mazes) { unchecked { h ^= m.ComputeHash(); h *= 1099511628211UL; } }
            return h;
        }
    }

    /// <summary>Les labyrinthes d'un relais, tirés de la graine donnée par le serveur.</summary>
    public static class RelayArena
    {
        const int MaxTries = 40;

        /// <summary>
        /// Niveau dont chaque labyrinthe reprend le contrat, parmi les actes ouverts au PvP : aux actes 1 et 2, un tombeau
        /// moyen (niveaux 4 à 7) et 2 étapes par coureur ; à l'acte 3, un grand (niveaux 8 à 10) et 3 étapes.
        /// </summary>
        public static LevelId LevelFor(int seed)
        {
            uint u = (uint)seed;
            int act = 1 + (int)(u % 3);
            return act < 3 ? new LevelId(act, 4 + (int)(u / 3 % 4)) : new LevelId(act, 8 + (int)(u / 3 % 3));
        }

        /// <summary>Dalles de relais de chaque labyrinthe : une de moins que d'étapes par coureur.</summary>
        public static int RelaysFor(LevelId id) => DifficultyTable.Tier(id.Index) - 1;

        public static ulong GeneratorSeed(int seed) =>
            Pcg32.Hash(Pcg32.Hash(0x52454C4159UL /* "RELAY" */, (ulong)DifficultyTable.GeneratorVersion), (ulong)(uint)seed);

        /// <summary>
        /// Le contrat du niveau, dont les premiers mécanismes deviennent des portes à bouton : chacun de ces boutons sera
        /// une dalle de relais. La chaîne du générateur place la porte d'un mécanisme devant le suivant, donc on les
        /// atteint dans l'ordre, et on ne va pas plus loin sans eux.
        /// </summary>
        public static LevelSpec SpecFor(LevelId id)
        {
            var spec = DifficultyTable.Spec(id);
            int relays = RelaysFor(id);
            for (int g = 0; g < relays && g < spec.Gates.Count; g++) spec.Gates[g] = Gate.Door;
            // Pas de poussière : ses pics sans détour se passent à la torche, et un relais coupé entre eux laisserait le
            // coéquipier sans issue. Une ombre à la place.
            spec.DarknessTraps += spec.DustPatches;
            spec.DustPatches = 0;
            return spec;
        }

        public static RelayMap Generate(int seed)
        {
            var id = LevelFor(seed);
            var spec = SpecFor(id);
            int relays = RelaysFor(id);
            var map = new RelayMap { Seed = seed, Id = id, Mazes = new Level[RelayConfig.Mazes], Relays = new IReadOnlyList<Cell>[RelayConfig.Mazes] };
            ulong root = GeneratorSeed(seed);
            for (int m = 0; m < RelayConfig.Mazes; m++)
            {
                for (int attempt = 0; attempt < MaxTries && map.Mazes[m] == null; attempt++)
                {
                    var level = LevelGenerator.Generate(spec, Pcg32.Hash(root, (ulong)(m * MaxTries + attempt)));
                    var plates = FindRelays(level, relays);
                    if (plates == null) continue;
                    map.Mazes[m] = level;
                    map.Relays[m] = plates;
                }
                if (map.Mazes[m] == null) throw new LevelGenerationException($"Relay {seed}: no maze {m} with {relays} relay plates ({spec})");
            }
            return map;
        }

        /// <summary>
        /// Les <paramref name="count"/> premiers boutons du chemin idéal, s'ils sont tous obligatoires (sans l'un d'eux, la
        /// sortie est hors d'atteinte) ; null sinon.
        /// </summary>
        internal static List<Cell> FindRelays(Level level, int count)
        {
            var plates = new List<Cell>();
            if (count == 0) return plates;
            if (level.Solution == null) return null;
            var session = new GameSession(level);
            foreach (var action in level.Solution.Actions)
            {
                var r = session.Apply(action);
                if (r.Has(StepFlags.ButtonPressed) && level[r.SteppedOn].Type == TileType.Button) plates.Add(r.SteppedOn);
                if (plates.Count == count) break;
            }
            if (plates.Count < count) return null;
            foreach (var plate in plates)
                if (Solver.Solve(level.WithTile(plate, Tile.Floor)) != null) return null;
            return plates;
        }
    }

    public enum RelayStatus { Running, Finished, Lost }

    /// <summary>Pourquoi un duo a perdu son relais.</summary>
    public enum RelayDefeat { None, Died, TimedOut, Abandoned, Cheated }

    /// <summary>
    /// Un relais en cours : les deux momies d'un duo, l'étape en cours, les actions jouées et leur rythme. Le téléphone y
    /// joue les actions de son joueur et celles qu'il reçoit de son coéquipier (et de chaque duo adverse dans sa propre
    /// instance) ; le serveur y rejoue tout le relais pour le vérifier.
    /// </summary>
    public sealed class RelayRace
    {
        public RelayMap Map { get; }
        readonly GameSession[] _sessions;
        readonly RunClock[] _clocks;
        readonly List<RelayInput> _inputs = new List<RelayInput>();
        readonly List<int> _segmentEnds = new List<int>();

        /// <summary>Étape en cours (= étapes terminées).</summary>
        public int Segment { get; private set; }
        public RelayStatus Status { get; private set; }
        public RelayDefeat Defeat { get; private set; }
        /// <summary>Une action arrivée hors de son tour, refusée par les règles ou dans le désordre.</summary>
        public bool Invalid { get; private set; }
        /// <summary>Une action partie avant la fin de l'animation précédente ou de la passation (<see cref="RelayConfig.HandoffMs"/>).</summary>
        public bool TooFast { get; private set; }
        public int LastTick { get; private set; }

        public IReadOnlyList<RelayInput> Inputs => _inputs;
        /// <summary>Instant (en pas) de l'action qui a terminé chaque étape.</summary>
        public IReadOnlyList<int> SegmentEndTicks => _segmentEnds;
        public int ActiveMaze => RelayMap.MazeOf(Segment);
        public Cell Goal => Map.GoalOf(Segment);
        public bool IsOver => Status != RelayStatus.Running;
        /// <summary>Temps du relais : l'arrivée, ou la dernière action.</summary>
        public int TimeMs => RunActions.MsOf(LastTick);

        public RelayRace(RelayMap map)
        {
            Map = map;
            _sessions = new GameSession[RelayConfig.Mazes];
            _clocks = new RunClock[RelayConfig.Mazes];
            for (int m = 0; m < RelayConfig.Mazes; m++)
            {
                _sessions[m] = new GameSession(map.Mazes[m]);
                _clocks[m] = new RunClock();
            }
        }

        public GameSession Session(int maze) => _sessions[maze];

        /// <summary>Le plus petit horodatage possible pour la prochaine action dans ce labyrinthe.</summary>
        public int MinTick(int maze)
        {
            int tick = _clocks[maze].MinTick;
            var last = LastInputOf(maze);
            if (last != null) tick = Math.Max(tick, last.Tick + PvpConfig.MinInputGapTicks);
            return Math.Max(tick, LastTick);
        }

        RelayInput LastInputOf(int maze)
        {
            for (int i = _inputs.Count - 1; i >= 0; i--)
                if (_inputs[i].Maze == maze) return _inputs[i];
            return null;
        }

        /// <summary>
        /// Joue une action du coureur de <paramref name="maze"/> à <paramref name="tick"/>. Null (et <see cref="Invalid"/>)
        /// hors de son tour, après la fin ou dans le désordre ; un mur renvoie un pas bloqué, ni joué ni enregistré.
        /// </summary>
        public StepResult? Apply(int maze, PlayerAction action, int tick)
        {
            if (IsOver || maze != ActiveMaze || tick < LastTick) { Invalid = true; return null; }
            var session = _sessions[maze];
            var before = session.State;
            var r = session.Apply(action);
            if (r.Has(StepFlags.Blocked)) return r;

            if (!_clocks[maze].Accept(tick, RunTiming.MinGapMs(session.Level, before, action, r))) TooFast = true;
            _inputs.Add(new RelayInput { Tick = tick, Maze = maze, Direction = RunActions.Encode(action) });
            LastTick = tick;

            if (RunActions.MsOf(tick) > RelayConfig.TimeLimitMs) Lose(RelayDefeat.TimedOut);
            else if (session.Status == SessionStatus.Dead) Lose(RelayDefeat.Died);
            else if (r.SteppedOn == Goal)
            {
                _segmentEnds.Add(tick);
                Segment++;
                if (Segment >= Map.Segments) Status = RelayStatus.Finished;
                // Le coéquipier part au plus tôt quand la dalle est enfoncée et la caméra passée chez lui.
                else _clocks[ActiveMaze].NotBefore(_clocks[maze].EarliestMs + RelayConfig.HandoffMs);
            }
            return r;
        }

        /// <summary>Le joueur a quitté la partie (son duo perd), ou la limite de temps est passée sans arrivée.</summary>
        public void Lose(RelayDefeat why)
        {
            if (IsOver) return;
            Status = RelayStatus.Lost;
            Defeat = why;
        }

        /// <summary>
        /// Ce que le serveur retient d'un relais envoyé : l'issue, le temps et les étapes qu'il a rejoués lui-même. Null
        /// quand le relais est impossible (action refusée, hors de son tour, plus rapide que le jeu, arrivée annoncée mais
        /// pas atteinte).
        /// </summary>
        public static RelayRace Verify(RelayMap map, IList<RelayInput> inputs)
        {
            var race = new RelayRace(map);
            if (inputs == null || inputs.Count > PvpConfig.MaxInputs * 2) return null;
            foreach (var input in inputs)
            {
                if (input == null || !RunActions.TryDecode(input.Direction, out var action)) return null;
                var r = race.Apply(input.Maze, action, input.Tick);
                if (race.Invalid || !r.HasValue || r.Value.Has(StepFlags.Blocked)) return null;
            }
            if (race.TooFast) return null;
            return race;
        }
    }

    /// <summary>« Qui commence ? » : chaque coéquipier désigne un joueur (lui-même ou l'autre) après l'aperçu.</summary>
    public static class RelayVote
    {
        /// <summary>
        /// D'accord : celui qu'ils ont désigné. En désaccord (chacun dit « moi », ou chacun dit « lui ») : au hasard. Un
        /// seul vote : celui-là. Aucun : au hasard.
        /// </summary>
        public static string Resolve(string playerA, string playerB, string voteA, string voteB, Random rng)
        {
            bool okA = voteA == playerA || voteA == playerB, okB = voteB == playerA || voteB == playerB;
            if (okA && okB && voteA == voteB) return voteA;
            if (okA && !okB) return voteA;
            if (okB && !okA) return voteB;
            return rng.Next(2) == 0 ? playerA : playerB;
        }
    }

    /// <summary>Un duo simulé (quand aucun duo réel n'est trouvé et que le joueur accepte les bots).</summary>
    public static class RelayBots
    {
        /// <summary>
        /// Le relais complet d'un duo de bots d'Elo proche : chaque coureur joue ses étapes comme une personne
        /// (<see cref="HumanPlayer"/>), repart un instant après avoir été libéré, et peut mourir.
        /// </summary>
        public static RelayRace Play(RelayMap map, Random rng, int duoElo)
        {
            var runners = new HumanPlayer[RelayConfig.Mazes];
            for (int m = 0; m < runners.Length; m++)
                runners[m] = new HumanPlayer(HumanPlayer.SkillForElo(Math.Max(PvpConfig.MinElo, duoElo + rng.Next(-150, 151))), rng);

            var race = new RelayRace(map);
            while (!race.IsOver)
            {
                int maze = race.ActiveMaze;
                var runner = runners[maze];
                // Libéré : le temps de voir que c'est à soi (le premier part à la chute du brouillard, comme en duel).
                double reaction = race.Segment == 0 ? 0 : 250 + rng.NextDouble() * 500 * (1.2 - runner.Skill);
                double startMs = RunActions.MsOf(race.MinTick(maze)) + reaction - 150; // HumanPlayer ajoute son élan
                var leg = map.Mazes[maze].WithGoal(race.Goal);
                var (inputs, outcome, _) = runner.Play(leg, rng, race.Session(maze).State, Math.Max(0, startMs), RelayConfig.TimeLimitMs);
                foreach (var input in inputs)
                {
                    RunActions.TryDecode(input.Direction, out var action);
                    race.Apply(maze, action, Math.Max(input.Tick, race.MinTick(maze)));
                    if (race.IsOver || race.ActiveMaze != maze) break;
                }
                if (!race.IsOver && race.ActiveMaze == maze)
                    race.Lose(outcome == RunOutcome.Died ? RelayDefeat.Died : RelayDefeat.TimedOut);
            }
            return race;
        }
    }
}
