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
        enum State { Empty, Ready, Pulling }

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

        public float PullFraction => _state == State.Pulling ? Mathf.Clamp01(_smoothedPull.magnitude / _maxPull) : 0f;

        // Read-only state for the feedback layer (SlingFx).
        public IReadOnlyList<IPinchInput> Inputs => _inputs;
        public bool IsReady => _state == State.Ready;
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
        public void Arm()
        {
            _state = State.Ready;
            _spark.gameObject.SetActive(true);
            _spark.Hold(transform.position);
        }

        void Update()
        {
            switch (_state)
            {
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
                if (!inReach) continue;

                _active = input;
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
            if (_preview != null) _preview.Hide();
            _spark.Launch(velocity);
            Launched?.Invoke(velocity);
        }

        void Cancel()
        {
            if (_preview != null) _preview.Hide();
            _smoothedPull = Vector3.zero;
            _state = State.Ready;
            Cancelled?.Invoke();
        }
    }
}
