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
        [SerializeField] float _subtitleScale = 0.035f;

        TextMeshPro _title, _subtitle;
        float _clock = -1f, _hold;
        Color _color;

        void Awake()
        {
            _title = Text("Title", 0f, _titleScale, FontStyles.Bold | FontStyles.UpperCase);
            _subtitle = Text("Subtitle", -0.075f, _subtitleScale, FontStyles.Normal);
            gameObject.SetActive(true);
            _title.gameObject.SetActive(false);
            _subtitle.gameObject.SetActive(false);
        }

        TextMeshPro Text(string name, float y, float scale, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = Vector3.one * scale;
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 10f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = style;
            text.rectTransform.sizeDelta = new Vector2(30f, 2f);
            text.outlineWidth = 0.18f;
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
            Update();
        }

        void Update()
        {
            if (_clock < 0f) return;
            _clock += Time.unscaledDeltaTime;
            const float fadeIn = 0.5f, fadeOut = 0.7f;
            float a = _clock < fadeIn ? _clock / fadeIn : Mathf.Clamp01(1f - (_clock - _hold) / fadeOut);
            float settle = 1f - Mathf.Pow(1f - Mathf.Clamp01(_clock / 1.2f), 3f);
            float leave = Mathf.Clamp01((_clock - _hold) / fadeOut);
            _title.characterSpacing = Mathf.Lerp(40f, 8f, settle) + 30f * leave;
            _subtitle.characterSpacing = Mathf.Lerp(24f, 4f, settle) + 20f * leave;
            _title.color = new Color(Mathf.Min(1f, _color.r + 0.3f), Mathf.Min(1f, _color.g + 0.3f), Mathf.Min(1f, _color.b + 0.3f), a);
            _subtitle.color = new Color(0.9f, 0.86f, 1f, a * 0.9f);
            if (_clock > _hold + fadeOut)
            {
                _clock = -1f;
                _title.gameObject.SetActive(false);
                _subtitle.gameObject.SetActive(false);
            }
        }
    }
}
