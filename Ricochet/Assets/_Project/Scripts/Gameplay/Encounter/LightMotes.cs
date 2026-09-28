using System;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Light in flight (CONCEPT section 3: "the accumulated light beams into the rift"). Each popped crystal sends a
    /// gold mote arcing into the creature; the creature's attacks fly back as magenta bolts to your shield.
    /// Pooled billboard halos on eased Bezier arcs; one Arrived event with a payload, so no per-launch allocations.
    /// </summary>
    public sealed class LightMotes : MonoBehaviour
    {
        /// <summary>Damage: crystal light into the creature. Bolt: an attack on the shield. Hex: amount = board index to corrupt.</summary>
        public enum Kind { Damage, Bolt, Hex }

        [SerializeField] Material _moteMaterial;
        [SerializeField] Material _boltMaterial;
        [SerializeField] int _poolSize = 24;
        [SerializeField] float _moteSize = 0.13f;       // grows with the light it carries
        [SerializeField] float _boltSize = 0.2f;

        // Each mote is a head plus two tail halos trailing along the arc: a comet for the cost of 3 instanced quads.
        const int Parts = 3;
        static readonly float[] TailLag = { 0f, 0.07f, 0.14f };
        static readonly float[] TailScale = { 1f, 0.65f, 0.4f };

        struct Mote
        {
            public Transform[] Tr;
            public MeshRenderer[] Renderer;
            public Vector3 From, Control, To;
            public float Age, Duration, Delay, Size;
            public Kind Kind;
            public int Amount;
            public bool Active;
        }

        Mote[] _pool;
        int _next;

        /// <summary>(kind, amount, arrival point).</summary>
        public event Action<Kind, int, Vector3> Arrived;

        public int InFlight { get; private set; }
        readonly int[] _inFlightByKind = new int[3];
        public int InFlightOf(Kind kind) => _inFlightByKind[(int)kind];

        void Awake()
        {
            _pool = new Mote[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                var m = new Mote { Tr = new Transform[Parts], Renderer = new MeshRenderer[Parts] };
                for (int k = 0; k < Parts; k++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    go.name = $"Mote{i}_{k}";
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    var r = go.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    go.SetActive(false);
                    m.Tr[k] = go.transform;
                    m.Renderer[k] = r;
                }
                _pool[i] = m;
            }
        }

        /// <summary>Send light from a to b along an arc that bows upward (toward the viewer's side for bolts).</summary>
        public void Launch(Kind kind, int amount, Vector3 from, Vector3 to, float delay, float duration)
        {
            ref Mote m = ref _pool[_next];
            _next = (_next + 1) % _pool.Length;
            if (m.Active) { Arrive(ref m); } // pool exhausted: land the oldest now rather than drop its damage

            float span = Vector3.Distance(from, to);
            m.From = from;
            m.To = to;
            m.Control = (from + to) * 0.5f + Vector3.up * (0.25f + 0.25f * span);
            m.Age = 0f;
            m.Delay = delay;
            m.Duration = duration;
            m.Kind = kind;
            m.Amount = amount;
            m.Size = kind == Kind.Damage ? _moteSize * (1f + 0.15f * Mathf.Min(amount - 1, 6)) : _boltSize;
            m.Active = true;
            var mat = kind == Kind.Damage ? _moteMaterial : _boltMaterial;
            for (int k = 0; k < Parts; k++)
            {
                m.Renderer[k].sharedMaterial = mat;
                m.Tr[k].position = from;
                m.Tr[k].localScale = Vector3.zero;
                m.Tr[k].gameObject.SetActive(true);
            }
            InFlight++;
            _inFlightByKind[(int)kind]++;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                ref Mote m = ref _pool[i];
                if (!m.Active) continue;
                if (m.Delay > 0f) { m.Delay -= dt; continue; }
                m.Age += dt;
                float t = Mathf.Clamp01(m.Age / m.Duration);
                for (int k = 0; k < Parts; k++)
                {
                    float tk = Mathf.Max(0f, t - TailLag[k]);
                    // Ease in-out along the arc: it gathers, streaks, and lands.
                    float e = tk * tk * (3f - 2f * tk);
                    float u = 1f - e;
                    m.Tr[k].position = u * u * m.From + 2f * u * e * m.Control + e * e * m.To;
                    float grow = Mathf.Min(1f, tk * 6f);
                    m.Tr[k].localScale = Vector3.one * (m.Size * TailScale[k] * grow * (1f + 0.3f * Mathf.Sin(tk * Mathf.PI)));
                }
                if (t >= 1f) Arrive(ref m);
            }
        }

        void Arrive(ref Mote m)
        {
            m.Active = false;
            for (int k = 0; k < Parts; k++) m.Tr[k].gameObject.SetActive(false);
            InFlight--;
            _inFlightByKind[(int)m.Kind]--;
            Arrived?.Invoke(m.Kind, m.Amount, m.To);
        }
    }
}
