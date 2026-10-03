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
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock _heatBlock, _haloBlock;
        Renderer _visualRenderer, _haloRenderer;
        Collider _collider;

        [Header("Spark types")]
        [SerializeField] float _heavyScale = 1.3f;
        [SerializeField] float _heavyKeep = 0.94f;       // speed kept plowing through each crystal
        [SerializeField] float _magnetRange = 0.9f;
        [SerializeField] float _magnetPull = 5f;         // m/s^2 at point blank, fading to 0 at the range
        [SerializeField] float _childScale = 0.72f;

        // Colliders passed through this shot (Ghost furniture, Heavy crystals): restored when the flight ends.
        readonly Collider[] _ignored = new Collider[48];
        int _ignoredCount;
        Vector3 _preStepVelocity;
        int _ghostCharges, _floorGrace;
        float _kindScale = 1f;

        public SparkKind Kind { get; private set; }
        /// <summary>A split-off Spark (it never splits again and is smaller).</summary>
        public bool IsChild { get; private set; }
        /// <summary>Unlit crystals the Magnet type curves toward (set by the director).</summary>
        public System.Collections.Generic.IReadOnlyList<Crystal> MagnetTargets { get; set; }
        /// <summary>Floor touches this flight that bounce instead of ending the shot (relic Second Wind).</summary>
        public int FloorGrace { get => _floorGrace; set => _floorGrace = value; }
        /// <summary>The scene label of the last room surface bounced off.</summary>
        public MRUKAnchor.SceneLabels LastBounceLabel { get; private set; }

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

        /// <summary>Loads a Spark type: its behaviour for the next flight and its light (rim and halo) while held.</summary>
        public void SetKind(SparkKind kind)
        {
            Kind = kind;
            _kindScale = (kind == SparkKind.Heavy ? _heavyScale : 1f) * (IsChild ? _childScale : 1f);
            Color c = Upgrades.Color(kind);
            if (_ribbon != null) { _ribbon.CoolColor = c; _ribbon.SetHeat(0f); }
            if (_visualRenderer != null)
            {
                _heatBlock ??= new MaterialPropertyBlock();
                _heatBlock.SetColor(RimColorId, c);
                _visualRenderer.SetPropertyBlock(_heatBlock);
            }
            if (_haloRenderer != null)
            {
                _haloBlock ??= new MaterialPropertyBlock();
                _haloBlock.SetColor(ColorId, c);
                _haloRenderer.SetPropertyBlock(_haloBlock);
            }
        }

        /// <summary>
        /// A second Spark for the Splitter: a copy of this one with its own ribbon, which reports through the same
        /// events. Built once (pooled by the director), inactive until launched.
        /// </summary>
        public Spark CreateChild(string name)
        {
            var ribbon = _ribbon != null ? Instantiate(_ribbon, _ribbon.transform.parent) : null;
            var child = Instantiate(this, transform.parent);
            child.name = name;
            child.IsChild = true;
            child._ribbon = ribbon;
            if (ribbon != null) { ribbon.name = name + "Ribbon"; ribbon.Target = child.transform; }
            child.gameObject.SetActive(false);
            child.SetKind(SparkKind.Plain);
            return child;
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
            _collider = GetComponent<Collider>();
            if (_visual != null)
            {
                _visualScale = _visual.localScale;
                _visualRenderer = _visual.GetComponent<Renderer>();
            }
            if (_halo != null)
            {
                _haloScale = _halo.localScale;
                _haloRenderer = _halo.GetComponent<Renderer>();
            }
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
            _visual.localScale = Vector3.Scale(_visualScale, new Vector3(side, side, axis)) * _kindScale;

            if (_halo != null)
            {
                float pulse = InFlight
                    ? (1.2f + 0.6f * _squashStrength * _squash) * FlightGlow
                    : (1f + 0.1f * Mathf.Sin(Time.time * 2.4f)) * HoldGlow;
                _halo.localScale = _haloScale * (pulse * _kindScale);
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
            if (_ignoredCount > 0) RestoreIgnored();
        }

        public void Launch(Vector3 velocity)
        {
            if (_ignoredCount > 0) RestoreIgnored();
            _flightTime = 0f;
            _slowTime = 0f;
            _squash = 0f;
            _roomBounces = 0;
            _ghostCharges = Kind == SparkKind.Ghost ? 1 : 0;
            _preStepVelocity = velocity;
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
            if (Kind == SparkKind.Magnet && MagnetTargets != null) Steer();
            _preStepVelocity = _body.linearVelocity;

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
                PredictedVelocity = _body.linearVelocity; // the approach velocity, kept from the step that touches
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

        /// <summary>The flight velocity just before the predicted first contact (PredictFirstContact).</summary>
        public Vector3 PredictedVelocity { get; private set; }
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
            if (collision.collider.TryGetComponent(out Crystal crystal))
            {
                // Heavy keeps its line: it shatters through the crystal instead of glancing off.
                if (Kind == SparkKind.Heavy) PassThrough(collision.collider, _heavyKeep);
                else Squash(collision, contact);
                CrystalHit?.Invoke(this, crystal);
                return;
            }
            var anchor = collision.collider.GetComponentInParent<MRUKAnchor>();
            LastBounceLabel = anchor != null ? anchor.Label : 0;
            if (_ghostCharges > 0 && anchor != null && (LastBounceLabel & PhaseThrough) != 0)
            {
                // Ghost: slips through the first piece of furniture as if it weren't there.
                _ghostCharges--;
                PassThrough(collision.collider, 1f);
                Phased?.Invoke(this, contact.point);
                return;
            }
            Squash(collision, contact);
            _roomBounces++;
            bool floor = LastBounceLabel == MRUKAnchor.SceneLabels.FLOOR;
            LastBounceWasGrace = floor && _floorEndsShot && _floorGrace > 0;
            if (LastBounceWasGrace) _floorGrace--;
            RoomBounced?.Invoke(this, contact.point, contact.normal);
            if (_floorEndsShot && floor && !LastBounceWasGrace) Die(EndReason.Floor);
        }

        /// <summary>The last room bounce was a floor touch forgiven by Second Wind.</summary>
        public bool LastBounceWasGrace { get; private set; }

        /// <summary>Ghost passed through furniture: (spark, point).</summary>
        public event Action<Spark, Vector3> Phased;

        // Furniture a Ghost may pass through: not the room's shell, nor things mounted flat on a wall.
        const MRUKAnchor.SceneLabels Shell = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING |
            MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
            MRUKAnchor.SceneLabels.WALL_ART | MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME |
            MRUKAnchor.SceneLabels.GLOBAL_MESH;
        const MRUKAnchor.SceneLabels PhaseThrough = ~Shell;

        /// <summary>Ignore this collider for the rest of the flight and carry on at the pre-contact velocity.</summary>
        void PassThrough(Collider other, float keep)
        {
            Physics.IgnoreCollision(_collider, other, true);
            if (_ignoredCount < _ignored.Length) _ignored[_ignoredCount++] = other;
            _body.linearVelocity = _preStepVelocity * keep;
        }

        void RestoreIgnored()
        {
            for (int i = 0; i < _ignoredCount; i++)
            {
                if (_ignored[i] != null) Physics.IgnoreCollision(_collider, _ignored[i], false);
                _ignored[i] = null;
            }
            _ignoredCount = 0;
        }

        /// <summary>Magnet: a gentle pull toward the nearest unlit crystal in range, stronger the closer it is.</summary>
        void Steer()
        {
            Vector3 p = _body.position;
            float best = _magnetRange * _magnetRange;
            Vector3 to = Vector3.zero;
            var targets = MagnetTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                var c = targets[i];
                if (c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                Vector3 d = c.transform.position - p;
                float d2 = d.sqrMagnitude;
                if (d2 < best) { best = d2; to = d; }
            }
            if (to == Vector3.zero) return;
            float dist = Mathf.Sqrt(best);
            _body.AddForce(to / dist * (_magnetPull * (1f - dist / _magnetRange)), ForceMode.Acceleration);
        }

        void Die(EndReason reason)
        {
            if (!InFlight) return;
            LastEnd = reason;
            InFlight = false;
            _body.isKinematic = true;
            if (_ribbon != null) _ribbon.Emitting = false;
            if (_ignoredCount > 0) RestoreIgnored();
            Died?.Invoke(this);
        }
    }
}
