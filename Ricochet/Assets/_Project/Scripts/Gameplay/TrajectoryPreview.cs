using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Draws the predicted flight up to the first bounce plus a short reflected stub.
    /// Showing only the first bounce keeps skill in the game (Peggle's aim guide does the same).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        const int MaxPoints = 48;

        [SerializeField] float _gravityScale = 0.35f;
        [SerializeField] float _step = 0.02f;
        [SerializeField] float _sparkRadius = 0.025f;
        [SerializeField] float _reflectStub = 0.35f;

        LineRenderer _line;
        readonly Vector3[] _points = new Vector3[MaxPoints];

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.enabled = false;
        }

        public void Show(Vector3 origin, Vector3 velocity)
        {
            Vector3 g = Physics.gravity * _gravityScale;
            Vector3 p = origin, v = velocity;
            int count = 0;
            _points[count++] = p;

            while (count < MaxPoints - 1)
            {
                Vector3 next = p + v * _step + 0.5f * _step * _step * g;
                Vector3 seg = next - p;
                if (Physics.SphereCast(p, _sparkRadius, seg.normalized, out RaycastHit hit, seg.magnitude, Layers.SparkHits, QueryTriggerInteraction.Ignore))
                {
                    Vector3 contact = hit.point + hit.normal * _sparkRadius;
                    _points[count++] = contact;
                    Vector3 reflected = Vector3.Reflect(v, hit.normal).normalized;
                    _points[count++] = contact + reflected * _reflectStub;
                    break;
                }
                v += g * _step;
                p = next;
                _points[count++] = p;
            }

            _line.positionCount = count;
            _line.SetPositions(_points);
            _line.enabled = true;
        }

        public void Hide() => _line.enabled = false;
    }
}
