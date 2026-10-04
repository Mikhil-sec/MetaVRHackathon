using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Zero-text onboarding (CONCEPT section 4): if the player has not launched yet and leaves the sling alone for a
    /// few seconds, a ghost hand of light demonstrates the one gesture: it drifts in open, pinches the Spark, draws it
    /// back along a glowing pull path, and lets go as a ghost Spark flies off. The hand is a real hand mesh baked from
    /// the tracked hand in both poses (Dev/HandBake), morphed open-to-pinch in its shader (GhostHand).
    /// Visual only: it never touches the real sling or Spark. It stops for good after the first real launch, and
    /// yields the moment a real hand comes near. Real time, zero GC.
    /// </summary>
    public sealed class GhostHandDemo : MonoBehaviour
    {
        const float Duration = 3.2f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int PinchId = Shader.PropertyToID("_Pinch");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int SweepId = Shader.PropertyToID("_Sweep");

        [SerializeField] Sling _sling;
        [SerializeField] Spark _spark;          // hidden while the ghost carries its stand-in, so the pull reads
        [SerializeField] Transform _head;
        [SerializeField] Renderer _hand;        // GhostHand mesh, posed by its pinch point in the sling's frame
        [SerializeField] Renderer _glint;       // Halo quad at the pinch point: flares as the fingers close
        [SerializeField] Renderer _ghostSpark;
        [SerializeField] LineRenderer _path;    // the pull path (Ribbon glow)
        [SerializeField] float _idleDelay = 4f;
        [SerializeField] float _repeatEvery = 8f;
        [SerializeField] Color _color = new(0.7f, 0.95f, 1f);

        MaterialPropertyBlock _mpb;
        float _idle, _t = -1f;
        bool _learned, _carrying;
        readonly Vector3[] _pathPoints = new Vector3[2];

        const string LearnedKey = "Ricochet.FirstLaunch";

        void Awake()
        {
            // The very first time (nothing ever launched on this headset) the demo comes sooner.
            if (PlayerPrefs.GetInt(LearnedKey, 0) == 0) _idleDelay = Mathf.Min(_idleDelay, 2.5f);
            _mpb = new MaterialPropertyBlock();
            Show(false);
            _path.positionCount = 2;
            _path.useWorldSpace = true;
        }

        void OnEnable() => _sling.Launched += OnLaunched;

        void OnDisable()
        {
            _sling.Launched -= OnLaunched;
            Carry(false);
        }

        void OnLaunched(Vector3 v)
        {
            if (!_learned && PlayerPrefs.GetInt(LearnedKey, 0) == 0) { PlayerPrefs.SetInt(LearnedKey, 1); PlayerPrefs.Save(); }
            _learned = true;
            _t = -1f;
            Show(false);
        }

        /// <summary>Dev/capture: holds the demo at this point of its timeline (seconds); negative resumes normal play.</summary>
        public float PinTime { get; set; } = -1f;

        void Update()
        {
            float dt = RealTime.DeltaTime;
            if (PinTime >= 0f)
            {
                Show(true);
                Animate(Mathf.Min(PinTime, Duration - 0.01f));
                return;
            }
            if (_t < 0f) Carry(false);
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
            // The hand drifts in from where a resting right hand would be: low, to the right, nearer the body.
            Vector3 start = anchor + right * 0.14f + back * 0.1f + Vector3.down * 0.07f;

            // Timeline: approach 0-0.6, pinch 0.6-0.9, pull 0.9-1.9, hold 1.9-2.1, release 2.1-2.6, fade 2.6-3.2.
            Vector3 pinch;
            float closed; // 0 open hand .. 1 pinching
            float fade = Mathf.Min(Smooth(t / 0.3f), 1f - Smooth((t - 2.6f) / 0.6f));
            Vector3 spark = anchor;
            float sparkGlow = 0f;
            if (t < 0.6f) { pinch = Vector3.Lerp(start, anchor, Smooth(t / 0.6f)); closed = 0f; }
            else if (t < 0.9f) { pinch = anchor; closed = Smooth((t - 0.6f) / 0.3f); }
            else if (t < 2.1f)
            {
                pinch = Vector3.Lerp(anchor, pulled, Smooth((t - 0.9f) / 1.0f));
                closed = 1f;
                spark = pinch;
                sparkGlow = 1f;
            }
            else
            {
                float r = Smooth((t - 2.1f) / 0.5f);
                pinch = pulled;
                closed = 1f - Smooth((t - 2.1f) / 0.25f); // the fingers spring open
                // The ghost Spark flies out past the anchor, fading.
                spark = Vector3.Lerp(pulled, anchor - back * 0.7f + Vector3.up * 0.05f, Mathf.Sqrt(r));
                sparkGlow = 1f - r;
            }

            if (_hand != null)
            {
                _hand.enabled = fade > 0.01f;
                // The hand was baked around its pinch point in the sling's frame, so this puts the fingertips on the Spark.
                _hand.transform.SetPositionAndRotation(pinch, _sling.transform.rotation);
                _mpb.Clear();
                _mpb.SetFloat(PinchId, closed);
                _mpb.SetFloat(FadeId, fade);
                _mpb.SetFloat(SweepId, t < 1.1f ? t / 0.8f - 0.15f : -1f); // a band of light runs up the arriving hand
                _hand.SetPropertyBlock(_mpb);
            }
            // The glint at the fingertips flares as they close on the Spark and fades through the pull.
            float flare = t < 0.6f ? 0f : t < 0.9f ? closed : t < 2.1f ? Mathf.Lerp(1f, 0.35f, Smooth((t - 0.9f) / 0.6f)) : 0f;
            SetDot(_glint, pinch, flare * fade);
            // From the pinch until it has flown, the Spark *is* the ghost's: the real one steps out of the sling, so the
            // hand visibly draws the Spark back instead of tugging at nothing beside it. It is back when the ghost fades.
            Carry(t >= 0.9f && t < 2.6f);
            SetDot(_ghostSpark, spark, sparkGlow * fade * 1.1f);

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
            if (_hand != null) _hand.enabled = on;
            _glint.enabled = on;
            _ghostSpark.enabled = on;
            _path.enabled = on;
            if (!on) Carry(false);
        }

        // Visuals only, and only while the Spark rests in the sling: every way out of the demo (a hand coming near, any
        // launch, the end of the demo, disabling) gives it back at once.
        void Carry(bool on)
        {
            on &= _spark != null && _sling.IsReady;
            if (on == _carrying) return;
            _carrying = on;
            _spark.SetVisible(!on);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
