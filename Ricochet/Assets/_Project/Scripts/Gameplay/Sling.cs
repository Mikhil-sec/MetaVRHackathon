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

            for (int i = 0; i < _inputs.Count; i++)
            {
                var input = _inputs[i];
                bool pinching = input.IsPinching;
                bool pinchStarted = pinching && !_wasPinching[i];
                _wasPinching[i] = pinching;
                if (!pinchStarted) continue;

                bool inReach = input.GrabsFromAnywhere ||
                               Vector3.Distance(input.PinchPoint, transform.position) <= _grabRadius;
                if (!inReach) continue;

                _active = input;
                _smoothedPull = Vector3.zero;
                _lostTime = 0f;
                _state = State.Pulling;
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
            if (_preview != null) _preview.Show(_spark.transform.position, velocity);

            if (!_active.IsPinching)
            {
                if (_smoothedPull.magnitude >= _minPull) Fire(velocity);
                else Cancel();
            }
        }

        Vector3 LaunchVelocity()
        {
            float t = Mathf.InverseLerp(_minPull, _maxPull, _smoothedPull.magnitude);
            return _smoothedPull.normalized * Mathf.Lerp(_minSpeed, _maxSpeed, t);
        }

        /// <summary>Automation hook (room sweep, Editor tests): fire from the anchor as if released at this aim/pull.</summary>
        public bool FireForTest(Vector3 aimDirection, float pull01)
        {
            if (_state != State.Ready) return false;
            _smoothedPull = aimDirection.normalized * Mathf.Lerp(_minPull, _maxPull, Mathf.Clamp01(pull01));
            _spark.Hold(transform.position);
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
            _state = State.Ready;
            Cancelled?.Invoke();
        }
    }
}
