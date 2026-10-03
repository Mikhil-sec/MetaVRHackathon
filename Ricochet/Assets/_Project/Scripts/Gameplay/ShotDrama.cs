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

        [Header("Lock-on (Ring shader quad): marks the target, tightening as the Spark closes in")]
        [SerializeField] Renderer _lockRing;
        [SerializeField] Color _lockColor = new(1f, 0.78f, 0.35f);
        [SerializeField] float _sparkGlowClose = 0.9f;   // extra Spark halo at arrival, so it reads at 2 m
        [SerializeField] float _burstTime = 0.35f;       // real seconds of the ring's expanding burst on the hit
        [SerializeField] float _linger = 0.5f;           // real seconds to wait for another lethal approach after a graze

        Rigidbody _body;
        Crystal _target;
        float _startDistance;
        float _closeness;
        bool _finaleDone; // one finale per shot
        MaterialPropertyBlock _mpb;
        Vector3 _burstAt;
        float _burst = -1f, _spin;
        float _lingerEnd;
        Vector3 _ringAt;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int SpinId = Shader.PropertyToID("_Spin");

        public bool Active { get; private set; }

        void Awake()
        {
            _body = _spark.GetComponent<Rigidbody>();
            _mpb = new MaterialPropertyBlock();
            if (_lockRing != null) _lockRing.enabled = false;
        }

        void BurstLockRing(Vector3 at)
        {
            _burstAt = at;
            _burst = 0f;
        }

        /// <summary>The lock-on ring runs on real time, so it keeps tightening smoothly through the slow motion.</summary>
        void UpdateLockRing()
        {
            if (_lockRing == null) return;
            float dt = RealTime.DeltaTime;
            _spin += dt * (0.2f + 1.2f * _closeness);
            float radius, intensity;
            Vector3 at;
            if (Active)
            {
                if (_target != null) _ringAt = _target.transform.position; // lingering: hold the last mark
                at = _ringAt;
                radius = Mathf.Lerp(0.45f, 0.2f, _closeness);
                intensity = (0.7f + 1.8f * _closeness) * (_target != null ? 1f : 0.5f);
            }
            else if (_burst >= 0f && _burst < 1f)
            {
                _burst += dt / _burstTime;
                float e = 1f - (1f - _burst) * (1f - _burst);
                at = _burstAt;
                radius = Mathf.Lerp(0.2f, 0.49f, e);
                intensity = 3f * Mathf.Max(0f, 1f - _burst);
            }
            else
            {
                _lockRing.enabled = false;
                return;
            }
            _lockRing.enabled = true;
            _lockRing.transform.position = at;
            _mpb.Clear();
            _mpb.SetColor(ColorId, _lockColor);
            _mpb.SetFloat(IntensityId, intensity);
            _mpb.SetFloat(RadiusId, radius);
            _mpb.SetFloat(SpinId, _spin);
            _lockRing.SetPropertyBlock(_mpb);
        }

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
            UpdateLockRing();
            if (!_spark.InFlight)
            {
                _finaleDone = false;
                return;
            }

            if (Active)
            {
                bool stillLethal = _encounter != null && _encounter.NextHitLethal;
                if (_target == null)
                {
                    // Lingering after a graze: the Spark is still among crystals that could end it. Hold the breath a
                    // moment for the next approach rather than exhaling and slamming back down a beat later.
                    if (stillLethal && FindBest(true, null, out var again, out float againDistance))
                        Retarget(again, againDistance);
                    else if (!stillLethal || RealTime.Now > _lingerEnd) End(0f);
                    else _warp.SlowTo(_slowScale);
                    return;
                }
                Measure(_target, out float t, out _, out float distance);
                if (t <= 0f || _target.IsLit || _target.IsPopped)
                {
                    if (stillLethal)
                    {
                        if (FindBest(true, _target, out var next, out float nextDistance)) Retarget(next, nextDistance);
                        else
                        {
                            _target.SetHighlight(0f);
                            _target = null;
                            _lingerEnd = RealTime.Now + _linger;
                        }
                        return;
                    }
                    End(0f); // it slipped past: exhale
                    return;
                }
                _closeness = 1f - Mathf.Clamp01(distance / _startDistance);
                _warp.SlowTo(Mathf.Lerp(_slowScale, _slowScaleClose, _closeness));
                _sfx.SetDrumroll(0.4f + 0.6f * _closeness);
                _spark.FlightGlow = 1f + _sparkGlowClose * _closeness;
                return;
            }
            if (_finaleDone) return;

            // Two moments earn the slow motion: the last crystal on the board, and (in an encounter) any crystal
            // whose hit would bank the light that finishes the creature. The soonest approach wins.
            bool lethal = _encounter != null && _encounter.NextHitLethal;
            if (FindBest(lethal, null, out var best, out float bestDistance)) Begin(best, bestDistance, lethal);
        }

        bool FindBest(bool lethal, Crystal exclude, out Crystal best, out float bestDistance)
        {
            best = null;
            bestDistance = 0f;
            float bestT = float.MaxValue;
            if (lethal)
            {
                var active = _board.Active;
                for (int i = 0; i < active.Count; i++)
                {
                    var c = active[i];
                    if (c == exclude || c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                    if (Triggers(c, out float t, out float d) && t < bestT) { best = c; bestT = t; bestDistance = d; }
                    Measure(c, out float tt, out float pass, out float dd);
                    if (tt > 0f && pass < _diagPass) { _diagPass = pass; _diagT = tt; _diagDistance = dd; }
                }
                _diagArmed = true;
            }
            else
            {
                var last = LastStanding();
                if (last != null && last != exclude && Triggers(last, out _, out float d)) { best = last; bestDistance = d; }
            }
            return best != null;
        }

        void Retarget(Crystal next, float distance)
        {
            if (_target != null) _target.SetHighlight(0f);
            _target = next;
            next.SetHighlight(1f);
            // Keep the current depth of slow motion: rescale closeness so it continues from where it is now.
            _startDistance = Mathf.Max(distance / Mathf.Max(1f - _closeness, 0.2f), 0.3f);
            Debug.Log($"[Ricochet] Drama: retarget to lethal crystal {distance:F2} m away");
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
            _closeness = 0f;
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
            _closeness = 0f;
            _spark.FlightGlow = 1f;
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
            if (caught) BurstLockRing(at);
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
