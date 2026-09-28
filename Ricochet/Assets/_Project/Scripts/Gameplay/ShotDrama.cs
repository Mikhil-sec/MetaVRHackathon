using Ricochet.Audio;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Peggle's last-peg moment (docs/TECH_GUIDE.md section 5, CONCEPT section 6). When the Spark is about to reach
    /// the last crystal standing, game time slows, a drumroll swells, the real room sinks and the Spark's light on
    /// the walls brightens. The camera never moves. A hit lands with a crash and a held beat of slow motion;
    /// a near miss lets everything exhale.
    /// </summary>
    public sealed class ShotDrama : MonoBehaviour
    {
        [SerializeField] Spark _spark;
        [SerializeField] BoardGenerator _board;
        [SerializeField] ShotDirector _director;
        [SerializeField] TimeWarp _warp;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] PassthroughMood _mood;

        [Header("Trigger: predicted closest approach to the last crystal")]
        [SerializeField] float _lookahead = 0.45f;       // game seconds until the closest approach
        [SerializeField] float _triggerDistance = 2.2f;
        [SerializeField] float _passDistance = 0.24f;    // includes near misses; that is the drama

        [Header("Payoff")]
        [SerializeField] float _slowScale = 0.3f;        // on trigger...
        [SerializeField] float _slowScaleClose = 0.1f;   // ...deepening to this as the Spark arrives
        [SerializeField] float _hitHold = 0.6f;          // real seconds of held slow motion after the final hit
        [SerializeField] float _sparkBoost = 2.2f;
        [SerializeField] Color _finaleGlow = new(2.6f, 2f, 0.9f);

        [Tooltip("Optional: adds the lethal-hit moment during encounters.")]
        [SerializeField] EncounterDirector _encounter;

        Rigidbody _body;
        Crystal _target;
        float _startDistance;
        bool _finaleDone; // one finale per shot

        public bool Active { get; private set; }

        void Awake() => _body = _spark.GetComponent<Rigidbody>();

        void OnEnable()
        {
            _director.CrystalLit += OnCrystalLit;
            _spark.Died += OnSparkDied;
        }

        void OnDisable()
        {
            _director.CrystalLit -= OnCrystalLit;
            _spark.Died -= OnSparkDied;
            if (Active) End(0f);
        }

        void Update()
        {
            if (!_spark.InFlight)
            {
                _finaleDone = false;
                return;
            }

            if (Active)
            {
                Measure(_target, out float t, out _, out float distance);
                if (t <= 0f || _target.IsLit || _target.IsPopped)
                {
                    End(0f); // it slipped past: exhale
                    return;
                }
                float closeness = 1f - Mathf.Clamp01(distance / _startDistance);
                _warp.SlowTo(Mathf.Lerp(_slowScale, _slowScaleClose, closeness));
                _sfx.SetDrumroll(0.4f + 0.6f * closeness);
                return;
            }
            if (_finaleDone) return;

            // Two moments earn the slow motion: the last crystal on the board, and (in an encounter) any crystal
            // whose hit would bank the light that finishes the creature. The soonest approach wins.
            bool lethal = _encounter != null && _encounter.NextHitLethal;
            Crystal best = null;
            float bestT = float.MaxValue, bestDistance = 0f;
            if (lethal)
            {
                var active = _board.Active;
                for (int i = 0; i < active.Count; i++)
                {
                    var c = active[i];
                    if (c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                    if (Triggers(c, out float t, out float d) && t < bestT) { best = c; bestT = t; bestDistance = d; }
                    Measure(c, out float tt, out float pass, out float dd);
                    if (tt > 0f && pass < _diagPass) { _diagPass = pass; _diagT = tt; _diagDistance = dd; }
                }
                _diagArmed = true;
            }
            else
            {
                var last = LastStanding();
                if (last != null && Triggers(last, out _, out float d)) { best = last; bestDistance = d; }
            }
            if (best != null) Begin(best, bestDistance, lethal);
        }

        bool Triggers(Crystal c, out float t, out float distance)
        {
            Measure(c, out t, out float pass, out distance);
            return t > 0f && t < _lookahead && distance < _triggerDistance && pass < _passDistance;
        }

        /// <summary>Closest approach to a crystal along the current arc (linear plus the Spark's own gravity).</summary>
        void Measure(Crystal c, out float t, out float pass, out float distance)
        {
            Vector3 rel = c.transform.position - _spark.transform.position;
            Vector3 v = _body.linearVelocity;
            t = Vector3.Dot(rel, v) / Mathf.Max(v.sqrMagnitude, 1e-4f);
            Vector3 drop = 0.5f * _spark.GravityAcceleration * t * t * Vector3.down;
            pass = (rel - v * t - drop).magnitude;
            distance = rel.magnitude;
        }

        void Begin(Crystal target, float distance, bool lethal)
        {
            Active = true;
            _diagBegan = true;
            _target = target;
            _startDistance = Mathf.Max(distance, 0.3f);
            _warp.SlowTo(_slowScale);
            _sfx.SetDrumroll(0.4f);
            target.SetHighlight(1f);
            if (_glow != null) _glow.SparkBoost = _sparkBoost;
            if (_mood != null) _mood.SetFocus(1f);
            Debug.Log($"[Ricochet] Drama: {(lethal ? "lethal" : "last")} crystal {distance:F2} m away");
        }

        void End(float hold)
        {
            Active = false;
            if (_target != null) _target.SetHighlight(0f);
            _target = null;
            _warp.Release(hold);
            _sfx.SetDrumroll(0f);
            if (_glow != null) _glow.SparkBoost = 1f;
            if (_mood != null) _mood.SetFocus(0f);
        }

        void OnCrystalLit(Crystal crystal, int combo)
        {
            // Whether or not the slow-mo caught it, lighting the last crystal (or banking the lethal light) is the finale.
            bool last = RemainingUnlit() == 0;
            bool lethal = _encounter != null && _encounter.ShotLethal;
            if (_finaleDone || !(last || lethal)) return;
            _finaleDone = true;
            Vector3 at = crystal.transform.position;
            _sfx.PlayCrash(at);
            if (_glow != null) _glow.Pulse(at, _finaleGlow, 1.8f, 1.3f);
            bool caught = Active;
            End(_hitHold);
            if (!caught) _warp.Hold(_slowScaleClose, _hitHold); // a surprise finale still gets its beat, as a hit-stop
            Debug.Log($"[Ricochet] Drama: {(lethal ? "lethal" : "final")} crystal lit (slow-mo {(caught ? "tracked" : "surprise")})");
        }

        // Diagnostics: when a lethal shot never earned its slow motion, say how close it came.
        bool _diagArmed, _diagBegan;
        float _diagPass = float.MaxValue, _diagT, _diagDistance;

        void OnSparkDied(Spark spark)
        {
            if (_diagArmed && !_diagBegan)
                Debug.Log($"[Ricochet] Drama: lethal armed but not triggered; best pass {_diagPass:F2} m at t={_diagT:F2} s, {_diagDistance:F2} m away");
            _diagArmed = _diagBegan = false;
            _diagPass = float.MaxValue;
            if (Active) End(0f);
        }

        /// <summary>The only crystal not yet lit or popped, or null if there are zero or several.</summary>
        Crystal LastStanding()
        {
            Crystal last = null;
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c.IsLit || c.IsPopped) continue;
                if (last != null) return null;
                last = c;
            }
            return last;
        }

        int RemainingUnlit()
        {
            int n = 0;
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].IsLit && !active[i].IsPopped) n++;
            return n;
        }
    }
}
