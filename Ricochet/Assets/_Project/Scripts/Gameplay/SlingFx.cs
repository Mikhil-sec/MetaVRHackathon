using Ricochet.Audio;
using Ricochet.Input;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The sling's look and voice (TECH_GUIDE section 6: every pinch, grab and release gets a sound and a visual
    /// state change, because there are no haptics). All of it is additive light over passthrough:
    /// - an energy band between two posts through the Spark, stretched by the pull, heating cyan to gold with charge,
    ///   and springing home with a damped twang on release;
    /// - a hover ring that tightens and turns gold as a hand comes within grab reach, plus a "grab me" ripple when idle;
    /// - a fingertip glow at each hand's pinch point, growing with pinch strength;
    /// - plucked-string audio: grab, a rising pentatonic ratchet while pulling, the release twang, a soft cancel.
    /// Visual only, zero GC per frame (one reused MaterialPropertyBlock, no allocations in LateUpdate).
    /// </summary>
    public sealed class SlingFx : MonoBehaviour
    {
        const int BandSegments = 6; // per side
        const int BandPoints = BandSegments * 2 + 1;
        const int MaxTips = 2;

        [Header("Refs")]
        [SerializeField] Sling _sling;
        [SerializeField] Spark _spark;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] LineRenderer _band;
        [SerializeField] Renderer _postLeft;
        [SerializeField] Renderer _postRight;
        [SerializeField] Renderer _ring;
        [SerializeField] Renderer _ripple;
        [SerializeField] Renderer _flash;
        [SerializeField] Renderer[] _tips = new Renderer[MaxTips];
        [SerializeField] RewardPicker _picker;  // the empty sling steps aside while you choose: its band crossed the centre orb's words

        [Header("Look")]
        [SerializeField] Color _idle = new(0.35f, 0.85f, 1f);
        [SerializeField] Color _hot = new(1f, 0.72f, 0.3f);
        [SerializeField] Color _reach = new(1f, 0.92f, 0.6f);
        [SerializeField] float _postSpacing = 0.065f;
        [SerializeField] float _bandWidthRest = 0.014f;
        [SerializeField] float _bandWidthTaut = 0.009f;
        [SerializeField] float _bandSag = 0.006f;

        [Header("Twang spring")]
        [SerializeField] float _springFrequency = 5.5f; // Hz
        [SerializeField] float _springDamping = 0.11f;  // damping ratio: low rings, high settles
        [SerializeField] float _cancelDamping = 0.55f;

        [Header("Grab-me ripple")]
        [SerializeField] float _rippleIdleDelay = 1.2f;
        [SerializeField] float _ripplePeriod = 2.2f;
        [SerializeField] float _rippleDuration = 0.9f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int SpinId = Shader.PropertyToID("_Spin");

        readonly Vector3[] _points = new Vector3[BandPoints];
        readonly IPinchInput[] _tipInputs = new IPinchInput[MaxTips];
        MaterialPropertyBlock _mpb;
        Vector3 _ringScale, _rippleScale, _flashScale, _tipScale;

        Vector3 _spring, _springVel;  // pouch offset from the anchor after release/cancel
        float _damping;
        Vector3 _lastPull;
        float _hover;                 // eased HoverProximity
        float _heat;                  // eased charge while pulling
        float _spin;
        float _idleTime, _rippleClock = -1f;
        float _flashTime = 1f, _flashPower;
        float _grabPop;
        float _aside;                 // 1 while a reward choice is open
        int _pullStep = -1;
        int _tipCount;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _ringScale = _ring.transform.localScale;
            _rippleScale = _ripple.transform.localScale;
            _flashScale = _flash.transform.localScale;
            _tipScale = _tips[0].transform.localScale;
            _band.positionCount = BandPoints;
            _band.useWorldSpace = true;
        }

        void OnEnable()
        {
            _sling.Grabbed += OnGrabbed;
            _sling.Launched += OnLaunched;
            _sling.Cancelled += OnCancelled;
        }

        void OnDisable()
        {
            _sling.Grabbed -= OnGrabbed;
            _sling.Launched -= OnLaunched;
            _sling.Cancelled -= OnCancelled;
        }

        void OnGrabbed()
        {
            _sfx.PlayGrab(_sling.transform.position);
            _grabPop = 1f;
            _pullStep = -1;
            Flash(0.35f);
        }

        void OnLaunched(Vector3 velocity)
        {
            float charge = _sling.Charge;
            // The pouch was at anchor - pull; it snaps through the anchor and rings.
            _spring = -_sling.PullVector;
            _springVel = Vector3.zero;
            _damping = _springDamping;
            _sfx.PlayTwang(_sling.transform.position, charge);
            Flash(0.6f + charge);
            _pullStep = -1;
        }

        void OnCancelled()
        {
            _spring = -_lastPull;
            _springVel = Vector3.zero;
            _damping = _cancelDamping;
            _sfx.PlayCancel(_sling.transform.position);
            _pullStep = -1;
        }

        void Flash(float power)
        {
            _flashTime = 0f;
            _flashPower = power;
        }

        void LateUpdate()
        {
            // UI-like feedback runs on real time: it must stay crisp even while game time is slowed.
            float dt = RealTime.DeltaTime;
            Vector3 anchor = _sling.transform.position;
            bool ready = _sling.IsReady, pulling = _sling.IsPulling;

            StepSpring(dt);
            _hover = Mathf.Lerp(_hover, ready ? _sling.HoverProximity : 0f, 1f - Mathf.Exp(-14f * dt));
            _heat = Mathf.Lerp(_heat, pulling ? _sling.Charge : 0f, 1f - Mathf.Exp(-(pulling ? 20f : 6f) * dt));
            _grabPop = Mathf.Max(0f, _grabPop - dt * 5f);
            _aside = Mathf.MoveTowards(_aside, _picker != null && _picker.IsChoosing ? 1f : 0f, dt * 4f);
            if (pulling) _lastPull = _sling.PullVector;

            // Pouch: the Spark while it sits on the sling (bent by the cancel spring), the bare band once it flies.
            Vector3 pouch;
            if (pulling) pouch = _spark.transform.position;
            else if (ready)
            {
                pouch = _spark.transform.position + _spring;
                if (_spring.sqrMagnitude > 1e-6f) _spark.Hold(pouch);
            }
            else pouch = anchor + _spring;

            UpdateBand(anchor, pouch, pulling);
            UpdateRing(pouch, ready, pulling, dt);
            UpdateRipple(anchor, ready, dt);
            UpdateFlash(anchor, dt);
            UpdateTips(anchor, pulling);
            UpdatePullTicks(pulling, anchor);

            // Modest: a drawn Spark sits ~25 cm from the eye, where a big halo would blow out the hand holding it.
            _spark.HoldGlow = pulling ? 0.9f + 0.35f * _heat + 0.3f * _grabPop : 1f + 0.35f * _hover;
        }

        void StepSpring(float dt)
        {
            if (_spring.sqrMagnitude < 1e-8f && _springVel.sqrMagnitude < 1e-8f)
            {
                _spring = _springVel = Vector3.zero;
                return;
            }
            float w = 2f * Mathf.PI * _springFrequency;
            float c = 2f * _damping * w;
            // Semi-implicit Euler in fixed substeps: stable at any frame rate.
            float remaining = Mathf.Min(dt, 0.1f);
            while (remaining > 0f)
            {
                float h = Mathf.Min(remaining, 1f / 240f);
                _springVel += (-w * w * _spring - c * _springVel) * h;
                _spring += _springVel * h;
                remaining -= h;
            }
        }

        void UpdateBand(Vector3 anchor, Vector3 pouch, bool pulling)
        {
            Vector3 right = _sling.transform.right * _postSpacing;
            Vector3 left = anchor - right, rightPost = anchor + right;
            float stretch = (pouch - anchor).magnitude;
            float sag = _bandSag * (1f - Mathf.Clamp01(stretch / 0.08f)); // slack at rest, taut when drawn

            for (int i = 0; i <= BandSegments; i++)
            {
                float t = (float)i / BandSegments;
                float bow = Mathf.Sin(t * Mathf.PI) * sag;
                _points[i] = Vector3.Lerp(left, pouch, t) + Vector3.down * bow;
                _points[BandPoints - 1 - i] = Vector3.Lerp(rightPost, pouch, t) + Vector3.down * bow;
            }
            _band.SetPositions(_points);

            float bright = 0.55f + 0.35f * _hover + 1.1f * _heat + 0.8f * _grabPop +
                           Mathf.Clamp01(_springVel.magnitude * 0.6f); // the twang flares as it rings
            // Vertex colors are 8-bit, so the hue goes there and the HDR brightness through the material block.
            Color c = Color.Lerp(_idle, _hot, _heat);
            c.a = 1f;
            _band.startColor = c;
            _band.endColor = c;
            _mpb.Clear();
            float shown = 1f - 0.85f * _aside;
            _mpb.SetFloat(IntensityId, 1.2f * bright * shown);
            _band.SetPropertyBlock(_mpb);
            _band.widthMultiplier = Mathf.Lerp(_bandWidthRest, _bandWidthTaut, Mathf.Clamp01(stretch / 0.2f));

            float postGlow = (0.8f + 0.6f * _hover + 1.6f * _heat) * shown;
            SetHalo(_postLeft, left, Color.Lerp(_idle, _hot, _heat), postGlow);
            SetHalo(_postRight, rightPost, Color.Lerp(_idle, _hot, _heat), postGlow);
        }

        void UpdateRing(Vector3 pouch, bool ready, bool pulling, float dt)
        {
            bool show = ready || pulling;
            _ring.enabled = show;
            if (!show) return;

            // Winds faster as it charges; drifts lazily at rest.
            _spin += dt * (0.08f + 0.35f * _hover + 1.4f * _heat);
            float radius, intensity;
            Color color;
            if (pulling)
            {
                radius = Mathf.Lerp(0.3f, 0.22f, _heat) + 0.1f * _grabPop;
                intensity = 0.5f + 1.3f * _heat + 1.2f * _grabPop;
                color = Color.Lerp(_reach, _hot, _heat);
            }
            else
            {
                radius = Mathf.Lerp(0.42f, 0.3f, _hover);
                intensity = 0.18f + 0.9f * _hover;
                color = _sling.InReach ? _reach : _idle;
            }
            _ring.transform.position = pouch;
            _ring.transform.localScale = _ringScale;
            _mpb.Clear();
            _mpb.SetColor(ColorId, color);
            _mpb.SetFloat(IntensityId, intensity);
            _mpb.SetFloat(RadiusId, radius);
            _mpb.SetFloat(SpinId, _spin);
            _ring.SetPropertyBlock(_mpb);
        }

        void UpdateRipple(Vector3 anchor, bool ready, float dt)
        {
            // "Grab me": a slow ripple while the sling waits untouched; a hand drawing near takes over with the ring.
            bool idle = ready && _hover < 0.15f;
            _idleTime = idle ? _idleTime + dt : 0f;
            if (idle && _idleTime >= _rippleIdleDelay && _rippleClock < 0f) _rippleClock = 0f;
            if (!idle) _rippleClock = -1f;

            float phase = _rippleClock >= 0f ? _rippleClock : -1f;
            if (_rippleClock >= 0f)
            {
                _rippleClock += dt;
                if (_rippleClock >= _ripplePeriod) _rippleClock = 0f;
            }
            bool show = phase >= 0f && phase < _rippleDuration;
            _ripple.enabled = show;
            if (!show) return;

            float t = phase / _rippleDuration;
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);
            _ripple.transform.position = anchor;
            _ripple.transform.localScale = _rippleScale;
            _mpb.Clear();
            _mpb.SetColor(ColorId, _idle);
            _mpb.SetFloat(IntensityId, 0.9f * (1f - t) * (1f - t));
            _mpb.SetFloat(RadiusId, Mathf.Lerp(0.1f, 0.46f, ease));
            _mpb.SetFloat(SpinId, 0f);
            _ripple.SetPropertyBlock(_mpb);
        }

        void UpdateFlash(Vector3 anchor, float dt)
        {
            const float Duration = 0.22f;
            _flashTime += dt;
            bool show = _flashTime < Duration;
            _flash.enabled = show;
            if (!show) return;
            float t = _flashTime / Duration;
            _flash.transform.position = anchor;
            _flash.transform.localScale = _flashScale * Mathf.Lerp(0.3f, 1f + 0.4f * _flashPower, Mathf.Sqrt(t));
            SetIntensity(_flash, Color.Lerp(_reach, Color.white, 0.5f), _flashPower * 2.2f * (1f - t) * (1f - t));
        }

        void UpdateTips(Vector3 anchor, bool pulling)
        {
            if (_tipCount == 0) CollectHands();
            for (int i = 0; i < MaxTips; i++)
            {
                var input = i < _tipCount ? _tipInputs[i] : null;
                var tip = _tips[i];
                if (input == null || !input.IsTracked) { tip.enabled = false; continue; }

                float s = input.PinchStrength;
                bool near = Vector3.Distance(input.PinchPoint, anchor) <= _sling.GrabRadius * input.ReachScale;
                bool holding = pulling && ReferenceEquals(input, _sling.ActiveInput);
                float glow = s * s;
                if (glow < 0.04f && !holding) { tip.enabled = false; continue; }

                tip.enabled = true;
                tip.transform.position = input.PinchPoint;
                tip.transform.localScale = _tipScale * Mathf.Lerp(0.5f, 1.2f, s);
                Color c = holding ? Color.Lerp(_reach, _hot, _heat) : near ? _reach : _idle;
                SetIntensity(tip, c, 0.25f + 1.1f * glow + (holding ? 0.6f : 0f));
            }
        }

        void CollectHands()
        {
            var inputs = _sling.Inputs;
            for (int i = 0; i < inputs.Count && _tipCount < MaxTips; i++)
                if (inputs[i].IsHand) _tipInputs[_tipCount++] = inputs[i];
        }

        void UpdatePullTicks(bool pulling, Vector3 anchor)
        {
            if (!pulling || !_sling.AboveMinPull)
            {
                if (!pulling) _pullStep = -1;
                return;
            }
            // One rising note per step of stretch; hysteresis so a steady hand never chatters on a boundary.
            float scaled = _sling.Charge * (SfxPlayer.PullSteps - 1);
            int step = Mathf.FloorToInt(scaled + 0.001f);
            if (step > _pullStep)
            {
                _pullStep = step;
                _sfx.PlayPullTick(step, anchor);
            }
            else if (step < _pullStep && scaled < _pullStep - 0.25f)
            {
                _pullStep = step;
            }
        }

        void SetHalo(Renderer r, Vector3 position, Color color, float intensity)
        {
            r.transform.position = position;
            SetIntensity(r, color, intensity);
        }

        void SetIntensity(Renderer r, Color color, float intensity)
        {
            _mpb.Clear();
            _mpb.SetColor(ColorId, color);
            _mpb.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(_mpb);
        }
    }
}
