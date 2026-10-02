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

        /// <summary>
        /// Leaderboard score, lower is better. Every run is a different maze, so runs are compared by the moves spent
        /// above that maze's optimal route, then HP lost, then interactions as tie breakers.
        /// </summary>
        public long LeaderboardScore => EncodeScore(OverPar, MaxHp - HpLeft, Interactions);

        public static long EncodeScore(int overPar, int hpLost, int interactions) =>
            (long)Math.Min(overPar, 99999) * 10000 + Math.Min(hpLost, 9) * 1000 + Math.Min(interactions, 999);

        public static (int overPar, int hpLost, int interactions) DecodeScore(long score) =>
            ((int)(score / 10000), (int)(score % 10000 / 1000), (int)(score % 1000));

        /// <summary>"parfait" or "+3 coups": how far from the optimal route, without revealing the route length.</summary>
        public static string FormatOverPar(int overPar) =>
            overPar <= 0 ? "parfait" : $"+{overPar} coup{(overPar > 1 ? "s" : "")}";

        public string ShareText(string gameUrl) =>
            $"🏺 Mummy Escape — Niveau {Level}\n" +
            $"Évadé en {Moves} coups ({FormatOverPar(OverPar)}) {new string('★', Stars)}{new string('☆', 3 - Stars)}\n" +
            $"{Interactions} interactions · {HpLeft}/{MaxHp} PV restants\n" +
            $"Feras-tu mieux ? {gameUrl}";
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
            bool improved = first || r.OverPar < BestOverPar || (r.OverPar == BestOverPar && r.HpLeft > BestHpLeft);
            if (improved) { BestOverPar = r.OverPar; BestMoves = r.Moves; BestHpLeft = r.HpLeft; }
            BestStars = Math.Max(BestStars, r.Stars);
            return improved;
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
