using System;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Light in flight (CONCEPT section 3: "the accumulated light beams into the rift"). Each popped crystal sends a
    /// gold mote arcing into the creature; the creature's attacks fly back as magenta bolts to your shield.
    /// Pooled comets on eased Bezier arcs; one Arrived event with a payload, so no per-launch allocations.
    /// </summary>
    public sealed class LightMotes : MonoBehaviour
    {
        /// <summary>
        /// Damage: crystal light into the creature. Bolt: an attack on the shield. Hex: amount = board index to corrupt.
        /// Seed: a crystal spilling out of the rift to its place on the board (visual only, not counted in InFlight).
        /// </summary>
        /// <summary>Seed and Crown are visual only (the board spilling out; the Queen's crown flying to the wall).</summary>
        public enum Kind { Damage, Bolt, Hex, Seed, Crown }

        [SerializeField] Material _moteMaterial;        // heads (Halo billboards)
        [SerializeField] Material _boltMaterial;
        [SerializeField] Material _seedMaterial;
        [SerializeField] Material _moteStreak;          // tails (Streak axial billboards)
        [SerializeField] Material _boltStreak;
        [SerializeField] Material _seedStreak;
        [SerializeField] int _poolSize = 48;            // a whole board's seeds (~15 in the air at once) plus the turn's light
        [SerializeField] float _moteSize = 0.13f;       // grows with the light it carries
        [SerializeField] float _boltSize = 0.2f;
        [SerializeField] float _seedSize = 0.13f;
        [SerializeField] float _tailLag = 0.16f;        // tail length, as a share of the flight time behind the head
        [SerializeField] float _seedTailLag = 0.22f;    // seeds: long tails, so the spill reads as streams out of the rift

        // Each mote is a head halo plus one streak along the arc behind it: a continuous comet for the cost of 2 quads.
        // (Separate tail halos read as loose dots once a mote is fast: the board's spill looked like dust.)
        const int Parts = 2;

        struct Mote
        {
            public Transform[] Tr;
            public MeshRenderer[] Renderer;
            public Vector3 From, Control, To;
            public float Age, Duration, Delay, Size, Lag;
            public Kind Kind;
            public int Amount;
            public bool Active;
        }

        Mote[] _pool;
        int _next;

        /// <summary>(kind, amount, arrival point).</summary>
        public event Action<Kind, int, Vector3> Arrived;

        /// <summary>Motes that carry gameplay (damage, bolts, hexes): the turn waits for these to land.</summary>
        public int InFlight { get; private set; }
        readonly int[] _inFlightByKind = new int[5];

        static bool Counts(Kind kind) => kind != Kind.Seed && kind != Kind.Crown; // the turn waits only on these
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
        public void Launch(Kind kind, int amount, Vector3 from, Vector3 to, float delay, float duration) =>
            Launch(kind, amount, from, to, delay, duration, Vector3.zero);

        /// <param name="outward">Non-zero: the arc bows out along it (the rift's normal for seeds), so the light pours
        /// out of the wall into the room before it curves to its place.</param>
        public void Launch(Kind kind, int amount, Vector3 from, Vector3 to, float delay, float duration, Vector3 outward)
        {
            ref Mote m = ref _pool[_next];
            _next = (_next + 1) % _pool.Length;
            if (m.Active) { Arrive(ref m); } // pool exhausted: land the oldest now rather than drop its damage

            float span = Vector3.Distance(from, to);
            m.From = from;
            m.To = to;
            m.Control = outward == Vector3.zero
                ? (from + to) * 0.5f + Vector3.up * (0.25f + 0.25f * span)
                : (from + to) * 0.5f + outward * (0.3f + 0.22f * span) + Vector3.up * (0.12f + 0.1f * span);
            m.Age = 0f;
            m.Delay = delay;
            m.Duration = duration;
            m.Kind = kind;
            m.Amount = amount;
            m.Size = kind switch
            {
                Kind.Damage => _moteSize * (1f + 0.15f * Mathf.Min(amount - 1, 6)),
                Kind.Seed => _seedSize,
                Kind.Crown => _moteSize * 1.6f,
                _ => _boltSize,
            };
            m.Lag = kind == Kind.Seed ? _seedTailLag : _tailLag;
            m.Active = true;
            var head = kind switch
            {
                Kind.Damage => _moteMaterial,
                Kind.Seed => _seedMaterial != null ? _seedMaterial : _moteMaterial,
                Kind.Crown => _moteMaterial,
                _ => _boltMaterial,
            };
            var tail = kind switch
            {
                Kind.Damage => _moteStreak,
                Kind.Seed => _seedStreak,
                Kind.Crown => _moteStreak,
                _ => _boltStreak,
            };
            m.Renderer[0].sharedMaterial = head;
            m.Renderer[1].sharedMaterial = tail != null ? tail : head;
            // A delayed mote stays inactive until it leaves: a whole board of queued seeds must not cost draw calls.
            for (int k = 0; k < Parts; k++)
            {
                m.Tr[k].position = from;
                m.Tr[k].localScale = Vector3.zero;
                m.Tr[k].gameObject.SetActive(delay <= 0f);
            }
            if (Counts(kind)) InFlight++;
            _inFlightByKind[(int)kind]++;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                ref Mote m = ref _pool[i];
                if (!m.Active) continue;
                if (m.Delay > 0f)
                {
                    m.Delay -= dt;
                    if (m.Delay <= 0f) for (int k = 0; k < Parts; k++) m.Tr[k].gameObject.SetActive(true);
                    continue;
                }
                m.Age += dt;
                float t = Mathf.Clamp01(m.Age / m.Duration);
                // Ease in-out along the arc: it gathers, streaks, and lands.
                Vector3 head = ArcPoint(in m, t);
                float grow = Mathf.Min(1f, t * 6f);
                m.Tr[0].position = head;
                m.Tr[0].localScale = Vector3.one * (m.Size * grow * (1f + 0.3f * Mathf.Sin(t * Mathf.PI)));

                // The tail trails the head along the same arc and draws into it over the last quarter (the landing).
                Vector3 tail = ArcPoint(in m, Mathf.Max(0f, t - m.Lag * Mathf.Min(1f, (1f - t) * 4f)));
                Vector3 along = head - tail;
                float length = along.magnitude;
                if (length > 1e-4f)
                {
                    m.Tr[1].SetPositionAndRotation(head, Quaternion.FromToRotation(Vector3.right, along / length));
                    m.Tr[1].localScale = new Vector3(length, m.Size * 0.5f * grow, 1f);
                }
                else m.Tr[1].localScale = Vector3.zero;
                if (t >= 1f) Arrive(ref m);
            }
        }

        static Vector3 ArcPoint(in Mote m, float t)
        {
            float e = t * t * (3f - 2f * t);
            float u = 1f - e;
            return u * u * m.From + 2f * u * e * m.Control + e * e * m.To;
        }

        void Arrive(ref Mote m)
        {
            m.Active = false;
            for (int k = 0; k < Parts; k++) m.Tr[k].gameObject.SetActive(false);
            if (Counts(m.Kind)) InFlight--;
            _inFlightByKind[(int)m.Kind]--;
            Arrived?.Invoke(m.Kind, m.Amount, m.To);
        }
    }
}
