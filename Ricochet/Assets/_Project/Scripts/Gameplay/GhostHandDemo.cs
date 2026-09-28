using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Zero-text onboarding (CONCEPT section 4): if the player has not launched yet and leaves the sling alone for a
    /// few seconds, a ghost of light demonstrates the one gesture. Two fingertip lights (thumb and index) drift in and
    /// pinch the Spark, draw it back along a glowing pull path, then spring apart as a ghost Spark flies off.
    /// Visual only: it never touches the real sling or Spark. It stops for good after the first real launch, and
    /// yields the moment a real hand comes near. Real time, zero GC.
    /// </summary>
    public sealed class GhostHandDemo : MonoBehaviour
    {
        const float Duration = 3.2f;
        const float Held = 0.022f; // thumb-index gap while holding: two fingertips visibly pinching the ghost Spark

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] Sling _sling;
        [SerializeField] Transform _head;
        [SerializeField] Renderer _thumb;       // Halo quads
        [SerializeField] Renderer _index;
        [SerializeField] Renderer _ghostSpark;
        [SerializeField] LineRenderer _path;    // the pull path (Ribbon glow)
        [SerializeField] float _idleDelay = 4f;
        [SerializeField] float _repeatEvery = 8f;
        [SerializeField] Color _color = new(0.7f, 0.95f, 1f);

        MaterialPropertyBlock _mpb;
        float _idle, _t = -1f;
        bool _learned;
        readonly Vector3[] _pathPoints = new Vector3[2];

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            Show(false);
            _path.positionCount = 2;
            _path.useWorldSpace = true;
        }

        void OnEnable() => _sling.Launched += OnLaunched;
        void OnDisable() => _sling.Launched -= OnLaunched;

        void OnLaunched(Vector3 v)
        {
            _learned = true;
            _t = -1f;
            Show(false);
        }

        /// <summary>Dev/capture: holds the demo at this point of its timeline (seconds); negative resumes normal play.</summary>
        public float PinTime { get; set; } = -1f;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (PinTime >= 0f)
            {
                Show(true);
                Animate(Mathf.Min(PinTime, Duration - 0.01f));
                return;
            }
            if (_learned) return;
            // A real hand nearby (or already holding) takes priority: fade the ghost out at once.
            bool quiet = _sling.IsReady && _sling.HoverProximity < 0.05f;
            if (!quiet)
            {
                _idle = 0f;
                if (_t >= 0f) { _t = -1f; Show(false); }
                return;
            }
            if (_t < 0f)
            {
                _idle += dt;
                if (_idle < _idleDelay) return;
                _idle = _idleDelay - _repeatEvery + Duration; // the next demo starts _repeatEvery after this one
                _t = 0f;
                Show(true);
            }
            _t += dt;
            if (_t >= Duration) { _t = -1f; Show(false); return; }
            Animate(_t);
        }

        void Animate(float t)
        {
            Vector3 anchor = _sling.transform.position;
            Vector3 toHead = _head != null ? (_head.position - anchor) : -_sling.transform.forward;
            Vector3 back = Vector3.ProjectOnPlane(toHead, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, -back).normalized;
            Vector3 pulled = anchor + back * 0.2f + Vector3.down * 0.04f;
            Vector3 start = anchor + right * 0.12f + back * 0.06f;

            // Timeline: approach 0-0.6, pinch 0.6-0.9, pull 0.9-1.9, hold 1.9-2.1, release 2.1-2.6, fade 2.6-3.2.
            Vector3 pinch;
            float spread; // gap between thumb and index (m)
            float fade = Mathf.Min(Smooth(t / 0.3f), 1f - Smooth((t - 2.6f) / 0.6f));
            Vector3 spark = anchor;
            float sparkGlow = 0f;
            if (t < 0.6f) { pinch = Vector3.Lerp(start, anchor, Smooth(t / 0.6f)); spread = 0.05f; }
            else if (t < 0.9f) { pinch = anchor; spread = Mathf.Lerp(0.05f, Held, Smooth((t - 0.6f) / 0.3f)); }
            else if (t < 2.1f)
            {
                pinch = Vector3.Lerp(anchor, pulled, Smooth((t - 0.9f) / 1.0f));
                spread = Held;
                spark = pinch;
                sparkGlow = 1f;
            }
            else
            {
                float r = Smooth((t - 2.1f) / 0.5f);
                pinch = pulled;
                spread = Mathf.Lerp(Held, 0.07f, r);
                // The ghost Spark flies out past the anchor, fading.
                spark = Vector3.Lerp(pulled, anchor - back * 0.7f + Vector3.up * 0.05f, Mathf.Sqrt(r));
                sparkGlow = 1f - r;
            }

            Vector3 gapDir = (Vector3.up * 0.8f + right * 0.6f).normalized;
            SetDot(_thumb, pinch - gapDir * spread * 0.5f, fade);
            SetDot(_index, pinch + gapDir * spread * 0.5f, fade);
            SetDot(_ghostSpark, spark, sparkGlow * fade * 0.8f);

            // The pull path: from the anchor to where the pinch is being drawn to, shown only while pulling.
            float pathAlpha = t > 0.9f && t < 2.3f ? fade : 0f;
            _pathPoints[0] = anchor;
            _pathPoints[1] = t > 0.9f ? Vector3.Lerp(anchor, pulled, Smooth((t - 0.9f) / 1.0f)) : anchor;
            _path.SetPositions(_pathPoints);
            _path.enabled = pathAlpha > 0.01f;
            _mpb.Clear();
            _mpb.SetFloat(IntensityId, 0.9f * pathAlpha);
            _path.SetPropertyBlock(_mpb);
        }

        void SetDot(Renderer r, Vector3 position, float intensity)
        {
            r.enabled = intensity > 0.01f;
            if (!r.enabled) return;
            r.transform.position = position;
            _mpb.Clear();
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, 1.4f * intensity);
            r.SetPropertyBlock(_mpb);
        }

        void Show(bool on)
        {
            _thumb.enabled = on;
            _index.enabled = on;
            _ghostSpark.enabled = on;
            _path.enabled = on;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
