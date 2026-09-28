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
        [SerializeField] SparkRibbon _ribbon;

        [Header("Visual (children; the root keeps the collider)")]
        [SerializeField] Transform _visual;
        [SerializeField] Transform _halo;
        [SerializeField] float _stretchPerSpeed = 0.07f;
        [SerializeField] float _maxStretch = 1.7f;
        [Tooltip("Bounce squash: flatten against the contact normal, then spring back (seconds of game time).")]
        [SerializeField] float _squashTime = 0.1f;
        [SerializeField] float _maxSquash = 0.5f;

        Rigidbody _body;
        Vector3 _visualScale, _haloScale;
        float _stretch = 1f;
        float _squash;          // 1 at impact, decays to 0
        float _squashStrength;
        Vector3 _squashNormal;
        float _flightTime;
        float _slowTime;
        int _roomBounces;

        public bool InFlight { get; private set; }
        public int RoomBounces => _roomBounces;
        public EndReason LastEnd { get; private set; }
        public float FlightTime => _flightTime;
        public float GravityAcceleration => Physics.gravity.magnitude * _gravityScale;
        public bool FloorEndsShot { get => _floorEndsShot; set => _floorEndsShot = value; }
        public SparkRibbon Ribbon => _ribbon;
        /// <summary>Halo size multiplier while held (SlingFx: hover brightening and pull charge-up).</summary>
        public float HoldGlow { get; set; } = 1f;
        /// <summary>Halo size multiplier in flight (ShotDrama: the Spark swells as it closes on the deciding crystal).</summary>
        public float FlightGlow { get; set; } = 1f;

        static readonly int HeatId = Shader.PropertyToID("_Heat");
        MaterialPropertyBlock _heatBlock;
        Renderer _visualRenderer;

        /// <summary>Chain heat 0..1: the trail and the core's rim shift from cyan toward gold.</summary>
        public void SetHeat(float heat)
        {
            heat = Mathf.Clamp01(heat);
            if (_ribbon != null) _ribbon.SetHeat(heat);
            if (_visualRenderer == null) return;
            _heatBlock ??= new MaterialPropertyBlock();
            _heatBlock.SetFloat(HeatId, heat);
            _visualRenderer.SetPropertyBlock(_heatBlock);
        }

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
            if (_visual != null)
            {
                _visualScale = _visual.localScale;
                _visualRenderer = _visual.GetComponent<Renderer>();
            }
            if (_halo != null) _haloScale = _halo.localScale;
        }

        /// <summary>
        /// Squash-and-stretch (TECH_GUIDE section 4): stretch along the velocity in flight, flatten against the surface
        /// on each impact, and a slow breathing halo while held. Visual children only; the collider never changes.
        /// </summary>
        void LateUpdate()
        {
            if (_visual == null) return;
            float dt = Time.deltaTime;
            Vector3 v = InFlight ? _body.linearVelocity : Vector3.zero;
            float speed = v.magnitude;
            float target = Mathf.Min(1f + speed * _stretchPerSpeed, _maxStretch);
            _stretch = Mathf.Lerp(_stretch, target, 1f - Mathf.Exp(-25f * dt));

            float axis; // scale along the visual's z
            if (_squash > 0f)
            {
                _squash = Mathf.Max(0f, _squash - dt / _squashTime);
                _visual.rotation = Quaternion.LookRotation(_squashNormal);
                axis = 1f - _maxSquash * _squashStrength * _squash * _squash;
            }
            else
            {
                if (speed > 0.2f) _visual.rotation = Quaternion.LookRotation(v);
                axis = _stretch;
            }
            float side = 1f / Mathf.Sqrt(axis); // preserve volume
            _visual.localScale = Vector3.Scale(_visualScale, new Vector3(side, side, axis));

            if (_halo != null)
            {
                float pulse = InFlight
                    ? (1.2f + 0.6f * _squashStrength * _squash) * FlightGlow
                    : (1f + 0.1f * Mathf.Sin(Time.time * 2.4f)) * HoldGlow;
                _halo.localScale = _haloScale * pulse;
            }
        }

        void Squash(Collision collision, ContactPoint contact)
        {
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            _squashStrength = Mathf.Clamp01(impact / 5f);
            if (_squashStrength < 0.1f) return;
            _squash = 1f;
            _squashNormal = contact.normal;
            _stretch = 1f; // spring back from round, not from the pre-impact stretch
        }

        /// <summary>Kinematic placement while held in the sling.</summary>
        public void Hold(Vector3 position)
        {
            InFlight = false;
            _body.isKinematic = true;
            transform.position = position;
            if (_ribbon != null) _ribbon.Emitting = false;
        }

        public void Launch(Vector3 velocity)
        {
            _flightTime = 0f;
            _slowTime = 0f;
            _squash = 0f;
            _roomBounces = 0;
            _body.isKinematic = false;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            if (_ribbon != null) { _ribbon.Clear(); _ribbon.Emitting = true; }
            SetHeat(0f);
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
            float dt = TimeWarp.PhysicsStep;
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
            var contact = collision.GetContact(0);
            Squash(collision, contact);
            if (collision.collider.TryGetComponent(out Crystal crystal))
            {
                CrystalHit?.Invoke(this, crystal);
                return;
            }
            _roomBounces++;
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
            if (_ribbon != null) _ribbon.Emitting = false;
            Died?.Invoke(this);
        }
    }
}
