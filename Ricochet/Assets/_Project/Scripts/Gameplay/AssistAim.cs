using Ricochet.Audio;
using Ricochet.Input;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Accessibility, "Look and Fire" (CONCEPT section 2 Accessibility Forward, TECH_GUIDE section 6 gaze aim). With
    /// the assist on, the crystal you look at (GazeFocus) is the target, and the shot is solved to hit it:
    /// <list type="bullet">
    /// <item>One hand, anywhere: pinch (no need to reach the sling), the band draws itself back, release to fire.</item>
    /// <item>No hands at all: keep looking; a ring closes in on the crystal and the shot fires. Reward orbs charge
    /// the same way (RewardPicker). It pauses for a while after any pinch, so a hand player is never surprised.</item>
    /// </list>
    /// Turned on and off with zero text and without hands: look at the small eye under the shield readout until its
    /// ring closes (or pinch it). Remembered per headset.
    /// </summary>
    public sealed class AssistAim : MonoBehaviour
    {
        const string PrefKey = "Ricochet.Assist";
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");

        /// <summary>Look and Fire is on.</summary>
        public static bool Enabled { get; private set; }
        /// <summary>Seconds of looking at a reward orb that take it.</summary>
        public const float PickDwell = 2.2f;
        /// <summary>The assist's color: every ring that closes as a look holds (shot, toggle, reward orb).</summary>
        public static Color Tint { get; private set; } = new(0.55f, 0.95f, 1f);

        [SerializeField] PlayArea _playArea;
        [SerializeField] Sling _sling;
        [SerializeField] GazeFocus _focus;
        [SerializeField] RewardPicker _picker;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] Renderer _dwellRing;      // Ring quad: closes in on the target as the look holds
        [SerializeField] Renderer _toggleGlyph;    // IntentGlyph quad (the eye)
        [SerializeField] Renderer _toggleRing;     // Ring quad around the eye: closes as the toggle look holds
        [SerializeField] float _fireDwell = 1.6f;
        [SerializeField] float _toggleDwell = 1.3f;
        [SerializeField] float _toggleGazeDeg = 4f;
        [SerializeField] float _pinchPause = 10f;  // seconds after a pinch before looking alone fires again
        // From the sling, in its frame: under the shield readout (right of the sling), still inside +/-25 degrees.
        [SerializeField] Vector3 _toggleOffset = new(0.2f, -0.075f, 0.02f);
        [SerializeField] float _toggleSize = 0.03f;
        [SerializeField] Color _color = new(0.55f, 0.95f, 1f);

        MaterialPropertyBlock _mpb;
        Crystal _dwellTarget;
        float _dwell, _toggleHold, _sincePinch = 999f, _toggleFlash, _shown;
        bool _seenSpark, _toggleLatched; // latched after a flip until the look leaves the eye (else it flips back and forth)
        bool[] _wasPinching = System.Array.Empty<bool>();

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            Tint = _color;
            Enabled = PlayerPrefs.GetInt(PrefKey, 0) == 1;
            _sling.AssistTarget = Target;
            _dwellRing.enabled = false;
            _toggleRing.enabled = false;
            _toggleGlyph.enabled = false;
        }

        /// <summary>Dev/tests: switch the assist without looking at the eye.</summary>
        public static void Set(bool on)
        {
            Enabled = on;
            PlayerPrefs.SetInt(PrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[Ricochet] Look and Fire {(on ? "on" : "off")}");
        }

        Vector3? Target()
        {
            if (!Enabled || _focus.Focused == null) return null;
            return _focus.Focused.transform.position;
        }

        void Update()
        {
            float dt = RealTime.DeltaTime;
            bool pinchStarted = ScanPinches(out Vector3 pinchAt);
            if (pinchStarted) _sincePinch = 0f;
            else _sincePinch += dt;

            UpdateToggle(dt, pinchStarted, pinchAt);
            UpdateDwellFire(dt);
        }

        bool ScanPinches(out Vector3 at)
        {
            at = default;
            var inputs = _sling.Inputs;
            if (_wasPinching.Length != inputs.Count) _wasPinching = new bool[inputs.Count];
            bool started = false;
            for (int i = 0; i < inputs.Count; i++)
            {
                bool p = inputs[i].IsTracked && inputs[i].IsPinching && !float.IsInfinity(inputs[i].ReachScale);
                if (p && !_wasPinching[i]) { started = true; at = inputs[i].PinchPoint; }
                _wasPinching[i] = p;
            }
            return started;
        }

        void UpdateDwellFire(float dt)
        {
            bool aiming = Enabled && _sling.IsReady && _focus.Focused != null && _focus.GazeOnFocused &&
                          _sincePinch > _pinchPause && (_picker == null || !_picker.IsChoosing);
            if (!aiming || _focus.Focused != _dwellTarget)
            {
                _dwellTarget = aiming ? _focus.Focused : null;
                _dwell = 0f;
            }
            else
            {
                int before = Mathf.FloorToInt(_dwell / _fireDwell * 3f);
                _dwell += dt;
                int after = Mathf.FloorToInt(_dwell / _fireDwell * 3f);
                if (after > before && after < 3) _sfx.PlayPullTick(after, _dwellTarget.transform.position);
                if (_dwell >= _fireDwell)
                {
                    _sling.FireAt(_dwellTarget.transform.position, "dwell");
                    _dwellTarget = null;
                    _dwell = 0f;
                }
            }

            // The ring closes from twice the reticle's size onto the crystal as the look holds.
            bool show = _dwellTarget != null && _dwell > 0.05f;
            _dwellRing.enabled = show;
            if (!show) return;
            float k = Mathf.Clamp01(_dwell / _fireDwell);
            Vector3 p = _dwellTarget.transform.position;
            float distance = _playArea.Head != null ? Vector3.Distance(_playArea.Head.position, p) : 2.5f;
            _dwellRing.transform.position = p;
            _dwellRing.transform.localScale = Vector3.one * (0.34f * Mathf.Clamp(distance / 2.5f, 0.6f, 2.5f) * Mathf.Lerp(2.4f, 1.05f, k * k));
            _mpb.Clear();
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, 0.6f + 2.2f * k);
            _mpb.SetFloat(RadiusId, 0.4f);
            _dwellRing.SetPropertyBlock(_mpb);
        }

        void UpdateToggle(float dt, bool pinchStarted, Vector3 pinchAt)
        {
            // Shown with the HUD: after the first Spark has arrived, and not while a reward is offered.
            if (!_seenSpark) _seenSpark = _sling.IsReady || _sling.IsPulling;
            bool visible = _seenSpark && (_picker == null || !_picker.IsChoosing);
            _shown = Mathf.MoveTowards(_shown, visible ? 1f : 0f, dt * 3f);
            _toggleGlyph.enabled = _shown > 0.01f;
            _toggleFlash = Mathf.Max(0f, _toggleFlash - dt * 2f);
            if (!_toggleGlyph.enabled) { _toggleRing.enabled = false; _toggleHold = 0f; return; }

            Transform head = _playArea.Head;
            Vector3 pos = _sling.transform.TransformPoint(_toggleOffset);
            _toggleGlyph.transform.position = pos;
            if (head != null) _toggleGlyph.transform.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);

            float angle = EyeGaze.Ray(head, out Vector3 eye, out Vector3 gaze) ? Vector3.Angle(gaze, pos - eye) : 180f;
            if (angle > _toggleGazeDeg * 1.5f) _toggleLatched = false;
            bool looked = visible && !_toggleLatched && angle < _toggleGazeDeg;
            bool poked = visible && pinchStarted && Vector3.Distance(pinchAt, pos) < 0.05f;
            _toggleHold = looked ? _toggleHold + dt : Mathf.MoveTowards(_toggleHold, 0f, dt * 3f);
            if (poked || _toggleHold >= _toggleDwell)
            {
                Set(!Enabled);
                _toggleHold = 0f;
                _toggleFlash = 1f;
                _toggleLatched = true;
                _focus.Clear();
                if (Enabled) _sfx.PlayChord(pos);
                else _sfx.PlayCancel(pos);
            }

            float k = Mathf.Clamp01(_toggleHold / _toggleDwell);
            float size = _toggleSize * (1f + 0.35f * k + 0.5f * _toggleFlash) * Mathf.Lerp(0.6f, 1f, _shown);
            _toggleGlyph.transform.localScale = Vector3.one * size;
            _mpb.Clear();
            _mpb.SetFloat(KindId, 2f); // the eye
            _mpb.SetColor(ColorId, Enabled ? _color : new Color(0.6f, 0.62f, 0.75f));
            _mpb.SetFloat(IntensityId, ((Enabled ? 1.5f : 0.55f) + 1.2f * k + 1.5f * _toggleFlash) * _shown);
            _toggleGlyph.SetPropertyBlock(_mpb);

            _toggleRing.enabled = k > 0.02f;
            if (!_toggleRing.enabled) return;
            _toggleRing.transform.position = pos;
            _toggleRing.transform.localScale = Vector3.one * (_toggleSize * 2.2f * Mathf.Lerp(2.2f, 1f, k * k));
            _mpb.Clear();
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, 0.6f + 2f * k);
            _mpb.SetFloat(RadiusId, 0.4f);
            _toggleRing.SetPropertyBlock(_mpb);
        }
    }
}
