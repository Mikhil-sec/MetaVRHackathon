using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// A peg grown on a room surface. Hit once to light it (it stays solid, as in Peggle);
    /// lit crystals are popped in sequence when the shot ends.
    /// </summary>
    public sealed class Crystal : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer _renderer;
        [SerializeField] Color _idleColor = new(0.55f, 0.35f, 1f);
        [SerializeField] Color _litColor = new(1f, 0.85f, 0.35f);

        MaterialPropertyBlock _block;
        Vector3 _baseScale;
        float _punch;
        float _spinSpeed;

        public bool IsLit { get; private set; }
        public bool IsPopped { get; private set; }

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            _spinSpeed = Random.Range(12f, 30f) * (Random.value < 0.5f ? -1f : 1f);
            gameObject.layer = Layers.Crystal;
            SetColor(_idleColor);
        }

        /// <summary>Returns a pooled crystal to its un-hit state.</summary>
        public void ResetState()
        {
            IsLit = false;
            IsPopped = false;
            _punch = 0f;
            transform.localScale = _baseScale;
            SetColor(_idleColor);
        }

        public void Light()
        {
            if (IsLit) return;
            IsLit = true;
            _punch = 1f;
            SetColor(_litColor);
        }

        public void Pop()
        {
            IsPopped = true;
            gameObject.SetActive(false);
        }

        void Update()
        {
            transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.Self);
            if (_punch > 0f)
            {
                _punch = Mathf.Max(0f, _punch - Time.deltaTime * 4f);
                // Overshoot pulse: fast grow, eased settle.
                float s = 1f + 0.45f * Mathf.Sin(_punch * Mathf.PI) * _punch;
                transform.localScale = _baseScale * s;
            }
        }

        void SetColor(Color c)
        {
            _block.SetColor(BaseColorId, c);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
