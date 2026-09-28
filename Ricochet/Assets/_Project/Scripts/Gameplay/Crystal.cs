using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// A peg grown on a room surface. Hit once to light it (it stays solid, as in Peggle);
    /// lit crystals are popped in sequence when the shot ends.
    /// All animation is visual-only (CrystalGlass _Scale/_Glow), so the collider never changes size.
    /// </summary>
    public sealed class Crystal : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");

        const float AppearSeconds = 0.35f;
        const float LitGlow = 0.7f;

        [SerializeField] Renderer _renderer;
        [SerializeField] Color _idleColor = new(0.55f, 0.35f, 1f);
        [SerializeField] Color _litColor = new(1f, 0.8f, 0.35f);

        MaterialPropertyBlock _block;
        float _punch;
        float _flash;
        float _spinSpeed;
        float _phase;
        float _appear = 1f;
        float _appearDelay;
        float _highlight, _highlightTarget;

        [SerializeField] Color _corruptColor = new(0.55f, 0.04f, 0.16f);

        public bool IsLit { get; private set; }
        public bool IsPopped { get; private set; }
        /// <summary>Hexed by the creature: hitting it scores nothing and breaks the chain.</summary>
        public bool IsCorrupt { get; private set; }

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _spinSpeed = Random.Range(12f, 30f) * (Random.value < 0.5f ? -1f : 1f);
            _phase = Random.value * 10f;
            gameObject.layer = Layers.Crystal;
            _block.SetColor(BaseColorId, _idleColor);
            Apply(1f, 0f);
        }

        /// <summary>Returns a pooled crystal to its un-hit state.</summary>
        public void ResetState()
        {
            IsLit = false;
            IsPopped = false;
            IsCorrupt = false;
            _punch = 0f;
            _flash = 0f;
            _highlight = _highlightTarget = 0f;
            _block.SetColor(BaseColorId, _idleColor);
        }

        /// <summary>Grow in with an overshoot after a delay (board reveal). Visual only.</summary>
        public void Appear(float delay)
        {
            _appear = 0f;
            _appearDelay = delay;
        }

        /// <summary>0..1: a heartbeat glow that marks this crystal as the target of a dramatic moment.</summary>
        public void SetHighlight(float amount) => _highlightTarget = Mathf.Clamp01(amount);

        public void Corrupt()
        {
            if (IsLit || IsPopped || IsCorrupt) return;
            IsCorrupt = true;
            _punch = 1f;
            _block.SetColor(BaseColorId, _corruptColor);
        }

        public void Light()
        {
            if (IsLit) return;
            IsLit = true;
            _punch = 1f;
            _flash = 1f;
            _block.SetColor(BaseColorId, _litColor);
        }

        public void Pop()
        {
            IsPopped = true;
            gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            transform.Rotate(Vector3.up, _spinSpeed * dt, Space.Self);

            if (_appearDelay > 0f) _appearDelay -= dt;
            else if (_appear < 1f) _appear = Mathf.Min(1f, _appear + dt / AppearSeconds);

            _punch = Mathf.Max(0f, _punch - dt * 4f);
            _flash = Mathf.Max(0f, _flash - dt * 3f);

            // Pop-in: OutBack. Hit: fast overshoot pulse, eased settle.
            // The highlight eases and beats (~100 bpm) in real time, so it stays alive in slow motion.
            _highlight = Mathf.MoveTowards(_highlight, _highlightTarget, Time.unscaledDeltaTime * 4f);
            float beat = _highlight > 0f
                ? _highlight * (0.6f + 0.4f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10.5f), 3f))
                : 0f;

            float scale = OutBack(_appear) * (1f + 0.45f * Mathf.Sin(_punch * Mathf.PI) * _punch) * (1f + 0.25f * beat);
            // Idle crystals shimmer faintly; lit ones flash white-hot, then burn steady.
            float glow = IsLit
                ? Mathf.Lerp(LitGlow, 1f, _flash)
                : IsCorrupt
                    ? 0.15f + 0.12f * Mathf.Sin(Time.time * 4.3f + _phase) // a sick, faster smoulder
                    : 0.06f + 0.06f * Mathf.Sin(Time.time * 1.7f + _phase) + 0.6f * beat;
            Apply(scale, glow);
        }

        void Apply(float scale, float glow)
        {
            _block.SetFloat(ScaleId, scale);
            _block.SetFloat(GlowId, glow);
            _renderer.SetPropertyBlock(_block);
        }

        static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
