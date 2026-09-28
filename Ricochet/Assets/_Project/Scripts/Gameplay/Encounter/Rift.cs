using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The crack of light in a real wall that the creature comes through (CONCEPT sections 3-4). It opens from a
    /// hairline, breathes while the encounter lasts, lights its own patch of wall through RoomGlow's steady slot,
    /// and seals with a flash when the creature falls. Visual only: the Spark never collides with it.
    /// </summary>
    public sealed class Rift : MonoBehaviour
    {
        static readonly int OpenId = Shader.PropertyToID("_Open");

        [SerializeField] MeshRenderer _crack;
        [SerializeField] Transform _halo;
        [SerializeField] RoomGlow _glow;
        [SerializeField] float _height = 0.8f;
        [SerializeField] float _width = 0.16f;
        [SerializeField] float _openSeconds = 1.1f;
        [SerializeField] float _sealSeconds = 0.5f;
        [SerializeField] Color _wallLight = new(1.1f, 0.25f, 0.8f);
        [SerializeField] float _wallLightRadius = 0.9f;

        MaterialPropertyBlock _block;
        Vector3 _haloScale;
        float _open;          // 0 closed .. 1 open
        float _target;
        float _flare;         // brief surge (opening crack, sealing flash)

        public bool IsOpen => _target > 0f;
        /// <summary>Where the creature hovers: just in front of the crack.</summary>
        public Vector3 Mouth => transform.position + transform.forward * 0.32f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_halo != null) _haloScale = _halo.localScale;
            Apply();
        }

        /// <summary>Places the rift on a wall (normal points out of the wall) and opens it.</summary>
        public void Open(Vector3 position, Vector3 normal)
        {
            // Sit a hair off the wall so it never z-fights the room mesh; forward = out of the wall.
            transform.SetPositionAndRotation(position + normal * 0.015f, Quaternion.LookRotation(normal, Vector3.up));
            _open = 0f;
            _target = 1f;
            _flare = 1f;
            gameObject.SetActive(true);
        }

        public void Seal()
        {
            _target = 0f;
            _flare = 1.5f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float rate = _target > _open ? 1f / _openSeconds : 1f / _sealSeconds;
            _open = Mathf.MoveTowards(_open, _target, rate * dt);
            _flare = Mathf.Max(0f, _flare - dt * 1.5f);
            Apply();
            if (_open <= 0f && _target <= 0f)
            {
                if (_glow != null) _glow.SetSteady(transform.position, Color.black, 0f);
                gameObject.SetActive(false);
            }
        }

        void Apply()
        {
            // Opening: the hairline lengthens first, then splits wider (ease-out), like a crack propagating.
            float length = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_open * 1.6f));
            float split = 1f - Mathf.Pow(1f - Mathf.Clamp01((_open - 0.3f) / 0.7f), 3f);
            float breathe = 1f + 0.06f * Mathf.Sin(Time.time * 2.1f);
            float width = _width * Mathf.Max(0.06f, split) * breathe * (1f + 0.5f * _flare);
            if (_crack != null)
            {
                _crack.transform.localScale = new Vector3(width, _height * Mathf.Max(0.02f, length), 1f);
                _block.SetFloat(OpenId, Mathf.Clamp01(_open * 2f) * (1f + _flare));
                _crack.SetPropertyBlock(_block);
            }
            if (_halo != null) _halo.localScale = _haloScale * (0.3f + 0.7f * split + 0.8f * _flare) * breathe;
            if (_glow != null && gameObject.activeInHierarchy)
                _glow.SetSteady(transform.position + transform.forward * 0.1f,
                    _wallLight * (_open * breathe + 1.5f * _flare), _wallLightRadius);
        }
    }
}
