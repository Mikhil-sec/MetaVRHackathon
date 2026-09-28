using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Grows crystals on real room surfaces, weighted toward the player's forward view
    /// (FoV-aware: VR Glasses see ~70° horizontally) with minimum spacing.
    /// </summary>
    public sealed class BoardGenerator : MonoBehaviour
    {
        [SerializeField] Crystal _crystalPrefab;
        [SerializeField] int _targetCount = 24;
        [SerializeField] float _minSpacing = 0.28f;
        [SerializeField] float _minDistance = 0.9f;
        [SerializeField] float _maxDistance = 4.5f;
        [SerializeField, Range(10f, 90f)] float _halfConeDegrees = 55f;
        [SerializeField] float _surfaceOffset = 0.06f;
        [SerializeField] int _attemptsPerCrystal = 40;

        readonly List<Crystal> _pool = new();
        readonly List<Crystal> _active = new();

        public IReadOnlyList<Crystal> Active => _active;

        // Everything except the floor directly under the player and invisible/unknown helpers.
        static readonly MRUKAnchor.SceneLabels SpawnLabels =
            MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.TABLE |
            MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT |
            MRUKAnchor.SceneLabels.WALL_ART | MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.FLOOR;

        public int Generate(MRUKRoom room, Pose seat, int seed)
        {
            Clear();
            var rng = new System.Random(seed);
            var saved = Random.state;
            Random.InitState(seed);

            var filter = LabelFilter.Included(SpawnLabels);
            var surfaces = MRUK.SurfaceType.VERTICAL | MRUK.SurfaceType.FACING_UP | MRUK.SurfaceType.FACING_DOWN;
            float cosCone = Mathf.Cos(_halfConeDegrees * Mathf.Deg2Rad);

            for (int n = 0; n < _targetCount; n++)
            {
                for (int attempt = 0; attempt < _attemptsPerCrystal; attempt++)
                {
                    if (!room.GenerateRandomPositionOnSurface(surfaces, 0.08f, filter, out Vector3 pos, out Vector3 normal))
                        continue;

                    Vector3 toPos = pos - seat.position;
                    float dist = toPos.magnitude;
                    if (dist < _minDistance || dist > _maxDistance) continue;

                    Vector3 flat = Vector3.ProjectOnPlane(toPos, Vector3.up).normalized;
                    float facing = Vector3.Dot(flat, seat.forward);
                    if (facing < cosCone) continue;
                    // Bias toward the center of view: accept edge positions less often.
                    float centrality = Mathf.InverseLerp(cosCone, 1f, facing);
                    if (rng.NextDouble() > 0.35 + 0.65 * centrality) continue;

                    Vector3 spawn = pos + normal * _surfaceOffset;
                    if (!FarFromOthers(spawn)) continue;

                    Spawn(spawn, normal);
                    break;
                }
            }

            Random.state = saved;
            return _active.Count;
        }

        bool FarFromOthers(Vector3 p)
        {
            float min2 = _minSpacing * _minSpacing;
            foreach (var c in _active)
                if ((c.transform.position - p).sqrMagnitude < min2) return false;
            return true;
        }

        void Spawn(Vector3 position, Vector3 normal)
        {
            Crystal crystal = null;
            foreach (var c in _pool)
                if (!c.gameObject.activeSelf) { crystal = c; break; }
            if (crystal == null)
            {
                crystal = Instantiate(_crystalPrefab, transform);
                _pool.Add(crystal);
            }

            // Point the crystal's long axis out of the surface.
            crystal.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal));
            crystal.gameObject.SetActive(true);
            crystal.ResetState();
            _active.Add(crystal);
        }

        public void Clear()
        {
            foreach (var c in _active) c.gameObject.SetActive(false);
            _active.Clear();
        }

        public int RemainingCount()
        {
            int n = 0;
            foreach (var c in _active) if (!c.IsPopped) n++;
            return n;
        }
    }
}
