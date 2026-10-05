// Mummy Escape PvP — la course d'un duel : le tombeau tiré de la graine, l'encodage des actions et leur relecture.
// Même graine + mêmes actions = même course, sur le téléphone du joueur, chez son adversaire et sur le serveur.
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>Le tombeau d'un duel, tiré de la graine donnée par le serveur.</summary>
    public static class PvpArena
    {
        /// <summary>
        /// Niveau dont le duel reprend le contrat : palier du milieu (niveaux 4 à 7) des actes 1 à 3, ouverts à tous ceux
        /// qui ont débloqué le PvP, et assez courts pour une course de 3 minutes.
        /// </summary>
        public static LevelId LevelFor(int seed)
        {
            uint u = (uint)seed;
            return new LevelId(1 + (int)(u % 3), 4 + (int)(u / 3 % 4));
        }

        /// <summary>Graine du générateur : jamais celle d'une partie solo, et liée à la version du générateur.</summary>
        public static ulong GeneratorSeed(int seed) =>
            Pcg32.Hash(Pcg32.Hash(0x50565055UL /* "PVPU" */, (ulong)DifficultyTable.GeneratorVersion), (ulong)(uint)seed);

        public static Level Generate(int seed) => LevelGenerator.Generate(DifficultyTable.Spec(LevelFor(seed)), GeneratorSeed(seed));
    }

    /// <summary>Actions du jeu ↔ <see cref="RunInput.Direction"/> : 1-4 déplacement (haut, droite, bas, gauche), 5-8 désamorçage.</summary>
    public static class RunActions
    {
        public static int Encode(PlayerAction a) => (int)a.Dir + 1 + (a.Kind == ActionKind.Disarm ? 4 : 0);

        public static bool TryDecode(int code, out PlayerAction action)
        {
            action = default;
            if (code < 1 || code > 8) return false;
            var dir = (Dir)((code - 1) % 4);
            action = code > 4 ? PlayerAction.Disarm(dir) : PlayerAction.Move(dir);
            return true;
        }

        public static int TickOf(int elapsedMs) => (int)((long)elapsedMs * PvpConfig.TickRate / 1000);
        public static int MsOf(int tick) => (int)((long)tick * 1000 / PvpConfig.TickRate);
    }

    /// <summary>Distance à la sortie : 0 au départ, 1 à la sortie, en coups restants sur le chemin idéal (portes, portails compris).</summary>
    public static class PvpProgress
    {
        public static float Of(Level level, RuleState state)
        {
            int par = level.Solution?.Moves ?? 0;
            if (par <= 0) return 0f;
            var rest = Solver.Solve(level, state, SolverOptions.Default);
            if (rest == null) return 0f;
            return Math.Max(0f, Math.Min(1f, 1f - rest.Moves / (float)par));
        }
    }

    /// <summary>
    /// Rejoue une course action par action avec les règles du jeu (<see cref="GameSession"/>) : le fantôme à l'écran,
    /// et la vérification du serveur, qui ne croit que ce qu'il a rejoué lui-même.
    /// </summary>
    public sealed class RunReplay
    {
        readonly IList<RunInput> _inputs;
        int _next;
        readonly RunClock _clock = new RunClock(); // jamais plus vite que les animations du jeu

        public GameSession Session { get; }
        /// <summary>Une action enregistrée a été refusée par les règles : la course a été trafiquée.</summary>
        public bool Invalid { get; private set; }
        /// <summary>
        /// Une action est partie avant la fin de l'animation de la précédente (<see cref="RunTiming"/>) : impossible avec
        /// le jeu, la course a été accélérée. Le fantôme la rejoue quand même ; le serveur la refuse.
        /// </summary>
        public bool TooFast { get; private set; }
        /// <summary>Instant (en pas) de la dernière action jouée.</summary>
        public int LastTick { get; private set; }
        /// <summary>Situation juste avant le coup fatal (ou la situation actuelle) : c'est elle que mesure la progression.</summary>
        public RuleState LastAlive { get; private set; }
        public bool IsOver => Session.Status != SessionStatus.Playing;
        public bool HasPendingInputs => _next < _inputs.Count;

        public RunReplay(Level level, IList<RunInput> inputs)
        {
            Session = new GameSession(level);
            _inputs = inputs ?? new List<RunInput>();
            LastAlive = Session.State;
        }

        /// <summary>Joue toutes les actions horodatées jusqu'à <paramref name="tick"/> inclus ; renvoie le dernier pas joué.</summary>
        public StepResult? AdvanceTo(int tick)
        {
            StepResult? last = null;
            while (_next < _inputs.Count && _inputs[_next].Tick <= tick && !Invalid)
            {
                var input = _inputs[_next++];
                if (IsOver || !RunActions.TryDecode(input.Direction, out var action)) { Invalid = true; break; }
                var before = Session.State;
                var r = Session.Apply(action);
                if (r.Has(StepFlags.Blocked)) { Invalid = true; break; }
                if (!_clock.Accept(input.Tick, RunTiming.MinGapMs(Session.Level, before, action, r))) TooFast = true;
                LastTick = input.Tick;
                LastAlive = Session.Status == SessionStatus.Dead ? before : Session.State;
                last = r;
            }
            return last;
        }

        public float Progress => Session.Status == SessionStatus.Won ? 1f : PvpProgress.Of(Session.Level, LastAlive);

        /// <summary>
        /// Ce que le serveur retient d'une course envoyée : l'issue, le temps et la progression qu'il a rejoués, pas ceux
        /// que le client annonce. Null quand la course est impossible (action refusée, sortie annoncée mais pas atteinte…).
        /// </summary>
        public static RunSubmission Verify(Level level, RunSubmission run)
        {
            if (run.Outcome == RunOutcome.Abandoned) return run;
            var replay = new RunReplay(level, run.Inputs);
            replay.AdvanceTo(int.MaxValue);
            if (replay.Invalid || replay.TooFast || RunActions.MsOf(replay.LastTick) > PvpConfig.TimeLimitMs) return null;
            var verified = new RunSubmission { MatchId = run.MatchId, Inputs = run.Inputs };
            var status = replay.Session.Status;
            if (status == SessionStatus.Won)
            {
                verified.Outcome = RunOutcome.Finished;
                verified.TimeMs = RunActions.MsOf(replay.LastTick);
                verified.Progress = 1f;
                if (verified.TimeMs > PvpConfig.TimeLimitMs || verified.TimeMs < PvpConfig.MinPlausibleTimeMs) return null;
                return verified;
            }
            if (run.Outcome == RunOutcome.Finished) return null; // annoncée sortie, mais le tombeau dit le contraire
            verified.Progress = replay.Progress;
            if (status == SessionStatus.Dead)
            {
                verified.Outcome = RunOutcome.Died;
                verified.TimeMs = RunActions.MsOf(replay.LastTick);
                return verified;
            }
            if (run.Outcome == RunOutcome.Died) return null;
            // Ni sortie ni mort : la limite de temps.
            verified.Outcome = RunOutcome.TimedOut;
            verified.TimeMs = PvpConfig.TimeLimitMs;
            return verified;
        }
    }
}
