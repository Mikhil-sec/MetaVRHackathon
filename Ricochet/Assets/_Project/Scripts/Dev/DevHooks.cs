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

        /// <summary>Stages the Fever light wave from the creature (slow it down to capture it mid-sweep).</summary>
        public static string Wave(float speed = 3.2f)
        {
            var creature = Object.FindAnyObjectByType<Creature>();
            Vector3 at = creature != null ? creature.Center : Vector3.forward * 3f;
            RoomGlow.Instance.Wave(at, new Color(1.6f, 1.15f, 0.5f), speed, 0.16f, 9f);
            return "wave from " + at.ToString("F1");
        }

        public static string TimeLogSummary() => s_timeLog != null ? s_timeLog.Summary() : "not recording";

        /// <summary>
        /// Fires a full-power ballistic shot straight at the nearest unlit crystal of a type ("Gold", "Amp", "Bomb",
        /// "Prism"), leaving the rest of the board alone (so a Bomb has neighbours). Exercises the crystal types.
        /// </summary>
        public static string FireAtKind(string kind)
        {
            var board = Object.FindAnyObjectByType<BoardGenerator>();
            var sling = Object.FindAnyObjectByType<Sling>();
            var spark = Object.FindAnyObjectByType<Spark>();
            if (!System.Enum.TryParse(kind, true, out CrystalKind want)) return "unknown kind " + kind;
            Vector3 origin = sling.transform.position;
            Crystal target = null;
            float best = float.MaxValue;
            var active = board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c.Kind != want || c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                float d = (c.transform.position - origin).sqrMagnitude;
                if (d < best) { best = d; target = c; }
            }
            if (target == null) return "no " + want + " on the board";
            Vector3 p = target.transform.position;
            // Clear the lane: pop crystals near the flight line, but keep the target's neighbourhood (a Bomb's blast).
            int cleared = 0;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c == target || c.IsPopped) continue;
                Vector3 q = c.transform.position;
                if ((q - p).sqrMagnitude < 0.5f * 0.5f) continue;
                Vector3 seg = p - origin;
                float h = Mathf.Clamp01(Vector3.Dot(q - origin, seg) / seg.sqrMagnitude);
                if ((origin + seg * h - q).sqrMagnitude < 0.15f * 0.15f) { c.Pop(); cleared++; }
            }
            float speed = sling.VelocityFor(Vector3.forward, 1f).magnitude;
            Vector3 aim = p - origin;
            for (int k = 0; k < 2; k++)
            {
                float t = (p - origin).magnitude / speed;
                aim = p + Vector3.up * (0.5f * spark.GravityAcceleration * t * t) - origin;
            }
            return $"{want} at {Mathf.Sqrt(best):F2} m (cleared {cleared}), fired={sling.FireForTest(aim, 1f)}";
        }

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
