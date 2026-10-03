using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Grows crystals on real room surfaces in readable formations (lines, arcs, staggered grids, rings),
    /// Peggle-style, instead of a uniform scatter: clusters are what make one shot chain several hits.
    /// The first formation always lands on the surface straight ahead of the seat. Placement is weighted
    /// toward the forward view (FoV-aware: VR Glasses see ~70° horizontally) and toward walls.
    /// </summary>
    public sealed class BoardGenerator : MonoBehaviour
    {
        /// <summary>The relaxed default shot (straight ahead, moderate pull) the hero formation is placed to catch.</summary>
        public struct HeroShot
        {
            public Vector3 Origin;
            public Vector3 Velocity;
            public Spark Spark; // predicts the landing with real physics
        }

        enum Shape { Line, Arc, Grid, Ring, Chevron }

        [SerializeField] Crystal _crystalPrefab;
        [SerializeField] int _targetCount = 36;
        [SerializeField] Vector2Int _formationSize = new(5, 8);
        [SerializeField, Range(0f, 0.5f)] float _looseFraction = 0.1f;
        [SerializeField] float _formationSpacing = 0.18f;
        [SerializeField] float _minSpacing = 0.15f;
        [SerializeField] int _heroMinCrystals = 6;     // the first straight shot's cluster, center included
        [SerializeField] int _reboundCrystals = 5;     // where the straight shot goes next, off the hero cluster
        static readonly float[] HeroTightening = { 1f, 0.8f, 0.68f }; // small landing surfaces pack the grid closer
        float _tight = 1f;
        int _rejSurface, _rejAnchor, _rejNear, _rejVolume, _rejSight; // hero grid diagnostics (board generation only)
        [SerializeField] float _minDistance = 0.9f;
        [SerializeField] float _maxDistance = 4.5f;
        [SerializeField] float _heroMinDistance = 1.2f;
        [SerializeField] float _heroMaxDistance = 7f;
        [SerializeField, Range(10f, 90f)] float _halfConeDegrees = 50f;
        [SerializeField] float _surfaceOffset = 0.07f;
        [SerializeField] int _attempts = 60;

        [Header("Surface preference (acceptance weight)")]
        [SerializeField, Range(0f, 1f)] float _wallWeight = 1f;
        [SerializeField, Range(0f, 1f)] float _topWeight = 0.7f;
        [SerializeField, Range(0f, 1f)] float _floorWeight = 0.25f;
        [SerializeField, Range(0f, 1f)] float _ceilingWeight = 0.3f;

        readonly List<Crystal> _pool = new();
        readonly List<Crystal> _active = new();
        readonly List<Vector2> _shape = new(16);

        System.Random _rng;
        MRUKRoom _room;
        Pose _seat;
        Vector3 _eye; // where shots start: every crystal must be visible from here
        float _cosCone;

        public IReadOnlyList<Crystal> Active => _active;
        /// <summary>Extra Gold crystals on every board (the Gold Rush relic). Scoring only: physics is unchanged.</summary>
        public int ExtraGold { get; set; }
        /// <summary>The hero shot actually used (it steepens when the relaxed arc lands too close to the seat).</summary>
        public Vector3 HeroVelocity { get; private set; }
        /// <summary>Where the relaxed straight shot lands (the hero cluster's center), if the board has one.</summary>
        public Vector3? HeroPoint { get; private set; }

        /// <summary>Diagnostics for the room sweep: where the hero shot lands and how many crystals were placed there.</summary>
        public string HeroInfo { get; private set; } = "";

        // Everything with a real surface. Floor is allowed but down-weighted.
        static readonly LabelFilter SpawnFilter = LabelFilter.Included(
            MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.TABLE |
            MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT |
            MRUKAnchor.SceneLabels.WALL_ART | MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.FLOOR);

        const MRUK.SurfaceType AllSurfaces =
            MRUK.SurfaceType.VERTICAL | MRUK.SurfaceType.FACING_UP | MRUK.SurfaceType.FACING_DOWN;

        public int Generate(MRUKRoom room, Pose seat, int seed, HeroShot hero)
        {
            Clear();
            _rng = new System.Random(seed);
            _room = room;
            _seat = seat;
            _eye = hero.Origin;
            _cosCone = Mathf.Cos(_halfConeDegrees * Mathf.Deg2Rad);
            var saved = Random.state;
            Random.InitState(seed);

            int looseTarget = Mathf.RoundToInt(_targetCount * _looseFraction);
            int formationTarget = _targetCount - looseTarget;

            // Hero formation: centered where the relaxed straight shot first lands, so it always has a cluster to hit.
            HeroInfo = "none";
            HeroPoint = null;
            if (TryHeroArc(hero, out Vector3 heroPos, out Vector3 heroNormal, out MRUKAnchor heroAnchor))
            {
                HeroPoint = heroPos;
                // The center crystal sits on the arc by construction, so it skips the line-of-sight test.
                Vector3 center = heroPos + heroNormal * _surfaceOffset;
                bool centerBlocked = _room.IsPositionInSceneVolume(center);
                if (!centerBlocked && !Excluded(center)) Spawn(center, heroNormal);
                // A small landing surface (a table, a cabinet top) clips the grid to a few crystals, so the first,
                // natural shot would score one. Pack it closer until the cluster is worth hitting (colliders ~9 cm).
                int start = _active.Count;
                _rejSurface = _rejAnchor = _rejNear = _rejVolume = _rejSight = 0;
                foreach (float tight in HeroTightening)
                {
                    _tight = tight;
                    PlaceFormation(heroPos, heroNormal, heroAnchor, _formationSize.y, false);
                    if (_active.Count >= _heroMinCrystals || tight == HeroTightening[HeroTightening.Length - 1]) break;
                    for (int i = _active.Count - 1; i >= start; i--)
                    {
                        _active[i].gameObject.SetActive(false);
                        _active.RemoveAt(i);
                    }
                }
                _tight = 1f;
                float pitch = Vector3.Angle(Vector3.ProjectOnPlane(HeroVelocity, Vector3.up), HeroVelocity);
                HeroInfo = $"{heroAnchor.Label} d={(heroPos - seat.position).magnitude:F1} pitch={pitch:F0} n={_active.Count}{(centerBlocked ? " centerInVolume" : "")}" +
                           (_active.Count < _heroMinCrystals ? $" rej surf/anchor/near/vol/sight={_rejSurface}/{_rejAnchor}/{_rejNear}/{_rejVolume}/{_rejSight}" : "");
                HeroInfo += " rebound=" + PlaceRebound(hero);
            }

            for (int guard = 0; _active.Count < formationTarget && guard < _attempts; guard++)
            {
                if (!TrySampleCenter(out Vector3 pos, out Vector3 normal, out MRUKAnchor anchor)) continue;
                int size = Mathf.Min(_rng.Next(_formationSize.x, _formationSize.y + 1), formationTarget - _active.Count);
                PlaceFormation(pos, normal, anchor, size);
            }

            for (int guard = 0; _active.Count < _targetCount && guard < _attempts * 4; guard++)
            {
                if (TrySampleCenter(out Vector3 pos, out Vector3 normal, out _))
                    TrySpawn(pos + normal * _surfaceOffset, normal);
            }

            AssignKinds();
            Random.state = saved;
            return _active.Count;
        }

        /// <summary>
        /// Sprinkles the special crystals (CONCEPT section 3) over a fresh board, deterministically from the board
        /// seed: a few Gold (critical), a couple each of Amp and Bomb, and one Prism. Visual and scoring only; the
        /// colliders are identical, so board physics and the sweep are untouched.
        /// </summary>
        void AssignKinds()
        {
            int n = _active.Count;
            if (n < 12) return;
            int gold = 3 + Mathf.Clamp(ExtraGold, 0, n - 8), amp = 2, bomb = 2, prism = 1;
            // Fisher-Yates over indices with the board's own RNG: the same seed gives the same board.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            int k = 0;
            for (int i = 0; i < gold; i++) _active[order[k++]].SetKind(CrystalKind.Gold);
            for (int i = 0; i < amp; i++) _active[order[k++]].SetKind(CrystalKind.Amp);
            for (int i = 0; i < bomb; i++) _active[order[k++]].SetKind(CrystalKind.Bomb);
            for (int i = 0; i < prism; i++) _active[order[k++]].SetKind(CrystalKind.Prism);
        }

        /// <summary>
        /// Finds the flattest hero arc (relaxed pitch first, then steeper) that lands on a crystal-worthy surface
        /// far enough out that its cluster doesn't crowd the sling, e.g. clears a desk right in front of the player.
        /// </summary>
        bool TryHeroArc(HeroShot hero, out Vector3 pos, out Vector3 normal, out MRUKAnchor anchor)
        {
            Vector3 right = Vector3.Cross(Vector3.up, _seat.forward);
            HeroVelocity = hero.Velocity;
            var why = new System.Text.StringBuilder("none:"); // board generation only, never per frame
            foreach (float extraPitch in HeroPitches)
            {
                hero.Velocity = Quaternion.AngleAxis(-extraPitch, right) * HeroVelocity;
                if (TryHeroLanding(hero, out pos, out normal, out anchor, why))
                {
                    float d = (pos - _seat.position).magnitude;
                    if (d < _heroMinDistance || d > _heroMaxDistance) { why.Append($" {extraPitch:F0}:d={d:F1}"); continue; }
                    HeroVelocity = hero.Velocity;
                    return true;
                }
                why.Append($"@{extraPitch:F0}");
            }
            HeroInfo = why.ToString();
            pos = normal = default;
            anchor = null;
            return false;
        }

        static readonly float[] HeroPitches = { 0f, 7f, 14f, 21f };

        /// <summary>
        /// Where the hero launch first touches the room, predicted with the real Spark physics (an analytic arc
        /// drifted from the simulated flight in some rooms), if that surface can hold crystals.
        /// </summary>
        bool TryHeroLanding(HeroShot hero, out Vector3 pos, out Vector3 normal, out MRUKAnchor anchor,
                            System.Text.StringBuilder why = null)
        {
            pos = normal = default;
            anchor = null;
            if (hero.Spark == null) { why?.Append(" noSpark"); return false; }
            if (!hero.Spark.PredictFirstContact(hero.Origin, hero.Velocity, out Vector3 point, out Vector3 n, out Collider collider))
            {
                why?.Append(" noContact");
                return false;
            }
            // The contact is on the room collider's surface; trust it (MRUK re-raycasts disagree at edges and volumes).
            anchor = collider.GetComponentInParent<MRUKAnchor>();
            if (anchor == null || !SpawnFilter.PassesFilter(anchor.Label))
            {
                why?.Append($" {collider.name}");
                return false;
            }
            RefineContact(hero.Spark, collider, anchor, ref point, ref n);
            pos = point;
            normal = n;
            return true;
        }

        /// <summary>
        /// The physics contact can be speculative (reported up to one step, ~14 cm, before the Spark touches), and on
        /// an edge its normal points at no face. Follow the flight on to where it meets the collider it touched (the
        /// surface the Spark really hits, which can sit a few cm off MRUK's analytic face), so a cluster stays on the
        /// shot's line in front of that surface, with the surface's own normal.
        /// </summary>
        static void RefineContact(Spark spark, Collider collider, MRUKAnchor anchor, ref Vector3 point, ref Vector3 n)
        {
            Vector3 v = spark.PredictedVelocity.normalized;
            if (collider.Raycast(new Ray(point - v * 0.3f, v), out RaycastHit surf, 0.8f))
            {
                point = surf.point;
                n = surf.normal;
            }
            else n = FaceNormal(anchor, n);
        }

        /// <summary>
        /// The first shot should chain (CONCEPT section 4: a straight shot hits 6+). Bouncing straight back off the
        /// hero cluster, the Spark used to find nothing more. Follow the hero shot on with the hero crystals in
        /// place (it really bounces off them, as in play) to its next room contact, and seed a small cluster
        /// centred on that path when it lands in view and clear of the sling. Returns a diagnostic.
        /// </summary>
        string PlaceRebound(HeroShot hero)
        {
            if (_reboundCrystals <= 0 || hero.Spark == null) return "off";
            string why = ReboundLanding(hero.Spark, hero.Origin, HeroVelocity, out Vector3 point, out Vector3 n, out MRUKAnchor anchor);
            if (why != null) return why;
            int start = _active.Count;
            Spawn(point + n * _surfaceOffset, n);
            _tight = 0.8f;
            PlaceFormation(point, n, anchor, _reboundCrystals, false);
            _tight = 1f;
            return $"{anchor.Label} d={(point - _seat.position).magnitude:F1} n={_active.Count - start}";
        }

        /// <summary>
        /// Where a shot goes after the hero cluster: its next room contact, predicted with the crystals in place.
        /// Null when that spot can hold a new cluster, else why not.
        /// </summary>
        string ReboundLanding(Spark spark, Vector3 origin, Vector3 velocity, out Vector3 point, out Vector3 n, out MRUKAnchor anchor)
        {
            anchor = null;
            Physics.SyncTransforms();
            // Off the centre crystal, the next room contact is the rebound. If the flight slips past the crystals
            // and touches the wall inside the hero cluster instead, the rebound is the contact after that.
            if (!spark.PredictFirstContact(origin, velocity, out point, out n, out Collider collider))
                return "none";
            if (HeroPoint.HasValue && (point - HeroPoint.Value).sqrMagnitude < 0.3f * 0.3f &&
                !spark.PredictFirstContact(origin, velocity, out point, out n, out collider, 3f, 1))
                return "none2";
            anchor = collider.GetComponentInParent<MRUKAnchor>();
            if (anchor == null || !SpawnFilter.PassesFilter(anchor.Label)) return collider.name;
            RefineContact(spark, collider, anchor, ref point, ref n);
            float d = (point - _seat.position).magnitude;
            if (d < _minDistance) return $"near d={d:F1}";
            if (!InForwardView(point, false)) return "outOfView";
            Vector3 center = point + n * _surfaceOffset;
            if (_room.IsPositionInSceneVolume(center) || Excluded(center)) return "blocked";
            foreach (var c in _active)
                if ((c.transform.position - center).sqrMagnitude < _minSpacing * _minSpacing) return "inCluster";
            return null;
        }

        /// <summary>A plane's normal (its forward), or the volume face whose normal is nearest n, signed toward n.</summary>
        static Vector3 FaceNormal(MRUKAnchor anchor, Vector3 n)
        {
            Transform t = anchor.transform;
            Vector3 best = t.forward;
            float bestDot = Vector3.Dot(best, n);
            if (anchor.VolumeBounds.HasValue)
            {
                float up = Vector3.Dot(t.up, n), right = Vector3.Dot(t.right, n);
                if (Mathf.Abs(up) > Mathf.Abs(bestDot)) { best = t.up; bestDot = up; }
                if (Mathf.Abs(right) > Mathf.Abs(bestDot)) { best = t.right; bestDot = right; }
            }
            return bestDot < 0f ? -best : best;
        }

        bool TrySampleCenter(out Vector3 pos, out Vector3 normal, out MRUKAnchor anchor)
        {
            anchor = null;
            if (!_room.GenerateRandomPositionOnSurface(AllSurfaces, 0.1f, SpawnFilter, out pos, out normal))
                return false;
            if (!InForwardView(pos, true)) return false;
            if (_rng.NextDouble() > SurfaceWeight(pos, normal)) return false;
            // Recover the anchor the point lies on, so formation members can be kept on the same surface.
            return OnSurface(pos, normal, out _, out anchor);
        }

        bool InForwardView(Vector3 pos, bool biasToCenter)
        {
            Vector3 toPos = pos - _seat.position;
            float dist = toPos.magnitude;
            if (dist < _minDistance || dist > _maxDistance) return false;
            Vector3 flat = Vector3.ProjectOnPlane(toPos, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) return !biasToCenter; // straight overhead/underfoot
            float facing = Vector3.Dot(flat.normalized, _seat.forward);
            if (facing < _cosCone) return false;
            if (!biasToCenter) return true;
            // Accept edge positions less often than central ones.
            float centrality = Mathf.InverseLerp(_cosCone, 1f, facing);
            return _rng.NextDouble() <= 0.35 + 0.65 * centrality;
        }

        float SurfaceWeight(Vector3 pos, Vector3 normal)
        {
            if (normal.y > 0.7f) return pos.y - FloorHeight() < 0.1f ? _floorWeight : _topWeight;
            if (normal.y < -0.7f) return _ceilingWeight;
            return _wallWeight;
        }

        float FloorHeight() => _room.FloorAnchor != null ? _room.FloorAnchor.transform.position.y : 0f;

        /// <summary>Casts back onto the surface: true if the point is on a real surface facing along the normal.</summary>
        bool OnSurface(Vector3 pos, Vector3 normal, out RaycastHit hit, out MRUKAnchor anchor)
        {
            var ray = new Ray(pos + normal * 0.12f, -normal);
            if (!_room.Raycast(ray, 0.24f, SpawnFilter, out hit, out anchor)) return false;
            return Vector3.Dot(hit.normal, normal) > 0.9f;
        }

        void PlaceFormation(Vector3 center, Vector3 normal, MRUKAnchor anchor, int count, bool randomShape = true)
        {
            // Tangent frame on the surface: u runs "across" the player's view, v is up on walls or away on tables.
            Vector3 u = Vector3.Cross(Vector3.up, normal);
            if (u.sqrMagnitude < 0.01f) u = Vector3.ProjectOnPlane(Quaternion.Euler(0f, 90f, 0f) * _seat.forward, normal);
            u.Normalize();
            Vector3 v = Vector3.Cross(normal, u).normalized;
            if (Mathf.Abs(normal.y) < 0.7f && v.y < 0f) v = -v;

            // The hero is a staggered grid: the widest target for a first, untrained shot.
            BuildShape(randomShape ? (Shape)_rng.Next(0, 5) : Shape.Grid, count);
            float tilt = (float)(_rng.NextDouble() * 2.0 - 1.0) * 25f * Mathf.Deg2Rad;
            float cs = Mathf.Cos(tilt), sn = Mathf.Sin(tilt);

            foreach (Vector2 p in _shape)
            {
                Vector2 r = new(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
                Vector3 onPlane = center + (u * r.x + v * r.y) * (_formationSpacing * _tight);
                if (!OnSurface(onPlane, normal, out RaycastHit hit, out MRUKAnchor hitAnchor)) { _rejSurface++; continue; }
                if (hitAnchor != anchor) { _rejAnchor++; continue; }
                if (randomShape && !InForwardView(hit.point, false)) continue;
                TrySpawn(hit.point + normal * _surfaceOffset, normal);
            }
        }

        /// <summary>Formation layouts in spacing units, centered on the origin.</summary>
        void BuildShape(Shape shape, int n)
        {
            _shape.Clear();
            switch (shape)
            {
                case Shape.Line:
                    for (int i = 0; i < n; i++) _shape.Add(new Vector2(i - (n - 1) * 0.5f, 0f));
                    break;
                case Shape.Arc:
                {
                    float radius = n * 0.42f;
                    float span = Mathf.Min(150f, n * 1.1f / radius * Mathf.Rad2Deg);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (-span * 0.5f + span * i / Mathf.Max(1, n - 1) - 90f) * Mathf.Deg2Rad;
                        _shape.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a) + 1f) * radius);
                    }
                    break;
                }
                case Shape.Grid:
                {
                    int cols = Mathf.CeilToInt(n / 2f);
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / cols, col = i % cols;
                        // Staggered rows, like a Peggle peg field.
                        _shape.Add(new Vector2(col - (cols - 1) * 0.5f + (row % 2) * 0.5f, row * 0.9f - 0.45f));
                    }
                    break;
                }
                case Shape.Ring:
                {
                    float radius = Mathf.Max(0.8f, n / (2f * Mathf.PI) * 1.05f);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        _shape.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                    }
                    break;
                }
                default: // Chevron
                    for (int i = 0; i < n; i++)
                    {
                        int k = (i + 1) / 2;
                        float side = i % 2 == 0 ? 1f : -1f;
                        _shape.Add(new Vector2(k * side * 0.8f, -k * 0.6f + n * 0.15f));
                    }
                    break;
            }
        }

        bool TrySpawn(Vector3 position, Vector3 normal)
        {
            if (_active.Count >= _targetCount || !FarFromOthers(position)) { _rejNear++; return false; }
            if (_room.IsPositionInSceneVolume(position)) { _rejVolume++; return false; } // hidden inside furniture
            if (!VisibleFromSling(position)) { _rejSight++; return false; }             // hidden behind furniture
            Spawn(position, normal);
            return true;
        }

        /// <summary>Direct line of sight from the sling, so every crystal can be aimed at, not only banked into.</summary>
        bool VisibleFromSling(Vector3 p)
        {
            Vector3 to = p - _eye;
            float dist = to.magnitude;
            // Stop short of the crystal's own surface so the surface it sits on doesn't count as a blocker.
            return !_room.Raycast(new Ray(_eye, to / dist), dist - 0.12f, out _);
        }

        /// <summary>Keep crystals out of a sphere (the rift and the creature in front of it). Radius 0 clears it.</summary>
        public void SetExclusion(Vector3 center, float radius)
        {
            _exclusionCenter = center;
            _exclusionRadius = radius;
        }

        Vector3 _exclusionCenter;
        float _exclusionRadius;

        /// <summary>Removes crystals inside the exclusion (call right after Generate, before they have appeared).</summary>
        public int ClearExclusion()
        {
            int removed = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (!Excluded(_active[i].transform.position)) continue;
                _active[i].gameObject.SetActive(false);
                _active.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        bool Excluded(Vector3 p) => _exclusionRadius > 0f && (p - _exclusionCenter).sqrMagnitude < _exclusionRadius * _exclusionRadius;

        bool FarFromOthers(Vector3 p)
        {
            if (Excluded(p)) return false;
            float min2 = _minSpacing * _tight * _minSpacing * _tight;
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

            crystal.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, CrystalAxis(position, normal)));
            crystal.gameObject.SetActive(true);
            crystal.ResetState();
            crystal.Appear(0.02f * _active.Count); // the board reveals as a quick cascade
            _active.Add(crystal);
        }

        /// <summary>
        /// Long axis for a crystal: grown out of the surface, but leaned across the player's line of sight so it shows
        /// its full diamond silhouette. Pointing straight at the player it would read as a dot.
        /// </summary>
        Vector3 CrystalAxis(Vector3 position, Vector3 normal)
        {
            Vector3 view = (position - _eye).normalized;
            Vector3 across = normal - Vector3.Dot(normal, view) * view;
            if (across.sqrMagnitude < 0.05f) across = Vector3.up - Vector3.Dot(Vector3.up, view) * view;
            if (across.sqrMagnitude < 0.05f) across = Vector3.Cross(Vector3.up, _seat.forward);
            return (across.normalized + normal * 0.5f).normalized;
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
