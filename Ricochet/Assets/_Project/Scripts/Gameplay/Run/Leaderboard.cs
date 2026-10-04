using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Oculus.Platform;
#endif

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Meta Platform leaderboards (CONCEPT section 7 "Should": Daily Rift with leaderboard). Two boards, created in the
    /// Developer Dashboard (Engagement > Leaderboards; Higher is better, client authoritative):
    ///   "daily_rift" - today's Daily Rift score. One attempt per day, so each entry is force-written (it replaces
    ///                  yesterday's); the date rides along as extra data.
    ///   "best_run"   - the best score of any run (kept only when it improves).
    /// Device builds with an App ID only: until the Dashboard app exists (or in the Editor) every call just logs.
    /// </summary>
    public static class Leaderboard
    {
        public const string Daily = "daily_rift";
        public const string BestRun = "best_run";

        static bool _started, _ready;

        /// <summary>Starts the Platform SDK and checks the entitlement, once per launch.</summary>
        public static void Init()
        {
            if (_started) return;
            _started = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (string.IsNullOrEmpty(PlatformSettings.MobileAppID))
                {
                    Debug.Log("[Ricochet] Platform: no App ID yet, leaderboards offline");
                    return;
                }
                Core.AsyncInitialize().OnComplete(init =>
                {
                    if (init.IsError)
                    {
                        Debug.LogWarning($"[Ricochet] Platform init failed: {init.GetError().Message}");
                        return;
                    }
                    Entitlements.IsUserEntitledToApplication().OnComplete(check =>
                    {
                        _ready = !check.IsError;
                        Debug.Log($"[Ricochet] Platform ready: entitled={_ready}");
                    });
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ricochet] Platform init threw: {e.Message}");
            }
#endif
        }

        /// <summary>Writes a score. <paramref name="date"/> (yyyymmdd) marks a daily entry; 0 for others.</summary>
        public static void Submit(string board, long score, int date = 0)
        {
            if (!_ready)
            {
                Debug.Log($"[Ricochet] Leaderboard {board}: {score} (offline)");
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            byte[] extra = date > 0 ? System.Text.Encoding.UTF8.GetBytes(date.ToString()) : null;
            Leaderboards.WriteEntry(board, score, extra, board == Daily).OnComplete(msg =>
                Debug.Log(msg.IsError
                    ? $"[Ricochet] Leaderboard {board} write failed: {msg.GetError().Message}"
                    : $"[Ricochet] Leaderboard {board}: {score} (updated={msg.Data})"));
#endif
        }
    }
}
