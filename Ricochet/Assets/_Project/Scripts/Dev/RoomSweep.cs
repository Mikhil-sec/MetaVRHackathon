using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Meta.XR.MRUtilityKit;
using Ricochet.Gameplay;
using Ricochet.Room;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ricochet.Dev
{
    /// <summary>
    /// Board-quality sweep over every MRUK room layout bundled in the scene settings (CONCEPT §5).
    /// Desktop Play Mode only. Physics runs in script mode so hundreds of shots per room finish in seconds.
    /// Start with <c>RoomSweep.Run()</c> from an eval, then poll <see cref="Status"/> and read <see cref="Report"/>.
    /// </summary>
    public sealed class RoomSweep : MonoBehaviour
    {
        enum Kind { Straight, Random, Aimed, NearHero }

        const float AimNoiseDegrees = 3f;
        // A real hand never repeats the hero shot exactly (release drag 2-7 deg seen in XR, before the lookback):
        // extra shots within these bounds of it, with their own RNG so the other classes stay comparable.
        const int NearHeroShots = 40;
        const float NearHeroDegrees = 1.5f;
        const float NearHeroPull01 = 0.12f; // +/-0.03 m of the 0.25 m pull range
        const long FrameBudgetMs = 40;

        public static string Status { get; private set; } = "idle";

        // Stopping Play mid-sweep must not leave physics in Script mode: in the Editor that sticks in ProjectSettings
        // and the next Play has a Spark that never flies (cost an afternoon, session 6).
        void OnDisable()
        {
            if (!Status.StartsWith("room")) return;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Status = "aborted";
        }
        public static string Report { get; private set; } = "";

        GameObject[] _rooms;
        int _shotsPerRoom;
        int _roomLimit;
        bool _floorEndsShot;

        Spark _spark;
        BoardGenerator _board;
        Sling _sling;
        int _hitsThisShot;
        Vector3 _firstContact;
        string _firstLabel;
        readonly HashSet<Crystal> _everHit = new();

        sealed class Stats
        {
            public readonly int[] Shots = new int[4], Hits = new int[4], AtLeastOne = new int[4], AtLeastThree = new int[4];
            public readonly int[] Damage = new int[4]; // the chain sum 1+2+..+hits: what a shot does to the creature (no crits)
            public int Bounces, Escapes, Total;
            public float Flight;
            public readonly int[] Ends = new int[5];

            public void Add(Kind kind, int hits, int bounces, float flight, Spark.EndReason end, bool escaped)
            {
                Flight += flight;
                int k = (int)kind;
                Shots[k]++; Hits[k] += hits; Damage[k] += hits * (hits + 1) / 2;
                if (hits >= 1) AtLeastOne[k]++;
                if (hits >= 3) AtLeastThree[k]++;
                Bounces += bounces; Total++;
                if (escaped) Escapes++; else Ends[(int)end]++;
            }

            public float Mean(Kind k) => Shots[(int)k] == 0 ? 0f : (float)Hits[(int)k] / Shots[(int)k];
            public float MeanDamage(Kind k) => Shots[(int)k] == 0 ? 0f : (float)Damage[(int)k] / Shots[(int)k];
            public float Pct(int[] a, Kind k) => Shots[(int)k] == 0 ? 0f : 100f * a[(int)k] / Shots[(int)k];
        }

        public static void Run(int shotsPerRoom = 120, int roomLimit = 0, bool floorEndsShot = true) =>
            Run(null, shotsPerRoom, roomLimit, floorEndsShot);

        /// <summary>Sweep the Pocket Arena (the no-scan fallback) as a single room, seated at the origin.</summary>
        public static void RunPocket(int shotsPerRoom = 400, bool floorEndsShot = true)
        {
            var eye = new Pose(new Vector3(0f, PlayArea.SeatedEyeHeight, 0f), Quaternion.identity);
            Run(new[] { PocketArena.BuildLayout(eye, 0f) }, shotsPerRoom, 0, floorEndsShot);
        }

        /// <summary>Sweep an explicit room list (e.g. every MRUK prefab, loaded by the Editor); null uses the scene settings.</summary>
        public static void Run(GameObject[] rooms, int shotsPerRoom = 120, int roomLimit = 0, bool floorEndsShot = true)
        {
            if (Status.StartsWith("room")) return;
            Status = "starting";
            var sweep = new GameObject("RoomSweep").AddComponent<RoomSweep>();
            sweep._rooms = rooms;
            sweep._shotsPerRoom = shotsPerRoom;
            sweep._roomLimit = roomLimit;
            sweep._floorEndsShot = floorEndsShot;
            sweep.StartCoroutine(sweep.Sweep());
        }

        IEnumerator Sweep()
        {
            var mruk = MRUK.Instance;
            var director = FindAnyObjectByType<ShotDirector>();
            _sling = FindAnyObjectByType<Sling>();
            _spark = FindAnyObjectByType<Spark>(FindObjectsInactive.Include);
            _board = FindAnyObjectByType<BoardGenerator>();
            director.enabled = false;
            _sling.enabled = false;
            _spark.gameObject.SetActive(true);
            bool savedFloorRule = _spark.FloorEndsShot;
            _spark.FloorEndsShot = _floorEndsShot;
            _spark.CrystalHit += OnCrystalHit;
            _spark.RoomBounced += OnRoomBounced;
            var savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            var prefabs = _rooms ?? mruk.SceneSettings.RoomPrefabs;
            int roomCount = _roomLimit > 0 ? Mathf.Min(_roomLimit, prefabs.Length) : prefabs.Length;
            var report = new StringBuilder();
            var all = new Stats();
            int straightMisses = 0, sparseBoards = 0, floatingRifts = 0;
            float worstAimed = float.MaxValue;
            string worstRoom = "";
            var clock = Stopwatch.StartNew();

            for (int r = 0; r < roomCount; r++)
            {
                Status = $"room {r + 1}/{roomCount}";
                var load = mruk.LoadSceneFromPrefab(prefabs[r]);
                while (!load.IsCompleted) yield return null;
                for (int f = 0; f < 6; f++) yield return null; // let EffectMesh build its colliders
                Physics.SyncTransforms();

                MRUKRoom room = mruk.GetCurrentRoom();
                int seed = StableSeed(prefabs[r].name); // per room, independent of list order, so failures reproduce
                Pose seat = PlayArea.ChooseSeat(room, seed);
                // Same order as the encounter: the board, then the rift clear of the hero cluster, then the rift's
                // patch of wall is cleared of crystals.
                _board.SetExclusion(Vector3.zero, 0f);
                int crystals = _board.Generate(room, seat, seed, director.HeroShot(seat));
                var rift = RiftPlacer.Place(room, seat, null, _board.HeroPoint);
                if (!rift.OnWall) floatingRifts++;
                _board.SetExclusion(rift.ExclusionCenter, rift.ExclusionRadius);
                crystals -= _board.ClearExclusion();
                _board.SetExclusion(Vector3.zero, 0f);
                Vector3 slingPos = director.SlingPosition(seat);
                var stats = new Stats();
                _everHit.Clear();
                var rng = new System.Random(seed * 31 + 17);

                for (int i = 0; i < _shotsPerRoom; i++)
                {
                    Kind kind = i == 0 ? Kind.Straight : (i % 2 == 0 ? Kind.Aimed : Kind.Random);
                    Vector3 dir = AimFor(kind, seat, slingPos, rng, out float pull);
                    FireShot(room, slingPos, dir, pull, out int bounces, out bool escaped);
                    if (kind == Kind.Straight) _straightFirst = _firstLabel == null ? "none" : $"{_firstLabel} d={(_firstContact - seat.position).magnitude:F1}";
                    stats.Add(kind, _hitsThisShot, bounces, _spark.FlightTime, _spark.LastEnd, escaped);
                    all.Add(kind, _hitsThisShot, bounces, _spark.FlightTime, _spark.LastEnd, escaped);
                    if (clock.ElapsedMilliseconds > FrameBudgetMs) { yield return null; clock.Restart(); }
                }
                var nearRng = new System.Random(seed * 13 + 5);
                for (int i = 0; i < NearHeroShots; i++)
                {
                    Vector3 dir = NearHero(seat, nearRng, out float pull);
                    FireShot(room, slingPos, dir, pull, out int bounces, out bool escaped);
                    stats.Add(Kind.NearHero, _hitsThisShot, bounces, _spark.FlightTime, _spark.LastEnd, escaped);
                    all.Add(Kind.NearHero, _hitsThisShot, bounces, _spark.FlightTime, _spark.LastEnd, escaped);
                }

                int straightHits = stats.Hits[(int)Kind.Straight];
                string straightInfo = straightHits > 0 ? "" : $" first={_straightFirst}";
                if (straightHits == 0) straightMisses++;
                if (crystals < 24) sparseBoards++;
                float aimed = stats.Mean(Kind.Aimed);
                if (aimed < worstAimed) { worstAimed = aimed; worstRoom = prefabs[r].name; }

                string line = $"{prefabs[r].name,-28} n={crystals,2} straight={straightHits}{straightInfo} hero=[{_board.HeroInfo}] rift=[{rift.Info}] " +
                              $"rand={stats.Mean(Kind.Random):F2} ({stats.Pct(stats.AtLeastOne, Kind.Random):F0}%>=1) " +
                              $"aimed={aimed:F2} ({stats.Pct(stats.AtLeastOne, Kind.Aimed):F0}%>=1, {stats.Pct(stats.AtLeastThree, Kind.Aimed):F0}%>=3) " +
                              $"near={stats.Mean(Kind.NearHero):F2} " +
                              $"reach={(crystals == 0 ? 0 : 100 * _everHit.Count / crystals)}% " +
                              $"bounce={(float)stats.Bounces / stats.Total:F1} esc={100f * stats.Escapes / stats.Total:F0}% " +
                              $"end L/R/B/F={stats.Ends[1]}/{stats.Ends[2]}/{stats.Ends[3]}/{stats.Ends[4]}";
                report.AppendLine(line);
                Debug.Log("[Sweep] " + line);
            }

            string summary = $"ROOMS {roomCount} shots/room {_shotsPerRoom} floorEnds={_floorEndsShot} | " +
                             $"rand={all.Mean(Kind.Random):F2} ({all.Pct(all.AtLeastOne, Kind.Random):F0}%>=1) " +
                             $"aimed={all.Mean(Kind.Aimed):F2} ({all.Pct(all.AtLeastOne, Kind.Aimed):F0}%>=1, {all.Pct(all.AtLeastThree, Kind.Aimed):F0}%>=3) " +
                             $"near={all.Mean(Kind.NearHero):F2} ({all.Pct(all.AtLeastOne, Kind.NearHero):F0}%>=1, {all.Pct(all.AtLeastThree, Kind.NearHero):F0}%>=3) " +
                             $"straightMiss={straightMisses} sparse(<24)={sparseBoards} floatingRift={floatingRifts} " +
                             $"bounce={(float)all.Bounces / all.Total:F1} flight={all.Flight / all.Total:F1}s esc={100f * all.Escapes / all.Total:F1}% " +
                             $"end L/R/B/F={all.Ends[1]}/{all.Ends[2]}/{all.Ends[3]}/{all.Ends[4]} worst={worstRoom} ({worstAimed:F2})";
            // Balance: aimed damage per shot, and how many aimed shots each creature takes (HP only, lap 1).
            float dmg = Mathf.Max(0.01f, all.MeanDamage(Kind.Aimed));
            var kill = new System.Text.StringBuilder($" | aimedDmg={dmg:F2} killShots");
            foreach (var def in CreatureDef.Roster) kill.Append($" {def.Name.Replace("The ", "")}={def.Hp / dmg:F1}");
            summary += kill.ToString();
            report.AppendLine(summary);
            Debug.Log("[Sweep] " + summary);
            Report = report.ToString();

            _spark.CrystalHit -= OnCrystalHit;
            _spark.RoomBounced -= OnRoomBounced;
            _spark.FloorEndsShot = savedFloorRule;
            Physics.simulationMode = savedMode;
            _sling.enabled = true;
            director.enabled = true;
            Status = "done";
            Destroy(gameObject);
        }

        Vector3 AimFor(Kind kind, Pose seat, Vector3 slingPos, System.Random rng, out float pull)
        {
            Vector3 right = Vector3.Cross(Vector3.up, seat.forward);
            switch (kind)
            {
                case Kind.Straight:
                    // The board's own hero arc (it may be steeper than the relaxed default).
                    pull = ShotDirector.HeroPullAmount;
                    return _board.HeroVelocity.sqrMagnitude > 0f ? _board.HeroVelocity.normalized : ShotDirector.HeroDirection(seat);

                case Kind.Random:
                {
                    pull = Range(rng, 0.4f, 1f);
                    float yaw = Range(rng, -40f, 40f), pitch = Range(rng, -10f, 35f);
                    return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, right) * seat.forward;
                }

                default:
                {
                    // A player who picks a crystal and lines it up with the preview, with a little hand error.
                    pull = Range(rng, 0.7f, 1f);
                    var active = _board.Active;
                    Vector3 target = active.Count > 0 ? active[rng.Next(active.Count)].transform.position : slingPos + seat.forward;
                    Vector3 to = target - slingPos;
                    float speed = _sling.VelocityFor(Vector3.forward, pull).magnitude;
                    float t = to.magnitude / speed;
                    Vector3 dir = (to + Vector3.up * (0.5f * _spark.GravityAcceleration * t * t)).normalized;
                    var noise = Quaternion.Euler(Range(rng, -AimNoiseDegrees, AimNoiseDegrees), Range(rng, -AimNoiseDegrees, AimNoiseDegrees), 0f);
                    return noise * dir;
                }
            }
        }

        Vector3 NearHero(Pose seat, System.Random rng, out float pull)
        {
            Vector3 right = Vector3.Cross(Vector3.up, seat.forward);
            Vector3 hero = _board.HeroVelocity.sqrMagnitude > 0f ? _board.HeroVelocity.normalized : ShotDirector.HeroDirection(seat);
            pull = ShotDirector.HeroPullAmount + Range(rng, -NearHeroPull01, NearHeroPull01);
            return Quaternion.AngleAxis(Range(rng, -NearHeroDegrees, NearHeroDegrees), Vector3.up) *
                   Quaternion.AngleAxis(Range(rng, -NearHeroDegrees, NearHeroDegrees), right) * hero;
        }

        void FireShot(MRUKRoom room, Vector3 slingPos, Vector3 dir, float pull, out int bounces, out bool escaped)
        {
            foreach (var c in _board.Active) c.ResetState();
            _hitsThisShot = 0;
            _firstLabel = null;
            escaped = false;

            _spark.Hold(_sling.LaunchPoint(slingPos, dir, pull));
            Physics.SyncTransforms();
            _spark.Launch(_sling.VelocityFor(dir, pull));

            float dt = Gameplay.TimeWarp.PhysicsStep;
            for (int step = 0; step < 2000 && _spark.InFlight; step++)
            {
                _spark.Step(dt);
                if (!_spark.InFlight) break;
                Physics.Simulate(dt);
                if (step % 5 == 0 && !room.IsPositionInRoom(_spark.transform.position, true))
                {
                    escaped = true;
                    break;
                }
            }
            bounces = _spark.RoomBounces;
            _spark.Hold(slingPos);
        }

        string _straightFirst;

        void OnRoomBounced(Spark spark, Vector3 point, Vector3 normal)
        {
            if (_firstLabel != null) return;
            _firstContact = point;
            var hit = Physics.OverlapSphere(point, 0.05f, 1 << Layers.Room);
            var anchor = hit.Length > 0 ? hit[0].GetComponentInParent<MRUKAnchor>() : null;
            _firstLabel = anchor != null ? anchor.Label.ToString() : "?";
        }

        void OnCrystalHit(Spark spark, Crystal crystal)
        {
            if (crystal.IsLit) return;
            crystal.Light();
            _hitsThisShot++;
            _everHit.Add(crystal);
        }

        static int StableSeed(string name)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in name) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
