// Mummy Rush — l'énergie de combat : une partie la dépense, elle revient d'un point toutes les 6 minutes.
using System;

namespace MummyEscape.Pvp
{
    public static class EnergyConfig
    {
        /// <summary>Partie solo (à partir de l'acte 2 ; l'acte 1 est libre). Gardée par l'appareil.</summary>
        public const int SoloMax = 10;
        /// <summary>Match en ligne (duel 1v1 ou 2v2, une seule réserve). Gardée par le serveur.</summary>
        public const int PvpMax = 3;
        /// <summary>Un point revient toutes les 6 minutes : 10 points en une heure.</summary>
        public const long RegenMs = 6 * 60_000L;
        public const int SoloAdRefill = 3;
        public const int PvpAdRefill = 1;
        /// <summary>Pubs qui rendent de l'énergie, par jour (UTC) et par réserve.</summary>
        public const int AdRefillsPerDay = 5;
    }

    /// <summary>
    /// Une réserve d'énergie : <see cref="Used"/> points dépensés (0 : pleine), et le moment d'où court le prochain retour.
    /// Ce sens « dépensé » fait qu'une donnée absente (ancien compte) donne une réserve pleine.
    /// </summary>
    [Serializable]
    public class EnergyMeter
    {
        public int Used;
        public long AtUnixMs;
        /// <summary>Jour (UTC, "2026-10-06") des pubs comptées dans <see cref="Ads"/>.</summary>
        public string AdsDay;
        public int Ads;
    }

    public static class Energy
    {
        /// <summary>
        /// Rend les points revenus depuis <see cref="EnergyMeter.AtUnixMs"/>. Une horloge qui recule ne rend rien : le compte
        /// repart de maintenant.
        /// </summary>
        public static void Regen(EnergyMeter m, long nowMs)
        {
            if (m.Used <= 0) { m.Used = 0; m.AtUnixMs = nowMs; return; }
            if (nowMs < m.AtUnixMs) { m.AtUnixMs = nowMs; return; }
            long back = (nowMs - m.AtUnixMs) / EnergyConfig.RegenMs;
            if (back <= 0) return;
            if (back >= m.Used) { m.Used = 0; m.AtUnixMs = nowMs; }
            else { m.Used -= (int)back; m.AtUnixMs += back * EnergyConfig.RegenMs; }
        }

        public static int Left(EnergyMeter m, int max, long nowMs)
        {
            Regen(m, nowMs);
            return Math.Max(0, max - m.Used);
        }

        /// <summary>Temps avant le prochain point (0 : réserve pleine).</summary>
        public static long NextInMs(EnergyMeter m, long nowMs)
        {
            Regen(m, nowMs);
            return m.Used <= 0 ? 0 : Math.Max(0, EnergyConfig.RegenMs - (nowMs - m.AtUnixMs));
        }

        /// <summary>Dépense un point ; false quand la réserve est vide.</summary>
        public static bool Spend(EnergyMeter m, int max, long nowMs)
        {
            Regen(m, nowMs);
            if (m.Used >= max) return false;
            if (m.Used == 0) m.AtUnixMs = nowMs;
            m.Used++;
            return true;
        }

        public static string Day(long nowMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        public static int AdsLeft(EnergyMeter m, long nowMs) =>
            m.AdsDay == Day(nowMs) ? Math.Max(0, EnergyConfig.AdRefillsPerDay - m.Ads) : EnergyConfig.AdRefillsPerDay;

        /// <summary>Rend <paramref name="points"/> après une pub ; false quand les pubs du jour sont épuisées.</summary>
        public static bool Refill(EnergyMeter m, int points, long nowMs)
        {
            if (AdsLeft(m, nowMs) <= 0) return false;
            string day = Day(nowMs);
            if (m.AdsDay != day) { m.AdsDay = day; m.Ads = 0; }
            m.Ads++;
            Regen(m, nowMs);
            m.Used = Math.Max(0, m.Used - points);
            if (m.Used == 0) m.AtUnixMs = nowMs;
            return true;
        }

        /// <summary>"4:12" avant le prochain point.</summary>
        public static string Clock(long ms)
        {
            long s = (ms + 999) / 1000;
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
