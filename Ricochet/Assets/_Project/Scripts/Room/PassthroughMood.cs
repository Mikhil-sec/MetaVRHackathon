using UnityEngine;

namespace Ricochet.Room
{
    /// <summary>
    /// "Light in a dark room" (CONCEPT section 6): once the play area is ready, the real room gently dims and
    /// desaturates so the game's light (RoomGlow, crystals, the Spark) reads as the brightest thing in it.
    /// Also the first beat of the opening (CONCEPT section 4, 0:00). Eases, then stops touching the layer.
    /// </summary>
    public sealed class PassthroughMood : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] OVRPassthroughLayer _layer;
        [SerializeField, Range(-1f, 0f)] float _brightness = -0.3f;
        [SerializeField, Range(-1f, 1f)] float _contrast = 0.1f;
        [SerializeField, Range(-1f, 0f)] float _saturation = -0.45f;
        [SerializeField] float _easeSeconds = 2f;

        [Header("Focus (drama): the room sinks further so the Spark is the spotlight")]
        [SerializeField, Range(-1f, 0f)] float _focusBrightness = -0.25f;
        [SerializeField, Range(-1f, 0f)] float _focusSaturation = -0.4f;

        float _t = -1f; // < 0: idle
        float _focus, _focusTarget;

        /// <summary>0 = the untouched room, 1 = the full game mood.</summary>
        public float Amount { get; private set; }

        void OnEnable() => _playArea.Ready += OnReady;

        void OnDisable() => _playArea.Ready -= OnReady;

        void OnReady()
        {
            if (!PlayArea.IsDesktop && _layer != null) _t = 0f;
        }

        /// <summary>0 = normal mood, 1 = everything but the game's light sinks away (eased in real time).</summary>
        public void SetFocus(float focus) => _focusTarget = Mathf.Clamp01(focus);

        void Update()
        {
            bool easing = _t >= 0f;
            bool focusing = !Mathf.Approximately(_focus, _focusTarget);
            if (!easing && !focusing) return;

            if (easing)
            {
                _t += Time.unscaledDeltaTime / _easeSeconds;
                Amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
                if (_t >= 1f) _t = -1f;
            }
            if (focusing)
            {
                float rate = _focusTarget > _focus ? 6f : 2.5f;
                _focus = Mathf.Lerp(_focus, _focusTarget, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
                if (Mathf.Abs(_focus - _focusTarget) < 0.005f) _focus = _focusTarget;
            }
            if (PlayArea.IsDesktop || _layer == null) return;
            float k = Amount;
            _layer.SetBrightnessContrastSaturation(
                _brightness * k + _focusBrightness * _focus,
                _contrast * k,
                _saturation * k + _focusSaturation * _focus);
        }
    }
}
