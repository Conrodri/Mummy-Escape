using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>End-of-level recap: what is shown on the results screen, saved, shared and sent to the leaderboard.</summary>
    [Serializable]
    public sealed class LevelResult
    {
        public LevelId Level;
        public bool Won;
        public int Moves;
        public int Interactions;
        public int HpLeft;
        public int MaxHp;
        public int Par;
        public int TrapsTriggered;

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
        /// Leaderboard score, lower is better: moves first, then HP lost, then interactions as tie breakers.
        /// </summary>
        public long LeaderboardScore => (long)Moves * 10000 + (MaxHp - HpLeft) * 1000 + Math.Min(Interactions, 999);

        public static (int moves, int hpLost, int interactions) DecodeScore(long score) =>
            ((int)(score / 10000), (int)(score % 10000 / 1000), (int)(score % 1000));

        public string ShareText(string gameUrl) =>
            $"🏺 Mummy Escape — Niveau {Level}\n" +
            $"Évadé en {Moves} coups (par {Par}) {new string('★', Stars)}{new string('☆', 3 - Stars)}\n" +
            $"{Interactions} interactions · {HpLeft}/{MaxHp} PV restants\n" +
            $"Feras-tu mieux ? {gameUrl}";
    }

    /// <summary>Per level best record (saved locally and mirrored online).</summary>
    [Serializable]
    public sealed class LevelRecord
    {
        public string Key;
        public int BestMoves;
        public int BestStars;
        public int BestHpLeft;
        public int Completions;
        public int Deaths;

        public bool Merge(LevelResult r)
        {
            if (!r.Won) { Deaths++; return false; }
            Completions++;
            bool improved = BestMoves == 0 || r.Moves < BestMoves || (r.Moves == BestMoves && r.HpLeft > BestHpLeft);
            if (improved) { BestMoves = r.Moves; BestHpLeft = r.HpLeft; }
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
