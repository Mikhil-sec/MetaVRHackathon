using Ricochet.Audio;
using Ricochet.Input;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Focus (CONCEPT section 3): glance at a crystal while aiming to mark it; if the Spark hits the focused crystal
    /// it is a critical (its chain value doubles, stacking with Gold). Head gaze is the default and must be fully
    /// playable (TECH_GUIDE section 6); eye gaze takes over where the device has it (<see cref="EyeGaze"/>).
    /// A short dwell keeps a glance across the board from flickering the mark. Focus locks when the Spark flies.
    /// </summary>
    public sealed class GazeFocus : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int SpinId = Shader.PropertyToID("_Spin");

        [SerializeField] PlayArea _playArea;
        [SerializeField] BoardGenerator _board;
        [SerializeField] Sling _sling;
        [SerializeField] Spark _spark;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] Renderer _marker;            // Ring shader quad
        [SerializeField] float _maxAngle = 4f;        // degrees off the gaze ray
        [SerializeField] float _dwell = 0.3f;         // seconds a candidate must hold before it takes the focus
        [SerializeField] Color _color = new(0.55f, 0.95f, 1f);

        Crystal _candidate;
        float _candidateTime;
        float _pop, _spin;
        MaterialPropertyBlock _mpb;
        Vector3 _markerScale;

        /// <summary>The focused crystal, or null.</summary>
        public Crystal Focused { get; private set; }
        /// <summary>The gaze is still on the focused crystal (the focus itself stays until another one takes it).</summary>
        public bool GazeOnFocused { get; private set; }

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _markerScale = _marker.transform.localScale;
            _marker.enabled = false;
        }

        void Update()
        {
            float dt = RealTime.DeltaTime;
            _pop = Mathf.Max(0f, _pop - dt * 4f);
            _spin += dt * 0.35f;

            if (Focused != null && (Focused.IsPopped || !Focused.gameObject.activeInHierarchy)) Clear();
            // Aiming phase only: the mark locks once the Spark flies.
            if ((_sling.IsReady || _sling.IsPulling) && !_spark.InFlight) Track(dt);
            DrawMarker();
        }

        void Track(float dt)
        {
            GazeOnFocused = false;
            if (!TryGaze(out Vector3 origin, out Vector3 dir)) return;
            if (Focused != null) GazeOnFocused = Vector3.Angle(dir, Focused.transform.position - origin) < _maxAngle * 1.5f;
            Crystal best = null;
            float bestAngle = _maxAngle;
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                float a = Vector3.Angle(dir, c.transform.position - origin);
                if (a < bestAngle) { bestAngle = a; best = c; }
            }

            if (best == null || best == Focused) { _candidate = null; return; }
            if (best != _candidate) { _candidate = best; _candidateTime = 0f; return; }
            _candidateTime += dt;
            if (_candidateTime < _dwell) return;

            Focused = best;
            _candidate = null;
            _pop = 1f;
            _sfx.PlayTick(3, best.transform.position);
        }

        bool TryGaze(out Vector3 origin, out Vector3 dir) => EyeGaze.Ray(_playArea.Head, out origin, out dir);

        public void Clear()
        {
            Focused = null;
            GazeOnFocused = false;
            _candidate = null;
        }

        void DrawMarker()
        {
            bool show = Focused != null;
            _marker.enabled = show;
            if (!show) return;
            // A reticle ring around the crystal: four dashes, turning slowly, that snap in when the focus lands.
            // Constant angular size (~5 degrees) whatever the crystal's distance, so it reads on far walls too.
            Vector3 p = Focused.transform.position;
            float distance = _playArea.Head != null ? Vector3.Distance(_playArea.Head.position, p) : 2.5f;
            _marker.transform.position = p;
            _marker.transform.localScale = _markerScale * (Mathf.Clamp(distance / 2.5f, 0.6f, 2.5f) * (1f + 0.6f * _pop));
            _mpb.Clear();
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, 1.6f + 1.5f * _pop);
            _mpb.SetFloat(RadiusId, 0.36f);
            _mpb.SetFloat(SpinId, _spin);
            _marker.SetPropertyBlock(_mpb);
        }
    }
}
