using System;
using System.Globalization;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The Daily Rift (CONCEPT section 8, Week 5): the first run of each day is the same run for everyone that day.
    /// Seeded by the UTC date (a shared day for a shared leaderboard): the reward offers, the boards (per encounter, so a
    /// room plays the same way all day) and a starting gift of one Spark type and one relic. One attempt per day; the
    /// best daily score is kept locally and written to the "daily_rift" leaderboard once the Platform SDK is live.
    /// Zero menus: it simply is the first rift you open today, in dawn gold, with a short banner.
    /// The Editor never starts one unless <c>DevHooks.Daily(true)</c> (the desktop tests expect a plain first run).
    /// </summary>
    public static class DailyRift
    {
        const string PlayedKey = "Ricochet.DailyPlayed";
        const string BestKeyPrefix = "Ricochet.DailyBest.";
        public const string DevKey = "Ricochet.DevDaily";

        /// <summary>Today's UTC date as yyyymmdd.</summary>
        public static int Today
        {
            get
            {
                var d = DateTime.UtcNow;
                return d.Year * 10000 + d.Month * 100 + d.Day;
            }
        }

        /// <summary>Today's Daily Rift hasn't been started yet (and, in the Editor, the dev switch is on).</summary>
        public static bool Due =>
            (!Application.isEditor || PlayerPrefs.GetInt(DevKey, 0) == 1) && PlayerPrefs.GetInt(PlayedKey, 0) != Today;

        /// <summary>A well-mixed seed from the date, so neighbouring days differ completely.</summary>
        public static int Seed(int date)
        {
            unchecked
            {
                uint h = (uint)date * 2654435761u;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>The day's starting gift: one Spark type and one relic, the same for everyone.</summary>
        public static (Reward spark, Reward relic) Gift(int date)
        {
            var rng = new System.Random(Seed(date) ^ 0x5bd1e995);
            return (Upgrades.GiftSpark(rng), Upgrades.GiftRelic(rng));
        }

        /// <summary>Starting counts as today's attempt (one per day), whatever happens next.</summary>
        public static void MarkPlayed(int date)
        {
            PlayerPrefs.SetInt(PlayedKey, date);
            PlayerPrefs.Save();
        }

        public static int Best(int date) => PlayerPrefs.GetInt(BestKeyPrefix + date, 0);

        /// <summary>Keeps the day's best and sends the score to the leaderboard. True when it is a new best.</summary>
        public static bool Record(int date, int score)
        {
            Leaderboard.Submit(Leaderboard.Daily, score, date);
            if (score <= Best(date)) return false;
            PlayerPrefs.SetInt(BestKeyPrefix + date, score);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>"Oct 4" style label for the banner.</summary>
        public static string Label(int date) =>
            new DateTime(date / 10000, date / 100 % 100, date % 100).ToString("MMM d", CultureInfo.InvariantCulture);

        /// <summary>Dev: allow (or stop) the daily in the Editor, and forget today's attempt so the next run is one.</summary>
        public static void DevEnable(bool on)
        {
            PlayerPrefs.SetInt(DevKey, on ? 1 : 0);
            PlayerPrefs.DeleteKey(PlayedKey);
            PlayerPrefs.Save();
        }
    }
}
