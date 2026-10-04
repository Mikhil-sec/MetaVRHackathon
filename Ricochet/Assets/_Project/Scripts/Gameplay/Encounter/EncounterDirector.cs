using System;
using System.Collections;
using System.Collections.Generic;
using Ricochet.Audio;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The encounter loop and the run around it (CONCEPT section 3). A rift opens on a real wall and a creature comes
    /// through, showing its next move. Each shot's popped crystals send their light into it as damage (chain value per
    /// crystal, so combos matter); then it acts: attacks your shield, guards, or hexes crystals. Seal the rift and the
    /// room fills with light (Fever), then you pinch one of three rewards before the next rift opens elsewhere.
    /// Five creatures, then the boss; lose your shield and the run starts over. The run saves after every turn.
    /// Hooks into ShotDirector's Intro and TurnGate, so the shot loop itself stays unchanged.
    /// </summary>
    public sealed class EncounterDirector : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] ShotDirector _director;
        [SerializeField] BoardGenerator _board;
        [SerializeField] Sling _sling;
        [SerializeField] Rift _rift;
        [SerializeField] Creature _creature;
        [SerializeField] LightMotes _motes;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] TimeWarp _warp;
        [SerializeField] ShatterFx _fx;
        [SerializeField] ScorePopups _popups;
        [SerializeField] RewardPicker _rewards;
        [SerializeField] Banner _banner;
        [SerializeField] Trophies _trophies;
        [SerializeField] MusicBed _music;

        [Header("Rules")]
        [SerializeField] int _maxShield = 30;
        [SerializeField] int _shieldRestore = 18;
        // Fewer crystals left than this: a fresh board next turn. A thinned-out board leaves only lone crystals (one hit,
        // one damage), which starved weaker players (soak test, session 11); every fresh board has a guaranteed hero shot.
        [SerializeField] int _refreshBelow = 16;
        [SerializeField] int _feverPoints = 50;
        [SerializeField] int _aegisShield = 10;
        [SerializeField] int _thornDamage = 2;
        [SerializeField] int _goldRushExtra = 3;

        [Header("Light")]
        [SerializeField] Color _damageGlow = new(1.6f, 1.1f, 0.4f);
        [SerializeField] Color _shieldHitGlow = new(2f, 0.25f, 0.2f);
        [SerializeField] Color _guardGlow = new(0.4f, 1.2f, 2f);
        [SerializeField] Color _victoryGlow = new(2.6f, 2f, 1.1f);
        [SerializeField] Color _feverWave = new(1.6f, 1.15f, 0.5f);
        [SerializeField] Color _runWave = new(0.9f, 0.6f, 1.8f);
        [SerializeField] float _feverWaveSpeed = 3.2f; // m/s: a 5 m room sweeps in ~1.5 s
        [SerializeField] Color _burstColor = new(1f, 0.35f, 0.8f);
        [SerializeField] Color _sparkArriveGlow = new(0.5f, 1.4f, 2f);
        [SerializeField] Color _shardColor = new(1f, 0.85f, 0.45f);

        readonly List<Crystal> _scratch = new();
        readonly Reward[] _offer = new Reward[3];
        CreatureDef _def;
        int _intentIndex;
        int _tick;
        Vector3? _lastRift;
        bool _placeRift;
        RiftPlacer.Placement _placement;
        readonly List<Vector3> _crowns = new();
        int _crownLanded;
        bool _hurtVoiced;                                // the creature's squeal, once per turn
        bool _announceDaily;                             // the Daily Rift's banner, once the first Spark is in the sling
        bool _announceAscension;                         // a harder run's banner, likewise
        bool _coldOpen = true;                           // the session's first encounter opens slowly (CONCEPT section 4)
        int _seedArrivals;
        RunState _run;
        System.Random _rng;

        public RunState Run => _run;
        public int Encounter => _run.Encounter;
        public int Shield => _run.Shield;
        public int MaxShield => _run.MaxShield;
        public Creature Creature => _creature;

        /// <summary>Shield changed: (new value, delta).</summary>
        public event Action<int, int> ShieldChanged;
        /// <summary>The run moved on (a new encounter, a reward, a new run): the HUD redraws its progress.</summary>
        public event Action RunChanged;

        /// <summary>This shot has already banked enough light to finish the creature.</summary>
        public bool ShotLethal => _creature.Alive && _director.ShotInProgress && _director.ShotLight >= _creature.EffectiveHp;

        /// <summary>The next crystal hit would bank the finishing light (Peggle's "this peg wins" moment).</summary>
        public bool NextHitLethal => _creature.Alive && _director.ShotInProgress && !ShotLethal &&
                                     _director.ShotLight + _director.NextHitValue >= _creature.EffectiveHp;

        /// <summary>
        /// Runs resume on device. In the Editor they start fresh (the desktop test scripts expect encounter 1) unless
        /// PlayerPrefs "Ricochet.ResumeInEditor" is 1 (DevHooks.ResumeInEditor).
        /// </summary>
        static bool ResumeEnabled => !Application.isEditor || PlayerPrefs.GetInt("Ricochet.ResumeInEditor", 0) == 1;

        void Awake()
        {
            Leaderboard.Init();
            var saved = ResumeEnabled ? RunState.Load() : null;
            StartRun(saved);
            if (saved != null) Debug.Log($"[Ricochet] Run resumed: {saved}");
            _director.Intro = OpenEncounter;
            // A killing shot opens the next encounter, which generates its own board: a Prism need not.
            _director.WillRenewBoard = () => _creature.Alive && _director.ShotLight >= _creature.EffectiveHp;
            _director.TurnGate = Turn;
            _director.BoardShaper = ShapeBoard;
        }

        void StartRun(RunState saved)
        {
            // The first new run of the day is the Daily Rift: the same seed, boards and starting gift for everyone today.
            int daily = saved == null && DailyRift.Due ? DailyRift.Today : 0;
            _run = saved ?? RunState.New(_maxShield, daily);
            _rng = new System.Random(_run.Seed + 7919 * _run.Encounter);
            _director.Run = _run;
            if (saved != null) _director.RestoreScore(saved.Score, saved.BestCombo);
            else _director.ResetScore();
            if (daily != 0)
            {
                DailyRift.MarkPlayed(daily);
                var gift = DailyRift.Gift(daily);
                Gift(gift.spark);
                Gift(gift.relic);
                Debug.Log($"[Ricochet] Daily Rift {daily}: gift {gift.spark.Name} + {gift.relic.Name}");
            }
            _announceDaily = _run.Daily != 0;
            _announceAscension = saved == null && _run.Daily == 0 && _run.Ascension > 0;
            _rift.SetDaily(_run.Daily != 0);
            ApplyRelicsToBoard();
            ShieldChanged?.Invoke(_run.Shield, 0);
            RunChanged?.Invoke();
        }

        // A starting gift goes straight into the fresh run (this can run in Awake, before the sling and HUD are up; they
        // read the run when they arm and draw).
        void Gift(Reward reward)
        {
            if (reward.Type == RewardType.Spark)
            {
                if (!_run.Bag.Contains(reward.Spark)) _run.Bag.Add(reward.Spark);
                _run.BagIndex = _run.Bag.IndexOf(reward.Spark); // the gift is the first Spark you hold
            }
            else if (reward.Type == RewardType.Relic)
            {
                _run.Add(reward.Relic);
                if (reward.Relic == Relic.Aegis) _run.Shield = _run.MaxShield += _aegisShield;
            }
        }

        void ApplyRelicsToBoard() => _board.ExtraGold = _run.Has(Relic.GoldRush) ? _goldRushExtra : 0;

        void OnEnable()
        {
            _director.CrystalPopped += OnCrystalPopped;
            _motes.Arrived += OnMoteArrived;
            _sling.Arrived += OnSparkArrived;
        }

        void OnDisable()
        {
            _director.CrystalPopped -= OnCrystalPopped;
            _motes.Arrived -= OnMoteArrived;
            _sling.Arrived -= OnSparkArrived;
        }

        /// <summary>The cold open's Spark settles into the band: a chime, a cyan pulse on the real room, a little burst.</summary>
        void OnSparkArrived()
        {
            Vector3 at = _sling.transform.position;
            _sfx.PlayGrab(at);
            _glow.Pulse(at, _sparkArriveGlow, 0.7f, 0.5f);
            if (_fx != null) _fx.Burst(at, _sparkArriveGlow, 0.5f);
        }

        // Quitting or taking the headset off mid-run: keep what the last turn saved, plus the score so far.
        void OnApplicationPause(bool paused)
        {
            if (paused && _run != null && !_director.ShotInProgress) SaveRun();
        }

        void SaveRun()
        {
            bool mid = _creature.Alive && _creature.gameObject.activeSelf;
            _run.CreatureHp = mid ? _creature.Hp : -1;
            _run.CreatureArmor = mid ? _creature.Armor : 0;
            _run.IntentIndex = _intentIndex;
            _run.Score = _director.Score;
            _run.BestCombo = _director.BestCombo;
            _run.Save();
        }

        /// <summary>
        /// Where the creature settles. Its intent is critical info, so it must sit inside the Glasses' +/-25 degree
        /// band (TECH_GUIDE section 7) even when the rift uses the +/-30 degree tier: past 20 degrees it glides out of
        /// the rift toward the centre of view (rotated about the eye) and a little nearer.
        /// </summary>
        Vector3 CreatureHome()
        {
            const float maxYaw = 20f;
            // The seat, not the live head: a lean (or a trailer camera) must not move where the creature settles.
            Vector3 eye = _playArea.Seat.position;
            // Out of the wall by the creature's own reach (body plus orbiting shards ~1x its drawn size): it grows with
            // distance, so a far wall's Queen (~0.75 m) would otherwise sink half into the wall and be occluded by it.
            Vector3 wall = _rift.transform.position, normal = _rift.transform.forward;
            float size = _def.Size * Creature.DistanceScale(Vector3.Distance(eye, wall));
            Vector3 mouth = wall + normal * Mathf.Max(0.32f, size + 0.06f);
            Vector3 forward = Vector3.ProjectOnPlane(_playArea.Seat.forward, Vector3.up);
            Vector3 to = mouth - eye;
            float yaw = Vector3.SignedAngle(forward, Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
            if (Mathf.Abs(yaw) <= maxYaw) return Unblocked(eye, mouth);
            Quaternion swing = Quaternion.AngleAxis(Mathf.Clamp(yaw, -maxYaw, maxYaw) - yaw, Vector3.up);
            return Unblocked(eye, eye + swing * to * 0.85f);
        }

        /// <summary>
        /// The swung home can land inside furniture (a wall cabinet over a bed hid the whole body, session 8). Walk it
        /// toward the seat until it is out of every scene volume and the seat sees it with room to spare.
        /// </summary>
        Vector3 Unblocked(Vector3 eye, Vector3 home)
        {
            const float clearance = 0.3f, step = 0.1f;
            var room = _playArea.Room;
            for (int i = 0; i < 12; i++)
            {
                Vector3 to = home - eye;
                float d = to.magnitude;
                bool inside = room != null && room.IsPositionInSceneVolume(home, clearance * 0.5f);
                bool hidden = Physics.Raycast(eye, to / d, d + clearance, 1 << Layers.Room);
                if ((!inside && !hidden) || d < 1.5f) break;
                home -= to / d * step;
            }
            return home;
        }

        IEnumerator OpenEncounter()
        {
            // The session's first rift is a cold open (CONCEPT section 4, zero text): the real room dims, a beat of
            // stillness, a slow hairline crack, the crystals spill out of it, the creature follows, and last the
            // Spark itself flies out of the rift into the sling. Later encounters keep the quick version.
            bool cold = _coldOpen;
            _coldOpen = false;
            float t0 = Time.time;
            _def = CreatureDef.ForEncounter(_run.Encounter, _run.Ascension);
            RunChanged?.Invoke();
            if (cold) yield return WaitReal(1.4f);
            // The board comes first so the rift can keep clear of the hero cluster (the guaranteed first shot);
            // PlaceRift runs as the board shaper, then clears crystals from the rift's patch of wall.
            _board.SetExclusion(Vector3.zero, 0f);
            _placeRift = true;
            _director.SeedBoards(_run.Seed + 7919 * _run.Encounter); // one run, one set of boards (the daily is everyone's)
            _director.RegenerateBoard(); // crystals cascade in while the crack spreads
            var place = _placement;
            Debug.Log($"[Ricochet] Encounter {_run.Encounter + 1}/{RunState.EncountersPerRun}: {_def.Name} (HP {_def.Hp}), rift {place.Info}");
            _rift.Open(place.Position, place.Normal, cold ? 2.2f : -1f);
            if (_trophies != null) _trophies.Yield(place.Position, 0.55f); // a crown under the crack steps aside
            _sfx.PlayRiftOpen(place.Position);
            if (_music != null) _music.SetIntensity(_def.Boss ? 1f : 0f);
            if (_def.Boss && _banner != null) _banner.Show(_def.Name, "the last rift", _def.Identity.a > 0f ? _def.Identity / Mathf.Max(1f, _def.Identity.maxColorComponent) : _burstColor, 3.2f);
            // The board spills out of the crack once it has split, nearest crystals first, each popping in as it lands.
            float spilled = Time.time - t0 + SeedBoard(_rift.Mouth, cold ? 1.2f : 0.45f, cold ? 1.6f : 0.9f);
            yield return Wait(cold ? 2.6f : _def.Boss ? 1.6f : 0.9f);

            _creature.Emerge(_def, place.Position, CreatureHome(), _playArea.Head);
            _sfx.PlayEmerge(place.Position, VoicePitch);
            _intentIndex = 0;
            if (_run.CreatureHp > 0)
            {
                // A resumed fight: the creature comes back as hurt as you left it.
                _creature.Restore(Mathf.Min(_run.CreatureHp, _def.Hp), _run.CreatureArmor);
                _intentIndex = _run.IntentIndex % _def.Cycle.Length;
            }
            yield return Wait(0.8f);
            _creature.ShowIntent(_def.Cycle[_intentIndex]);
            SaveRun();
            while (Time.time - t0 < spilled) yield return null; // every crystal is in place before the Spark arms
            if (cold) _sling.ArriveFrom = _rift.Mouth;          // the rift's light, now yours
            if (_announceDaily)
            {
                _announceDaily = false;
                StartCoroutine(AnnounceDaily(cold ? 1.3f : 0.4f));
            }
            else if (_announceAscension)
            {
                _announceAscension = false;
                StartCoroutine(AnnounceAscension(cold ? 1.3f : 0.4f));
            }
        }

        /// <summary>Each creature's voice: small ones chirp higher, big ones rumble lower (Wisp ~1.25 .. Queen ~0.8).</summary>
        float VoicePitch => Mathf.Lerp(1.25f, 0.8f, Mathf.InverseLerp(0.24f, 0.42f, _def != null ? _def.Size : 0.3f));

        /// <summary>
        /// Where a damage number goes: beside the creature, alternating sides, never over its body (the cracks, the
        /// seizure and the shatter are the feedback that matters there).
        /// </summary>
        Vector3 BesideCreature(int n)
        {
            Vector3 at = _creature.Center;
            Vector3 eye = _playArea.Head != null ? _playArea.Head.position : _playArea.Seat.position;
            Vector3 right = Vector3.Cross(eye - at, Vector3.up).normalized; // the viewer's right
            float side = (n & 1) == 0 ? 1f : -1f;
            return at + right * (side * _creature.Size * 1.25f) + Vector3.up * (_creature.Size * 0.35f);
        }

        /// <summary>Once the Spark is in the sling (the opening itself stays wordless): which day, and the day's gift.</summary>
        IEnumerator AnnounceDaily(float delay)
        {
            yield return WaitReal(delay);
            if (_banner == null) yield break;
            var gift = DailyRift.Gift(_run.Daily);
            int best = DailyRift.Best(_run.Daily);
            string sub = $"{DailyRift.Label(_run.Daily)}    {gift.spark.Name} + {gift.relic.Name}";
            _banner.Show("Daily Rift", best > 0 ? sub + $"    best {best:N0}" : sub, DailyColor, 3.5f);
        }

        static readonly Color DailyColor = new(1f, 0.66f, 0.3f);
        static readonly Color AscensionColor = new(1f, 0.45f, 0.28f);

        IEnumerator AnnounceAscension(float delay)
        {
            yield return WaitReal(delay);
            if (_banner != null)
                _banner.Show($"Ascension {_run.Ascension}", "the rifts grow stronger", AscensionColor, 3f);
        }

        /// <summary>The run is over (sealed or lost): keep the score where it counts. Returns the banner's extra line.</summary>
        string RecordRun(bool won)
        {
            int score = _director.Score;
            Leaderboard.Submit(Leaderboard.BestRun, score);
            if (_run.Daily == 0)
            {
                // A completed run climbs one Ascension tier (a loss never lowers it).
                if (!won || _run.Ascension >= Ascension.Max) return "";
                return $"    ascension {Ascension.Completed(_run.Ascension)} next";
            }
            bool best = DailyRift.Record(_run.Daily, score);
            if (best) return "    daily best!";
            return DailyRift.Best(_run.Daily) > 0 ? $"    daily best {DailyRift.Best(_run.Daily):N0}" : "";
        }

        /// <summary>
        /// Every crystal of the fresh board flies out of the rift to its place as a streak of light and pops in where it
        /// lands, nearest first, so the board reads as something the rift let into the room. Visual only: the crystals
        /// (and their colliders) are already placed; they are just hidden until their streak arrives.
        /// </summary>
        /// <returns>Seconds until the last crystal lands.</returns>
        float SeedBoard(Vector3 from, float start, float spread)
        {
            var active = _board.Active;
            float far = 0.01f;
            for (int i = 0; i < active.Count; i++) far = Mathf.Max(far, Vector3.Distance(from, active[i].transform.position));
            float last = 0f;
            _seedArrivals = 0;
            Vector3 outward = _rift.transform.forward; // out of the wall: the streams pour into the room, then curve home
            for (int i = 0; i < active.Count; i++)
            {
                Vector3 to = active[i].transform.position;
                float d = Vector3.Distance(from, to);
                float delay = start + spread * (d / far) * (0.8f + 0.2f * Mathf.Repeat(i * 0.618034f, 1f));
                float flight = 0.38f + 0.08f * d;
                _motes.Launch(LightMotes.Kind.Seed, i, from, to, delay, flight, outward);
                active[i].Appear(delay + flight * 0.9f);
                last = Mathf.Max(last, delay + flight);
            }
            return last;
        }

        int ShapeBoard()
        {
            if (_placeRift)
            {
                _placeRift = false;
                if (_trophies != null) _trophies.GetPositions(_crowns);
                _placement = RiftPlacer.Place(_playArea.Room, _playArea.Seat, _lastRift, _board.HeroPoint, _crowns);
                _lastRift = _placement.Position;
                _board.SetExclusion(_placement.ExclusionCenter, _placement.ExclusionRadius);
                _board.ClearExclusion();
            }
            return _board.Active.Count;
        }

        void OnCrystalPopped(Vector3 at, int light)
        {
            if (light <= 0 || !_creature.Alive) return;
            // Brighter chains fly a touch slower, so a big combo lands as a visible volley.
            _motes.Launch(LightMotes.Kind.Damage, light, at, _creature.Center, 0.05f, 0.5f + 0.02f * light);
        }

        void OnMoteArrived(LightMotes.Kind kind, int amount, Vector3 at)
        {
            switch (kind)
            {
                case LightMotes.Kind.Seed:
                    if ((_seedArrivals++ & 1) == 0) _sfx.PlaySeed(_seedArrivals / 2, at);
                    break;

                case LightMotes.Kind.Crown:
                    _sfx.PlaySeed(4 + _crownLanded++, at); // the crown arrives as a rising run of notes
                    break;

                case LightMotes.Kind.Damage:
                    bool armored = _creature.Armor > 0;
                    int dealt = _creature.TakeDamage(amount);
                    if (!_hurtVoiced && dealt > 0 && _creature.Alive)
                    {
                        _hurtVoiced = true; // it squeals once per turn, at the first light that gets through
                        _sfx.PlayHurt(_creature.Center, VoicePitch);
                    }
                    _sfx.PlayTick(_tick++, at);
                    if (armored) _sfx.PlayGuard(at); // light spent on armor rings hollow
                    if (_popups != null) _popups.Show(BesideCreature(_tick), amount, _tick, true);
                    _glow.Pulse(at, armored ? _guardGlow : _damageGlow, 0.6f, 0.3f);
                    break;

                case LightMotes.Kind.Bolt:
                    SetShield(_run.Shield - amount);
                    _sfx.PlayShieldHit(at);
                    _glow.Pulse(at, _shieldHitGlow, 0.8f, 0.5f);
                    if (_fx != null) _fx.Burst(at, _burstColor);
                    if (_popups != null) _popups.Show(at + Vector3.up * 0.08f, amount, 0, true);
                    if (_run.Has(Relic.Thornlight) && _creature.Alive)
                    {
                        // Thornlight: the shield throws some of the blow back as light.
                        _motes.Launch(LightMotes.Kind.Damage, _thornDamage, at, _creature.Center, 0.1f, 0.45f);
                        _glow.Pulse(at, Upgrades.RelicColor * 1.3f, 0.6f, 0.4f);
                        _director.TriggerRelic(Relic.Thornlight, at);
                    }
                    break;

                case LightMotes.Kind.Hex:
                    var active = _board.Active;
                    if (amount >= 0 && amount < active.Count) active[amount].Corrupt();
                    _glow.Pulse(at, _burstColor * 0.6f, 0.4f, 0.3f);
                    break;
            }
        }

        void SetShield(int value)
        {
            value = Mathf.Clamp(value, 0, _run.MaxShield);
            int delta = value - _run.Shield;
            _run.Shield = value;
            ShieldChanged?.Invoke(value, delta);
        }

        IEnumerator Turn()
        {
            // Let the light land first: the damage ticks are the payoff of the shot.
            while (_motes.InFlight > 0) yield return null;
            _tick = 0;
            _hurtVoiced = false;
            yield return Wait(0.2f);

            if (!_creature.Alive)
            {
                yield return Victory();
                yield break;
            }

            yield return CreatureAct(_creature.NextIntent);
            if (!_creature.Alive)
            {
                yield return Victory(); // Thornlight finished it on its own attack
                yield break;
            }
            if (_run.Shield <= 0)
            {
                yield return Defeat();
                yield break;
            }

            _intentIndex = (_intentIndex + 1) % _def.Cycle.Length;
            _creature.ShowIntent(_def.Cycle[_intentIndex]);

            if (_board.RemainingCount() < _refreshBelow)
            {
                _director.RegenerateBoard();
                yield return Wait(0.5f);
            }
            SaveRun();
        }

        IEnumerator CreatureAct(Intent intent)
        {
            // Anticipation: it draws back and burns in the color of its move, with a rising growl.
            _sfx.PlayGrowl(_creature.Center, VoicePitch);
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                _creature.SetWindup(t / 0.45f);
                yield return null;
            }
            _creature.Lunge();
            Vector3 from = _creature.Center;

            switch (intent.Kind)
            {
                case IntentKind.Attack:
                    _sfx.PlayBolt(from);
                    // Aim at your shield: just below the sling, never at the face.
                    _motes.Launch(LightMotes.Kind.Bolt, intent.Amount, from, _sling.transform.position + Vector3.down * 0.06f, 0f, 0.55f);
                    break;

                case IntentKind.Guard:
                    _creature.AddArmor(intent.Amount);
                    _sfx.PlayGuard(from);
                    _glow.Pulse(from, _guardGlow, 0.9f, 0.6f);
                    break;

                case IntentKind.Hex:
                    int n = HexTargets(intent.Amount);
                    for (int i = 0; i < n; i++)
                    {
                        int index = BoardIndex(_scratch[i]);
                        _motes.Launch(LightMotes.Kind.Hex, index, from, _scratch[i].transform.position, i * 0.08f, 0.5f);
                    }
                    if (n > 0) _sfx.PlayBolt(from);
                    break;
            }
            while (_motes.InFlight > 0) yield return null;
            yield return Wait(0.3f);
        }

        int BoardIndex(Crystal crystal)
        {
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i] == crystal) return i;
            return -1;
        }

        /// <summary>Picks up to n unlit, clean crystals to hex, favouring the ones nearest the view center.</summary>
        int HexTargets(int n)
        {
            _scratch.Clear();
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (!c.IsPopped && !c.IsLit && !c.IsCorrupt) _scratch.Add(c);
            }
            Vector3 eye = _playArea.Seat.position, fwd = _playArea.Seat.forward;
            _scratch.Sort((a, b) => Vector3.Angle(fwd, a.transform.position - eye).CompareTo(Vector3.Angle(fwd, b.transform.position - eye)));
            // Spread the hex through the central group instead of always the same crystals.
            int pool = Mathf.Min(_scratch.Count, n * 3);
            for (int i = 0; i < Mathf.Min(n, pool); i++)
            {
                int j = UnityEngine.Random.Range(i, pool);
                (_scratch[i], _scratch[j]) = (_scratch[j], _scratch[i]);
            }
            return Mathf.Min(n, pool);
        }

        IEnumerator Victory()
        {
            Vector3 at = _creature.Center;
            bool boss = _def.Boss;
            Debug.Log($"[Ricochet] Encounter {_run.Encounter + 1} won: {_def.Name} sealed");
            // The killing blow lands (crash, a flash of light); it seizes, cracks blazing, inside the hit-stop; then it
            // breaks apart along its cracks (shatter, chord, the big pulse) and the rift zips shut behind it.
            _creature.Die();
            _sfx.PlayCrash(at);
            _glow.Pulse(at, _victoryGlow, 1.2f, 1f);
            if (_fx != null) _fx.Burst(at, _burstColor);
            _warp.Hold(boss ? 0.2f : 0.3f, boss ? 1.6f : 0.9f);
            for (float waited = 0f; !_creature.Shattered && _creature.gameObject.activeSelf && waited < 3f; waited += Time.deltaTime)
                yield return null;
            at = _creature.Center;
            _sfx.PlayShatter(at);
            _sfx.PlayChord(at);
            if (_music != null) { _music.Lift(4f); _music.SetIntensity(1f); }
            _glow.Pulse(at, _victoryGlow, 2.2f, 1.6f);
            if (_fx != null) { _fx.Burst(at, _shardColor); _fx.Burst(at, _burstColor); }
            yield return WaitReal(0.3f);
            _rift.Seal();
            if (_trophies != null) _trophies.Return();
            // Game time: the zip runs on it (a pause or a freeze must not let Fever start mid-zip).
            for (float waited = 0f; _rift.IsZipping && waited < 2f; waited += Time.deltaTime) yield return null;
            yield return WaitReal(0.25f);

            yield return Fever(at);

            if (boss)
            {
                yield return RunComplete(at);
                yield break;
            }

            SetShield(_run.Shield + _shieldRestore);
            if (_music != null) _music.SetIntensity(0f);
            yield return WaitReal(1.2f);

            // Between rifts: three rewards float up in front of you; pinch one, draw it in, let go.
            _run.Encounter++;
            _run.CreatureHp = -1;
            _rng = new System.Random(_run.Seed + 7919 * _run.Encounter);
            _run.Save(); // an encounter won is never lost, even if you quit at the reward
            RunChanged?.Invoke();
            if (_rewards != null)
            {
                int count = Upgrades.Offer(_run, _rng, _offer);
                yield return _rewards.Choose(_offer, count);
                Apply(_offer[Mathf.Clamp(_rewards.Chosen, 0, count - 1)]);
            }
            yield return OpenEncounter();
        }

        /// <summary>
        /// Fever: a front of light races out from where the creature fell, across every real wall, and each crystal left
        /// shatters the moment the front reaches it. A rising swell carries it.
        /// </summary>
        IEnumerator Fever(Vector3 at)
        {
            _glow.Wave(at, _feverWave, _feverWaveSpeed, 0.16f, 9f);
            _sfx.PlaySwell(at);
            _scratch.Clear();
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].IsPopped) _scratch.Add(active[i]);
            _scratch.Sort((a, b) => (a.transform.position - at).sqrMagnitude.CompareTo((b.transform.position - at).sqrMagnitude));
            float front = 0f; // distance the wave has covered so far
            for (int i = 0; i < _scratch.Count; i++)
            {
                var c = _scratch[i];
                Vector3 p = c.transform.position;
                float d = Vector3.Distance(p, at);
                if (d > front)
                {
                    yield return WaitReal((d - front) / _feverWaveSpeed);
                    front = d;
                }
                c.Light();
                c.Pop();
                if (_fx != null) _fx.Burst(p, _shardColor);
                _glow.Pulse(p, _victoryGlow * 0.6f, 0.8f, 0.5f);
                if (i % 2 == 0) _sfx.PlayComboNote(i / 2 % 15, p);
                _director.AddScore(_feverPoints);
                if (_popups != null && i % 3 == 0) _popups.Show(p, _feverPoints, Mathf.Min(i, 8));
            }
            Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
            _glow.Pulse(center, _victoryGlow, 4f, 1.8f);
            _sfx.PlayChord(center);
        }

        void Apply(Reward reward)
        {
            switch (reward.Type)
            {
                case RewardType.Spark:
                    int slot = _run.Bag.IndexOf(reward.Spark);
                    if (slot < 0) { _run.Bag.Add(reward.Spark); slot = _run.Bag.Count - 1; }
                    _run.BagIndex = slot; // try the new Spark on the very next shot
                    break;
                case RewardType.Relic:
                    _run.Add(reward.Relic);
                    if (reward.Relic == Relic.Aegis)
                    {
                        _run.MaxShield += _aegisShield;
                        SetShield(_run.MaxShield);
                    }
                    ApplyRelicsToBoard();
                    break;
                case RewardType.Mend:
                    SetShield(_run.Shield + Upgrades.MendAmount);
                    break;
            }
            _director.RefreshArmedKind();
            SaveRun();
            Debug.Log($"[Ricochet] Reward: {reward.Name}. Run: {_run}");
            RunChanged?.Invoke();
        }

        /// <summary>The Queen's crown stays on the wall where her rift was (behind a floating rift: the wall beyond it).</summary>
        /// <summary>Where the Queen's crown goes: her rift's wall (behind a floating rift: the wall beyond it).</summary>
        bool TrophySpot(out Vector3 position, out Vector3 normal)
        {
            position = _placement.Position;
            normal = _placement.Normal;
            if (_trophies == null) return false;
            if (_placement.OnWall) return true;
            Vector3 eye = _playArea.Seat.position;
            Vector3 dir = (position - eye).normalized;
            if (_playArea.Room.Raycast(new Ray(eye, dir), 10f, out RaycastHit hit, out var anchor) && anchor != null &&
                (anchor.Label & Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels.WALL_FACE) != 0)
            {
                position = hit.point;
                normal = hit.normal;
                return true;
            }
            // Furniture in the way (or no wall on that line): the wall nearest to where her rift hung.
            float d = _playArea.Room.TryGetClosestSurfacePosition(position, out Vector3 surface, out var wall, out Vector3 n,
                Meta.XR.MRUtilityKit.LabelFilter.Included(Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels.WALL_FACE));
            if (float.IsInfinity(d) || wall == null || n.sqrMagnitude < 0.5f) return false;
            if (Vector3.Dot(n, eye - surface) < 0f) n = -n; // out of the wall, into the room
            position = surface;
            normal = n.normalized;
            return true;
        }

        /// <summary>
        /// The boss is sealed: the whole room answers in waves of light, her crown streams as gold light to the wall where
        /// her rift was and grows there as your trophy, then a new run begins.
        /// </summary>
        IEnumerator RunComplete(Vector3 fell)
        {
            Debug.Log($"[Ricochet] Run complete! score {_director.Score}, best chain {_director.BestCombo}");
            RunState.Clear();
            Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
            for (int i = 0; i < 3; i++)
            {
                _glow.Wave(center + UnityEngine.Random.insideUnitSphere * 0.8f, i == 1 ? _runWave : _feverWave,
                           _feverWaveSpeed * 0.8f, 0.2f, 9f);
                _sfx.PlayChord(center);
                if (_fx != null) { _fx.Burst(center, _shardColor); _fx.Burst(center, _burstColor); }
                yield return WaitReal(0.7f);
            }
            if (TrophySpot(out Vector3 crownAt, out Vector3 wallNormal))
            {
                Vector3 to = crownAt + wallNormal * 0.05f;
                _crownLanded = 0;
                for (int i = 0; i < 6; i++)
                    _motes.Launch(LightMotes.Kind.Crown, 0, fell + UnityEngine.Random.insideUnitSphere * 0.08f, to, 0.07f * i, 0.8f, Vector3.up);
                yield return WaitReal(1.3f);
                _trophies.Place(crownAt, wallNormal, _director.Score, _director.BestCombo);
            }
            string extra = RecordRun(true);
            if (_banner != null) _banner.Show(_run.Daily != 0 ? "Daily Rift sealed" : "Rift sealed",
                $"score {_director.Score:N0}    best chain ×{_director.BestCombo}{extra}", _run.Daily != 0 ? DailyColor : _shardColor, 4.5f);
            yield return WaitReal(5f);

            _lastRift = null;
            StartRun(null);
            yield return OpenEncounter();
        }

        IEnumerator Defeat()
        {
            Debug.Log($"[Ricochet] Run lost at encounter {_run.Encounter + 1} (score {_director.Score})");
            RunState.Clear();
            Vector3 heart = _sling.transform.position;
            _glow.Pulse(heart, _shieldHitGlow * 1.5f, 3f, 2f);
            _sfx.PlayShieldHit(heart);
            yield return WaitReal(1.2f);
            _creature.Retreat(); // it slips back into the rift, and the rift zips shut on it
            if (_music != null) _music.SetIntensity(0f);
            yield return WaitReal(0.6f);
            _rift.Seal();
            if (_trophies != null) _trophies.Return();
            string extra = RecordRun(false);
            if (_banner != null) _banner.Show("The rift holds", $"score {_director.Score:N0}    rift {_run.Encounter + 1} of {RunState.EncountersPerRun}{extra}",
                new Color(1f, 0.35f, 0.5f), 3.2f);
            yield return WaitReal(3.6f);

            _lastRift = null;
            StartRun(null);
            yield return OpenEncounter();
        }

        /// <summary>Dev: pick up a Spark type or relic without winning an encounter.</summary>
        public void Grant(Reward reward) => Apply(reward);

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        static IEnumerator WaitReal(float seconds)
        {
            for (float t = 0f; t < seconds; t += RealTime.DeltaTime) yield return null;
        }
    }
}
