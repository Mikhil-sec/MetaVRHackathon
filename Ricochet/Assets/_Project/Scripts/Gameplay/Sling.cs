using System;
using System.Collections.Generic;
using Ricochet.Input;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Pinch-pull-release launcher (docs/TECH_GUIDE.md §6).
    /// Launch direction comes from the smoothed aim line, never from hand velocity.
    /// Tracking loss while pulling cancels softly; it never fires.
    /// </summary>
    public sealed class Sling : MonoBehaviour
    {
        enum State { Empty, Arriving, Ready, Pulling }
        const float ArriveSeconds = 0.9f;

        [Header("Refs")]
        [SerializeField] Spark _spark;
        [SerializeField] TrajectoryPreview _preview;

        [Header("Feel")]
        [SerializeField] float _grabRadius = 0.09f;
        [SerializeField] float _minPull = 0.03f;
        [SerializeField] float _maxPull = 0.28f;
        [SerializeField] float _minSpeed = 2.5f;
        [SerializeField] float _maxSpeed = 8.5f;
        [SerializeField] float _aimSmoothing = 18f;
        [SerializeField] float _trackingGrace = 0.25f;
        [SerializeField] float _idleBobAmplitude = 0.006f;
        [Tooltip("Release jitter: opening the fingers drags the pinch point before the pinch ends. Fire with the aim this many real seconds before release.")]
        [SerializeField] float _releaseLookback = 0.08f;

        const int HistorySize = 32; // ~0.35 s at 90 Hz, more than the lookback needs
        readonly Vector3[] _pullHistory = new Vector3[HistorySize];
        readonly float[] _pullTimes = new float[HistorySize];
        int _historyHead, _historyCount;

        readonly List<IPinchInput> _inputs = new();
        State _state = State.Empty;
        IPinchInput _active;
        Vector3 _smoothedPull;
        float _lostTime;
        bool[] _wasPinching = Array.Empty<bool>();

        public event Action<Vector3> Launched;
        public event Action Grabbed;
        public event Action Cancelled;
        /// <summary>The Spark flown in by <see cref="ArriveFrom"/> has settled in the band.</summary>
        public event Action Arrived;

        /// <summary>Set before <see cref="Arm"/>: the Spark flies in from here (the rift) instead of appearing in place.</summary>
        public Vector3? ArriveFrom { get; set; }
        Vector3 _arriveFrom;
        float _arrive;

        /// <summary>
        /// Accessibility (AssistAim): while this returns a point, a pinch anywhere (out of the sling's reach) draws a
        /// shot solved to hit it; the band pulls itself back and the release fires. A pinch at the sling stays manual.
        /// </summary>
        public Func<Vector3?> AssistTarget;
        bool _assisted;
        float _assistDraw;

        public float PullFraction => _state == State.Pulling ? Mathf.Clamp01(_smoothedPull.magnitude / _maxPull) : 0f;

        // Read-only state for the feedback layer (SlingFx).
        public IReadOnlyList<IPinchInput> Inputs => _inputs;
        public bool IsReady => _state == State.Ready;
        public bool IsArriving => _state == State.Arriving;
        public bool IsPulling => _state == State.Pulling;
        public float GrabRadius => _grabRadius;
        /// <summary>The input holding the Spark while pulling, else null.</summary>
        public IPinchInput ActiveInput => _state == State.Pulling ? _active : null;
        /// <summary>Anchor minus the smoothed pinch point (the band's stretch). Still valid during Launched.</summary>
        public Vector3 PullVector => _smoothedPull;
        /// <summary>Launch power 0..1: 0 below the minimum pull (a release there cancels).</summary>
        public float Charge => Mathf.InverseLerp(_minPull, _maxPull, _smoothedPull.magnitude);
        public bool AboveMinPull => _smoothedPull.magnitude >= _minPull;
        /// <summary>While ready: 0 with no tracked hand near, 1 when a pinch would grab (eases in from 4x reach).</summary>
        public float HoverProximity { get; private set; }
        /// <summary>While ready: the nearest tracked input is within grab reach.</summary>
        public bool InReach { get; private set; }

        public void AddInput(IPinchInput input)
        {
            _inputs.Add(input);
            _wasPinching = new bool[_inputs.Count];
        }

        /// <summary>Loads the Spark onto the sling so it can be grabbed.</summary>
        // Nothing sits in the sling until the first Arm (the cold open flies it in from the rift).
        void Start()
        {
            if (_state == State.Empty) _spark.SetVisible(false);
        }

        public void Arm()
        {
            _spark.gameObject.SetActive(true);
            _spark.SetVisible(true);
            if (ArriveFrom.HasValue)
            {
                _arriveFrom = ArriveFrom.Value;
                ArriveFrom = null;
                _arrive = 0f;
                _state = State.Arriving;
                _spark.Glide(_arriveFrom, true);
                return;
            }
            _state = State.Ready;
            _spark.Hold(transform.position);
        }

        void UpdateArriving()
        {
            // Out of the rift fast, then settling into the band along a gentle arc (ease-out cubic).
            _arrive = Mathf.Min(1f, _arrive + Time.deltaTime / ArriveSeconds);
            float e = 1f - Mathf.Pow(1f - _arrive, 3f), u = 1f - e;
            Vector3 to = transform.position;
            Vector3 control = (_arriveFrom + to) * 0.5f + Vector3.up * (0.15f + 0.08f * Vector3.Distance(_arriveFrom, to));
            _spark.Glide(u * u * _arriveFrom + 2f * u * e * control + e * e * to, false);
            if (_arrive < 1f) return;
            _spark.Hold(to);
            _state = State.Ready;
            Arrived?.Invoke();
        }

        void Update()
        {
            switch (_state)
            {
                case State.Arriving: UpdateArriving(); break;
                case State.Ready: UpdateReady(); break;
                case State.Pulling: UpdatePulling(); break;
            }
        }

        void UpdateReady()
        {
            _spark.Hold(transform.position + Vector3.up * Mathf.Sin(Time.time * 2.4f) * _idleBobAmplitude);

            // Hover: how close the nearest real reach (hand/controller, not the mouse) is to the anchor.
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < _inputs.Count; i++)
            {
                var input = _inputs[i];
                if (float.IsInfinity(input.ReachScale) || !input.IsTracked) continue;
                float reach = _grabRadius * input.ReachScale;
                nearest = Mathf.Min(nearest, Vector3.Distance(input.PinchPoint, transform.position) / reach);
            }
            InReach = nearest <= 1f;
            HoverProximity = float.IsInfinity(nearest) ? 0f : 1f - Mathf.InverseLerp(1f, 4f, nearest);

            for (int i = 0; i < _inputs.Count; i++)
            {
                var input = _inputs[i];
                bool pinching = input.IsPinching;
                bool pinchStarted = pinching && !_wasPinching[i];
                _wasPinching[i] = pinching;
                if (!pinchStarted) continue;

                bool inReach = Vector3.Distance(input.PinchPoint, transform.position) <= _grabRadius * input.ReachScale;
                _assisted = !inReach && AssistTarget?.Invoke() != null;
                if (!inReach && !_assisted) continue;

                _active = input;
                _assistDraw = 0f;
                _smoothedPull = Vector3.zero;
                _historyCount = 0;
                _lostTime = 0f;
                _state = State.Pulling;
                HoverProximity = 0f;
                InReach = false;
                Grabbed?.Invoke();
                return;
            }
        }

        void UpdatePulling()
        {
            if (_assisted) { UpdateAssisted(); return; }
            if (!_active.IsTracked)
            {
                _lostTime += Time.deltaTime;
                if (_lostTime > _trackingGrace) Cancel();
                return;
            }
            _lostTime = 0f;

            Vector3 pull = Vector3.ClampMagnitude(transform.position - _active.PinchPoint, _maxPull);
            float k = 1f - Mathf.Exp(-_aimSmoothing * Time.deltaTime);
            _smoothedPull = Vector3.Lerp(_smoothedPull, pull, k);

            _spark.Hold(transform.position - _smoothedPull);
            Vector3 velocity = LaunchVelocity();
            if (_preview != null)
            {
                if (_smoothedPull.magnitude >= _minPull) _preview.Show(_spark.transform.position, velocity);
                else _preview.Hide();
            }

            if (!_active.IsPinching)
            {
                // Aim from just before the fingers started opening, not the dragged last frames.
                Vector3 lastPull = _smoothedPull;
                _smoothedPull = PullAt(RealTime.Now - _releaseLookback);
                if (_smoothedPull.magnitude >= _minPull)
                {
                    Vector3 v = LaunchVelocity();
                    // One line per real shot (never per sweep shot): how much the release drag would have bent the aim.
                    Debug.Log($"[Ricochet] Launch: pull {_smoothedPull.magnitude:F3} m, dir {v.normalized:F2}, " +
                              $"release drag {Vector3.Angle(lastPull, _smoothedPull):F1} deg");
                    Fire(v);
                }
                else Cancel();
                return;
            }
            RecordPull();
        }

        void UpdateAssisted()
        {
            if (!_active.IsTracked)
            {
                _lostTime += Time.deltaTime;
                if (_lostTime > _trackingGrace) Cancel();
                return;
            }
            _lostTime = 0f;
            Vector3? target = AssistTarget?.Invoke();
            if (!target.HasValue) { Cancel(); return; } // the assist went off, or no crystal is in focus

            // The band draws itself back along the solved aim over a third of a second (the gesture's feedback).
            SolveShot(target.Value, out Vector3 aim, out float pull01);
            _assistDraw = Mathf.MoveTowards(_assistDraw, 1f, Time.deltaTime / 0.35f);
            float draw = _assistDraw * _assistDraw * (3f - 2f * _assistDraw);
            Vector3 want = aim * (Mathf.Lerp(_minPull, _maxPull, pull01) * draw);
            _smoothedPull = Vector3.Lerp(_smoothedPull, want, 1f - Mathf.Exp(-_aimSmoothing * Time.deltaTime));
            _spark.Hold(transform.position - _smoothedPull);
            if (_preview != null) _preview.Show(_spark.transform.position, VelocityFor(aim, pull01));

            if (_active.IsPinching) return;
            if (_assistDraw >= 0.3f) FireAt(target.Value, "assist", true); // even a quick pinch counts
            else Cancel();
        }

        /// <summary>
        /// Accessibility (AssistAim): fire a shot solved to hit this point. "dwell" from looking alone, "assist" from a
        /// pinch anywhere. Returns false when the sling has no Spark ready.
        /// </summary>
        public bool FireAt(Vector3 target, string how, bool fromPull = false)
        {
            if (_state != State.Ready && !(fromPull && _state == State.Pulling)) return false;
            bool reachable = SolveShot(target, out Vector3 aim, out float pull01);
            _smoothedPull = aim * Mathf.Lerp(_minPull, _maxPull, pull01);
            _spark.Hold(transform.position - _smoothedPull); // where a hand release starts
            Debug.Log($"[Ricochet] Launch ({how}): dir {aim:F2}, pull {pull01:F2}{(reachable ? "" : ", out of reach")}");
            Fire(VelocityFor(aim, pull01));
            return true;
        }

        /// <summary>
        /// The low-arc aim that lands the Spark on a point (its custom gravity, no drag), at a strong pull. The launch
        /// point depends on the aim (the Spark sits pulled back along it), so it is refined a few times.
        /// Returns false when the point is out of range (then: full pull at 45 degrees toward it).
        /// </summary>
        public bool SolveShot(Vector3 target, out Vector3 aim, out float pull01)
        {
            const float pull = 0.9f;
            float g = Mathf.Max(0.01f, _spark.GravityAcceleration);
            float v = Mathf.Lerp(_minSpeed, _maxSpeed, pull), v2 = v * v;
            pull01 = pull;
            aim = (target - transform.position).normalized;
            for (int i = 0; i < 3; i++)
            {
                Vector3 d = target - LaunchPoint(transform.position, aim, pull01);
                Vector3 flat = Vector3.ProjectOnPlane(d, Vector3.up);
                float x = flat.magnitude, y = d.y;
                if (x < 1e-3f) { aim = d.normalized; return true; }
                flat /= x;
                float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
                if (disc < 0f)
                {
                    pull01 = 1f;
                    aim = (flat + Vector3.up).normalized;
                    return false;
                }
                float theta = Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x));
                aim = (flat * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
            }
            return true;
        }

        void RecordPull()
        {
            _pullHistory[_historyHead] = _smoothedPull;
            _pullTimes[_historyHead] = RealTime.Now;
            _historyHead = (_historyHead + 1) % HistorySize;
            if (_historyCount < HistorySize) _historyCount++;
        }

        /// <summary>The newest recorded pull at or before a real time (the oldest one if none is that old).</summary>
        Vector3 PullAt(float time)
        {
            if (_historyCount == 0) return _smoothedPull;
            Vector3 result = _smoothedPull;
            for (int k = 1; k <= _historyCount; k++)
            {
                int i = (_historyHead - k + HistorySize) % HistorySize;
                result = _pullHistory[i];
                if (_pullTimes[i] <= time) break;
            }
            return result;
        }

        Vector3 LaunchVelocity() =>
            VelocityFor(_smoothedPull, Mathf.InverseLerp(_minPull, _maxPull, _smoothedPull.magnitude));

        /// <summary>
        /// Where a release at this aim and pull depth starts: the Spark sits pulled back behind the anchor, so a
        /// hand-fired shot travels that much further (and drops that much more) than one launched from the anchor.
        /// Predictions, tests and the sweep launch from here so boards are built for the shot a hand makes.
        /// </summary>
        public Vector3 LaunchPoint(Vector3 anchor, Vector3 aimDirection, float pull01) =>
            anchor - aimDirection.normalized * Mathf.Lerp(_minPull, _maxPull, Mathf.Clamp01(pull01));

        /// <summary>Launch velocity for an aim direction and a pull depth in [0, 1].</summary>
        public Vector3 VelocityFor(Vector3 aimDirection, float pull01) =>
            aimDirection.normalized * Mathf.Lerp(_minSpeed, _maxSpeed, Mathf.Clamp01(pull01));

        /// <summary>Automation hook (room sweep, Editor tests): fire from the anchor as if released at this aim/pull.</summary>
        public bool FireForTest(Vector3 aimDirection, float pull01)
        {
            if (_state != State.Ready) return false;
            _smoothedPull = aimDirection.normalized * Mathf.Lerp(_minPull, _maxPull, Mathf.Clamp01(pull01));
            _spark.Hold(transform.position - _smoothedPull); // where a hand release starts
            Fire(LaunchVelocity());
            return true;
        }

        void Fire(Vector3 velocity)
        {
            _state = State.Empty;
            _assisted = false;
            if (_preview != null) _preview.Hide();
            _spark.Launch(velocity);
            Launched?.Invoke(velocity);
        }

        void Cancel()
        {
            _assisted = false;
            if (_preview != null) _preview.Hide();
            _smoothedPull = Vector3.zero;
            _state = State.Ready;
            Cancelled?.Invoke();
        }
    }
}
