using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Feeds the RoomGlow shader: the game's light cast onto the real room (docs/TECH_GUIDE.md section 3).
    /// Slot 0 follows the Spark; the other slots are short, decaying flashes (bounces, crystal hits, pops).
    /// Fixed arrays, published as shader globals every frame: zero allocations.
    /// </summary>
    public sealed class RoomGlow : MonoBehaviour
    {
        const int MaxGlows = 8; // must match MAX_GLOWS in RoomGlow.shader

        static readonly int SourcesId = Shader.PropertyToID("_GlowSources");
        static readonly int ColorsId = Shader.PropertyToID("_GlowColors");

        struct Flash
        {
            public Vector3 Position;
            public Color Color;
            public float Radius;
            public float Age;
            public float Duration;
        }

        [SerializeField] Spark _spark;
        [SerializeField] Color _sparkColor = new(0.3f, 0.85f, 1f);
        [SerializeField] float _sparkRadius = 0.9f;
        [SerializeField] float _sparkFlightIntensity = 1.3f;
        [SerializeField] float _sparkIdleIntensity = 0.4f;

        readonly Vector4[] _sources = new Vector4[MaxGlows];
        readonly Vector4[] _colors = new Vector4[MaxGlows];
        // Slot 0: the Spark. Slots 1..6: flashes. Slot 7: one steady light (the rift on its wall).
        readonly Flash[] _flashes = new Flash[MaxGlows - 2];
        float _sparkIntensity;
        Vector4 _steadySource;
        Color _steadyColor;

        public static RoomGlow Instance { get; private set; }

        /// <summary>Multiplies the Spark's light on the room (the drama spotlight). 1 = normal.</summary>
        public float SparkBoost { get; set; } = 1f;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < _flashes.Length; i++) _flashes[i].Duration = 0f;
            Publish(); // fixes the global array sizes before any shader reads them
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            System.Array.Clear(_colors, 0, _colors.Length);
            Shader.SetGlobalVectorArray(ColorsId, _colors);
        }

        /// <summary>A brief pulse of light on nearby real surfaces. Replaces the oldest flash when all are busy.</summary>
        public void Pulse(Vector3 position, Color color, float radius, float duration)
        {
            int slot = 0;
            float oldest = -1f;
            for (int i = 0; i < _flashes.Length; i++)
            {
                if (_flashes[i].Duration <= 0f) { slot = i; break; }
                float progress = _flashes[i].Age / _flashes[i].Duration;
                if (progress > oldest) { oldest = progress; slot = i; }
            }
            _flashes[slot] = new Flash { Position = position, Color = color, Radius = radius, Duration = duration };
        }

        /// <summary>A light that stays until changed (the owner animates it). Color black switches it off.</summary>
        public void SetSteady(Vector3 position, Color color, float radius)
        {
            _steadySource = new Vector4(position.x, position.y, position.z, radius);
            _steadyColor = color;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float realDt = Time.unscaledDeltaTime;

            // The Spark: a soft light that brightens in flight, with a slow breathing pulse while it waits.
            bool active = _spark != null && _spark.gameObject.activeInHierarchy;
            float target = !active ? 0f
                : _spark.InFlight ? _sparkFlightIntensity * SparkBoost
                : _sparkIdleIntensity * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f));
            _sparkIntensity = Mathf.Lerp(_sparkIntensity, target, 1f - Mathf.Exp(-10f * realDt));
            if (active)
            {
                Vector3 p = _spark.transform.position;
                _sources[0] = new Vector4(p.x, p.y, p.z, _sparkRadius);
            }
            _colors[0] = _sparkColor * _sparkIntensity;

            for (int i = 0; i < _flashes.Length; i++)
            {
                ref Flash f = ref _flashes[i];
                int slot = i + 1;
                if (f.Duration <= 0f) { _colors[slot] = Vector4.zero; continue; }
                f.Age += dt;
                float t = f.Age / f.Duration;
                if (t >= 1f) { f.Duration = 0f; _colors[slot] = Vector4.zero; continue; }
                // Fast attack, eased decay; the radius swells slightly as it fades.
                float intensity = (1f - t) * (1f - t);
                _sources[slot] = new Vector4(f.Position.x, f.Position.y, f.Position.z, f.Radius * (0.8f + 0.4f * t));
                _colors[slot] = f.Color * intensity;
            }
            _sources[MaxGlows - 1] = _steadySource;
            _colors[MaxGlows - 1] = _steadyColor;

            Publish();
        }

        void Publish()
        {
            Shader.SetGlobalVectorArray(SourcesId, _sources);
            Shader.SetGlobalVectorArray(ColorsId, _colors);
        }
    }
}
