using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// The Halloween event: a seasonal act outside the campaign, open from 24 October to 7 November included (Paris time),
    /// hidden the rest of the year (progress kept). Its 10 tombs are stored (<see cref="TombArchive"/>, drawn once by
    /// LevelLab <c>--halloween</c>), so they never change with the generator. The same window rule runs in the game and on
    /// the server (golden item, wheel legendary).
    /// </summary>
    public static class Halloween
    {
        /// <summary>Stored tombs of the act, a text asset under Resources, in play order.</summary>
        public const string ResourcePath = "Tombs/halloween";

        public const int TombCount = 10;

        /// <summary>Number of its look (TombTheme, home theme), after the five campaign acts.</summary>
        public const int Theme = 6;

        /// <summary>Variants of the stored tombs start here, away from campaign runs and the endless ladders.</summary>
        public const int VariantBase = 2_000_000;

        // Cosmetics: the act's reward (free), the shop's (scarabs), the Treasure's (golden scarabs) and the scarab wheel's.
        public const string RewardSkin = "hw_hat_pumpkin";
        public const string ShopSkin = "hw_col_midnight";
        public const string GoldSkin = "gold_hw_witch";
        public const string WheelLegendary = "leg_hw_samhain";

        /// <summary>
        /// Campaign levels whose specs drew the tombs, easiest first (Facile then Normal), all past act 1 so each tomb costs a
        /// point of solo energy like the campaign. Used by the tool only: once stored, a tomb never depends on this again.
        /// </summary>
        static readonly string[] Sources = { "F2-1", "F3-4", "F4-2", "F5-3", "F3-9", "N2-2", "N3-5", "N4-4", "N5-6", "N4-9" };

        public static LevelId Source(int tomb, out int variant)
        {
            LevelId.TryParse(Sources[tomb], out var id);
            variant = VariantBase + tomb;
            return id;
        }

        /// <summary>The stored lines of a file, in file order (comments and blank lines skipped).</summary>
        public static List<string> Lines(string text)
        {
            var list = new List<string>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0 && line[0] != '#') list.Add(line);
            }
            return list;
        }

        /// <summary>Opens the event whatever the date: set by the game in the editor and test builds, never by the server.</summary>
        public static bool TestOpen;

        /// <summary>Open from 24 October 00:00 to 7 November 23:59:59, Paris time (always with <see cref="TestOpen"/>).</summary>
        public static bool IsOpen(DateTime utc)
        {
            if (TestOpen) return true;
            var paris = ParisTime(utc);
            return paris.Month == 10 && paris.Day >= 24 || paris.Month == 11 && paris.Day <= 7;
        }

        /// <summary>The next 24 October 00:00 Paris time, as a UTC instant (24 October is always in summer time: UTC+2).</summary>
        public static DateTime NextOpening(DateTime utc)
        {
            var paris = ParisTime(utc);
            int year = paris.Month > 10 || paris.Month == 10 && paris.Day >= 24 ? paris.Year + 1 : paris.Year;
            return new DateTime(year, 10, 23, 22, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Paris wall-clock time of a UTC instant, by the EU rule (summer time from the last Sunday of March to the last
        /// Sunday of October, both at 01:00 UTC). No time zone database: device and server ids differ, this does not.
        /// </summary>
        public static DateTime ParisTime(DateTime utc)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
            int y = utc.Year;
            bool summer = utc >= LastSunday(y, 3).AddHours(1) && utc < LastSunday(y, 10).AddHours(1);
            return utc.AddHours(summer ? 2 : 1);
        }

        static DateTime LastSunday(int year, int month)
        {
            var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            return d.AddDays(-(int)d.DayOfWeek);
        }
    }
}
