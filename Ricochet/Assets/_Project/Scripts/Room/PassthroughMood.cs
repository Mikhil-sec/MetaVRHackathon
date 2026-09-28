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

        float _t = -1f; // < 0: idle

        /// <summary>0 = the untouched room, 1 = the full game mood.</summary>
        public float Amount { get; private set; }

        void OnEnable() => _playArea.Ready += OnReady;

        void OnDisable() => _playArea.Ready -= OnReady;

        void OnReady()
        {
            if (!PlayArea.IsDesktop && _layer != null) _t = 0f;
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.deltaTime / _easeSeconds;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
            Amount = k;
            _layer.SetBrightnessContrastSaturation(_brightness * k, _contrast * k, _saturation * k);
            if (_t >= 1f) _t = -1f;
        }
    }
}
