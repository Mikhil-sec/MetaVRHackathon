using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Ascension (CONCEPT section 3, Meta): every run you complete makes the next one harder, up to tier 5 - stronger
    /// creatures (more HP, harder attacks and guards), the same rifts. Automatic, no menu: losing never lowers it, and
    /// the Daily Rift always plays at tier 0 so its leaderboard stays fair. The Editor plays tier 0 unless
    /// <c>DevHooks.Ascension(n)</c> (the desktop tests expect a plain run).
    /// </summary>
    public static class Ascension
    {
        public const int Max = 5;
        const string Key = "Ricochet.Ascension";
        public const string DevKey = "Ricochet.DevAscension";

        /// <summary>The highest tier reached so far.</summary>
        public static int Unlocked => Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Max);

        /// <summary>The tier a fresh (non-daily) run starts at.</summary>
        public static int ForNewRun => Application.isEditor ? Mathf.Clamp(PlayerPrefs.GetInt(DevKey, 0), 0, Max) : Unlocked;

        /// <summary>A run at this tier was completed: the next one climbs. Returns the new tier (or the same at the top).</summary>
        public static int Completed(int tier)
        {
            int next = Mathf.Min(Max, tier + 1);
            if (next > Unlocked)
            {
                PlayerPrefs.SetInt(Key, next);
                PlayerPrefs.Save();
            }
            if (Application.isEditor && PlayerPrefs.GetInt(DevKey, 0) > 0) PlayerPrefs.SetInt(DevKey, next);
            return next;
        }

        /// <summary>Creature HP at a tier (x1 .. x1.5).</summary>
        public static float HpScale(int tier) => 1f + 0.1f * tier;

        /// <summary>Extra points on attacks and guards at a tier (+0 .. +2).</summary>
        public static int MoveBonus(int tier) => tier / 2;
    }
}
