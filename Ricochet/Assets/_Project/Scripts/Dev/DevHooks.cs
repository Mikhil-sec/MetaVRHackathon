using Ricochet.Gameplay;
using UnityEngine;

namespace Ricochet.Dev
{
    /// <summary>Play Mode automation helpers for Editor evals (desktop and simulator testing).</summary>
    public static class DevHooks
    {
        static TimeLog s_timeLog;
        const string FreezeNextKey = "Ricochet.Dev.FreezeNextPlay";

        /// <summary>
        /// Call outside Play: the next Play arms <see cref="FreezeOn"/>(moment) as soon as the scene loads, for moments
        /// that happen before an eval can reach a freshly started Play session (the cold open's "seeds", "arrive").
        /// </summary>
        public static string FreezeNextPlay(string moment)
        {
            PlayerPrefs.SetString(FreezeNextKey, moment);
            PlayerPrefs.Save();
            return "next Play freezes on " + moment;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ArmFreezeNextPlay()
        {
            if (!Application.isEditor) return;
            string moment = PlayerPrefs.GetString(FreezeNextKey, "");
            if (moment.Length == 0) return;
            PlayerPrefs.DeleteKey(FreezeNextKey);
            Debug.Log($"[Ricochet] Dev: {FreezeOn(moment)}");
        }

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
        /// "windup" (the creature drawing back), "drama" (last-crystal / lethal slow motion), "seize" / "shatter" (the
        /// creature dying), "zip" (the rift sealing).
        /// </summary>
        public static string FreezeOn(string moment, float scale = 0.001f)
        {
            if (s_timeLog == null) StartTimeLog();
            var motes = Object.FindAnyObjectByType<LightMotes>();
            var director = Object.FindAnyObjectByType<ShotDirector>();
            var drama = Object.FindAnyObjectByType<ShotDrama>();
            var sling = Object.FindAnyObjectByType<Sling>();
            var creature = Object.FindAnyObjectByType<Creature>(FindObjectsInactive.Include);
            var rift = Object.FindAnyObjectByType<Rift>(FindObjectsInactive.Include);
            float seen = 0f;
            System.Func<bool> when = moment switch
            {
                "motes" => () => motes.InFlightOf(LightMotes.Kind.Damage) > 0 && (seen += Time.deltaTime) > 0.25f,
                "bolt" => () => motes.InFlightOf(LightMotes.Kind.Bolt) + motes.InFlightOf(LightMotes.Kind.Hex) > 0
                                && (seen += Time.deltaTime) > 0.3f,
                "drama" => () => drama.Active && (seen += Time.unscaledDeltaTime) > 0.3f,
                // mid-spill: the board pouring out of the rift, about a third of it landed (InFlightOf counts queued seeds)
                "seeds" => () => motes.InFlightOf(LightMotes.Kind.Seed) is > 0 and < 22,
                "arrive" => () => sling.IsArriving && (seen += Time.deltaTime) > 0.35f, // the Spark flying out of the rift
                "relic" => RelicMoment(director), // a relic acting: its glyph popping where it acted
                // the kill: the creature seizing with its cracks blazing, then breaking apart, then the rift zipping shut
                "seize" => () => creature.Dying && !creature.Shattered && (seen += Time.unscaledDeltaTime) > 0.5f,
                "shatter" => () => creature.Shattered && (seen += Time.deltaTime) > 0.15f,
                "zip" => () => rift.IsZipping && (seen += Time.deltaTime) > 0.3f,
                // the Queen's crown streaming to the wall as gold light
                "crown" => () => motes.InFlightOf(LightMotes.Kind.Crown) > 0 && (seen += Time.deltaTime) > 0.45f,
                _ => null,
            };
            if (when == null) return "unknown moment";
            s_timeLog.FreezeWhen(when, scale);
            return "freeze armed: " + moment;
        }

        static System.Func<bool> RelicMoment(ShotDirector director)
        {
            float since = -1f;
            director.RelicTriggered += (_, _) => { if (since < 0f) since = 0f; };
            return () => since >= 0f && (since += Time.unscaledDeltaTime) > 0.18f;
        }

        /// <summary>Fire the hero shot: the relaxed straight shot the board guarantees a cluster for.</summary>
        /// <summary>Allow the Daily Rift in the Editor (and forget today's attempt): the next fresh run is today's daily.</summary>
        public static string Daily(bool on)
        {
            DailyRift.DevEnable(on);
            return $"daily {(on ? "on" : "off")} for the next run (today {DailyRift.Today}, gift {DailyRift.Gift(DailyRift.Today).spark.Name} + {DailyRift.Gift(DailyRift.Today).relic.Name})";
        }

        /// <summary>A recenter, as the headset would send it: the seat (sling, HUD) follows the head as it is now.</summary>
        public static string Reseat()
        {
            var area = Object.FindAnyObjectByType<Ricochet.Room.PlayArea>();
            area.Reseat();
            return "seat " + area.Seat.position.ToString("F2") + " fwd " + area.Seat.forward.ToString("F2");
        }

        /// <summary>Play the next fresh runs at this Ascension tier in the Editor (0 = off).</summary>
        public static string Ascension(int tier)
        {
            PlayerPrefs.SetInt(Gameplay.Ascension.DevKey, Mathf.Clamp(tier, 0, Gameplay.Ascension.Max));
            PlayerPrefs.Save();
            return "editor ascension " + PlayerPrefs.GetInt(Gameplay.Ascension.DevKey, 0) + " (unlocked on device: " + Gameplay.Ascension.Unlocked + ")";
        }

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

        /// <summary>Run: take a Spark type or relic by name ("Splitter", "Heavy", "Carom", "SecondWind"...) or "Mend".</summary>
        public static string Grant(string name)
        {
            var encounter = Object.FindAnyObjectByType<EncounterDirector>();
            if (encounter == null) return "no encounter";
            Reward reward;
            if (System.Enum.TryParse(name, true, out SparkKind spark) && spark != SparkKind.Plain) reward = new Reward(spark);
            else if (System.Enum.TryParse(name, true, out Relic relic) && relic != Relic.None) reward = new Reward(relic);
            else if (name.Equals("Mend", System.StringComparison.OrdinalIgnoreCase)) reward = Reward.Mend;
            else return "unknown reward " + name;
            encounter.Grant(reward);
            return "granted " + reward.Name + "; " + encounter.Run;
        }

        /// <summary>Run: the reward picker takes this orb by itself after a beat (-1: wait for a real pinch).</summary>
        public static string AutoPick(int index)
        {
            RewardPicker.AutoPick = index;
            return "autopick " + index;
        }

        /// <summary>Stand in for taking the headset off (true) or putting it back on (false).</summary>
        public static string Away(bool away)
        {
            var life = Object.FindAnyObjectByType<LifecyclePause>();
            if (life == null) return "no LifecyclePause";
            life.Simulate(away);
            return "paused=" + LifecyclePause.Paused + " timeScale=" + Time.timeScale;
        }

        public static string RunInfo()
        {
            var encounter = Object.FindAnyObjectByType<EncounterDirector>();
            return encounter != null ? encounter.Run.ToString() : "no encounter";
        }

        /// <summary>Editor Play resumes the saved run when on (device always does).</summary>
        public static string ResumeInEditor(bool on)
        {
            PlayerPrefs.SetInt("Ricochet.ResumeInEditor", on ? 1 : 0);
            PlayerPrefs.Save();
            return "resume in editor " + on;
        }

        /// <summary>Editor Play starts in the Pocket Arena (the no-scan fallback) when on. Takes effect on the next Play.</summary>
        public static string Pocket(bool on)
        {
            PlayerPrefs.SetInt(Room.PlayArea.ForcePocketKey, on ? 1 : 0);
            PlayerPrefs.Save();
            return "pocket arena " + on;
        }

        /// <summary>
        /// Run: make the current creature one hit from death and count it as encounter (n - 1), so the next hit wins
        /// it and the run moves on to encounter n (0-based; 5 is the boss).
        /// </summary>
        public static string JumpTo(int encounterIndex)
        {
            var encounter = Object.FindAnyObjectByType<EncounterDirector>();
            if (encounter == null) return "no encounter";
            encounter.Run.Encounter = Mathf.Clamp(encounterIndex - 1, 0, RunState.EncountersPerRun - 1);
            var c = encounter.Creature;
            c.TakeDamage(c.EffectiveHp - 1);
            return $"next kill moves on to encounter {encounterIndex + 1}; creature hp {c.Hp}";
        }

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
            // Aim like Look and Fire does (the exact low-arc solve), then clear the lane along that arc.
            sling.SolveShot(p, out Vector3 aim, out float pull);
            Vector3 from = sling.LaunchPoint(sling.transform.position, aim, pull);
            Vector3 v = sling.VelocityFor(aim, pull);
            float g = spark.GravityAcceleration;
            float flight = Vector3.ProjectOnPlane(p - from, Vector3.up).magnitude /
                           Mathf.Max(0.1f, Vector3.ProjectOnPlane(v, Vector3.up).magnitude);
            // Only what the arc meets before it reaches the target goes; crystals beside and behind it stay.
            int cleared = 0;
            float before = Mathf.Max(0f, flight - 0.12f / Mathf.Max(0.1f, v.magnitude));
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c == target || c.IsPopped) continue;
                Vector3 q = c.transform.position;
                for (int k = 1; k <= 32; k++)
                {
                    float t = before * k / 32f;
                    Vector3 at = from + v * t + Vector3.down * (0.5f * g * t * t);
                    if ((at - q).sqrMagnitude < 0.17f * 0.17f) { c.Pop(); cleared++; break; }
                }
            }
            return $"{want} at {Mathf.Sqrt(best):F2} m (cleared {cleared}), fired={sling.FireForTest(aim, pull)}";
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
