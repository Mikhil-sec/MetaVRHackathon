using Ricochet.Room;
using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// A title card for the run's big beats (the boss's entrance, the run sealed): a title and a subtitle, world-locked
    /// ~1.2 m ahead at eye level, inside the Glasses' central band. The letters drift together from wide tracking as it
    /// fades in and drift apart as it fades out, a cinematic beat with no camera motion. Real time, so slow motion
    /// never stretches it.
    /// </summary>
    public sealed class Banner : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] float _distance = 1.2f;
        [SerializeField] float _lift = 0.06f;
        [SerializeField] float _titleScale = 0.075f;
        [SerializeField] float _subtitleScale = 0.045f;
        [SerializeField] Material _scrimMaterial;   // a dark card behind the words, so they read over a bright room
        [SerializeField] float _cardOpacity = 0.66f;

        TextMeshPro _title, _subtitle;
        Transform _card;
        MeshRenderer _cardR;
        MaterialPropertyBlock _block;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        float _clock = -1f, _hold;
        Color _color;

        void Awake()
        {
            _title = Text("Title", 0f, _titleScale, FontStyles.Bold | FontStyles.UpperCase, true);
            _subtitle = Text("Subtitle", -0.085f, _subtitleScale, FontStyles.Bold, false);
            _subtitle.outlineWidth = 0.24f;
            if (_scrimMaterial != null)
            {
                _block = new MaterialPropertyBlock();
                var go = new GameObject("Card");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, -0.03f, 0.03f); // just behind the words
                go.AddComponent<MeshFilter>().sharedMesh = RewardPicker.Quad();
                _cardR = go.AddComponent<MeshRenderer>();
                _cardR.sharedMaterial = _scrimMaterial;
                _cardR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cardR.receiveShadows = false;
                _card = go.transform;
                go.SetActive(false);
            }
            gameObject.SetActive(true);
            _title.gameObject.SetActive(false);
            _subtitle.gameObject.SetActive(false);
        }

        TextMeshPro Text(string name, float y, float scale, FontStyles style, bool display)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = Vector3.one * scale;
            var text = go.AddComponent<TextMeshPro>();
            text.fontStyle = style;
            UiFonts.Use(text, display);
            style = text.fontStyle;
            text.fontSize = 10f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = style;
            text.rectTransform.sizeDelta = new Vector2(30f, 2f);
            text.outlineWidth = 0.28f;
            text.outlineColor = new Color32(16, 6, 30, 255);
            return text;
        }

        public void Show(string title, string subtitle, Color color, float seconds)
        {
            Transform head = _playArea.Head;
            Vector3 fwd = Vector3.ProjectOnPlane(_playArea.Seat.forward, Vector3.up).normalized;
            Vector3 pos = head.position + fwd * _distance + Vector3.up * _lift;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            _title.SetText(title);
            _subtitle.SetText(subtitle);
            _color = color;
            _hold = seconds;
            _clock = 0f;
            _title.gameObject.SetActive(true);
            _subtitle.gameObject.SetActive(true);
            if (_card != null)
            {
                // Sized to the settled title (once per banner, not per frame).
                _title.characterSpacing = 8f;
                _subtitle.characterSpacing = 4f;
                float w = Mathf.Max(_title.GetPreferredValues(title).x * _titleScale,
                                    _subtitle.GetPreferredValues(subtitle).x * _subtitleScale);
                _card.localScale = new Vector3(Mathf.Clamp(w + 0.34f, 0.6f, 1.15f), 0.3f, 1f);
                _card.gameObject.SetActive(true);
            }
            Update();
        }

        void Update()
        {
            if (_clock < 0f) return;
            _clock += RealTime.DeltaTime;
            const float fadeIn = 0.5f, fadeOut = 0.7f;
            float a = _clock < fadeIn ? _clock / fadeIn : Mathf.Clamp01(1f - (_clock - _hold) / fadeOut);
            float settle = 1f - Mathf.Pow(1f - Mathf.Clamp01(_clock / 1.2f), 3f);
            float leave = Mathf.Clamp01((_clock - _hold) / fadeOut);
            _title.characterSpacing = Mathf.Lerp(40f, 8f, settle) + 30f * leave;
            _subtitle.characterSpacing = Mathf.Lerp(24f, 4f, settle) + 20f * leave;
            // Bright, nearly white with the beat's hue: on the dark card it reads from the seat.
            Color hue = Color.Lerp(_color, Color.white, 0.55f);
            _title.color = new Color(hue.r, hue.g, hue.b, a);
            _subtitle.color = new Color(0.95f, 0.92f, 1f, a);
            if (_cardR != null)
            {
                _block.SetFloat(IntensityId, a * _cardOpacity);
                _cardR.SetPropertyBlock(_block);
            }
            if (_clock > _hold + fadeOut)
            {
                _clock = -1f;
                _title.gameObject.SetActive(false);
                _subtitle.gameObject.SetActive(false);
                if (_card != null) _card.gameObject.SetActive(false);
            }
        }
    }
}
