using Ricochet.Gameplay;
using UnityEngine;

namespace Ricochet.Dev
{
    /// <summary>Play Mode automation helpers for Editor evals (desktop and simulator testing).</summary>
    public static class DevHooks
    {
        static TimeLog s_timeLog;

        /// <summary>Start (or restart) recording the time scale every frame.</summary>
        public static string StartTimeLog()
        {
            if (s_timeLog != null) Object.Destroy(s_timeLog.gameObject);
            s_timeLog = new GameObject("TimeLog").AddComponent<TimeLog>();
            return "recording";
        }

        /// <summary>Freeze game time (near-still) after real seconds, for captures of fast effects. Unfreeze() restores.</summary>
        public static string FreezeAfter(float seconds, float scale = 0.001f)
        {
            if (s_timeLog == null) StartTimeLog();
            s_timeLog.FreezeAfter(seconds, scale);
            return "freeze scheduled";
        }

        /// <summary>
        /// Freeze at a gameplay moment: "motes" (light flying into the creature), "bolt" (its attack in flight),
        /// "windup" (the creature drawing back), "drama" (last-crystal / lethal slow motion).
        /// </summary>
        public static string FreezeOn(string moment, float scale = 0.001f)
        {
            if (s_timeLog == null) StartTimeLog();
            var motes = Object.FindAnyObjectByType<LightMotes>();
            var director = Object.FindAnyObjectByType<ShotDirector>();
            var drama = Object.FindAnyObjectByType<ShotDrama>();
            float seen = 0f;
            System.Func<bool> when = moment switch
            {
                "motes" => () => motes.InFlightOf(LightMotes.Kind.Damage) > 0 && (seen += Time.deltaTime) > 0.25f,
                "bolt" => () => motes.InFlightOf(LightMotes.Kind.Bolt) + motes.InFlightOf(LightMotes.Kind.Hex) > 0
                                && (seen += Time.deltaTime) > 0.3f,
                "drama" => () => drama.Active && (seen += Time.unscaledDeltaTime) > 0.3f,
                _ => null,
            };
            if (when == null) return "unknown moment";
            s_timeLog.FreezeWhen(when, scale);
            return "freeze armed: " + moment;
        }

        /// <summary>Fire the hero shot: the relaxed straight shot the board guarantees a cluster for.</summary>
        public static string FireHero()
        {
            var sling = Object.FindAnyObjectByType<Sling>();
            var board = Object.FindAnyObjectByType<BoardGenerator>();
            var pa = Object.FindAnyObjectByType<Room.PlayArea>();
            Vector3 dir = board.HeroVelocity.sqrMagnitude > 0f ? board.HeroVelocity : ShotDirector.HeroDirection(pa.Seat);
            return "hero fired=" + sling.FireForTest(dir, ShotDirector.HeroPullAmount);
        }

        public static string Unfreeze()
        {
            var warp = Object.FindAnyObjectByType<TimeWarp>();
            if (warp != null) { warp.Unfreeze(); warp.Release(); }
            return "released";
        }

        public static string TimeLogSummary() => s_timeLog != null ? s_timeLog.Summary() : "not recording";

        /// <summary>
        /// Pops every crystal but the one nearest the sling's forward line, then fires a full-power shot aimed
        /// straight at it (ballistic, with the Spark's own gravity). Exercises the last-crystal drama.
        /// Pass missBy (meters) to aim beside it. Returns a short report, or why it could not fire.
        /// </summary>
        public static string FireAtLastCrystal(float missBy = 0f)
        {
            var board = Object.FindAnyObjectByType<BoardGenerator>();
            var sling = Object.FindAnyObjectByType<Sling>();
            var spark = Object.FindAnyObjectByType<Spark>();
            if (board == null || sling == null || spark == null) return "missing board/sling/spark";

            Vector3 origin = sling.transform.position;
            Vector3 forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            Crystal keep = null;
            float best = float.MaxValue;
            var active = board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c.IsPopped) continue;
                Vector3 to = c.transform.position - origin;
                float score = Vector3.Angle(forward, to) + to.magnitude * 2f;
                if (score < best) { best = score; keep = c; }
            }
            if (keep == null) return "no crystals";
            for (int i = 0; i < active.Count; i++)
                if (active[i] != keep && !active[i].IsPopped) active[i].Pop();

            // Aim above the target by the drop over the flight time; two iterations are plenty at this speed.
            Vector3 target = keep.transform.position;
            float speed = sling.VelocityFor(Vector3.forward, 1f).magnitude;
            Vector3 aim = target - origin;
            for (int k = 0; k < 2; k++)
            {
                float t = (target - origin).magnitude / speed;
                aim = target + Vector3.up * (0.5f * spark.GravityAcceleration * t * t) - origin;
            }
            aim += Vector3.Cross(Vector3.up, aim).normalized * missBy; // sideways miss, in meters at the target
            bool fired = sling.FireForTest(aim, 1f);
            return $"kept {keep.name} at {(target - origin).magnitude:F2} m, fired={fired}";
        }
    }
}
