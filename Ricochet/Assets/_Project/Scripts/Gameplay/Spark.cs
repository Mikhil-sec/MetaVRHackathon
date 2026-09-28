using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The projectile. Floaty custom gravity, continuous-speculative collision against room and crystals
    /// (per the MRUK Bouncing Ball sample), and a clear end-of-shot rule.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class Spark : MonoBehaviour
    {
        public enum EndReason { None, Lifetime, Rest, Bounces, Floor }

        [SerializeField] float _gravityScale = 0.35f;
        [SerializeField] float _maxLifetime = 9f;
        [SerializeField] int _maxRoomBounces = 14;
        [SerializeField] float _restSpeed = 0.35f;
        [SerializeField] float _restTime = 0.5f;
        [Tooltip("CONCEPT section 3: the shot ends when the Spark touches the floor.")]
        [SerializeField] bool _floorEndsShot = true;
        [SerializeField] TrailRenderer _trail;

        [Header("Visual (children; the root keeps the collider)")]
        [SerializeField] Transform _visual;
        [SerializeField] Transform _halo;
        [SerializeField] float _stretchPerSpeed = 0.07f;
        [SerializeField] float _maxStretch = 1.7f;

        Rigidbody _body;
        Vector3 _visualScale, _haloScale;
        float _stretch = 1f;
        float _flightTime;
        float _slowTime;
        int _roomBounces;

        public bool InFlight { get; private set; }
        public int RoomBounces => _roomBounces;
        public EndReason LastEnd { get; private set; }
        public float FlightTime => _flightTime;
        public float GravityAcceleration => Physics.gravity.magnitude * _gravityScale;
        public bool FloorEndsShot { get => _floorEndsShot; set => _floorEndsShot = value; }

        public event Action<Spark, Crystal> CrystalHit;
        public event Action<Spark, Vector3, Vector3> RoomBounced;
        public event Action<Spark> Died;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.useGravity = false;
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.mass = 0.01f;
            gameObject.layer = Layers.Spark;
            if (_visual != null) _visualScale = _visual.localScale;
            if (_halo != null) _haloScale = _halo.localScale;
        }

        /// <summary>Squash-and-stretch along the velocity (TECH_GUIDE section 4); a slow breathing halo while held.</summary>
        void LateUpdate()
        {
            if (_visual == null) return;
            Vector3 v = InFlight ? _body.linearVelocity : Vector3.zero;
            float speed = v.magnitude;
            float target = Mathf.Min(1f + speed * _stretchPerSpeed, _maxStretch);
            _stretch = Mathf.Lerp(_stretch, target, 1f - Mathf.Exp(-25f * Time.deltaTime));
            if (speed > 0.2f) _visual.rotation = Quaternion.LookRotation(v);
            float side = 1f / Mathf.Sqrt(_stretch); // preserve volume
            _visual.localScale = Vector3.Scale(_visualScale, new Vector3(side, side, _stretch));

            if (_halo != null)
            {
                float pulse = InFlight ? 1.2f : 1f + 0.1f * Mathf.Sin(Time.time * 2.4f);
                _halo.localScale = _haloScale * pulse;
            }
        }

        /// <summary>Kinematic placement while held in the sling.</summary>
        public void Hold(Vector3 position)
        {
            InFlight = false;
            _body.isKinematic = true;
            transform.position = position;
            if (_trail != null) _trail.emitting = false;
        }

        public void Launch(Vector3 velocity)
        {
            _flightTime = 0f;
            _slowTime = 0f;
            _roomBounces = 0;
            _body.isKinematic = false;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            if (_trail != null) { _trail.Clear(); _trail.emitting = true; }
            LastEnd = EndReason.None;
            InFlight = true;
        }

        void FixedUpdate()
        {
            if (InFlight) Step(Time.fixedDeltaTime);
        }

        /// <summary>One physics tick of flight rules. Public so the room sweep can drive Physics.Simulate itself.</summary>
        public void Step(float dt)
        {
            if (!InFlight) return;
            _body.AddForce(Physics.gravity * _gravityScale, ForceMode.Acceleration);

            _flightTime += dt;
            _slowTime = _body.linearVelocity.magnitude < _restSpeed ? _slowTime + dt : 0f;
            if (_flightTime > _maxLifetime) Die(EndReason.Lifetime);
            else if (_slowTime > _restTime) Die(EndReason.Rest);
            else if (_roomBounces > _maxRoomBounces) Die(EndReason.Bounces);
        }

        /// <summary>
        /// Where would this launch first touch the room? Runs the real Spark physics for a moment (script-mode
        /// simulation, no events), so board generation can put the hero cluster exactly where the Spark lands.
        /// Call only between shots, with no crystals active.
        /// </summary>
        public bool PredictFirstContact(Vector3 origin, Vector3 velocity, out Vector3 point, out Vector3 normal,
                                        out Collider collider, float maxTime = 3f)
        {
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            _predicting = true;
            _predictedCollider = null;
            Hold(origin);
            Physics.SyncTransforms();
            Launch(velocity);
            float dt = Time.fixedDeltaTime;
            for (float t = 0f; t < maxTime && _predictedCollider == null; t += dt)
            {
                _body.AddForce(Physics.gravity * _gravityScale, ForceMode.Acceleration);
                Physics.Simulate(dt);
            }
            Hold(origin);
            _predicting = false;

            Physics.simulationMode = mode;
            gameObject.SetActive(wasActive);
            point = _predictedPoint;
            normal = _predictedNormal;
            collider = _predictedCollider;
            return collider != null;
        }

        bool _predicting;
        Collider _predictedCollider;
        Vector3 _predictedPoint, _predictedNormal;

        void OnCollisionEnter(Collision collision)
        {
            if (!InFlight) return;
            if (_predicting)
            {
                if (_predictedCollider == null && !collision.collider.TryGetComponent(out Crystal _))
                {
                    var c = collision.GetContact(0);
                    _predictedCollider = collision.collider;
                    _predictedPoint = c.point;
                    _predictedNormal = c.normal;
                }
                return;
            }
            if (collision.collider.TryGetComponent(out Crystal crystal))
            {
                CrystalHit?.Invoke(this, crystal);
                return;
            }
            _roomBounces++;
            var contact = collision.GetContact(0);
            RoomBounced?.Invoke(this, contact.point, contact.normal);
            if (_floorEndsShot && IsFloor(collision.collider)) Die(EndReason.Floor);
        }

        static bool IsFloor(Collider collider)
        {
            var anchor = collider.GetComponentInParent<MRUKAnchor>();
            return anchor != null && anchor.Label == MRUKAnchor.SceneLabels.FLOOR;
        }

        void Die(EndReason reason)
        {
            if (!InFlight) return;
            LastEnd = reason;
            InFlight = false;
            _body.isKinematic = true;
            if (_trail != null) _trail.emitting = false;
            Died?.Invoke(this);
        }
    }
}
