using System;
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
        [SerializeField] float _gravityScale = 0.35f;
        [SerializeField] float _maxLifetime = 9f;
        [SerializeField] int _maxRoomBounces = 14;
        [SerializeField] float _restSpeed = 0.35f;
        [SerializeField] float _restTime = 0.5f;
        [SerializeField] TrailRenderer _trail;

        Rigidbody _body;
        float _flightTime;
        float _slowTime;
        int _roomBounces;

        public bool InFlight { get; private set; }
        public int RoomBounces => _roomBounces;

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
            InFlight = true;
        }

        void FixedUpdate()
        {
            if (!InFlight) return;
            _body.AddForce(Physics.gravity * _gravityScale, ForceMode.Acceleration);

            _flightTime += Time.fixedDeltaTime;
            _slowTime = _body.linearVelocity.magnitude < _restSpeed ? _slowTime + Time.fixedDeltaTime : 0f;
            if (_flightTime > _maxLifetime || _slowTime > _restTime || _roomBounces > _maxRoomBounces)
                Die();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!InFlight) return;
            if (collision.collider.TryGetComponent(out Crystal crystal))
            {
                CrystalHit?.Invoke(this, crystal);
                return;
            }
            _roomBounces++;
            var contact = collision.GetContact(0);
            RoomBounced?.Invoke(this, contact.point, contact.normal);
        }

        void Die()
        {
            InFlight = false;
            _body.isKinematic = true;
            if (_trail != null) _trail.emitting = false;
            Died?.Invoke(this);
        }
    }
}
