using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The Spark's light trail: a camera-facing ribbon built from a ring buffer of recent positions
    /// (docs/TECH_GUIDE.md section 4; cheaper and more controllable than a particle or stock trail).
    /// It tapers and fades with age in game time, so slow motion keeps its length in space, and it heats from
    /// cyan to gold as the combo climbs. Fixed-size buffers and one dynamic mesh: zero allocations per frame.
    /// Lives on its own GameObject at the world origin, so vertices are world positions.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SparkRibbon : MonoBehaviour
    {
        const int Capacity = 48;

        [SerializeField] Transform _target;
        [SerializeField] float _lifetime = 0.35f;
        [SerializeField] float _width = 0.07f;
        [SerializeField] float _minSpacing = 0.02f;
        [SerializeField] Color _coolColor = new(0.3f, 0.8f, 1f);
        [SerializeField] Color _hotColor = new(1f, 0.72f, 0.25f);

        readonly Vector3[] _points = new Vector3[Capacity];
        readonly float[] _times = new float[Capacity];
        readonly Vector3[] _vertices = new Vector3[Capacity * 2];
        readonly Color[] _colors = new Color[Capacity * 2];
        readonly Vector2[] _uvs = new Vector2[Capacity * 2];
        int _head;   // index of the newest stored point
        int _count;
        Mesh _mesh;
        Transform _camera;
        Color _color;

        /// <summary>While true, new points are recorded; existing ones always age out.</summary>
        public bool Emitting { get; set; }

        /// <summary>The transform the ribbon follows (a split-off Spark gets its own ribbon).</summary>
        public Transform Target { get => _target; set => _target = value; }

        /// <summary>The trail's cold color (the Spark type's light); the chain still heats it toward gold.</summary>
        public Color CoolColor { get => _coolColor; set => _coolColor = value; }

        void Awake()
        {
            _mesh = new Mesh { name = "SparkRibbon" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
            _mesh.uv = _uvs;
            // Fixed topology: a quad between every pair of neighbouring points. Unused points collapse to zero area.
            var tris = new int[(Capacity - 1) * 6];
            for (int i = 0; i < Capacity - 1; i++)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            _color = _coolColor;
        }

        /// <summary>0 = cool (no combo), 1 = fully hot.</summary>
        public void SetHeat(float heat) => _color = Color.Lerp(_coolColor, _hotColor, Mathf.Clamp01(heat));

        public void Clear()
        {
            _count = 0;
            _color = _coolColor;
        }

        void LateUpdate()
        {
            if (_target == null) return;
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            float now = Time.time;

            if (Emitting)
            {
                // The head point rides the Spark; once it is far enough from the previous point it freezes there
                // and a new head starts, so spacing stays even and the ribbon always meets the Spark.
                Vector3 tip = _target.position;
                if (_count < 2 || (tip - _points[(_head - 1 + Capacity) % Capacity]).sqrMagnitude > _minSpacing * _minSpacing)
                    Push(tip, now);
                else
                {
                    _points[_head] = tip;
                    _times[_head] = now;
                }
            }
            // Drop points that have aged out (always the oldest).
            while (_count > 0 && now - _times[(_head - _count + 1 + Capacity) % Capacity] > _lifetime) _count--;

            Build(now);
        }

        void Push(Vector3 p, float now)
        {
            _head = (_head + 1) % Capacity;
            _points[_head] = p;
            _times[_head] = now;
            if (_count < Capacity) _count++;
        }

        Vector3 Point(int i) => _points[(_head - i + Capacity) % Capacity]; // 0 = newest

        void Build(float now)
        {
            Vector3 eye = _camera != null ? _camera.position : Vector3.zero;
            int n = _count;
            Vector3 collapse = n > 0 ? Point(0) : _target.position;
            for (int i = 0; i < Capacity; i++)
            {
                int v = i * 2;
                if (n < 2 || i >= n)
                {
                    _vertices[v] = _vertices[v + 1] = collapse;
                    _colors[v] = _colors[v + 1] = Color.clear;
                    continue;
                }
                int idx = (_head - i + Capacity) % Capacity;
                Vector3 p = _points[idx];
                float k = 1f - Mathf.Clamp01((now - _times[idx]) / _lifetime);

                // Width lies across the local direction of travel, facing the eye.
                Vector3 tangent = (i > 0 ? Point(i - 1) : p) - (i + 1 < n ? Point(i + 1) : p);
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.up;
                Vector3 side = Vector3.Cross(tangent, p - eye).normalized;

                float half = 0.5f * _width * k;
                _vertices[v] = p - side * half;
                _vertices[v + 1] = p + side * half;
                Color c = _color;
                c.a = k * k;
                _colors[v] = _colors[v + 1] = c;
                float u = (float)i / (n - 1);
                _uvs[v] = new Vector2(u, 0f);
                _uvs[v + 1] = new Vector2(u, 1f);
            }
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
            _mesh.uv = _uvs;
            _mesh.RecalculateBounds();
        }
    }
}
