using Ricochet.Audio;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The crack of light in a real wall that the creature comes through (CONCEPT sections 3-4). It opens from a
    /// hairline, breathes while the encounter lasts, lights its own patch of wall through RoomGlow's steady slot,
    /// and zips shut when the creature falls: the crack closes from the bottom up behind a white-hot bead, the wall
    /// fractures pull in after it, and it pops closed at the top. Visual only: the Spark never collides with it.
    /// </summary>
    public sealed class Rift : MonoBehaviour
    {
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int SpreadId = Shader.PropertyToID("_Spread");
        static readonly int ZipId = Shader.PropertyToID("_Zip");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] MeshRenderer _crack;
        [SerializeField] Transform _halo;
        [SerializeField] MeshRenderer _web;
        [SerializeField] float _webSpread = 0.75f;   // metres the wall fractures out to once fully open
        [SerializeField] RoomGlow _glow;
        [SerializeField] float _height = 0.95f;
        [SerializeField] float _width = 0.26f;  // wide enough that the void inside reads at 4-5 m
        [SerializeField] float _openSeconds = 1.1f;
        [SerializeField] float _sealSeconds = 0.5f;
        [SerializeField] float _zipSeconds = 0.55f;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] Color _wallLight = new(1.1f, 0.25f, 0.8f);
        [SerializeField] float _wallLightRadius = 0.9f;

        [SerializeField] float _humLevel = 0.35f;
        [SerializeField] Color _dailyColor = new(1f, 0.6f, 0.22f);      // the Daily Rift opens in dawn gold
        [SerializeField] Color _dailyWallLight = new(1.15f, 0.62f, 0.25f);

        MaterialPropertyBlock _block;
        AudioSource _hum;     // the rift hums where it is: you can find it by ear when it opens outside your view
        MeshRenderer _haloRenderer;
        bool _daily;
        Vector3 _haloScale;
        float _openFor;       // this opening's duration (seconds)
        float _open;          // 0 closed .. 1 open
        float _target;
        float _flare;         // brief surge (opening crack, sealing flash)
        float _zip = -1f;     // sealing: 0..1 along the crack (-1 = not zipping)
        float _zipFront;      // eased bead position, 0 bottom .. 1 top

        public bool IsOpen => _target > 0f;
        /// <summary>The zip is still running (the crack is closing).</summary>
        public bool IsZipping => _zip >= 0f && _zip < 1f;
        /// <summary>Where the creature hovers: just in front of the crack.</summary>
        public Vector3 Mouth => transform.position + transform.forward * 0.32f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_halo != null)
            {
                _haloScale = _halo.localScale;
                _haloRenderer = _halo.GetComponent<MeshRenderer>();
                ApplyHaloColor();
            }
            _hum = gameObject.AddComponent<AudioSource>();
            _hum.loop = true;
            _hum.playOnAwake = false;
            _hum.spatialBlend = 1f;
            _hum.spatialize = true;
            _hum.minDistance = 0.5f;
            _hum.maxDistance = 12f;
            _hum.rolloffMode = AudioRolloffMode.Logarithmic;
            _hum.dopplerLevel = 0f;
            _hum.volume = 0f;
            Apply();
        }

        /// <summary>Places the rift on a wall (normal points out of the wall) and opens it.</summary>
        /// <param name="seconds">How long the crack takes to open (default: the usual quick tear).</param>
        public void Open(Vector3 position, Vector3 normal, float seconds = -1f)
        {
            _openFor = seconds > 0f ? seconds : _openSeconds;
            // Sit a hair off the wall so it never z-fights the room mesh; forward = out of the wall.
            transform.SetPositionAndRotation(position + normal * 0.015f, Quaternion.LookRotation(normal, Vector3.up));
            _open = 0f;
            _target = 1f;
            _flare = 1f;
            _zip = -1f;
            _zipFront = 0f;
            if (_halo != null) _halo.localPosition = Vector3.zero;
            gameObject.SetActive(true);
            if (_sfx != null && _hum.clip == null) _hum.clip = _sfx.RiftHum;
            if (_hum.clip != null && !_hum.isPlaying) _hum.Play();
        }

        /// <summary>The Daily Rift: crack, fractures, halo and wall light in dawn gold instead of the void's magenta.</summary>
        public void SetDaily(bool daily)
        {
            _daily = daily;
            ApplyHaloColor();
        }

        // Called again from Awake: the run can set the daily before the rift has woken up.
        void ApplyHaloColor()
        {
            if (_haloRenderer == null) return;
            if (_daily)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor(ColorId, _dailyColor);
                _haloRenderer.SetPropertyBlock(block);
            }
            else _haloRenderer.SetPropertyBlock(null);
        }

        public void Seal()
        {
            if (!gameObject.activeInHierarchy || _target <= 0f) return;
            if (_open < 0.5f || _zip >= 0f)
            {
                // Barely open: just snap shut.
                _target = 0f;
                _flare = 1.5f;
                return;
            }
            _zip = 0f;
            if (_sfx != null) _sfx.PlayZip(transform.position);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_zip >= 0f && _zip < 1f)
            {
                _zip = Mathf.Min(1f, _zip + dt / _zipSeconds);
                _zipFront = Mathf.Pow(_zip, 1.4f); // a zipper pull gathers speed
                if (_zip >= 1f)
                {
                    // Closed at the top: a pop of light, then the scar fades with the rest.
                    _target = 0f;
                    _flare = 1.5f;
                    if (_sfx != null) _sfx.PlaySealPop(BeadPosition);
                }
            }
            float rate = _target > _open ? 1f / Mathf.Max(0.05f, _openFor) : 1f / _sealSeconds;
            _open = Mathf.MoveTowards(_open, _target, rate * dt);
            _flare = Mathf.Max(0f, _flare - dt * 1.5f);
            Apply();
            if (_open <= 0f && _target <= 0f)
            {
                if (_glow != null) _glow.SetSteady(transform.position, Color.black, 0f);
                gameObject.SetActive(false);
            }
        }

        Vector3 BeadPosition => transform.position + transform.up * ((_zipFront - 0.5f) * _height * 0.9f);

        void Apply()
        {
            bool zipping = _zip >= 0f;
            float zip = zipping ? _zipFront : 0f;
            // Opening: the hairline lengthens first, then splits wider (ease-out), like a crack propagating.
            float length = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_open * 1.6f));
            float split = 1f - Mathf.Pow(1f - Mathf.Clamp01((_open - 0.3f) / 0.7f), 3f);
            float breathe = 1f + 0.06f * Mathf.Sin(Time.time * 2.1f);
            // While zipping the quad keeps its size: the shader closes it along its length.
            float width = _width * Mathf.Max(0.06f, zipping ? 1f : split) * breathe * (1f + (zipping ? 0f : 0.5f * _flare));
            if (_crack != null)
            {
                _crack.transform.localScale = new Vector3(width, _height * Mathf.Max(0.02f, zipping ? 1f : length), 1f);
                _block.Clear();
                _block.SetFloat(OpenId, Mathf.Clamp01(_open * 2f) * (1f + _flare));
                _block.SetFloat(ZipId, zipping ? zip : -1f);
                if (_daily) _block.SetColor(ColorId, _dailyColor);
                _crack.SetPropertyBlock(_block);
            }
            if (_web != null)
            {
                // The wall fractures outward as the crack splits (the tear), with a jolt on the opening flare;
                // on the seal it pulls back in with the crack.
                _block.Clear();
                _block.SetFloat(SpreadId, _webSpread * (0.15f + 0.85f * split) * (1f + 0.25f * _flare) * (1f - 0.85f * zip));
                _block.SetFloat(OpenId, Mathf.Clamp01(_open * 1.5f) * (1f + 0.6f * _flare));
                if (_daily) _block.SetColor(ColorId, _dailyColor);
                _web.SetPropertyBlock(_block);
            }
            if (_halo != null)
            {
                if (zipping)
                {
                    // The halo rides the bead up the seam, small and hot.
                    _halo.position = BeadPosition + transform.forward * 0.02f;
                    _halo.localScale = _haloScale * (0.45f + 0.9f * _flare) * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
                }
                else _halo.localScale = _haloScale * (0.3f + 0.7f * split + 0.8f * _flare) * breathe;
            }
            if (_hum != null) _hum.volume = _humLevel * Mathf.Clamp01(_open * (1f - 0.7f * zip)) * (1f + 0.5f * _flare);
            if (_glow != null && gameObject.activeInHierarchy)
                _glow.SetSteady((zipping ? BeadPosition : transform.position) + transform.forward * 0.1f,
                    (_daily ? _dailyWallLight : _wallLight) * (_open * breathe + 1.5f * _flare) * (zipping ? 1.2f - 0.5f * zip : 1f),
                    _wallLightRadius);
        }
    }
}
