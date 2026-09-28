using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Floating "+points" on each crystal hit (docs/TECH_GUIDE.md sections 4-5): pop in with overshoot, rise, fade,
    /// always face the player, and escalate in size and color with the combo. Pooled; no per-frame allocations.
    /// Scaled with distance so the text never drops below ~1.5 degrees of view.
    /// </summary>
    public sealed class ScorePopups : MonoBehaviour
    {
        [SerializeField] int _poolSize = 12;
        [SerializeField] float _lifetime = 0.9f;
        [SerializeField] float _rise = 0.16f;
        [SerializeField] float _sizePerMeter = 0.11f;   // transform scale per meter of viewing distance (~3 deg tall)
        [SerializeField] Color _lowColor = new(0.9f, 0.82f, 1f);
        [SerializeField] Color _highColor = new(1f, 0.85f, 0.25f);
        [SerializeField] int _comboForHighColor = 6;

        struct Popup
        {
            public TextMeshPro Text;
            public Vector3 Origin;
            public float Age;
            public float Size;
            public Color Color;
            public bool Active;
        }

        Popup[] _pool;
        int _next;
        Transform _camera;

        void Awake()
        {
            _pool = new Popup[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                var go = new GameObject("Popup" + i);
                go.transform.SetParent(transform, false);
                var text = go.AddComponent<TextMeshPro>();
                text.fontSize = 10f; // 10 pt ~ 1 world unit line height; the transform scale sets the real size
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.fontStyle = FontStyles.Bold;
                text.rectTransform.sizeDelta = new Vector2(4f, 1.2f);
                // A dark outline keeps the number readable over any real room (one material instance per popup).
                text.outlineWidth = 0.25f;
                text.outlineColor = new Color32(20, 10, 40, 255);
                go.SetActive(false);
                _pool[i].Text = text;
            }
        }

        public void Show(Vector3 position, int points, int combo)
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            if (_camera == null) return;

            ref Popup p = ref _pool[_next];
            _next = (_next + 1) % _pool.Length;

            // Sit slightly toward the viewer so the text never clips into the wall the crystal grows from.
            Vector3 toEye = (_camera.position - position).normalized;
            p.Origin = position + toEye * 0.12f;
            p.Age = 0f;
            p.Active = true;
            float comboScale = 1f + 0.08f * Mathf.Min(combo, 8);
            p.Size = _sizePerMeter * Vector3.Distance(_camera.position, position) * comboScale;
            p.Color = Color.Lerp(_lowColor, _highColor, Mathf.Clamp01(combo / (float)_comboForHighColor));
            p.Text.SetText("+{0}", points);
            p.Text.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                ref Popup p = ref _pool[i];
                if (!p.Active) continue;
                p.Age += dt;
                float t = p.Age / _lifetime;
                if (t >= 1f)
                {
                    p.Active = false;
                    p.Text.gameObject.SetActive(false);
                    continue;
                }

                float pop = t < 0.18f ? OutBack(t / 0.18f) : 1f;
                float rise = 1f - (1f - t) * (1f - t); // ease out
                Vector3 pos = p.Origin + Vector3.up * (_rise * rise);
                var tr = p.Text.transform;
                tr.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - _camera.position, Vector3.up));
                tr.localScale = Vector3.one * (p.Size * pop);

                Color c = p.Color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                p.Text.color = c;
            }
        }

        static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
