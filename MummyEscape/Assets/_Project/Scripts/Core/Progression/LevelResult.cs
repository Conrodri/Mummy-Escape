using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>End-of-level recap: what is shown on the results screen, saved, shared and sent to the leaderboard.</summary>
    [Serializable]
    public sealed class LevelResult
    {
        public LevelId Level;
        /// <summary>Which maze of the level was played (every run draws a new one).</summary>
        public int Variant;
        public bool Won;
        public int Moves;
        public int Interactions;
        public int HpLeft;
        public int MaxHp;
        /// <summary>Optimal move count of this maze. Never shown as such: only the gap to it is.</summary>
        public int Par;
        public int TrapsTriggered;
        /// <summary>Play time after the map preview, in milliseconds (second sort key of the leaderboard).</summary>
        public int TimeMs;

        /// <summary>Moves spent above the optimal route of this maze (0 = perfect run).</summary>
        public int OverPar => Math.Max(0, Moves - Par);

        /// <summary>3 stars at par, 2 stars within +50%, 1 star for escaping at all.</summary>
        public int Stars
        {
            get
            {
                if (!Won) return 0;
                if (Moves <= Par) return 3;
                if (Moves * 2 <= Par * 3) return 2;
                return 1;
            }
        }

        /// <summary>Bump when the score encoding changes: it is part of the leaderboard ids.</summary>
        public const int ScoreFormat = 2;
        /// <summary>Time slots per move over par in the encoded score (time capped just under 27.8 hours).</summary>
        const long TimeSlots = 100_000_000;

        /// <summary>
        /// Leaderboard score, lower is better. Every run is a different maze, so runs are ranked by the moves spent
        /// above that maze's optimal route first, then by play time.
        /// </summary>
        public long LeaderboardScore => EncodeScore(OverPar, TimeMs);

        public static long EncodeScore(int overPar, int timeMs) =>
            Math.Min(Math.Max(overPar, 0), 99999) * TimeSlots + TimeKey(timeMs);

        public static (int overPar, int timeMs) DecodeScore(long score)
        {
            int time = (int)(score % TimeSlots);
            return ((int)(score / TimeSlots), time >= TimeSlots - 1 ? 0 : time);
        }

        /// <summary>Unknown times (0, legacy records) sort after every measured time.</summary>
        static long TimeKey(int timeMs) => timeMs <= 0 ? TimeSlots - 1 : Math.Min(timeMs, TimeSlots - 2);

        /// <summary>Negative when run A beats run B: fewer moves over par, then faster.</summary>
        public static int CompareRuns(int overParA, int timeMsA, int overParB, int timeMsB) =>
            EncodeScore(overParA, timeMsA).CompareTo(EncodeScore(overParB, timeMsB));

        /// <summary>"42,3 s" or "1:05,3" (tenths, truncated); "—" when unknown. Separator follows the language.</summary>
        public static string FormatTime(int timeMs)
        {
            if (timeMs <= 0) return "—";
            int tenths = timeMs / 100, s = tenths / 10, t = tenths % 10;
            string sep = CoreText.DecimalSeparator;
            return s < 60 ? $"{s}{sep}{t} s" : $"{s / 60}:{s % 60:00}{sep}{t}";
        }

        /// <summary>Leaderboard cell: "parfait · 42,3 s".</summary>
        public static string FormatScore(int overPar, int timeMs) =>
            timeMs > 0 ? $"{FormatOverPar(overPar)} · {FormatTime(timeMs)}" : FormatOverPar(overPar);

        /// <summary>"parfait" or "+3 coups": how far from the optimal route, without revealing the route length.</summary>
        public static string FormatOverPar(int overPar) =>
            overPar <= 0 ? CoreText.T("parfait") : CoreText.F(overPar > 1 ? "+{0} coups" : "+{0} coup", overPar);

        public string ShareText(string gameUrl) =>
            "🏺 Mummy Escape — " + CoreText.F("Niveau {0}", Level) + "\n" +
            CoreText.F("Évadé en {0} coups, {1} ({2})", Moves, FormatTime(TimeMs), FormatOverPar(OverPar)) +
            $" {new string('★', Stars)}{new string('☆', 3 - Stars)}\n" +
            CoreText.F("{0} interactions · {1}/{2} PV restants", Interactions, HpLeft, MaxHp) + "\n" +
            CoreText.F("Feras-tu mieux ? {0}", gameUrl);
    }

    /// <summary>Per level best record (saved locally and mirrored online).</summary>
    [Serializable]
    public sealed class LevelRecord
    {
        public string Key;
        /// <summary>Best gap to the optimal route over all mazes played (see <see cref="HasBest"/>; -1 = unknown, legacy save).</summary>
        public int BestOverPar;
        /// <summary>Moves of the best run (informative only: mazes differ between runs).</summary>
        public int BestMoves;
        public int BestStars;
        public int BestHpLeft;
        /// <summary>Play time of the best run in milliseconds (0 = unknown, legacy save).</summary>
        public int BestTimeMs;
        public int Completions;
        public int Deaths;
        /// <summary>Mazes drawn so far for this level: the next run plays maze number Runs (a new tomb every time).</summary>
        public int Runs;

        public bool HasBest => Completions > 0 && BestOverPar >= 0;

        public bool Merge(LevelResult r)
        {
            if (!r.Won) { Deaths++; return false; }
            bool first = !HasBest;
            Completions++;
            bool improved = first || LevelResult.CompareRuns(r.OverPar, r.TimeMs, BestOverPar, BestTimeMs) < 0;
            if (improved) { BestOverPar = r.OverPar; BestTimeMs = r.TimeMs; BestMoves = r.Moves; BestHpLeft = r.HpLeft; }
            BestStars = Math.Max(BestStars, r.Stars);
            return improved;
        }

        /// <summary>Combines the record of the same level from another device (cloud save): best of both.</summary>
        public void MergeWith(LevelRecord o)
        {
            if (o == null || o.Key != Key) return;
            bool theirsBetter = o.HasBest && (!HasBest || LevelResult.CompareRuns(o.BestOverPar, o.BestTimeMs, BestOverPar, BestTimeMs) < 0);
            if (theirsBetter) { BestOverPar = o.BestOverPar; BestTimeMs = o.BestTimeMs; BestMoves = o.BestMoves; BestHpLeft = o.BestHpLeft; }
            BestStars = Math.Max(BestStars, o.BestStars);
            Completions = Math.Max(Completions, o.Completions);
            Deaths = Math.Max(Deaths, o.Deaths);
            // Never replay a maze already drawn on either device.
            Runs = Math.Max(Runs, o.Runs);
        }
    }

    /// <summary>Unlock rules: level N+1 opens after escaping level N; the next act opens after the act's last level.</summary>
    public static class Progression
    {
        public static bool IsUnlocked(LevelId id, Func<LevelId, bool> isCompleted)
        {
            if (id.Act == 1 && id.Index == 1) return true;
            var prev = Previous(id);
            return prev.HasValue && isCompleted(prev.Value);
        }

        public static LevelId? Previous(LevelId id)
        {
            if (id.Index > 1) return new LevelId(id.Act, id.Index - 1);
            if (id.Act > 1) return new LevelId(id.Act - 1, DifficultyTable.GetAct(id.Act - 1).Levels);
            return null;
        }

        public static LevelId? Next(LevelId id)
        {
            if (id.Index < DifficultyTable.GetAct(id.Act).Levels) return new LevelId(id.Act, id.Index + 1);
            if (id.Act < DifficultyTable.ActCount) return new LevelId(id.Act + 1, 1);
            return null;
        }

        /// <summary>Scarab coins awarded (shop currency): only newly earned stars pay out, so replays can't be farmed.</summary>
        public static int CoinsFor(LevelResult r, int previousBestStars) =>
            r.Won ? Math.Max(0, r.Stars - previousBestStars) * 10 + 2 : 0;

        public static int TotalStars(IEnumerable<LevelRecord> records)
        {
            int total = 0;
            foreach (var r in records) total += r.BestStars;
            return total;
        }
    }
}
