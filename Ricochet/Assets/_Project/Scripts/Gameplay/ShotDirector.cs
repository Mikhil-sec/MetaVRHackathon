using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Ricochet.Audio;
using Ricochet.Input;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Grey-box game loop: place the sling in front of the seat, generate a board, run shots,
    /// score rising combos, pop lit crystals at shot end, regenerate on clear.
    /// </summary>
    public sealed class ShotDirector : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] Sling _sling;
        [SerializeField] Spark _spark;
        [SerializeField] BoardGenerator _board;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] ShatterFx _fx;
        [SerializeField] TimeWarp _warp;
        [SerializeField] ScorePopups _popups;
        [SerializeField] Oculus.Interaction.Input.Hand _leftHand;
        [SerializeField] Oculus.Interaction.Input.Hand _rightHand;
        [SerializeField] Transform _trackingSpace;

        [Header("Sling placement relative to the seated head")]
        // About 20 deg below eye level (TECH_GUIDE section 7 keeps the sling within +/-20 deg vertical), ~40 cm out.
        [SerializeField] float _slingForward = 0.42f;
        [SerializeField] float _slingDown = 0.15f;

        [Header("Room light (RoomGlow pulses)")]
        // Linear HDR-ish intensities: RoomGlow adds light over passthrough, so it needs to be strong to read.
        [SerializeField] Color _hitGlow = new(1.3f, 0.8f, 2f);
        [SerializeField] Color _popGlow = new(2.2f, 1.7f, 0.75f);
        [SerializeField] Color _bounceGlow = new(0.6f, 1.7f, 2f);
        [SerializeField] Color _shardColor = new(1f, 0.85f, 0.45f);
        [SerializeField] Color _corruptGlow = new(0.9f, 0.1f, 0.35f);
        [SerializeField] Color _ampGlow = new(0.4f, 2f, 1.8f);
        [SerializeField] Color _bombGlow = new(2.4f, 0.9f, 0.3f);
        [SerializeField] Color _prismGlow = new(1.6f, 1.4f, 2.2f);
        [SerializeField] Color _focusGlow = new(0.9f, 2f, 2.2f);
        [Tooltip("Optional: Focus (gaze). A hit on the focused crystal is a critical.")]
        [SerializeField] GazeFocus _focus;

        [Header("Crystal types")]
        [SerializeField] float _bombRadius = 0.4f;
        [SerializeField] float _bigBangScale = 1.6f;

        [Header("Spark types")]
        [SerializeField] float _splitAngle = 32f;       // each child fans this far off the parent's bounce
        [SerializeField] int _sparkBombHits = 3;        // the Bomb Spark bursts on this clean hit
        [SerializeField] float _sparkBombRadius = 0.5f;
        [SerializeField] int _maxCarom = 5;

        [Header("Scoring")]
        [SerializeField] int _basePoints = 10;
        [SerializeField] float _popInterval = 0.07f;

        readonly List<Crystal> _litThisShot = new();
        readonly List<int> _litValues = new();
        readonly List<Crystal> _blast = new();
        readonly Spark[] _children = new Spark[2];     // the Splitter's two extra Sparks, pooled
        int _mult = 1;
        bool _prismHit;
        WaitForSeconds _popWait;
        Rigidbody _sparkBody;
        int _combo;
        int _boardSeed = 1;
        int _live;              // Sparks still flying this shot
        bool _split, _sparkBurst, _skyCrit, _anyHit;
        int _sparkHits, _carom;

        /// <summary>The run in progress (relics and the Spark bag); null plays plain Sparks with no relics.</summary>
        public RunState Run { get; set; }
        bool Has(Relic relic) => Run != null && Run.Has(relic);

        public int Score { get; private set; }
        public int LastShotScore { get; private set; }
        public int BestCombo { get; private set; }
        /// <summary>Current chain length this shot (0 before the first hit).</summary>
        public int Combo => _combo;
        public bool ShotInProgress { get; private set; }

        /// <summary>A crystal was lit for the first time: (crystal, combo index of this hit).</summary>
        public event System.Action<Crystal, int> CrystalLit;
        /// <summary>The shot's lit crystals have all popped: (crystals, points this shot).</summary>
        public event System.Action<int, int> ShotScored;
        public event System.Action BoardGenerated;
        /// <summary>A lit crystal popped at shot end: (position, chain value = its hit index + 1).</summary>
        public event System.Action<Vector3, int> CrystalPopped;
        /// <summary>A relic just acted (relic, where): the HUD punches its glyph and shows it at the spot.</summary>
        public event System.Action<Relic, Vector3> RelicTriggered;

        /// <summary>Reports a relic's moment (also used by the encounter, e.g. Thornlight's reply).</summary>
        public void TriggerRelic(Relic relic, Vector3 at)
        {
            Debug.Log($"[Ricochet] Relic: {relic}");
            RelicTriggered?.Invoke(relic, at);
        }

        /// <summary>Optional: runs once the room is ready, before the first board and arming (the encounter intro).</summary>
        public System.Func<IEnumerator> Intro;
        /// <summary>Optional: runs after each shot is scored, before the sling re-arms (the encounter's turn).</summary>
        public System.Func<IEnumerator> TurnGate;
        /// <summary>Optional: true when the turn that follows will renew the board anyway (a Prism then skips its own).</summary>
        public System.Func<bool> WillRenewBoard;
        /// <summary>Optional: adjusts a freshly generated board before it is announced; returns the crystal count.</summary>
        public System.Func<int> BoardShaper;
        /// <summary>Chain units of light banked this shot (each hit adds its chain value: 1, 2, 3...).</summary>
        public int ShotLight => LastShotScore / Mathf.Max(1, _basePoints);

        void Awake()
        {
            _popWait = new WaitForSeconds(_popInterval);
            _sparkBody = _spark.GetComponent<Rigidbody>();
            for (int i = 0; i < _children.Length; i++) _children[i] = _spark.CreateChild("SplitSpark" + i);
        }

        void OnEnable()
        {
            _playArea.Ready += OnRoomReady;
            _playArea.Reseated += OnReseated;
            Subscribe(_spark, true);
            for (int i = 0; i < _children.Length; i++) if (_children[i] != null) Subscribe(_children[i], true);
            _sling.Launched += OnLaunched;
        }

        void OnDisable()
        {
            _playArea.Ready -= OnRoomReady;
            _playArea.Reseated -= OnReseated;
            Subscribe(_spark, false);
            for (int i = 0; i < _children.Length; i++) if (_children[i] != null) Subscribe(_children[i], false);
            _sling.Launched -= OnLaunched;
        }

        void Subscribe(Spark spark, bool on)
        {
            if (on)
            {
                spark.CrystalHit += OnCrystalHit;
                spark.RoomBounced += OnRoomBounced;
                spark.Died += OnSparkDied;
                spark.Phased += OnPhased;
            }
            else
            {
                spark.CrystalHit -= OnCrystalHit;
                spark.RoomBounced -= OnRoomBounced;
                spark.Died -= OnSparkDied;
                spark.Phased -= OnPhased;
            }
        }

        /// <summary>Loads the run's next Spark type onto the sling and arms it.</summary>
        void ArmNext()
        {
            _spark.SetKind(Run != null ? Run.NextSpark : SparkKind.Plain);
            _sling.Arm();
        }

        /// <summary>The bag changed between shots (a reward): the Spark already waiting in the sling takes the new type.</summary>
        public void RefreshArmedKind()
        {
            if (_sling.IsReady) _spark.SetKind(Run != null ? Run.NextSpark : SparkKind.Plain);
        }

        /// <summary>A resumed run's score.</summary>
        public void RestoreScore(int score, int bestCombo)
        {
            Score = score;
            BestCombo = bestCombo;
        }

        void PlaceSling()
        {
            _sling.transform.position = SlingPosition(_playArea.Seat);
            // Face the seat's forward so the band's posts sit left and right of the player (SlingFx).
            Vector3 flat = Vector3.ProjectOnPlane(_playArea.Seat.forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f) _sling.transform.rotation = Quaternion.LookRotation(flat);
            IgnoreSeatFurniture(_playArea.Room, _playArea.Seat);
        }

        readonly Collider[] _seatFurniture = new Collider[8];
        readonly List<Collider> _anchorColliders = new();

        /// <summary>
        /// The furniture the player sits in. A scene volume is a box up to its highest point, so in a deep armchair or
        /// on a couch with a tall back the sling, or the hand drawing it back, can be inside that box: every Spark
        /// would hit it from the inside and die on launch. Sparks never collide with a volume that holds the sling or
        /// a draw's launch point (a hand can't be inside a real desk, so a desk in front of you stays in play).
        /// Call when the seat changes; the room sweep calls it per room.
        /// </summary>
        public void IgnoreSeatFurniture(MRUKRoom room, Pose seat)
        {
            int count = 0;
            if (room != null)
            {
                Vector3 sling = SlingPosition(seat);
                Vector3 hero = HeroDirection(seat);
                foreach (var anchor in room.Anchors)
                {
                    if (anchor == null || !anchor.VolumeBounds.HasValue || !HoldsSling(anchor, sling, hero)) continue;
                    anchor.GetComponentsInChildren(true, _anchorColliders);
                    for (int i = 0; i < _anchorColliders.Count && count < _seatFurniture.Length; i++) _seatFurniture[count++] = _anchorColliders[i];
                    Debug.Log($"[Ricochet] Seat furniture: {anchor.Label} holds the sling, the Spark passes through it ({_anchorColliders.Count} colliders)");
                }
            }
            _spark.SetSeatFurniture(_seatFurniture, count);
            for (int i = 0; i < _children.Length; i++) if (_children[i] != null) _children[i].SetSeatFurniture(_seatFurniture, count);
        }

        bool HoldsSling(MRUKAnchor anchor, Vector3 sling, Vector3 hero)
        {
            if (anchor.IsPositionInVolume(sling, true, 0.05f)) return true; // the Spark at rest, plus its radius
            // Where a half and a full draw put the Spark: straight ahead and aimed 30 deg to either side.
            for (int yaw = -30; yaw <= 30; yaw += 30)
            {
                Vector3 aim = Quaternion.AngleAxis(yaw, Vector3.up) * hero;
                if (anchor.IsPositionInVolume(_sling.LaunchPoint(sling, aim, 0.5f), true) ||
                    anchor.IsPositionInVolume(_sling.LaunchPoint(sling, aim, 1f), true)) return true;
            }
            return false;
        }

        void OnReseated() => StartCoroutine(PlaceSlingWhenFree());

        // A pull in progress keeps its sling; it moves the moment the hand lets go (the shot launches from the old one).
        IEnumerator PlaceSlingWhenFree()
        {
            while (_sling.IsPulling) yield return null;
            PlaceSling();
            Debug.Log($"[Ricochet] Sling re-seated at {_sling.transform.position:F2}");
        }

        void OnRoomReady()
        {
            PlaceSling();

            if (_leftHand != null) _sling.AddInput(new HandPinchInput(_leftHand));
            if (_rightHand != null) _sling.AddInput(new HandPinchInput(_rightHand));
            _sling.AddInput(new ControllerPinchInput(OVRInput.Controller.LTouch, _trackingSpace));
            _sling.AddInput(new ControllerPinchInput(OVRInput.Controller.RTouch, _trackingSpace));
            if (PlayArea.IsDesktop) _sling.AddInput(new MousePinchInput(Camera.main, _sling.transform));

            if (Intro != null) StartCoroutine(IntroThenArm());
            else
            {
                NewBoard();
                ArmNext();
            }
        }

        IEnumerator IntroThenArm()
        {
            yield return Intro(); // the intro owns board generation
            ArmNext();
        }

        const float HeroPull = 0.8f;
        const float HeroPitchUp = 3f;

        /// <summary>The relaxed straight-ahead shot a first-time player makes; the board guarantees it a cluster.</summary>
        public BoardGenerator.HeroShot HeroShot(Pose seat) => new()
        {
            Origin = _sling.LaunchPoint(SlingPosition(seat), HeroDirection(seat), HeroPull),
            Velocity = _sling.VelocityFor(HeroDirection(seat), HeroPull),
            Spark = _spark,
        };

        public static Vector3 HeroDirection(Pose seat) =>
            Quaternion.AngleAxis(-HeroPitchUp, Vector3.Cross(Vector3.up, seat.forward)) * seat.forward;

        public static float HeroPullAmount => HeroPull;

        public Vector3 SlingPosition(Pose seat) => seat.position + seat.forward * _slingForward + Vector3.down * _slingDown;

        /// <summary>Points from outside a shot (the Fever bonus).</summary>
        public void AddScore(int points) => Score += points;

        /// <summary>A new run: score and best chain back to zero.</summary>
        public void ResetScore()
        {
            Score = 0;
            BestCombo = 0;
        }

        /// <summary>A fresh board in the same room (it reveals as a cascade).</summary>
        public void RegenerateBoard() => NewBoard();

        /// <summary>The next board's seed (later boards in the encounter follow on from it).</summary>
        public void SeedBoards(int seed) => _boardSeed = seed;

        void NewBoard()
        {
            int count = _board.Generate(_playArea.Room, _playArea.Seat, _boardSeed++, HeroShot(_playArea.Seat));
            if (BoardShaper != null) count = BoardShaper();
            Debug.Log($"[Ricochet] Board generated: {count} crystals in room '{_playArea.Room.name}', hero {_board.HeroInfo}");
            BoardGenerated?.Invoke();
        }

        void OnLaunched(Vector3 velocity)
        {
            _combo = 0;
            _mult = 1;
            _prismHit = false;
            _split = _sparkBurst = _skyCrit = _anyHit = false;
            _sparkHits = _carom = 0;
            _live = 1;
            LastShotScore = 0;
            ShotInProgress = true;
            _litThisShot.Clear();
            _litValues.Clear();
            _spark.FloorGrace = Has(Relic.SecondWind) ? 1 : 0;
            _spark.MagnetTargets = _board.Active;
            _sfx.PlayLaunch(_spark.transform.position);
        }

        void OnCrystalHit(Spark spark, Crystal crystal)
        {
            if (crystal.IsLit) return;
            bool corrupt = crystal.IsCorrupt;
            crystal.Light();
            _litThisShot.Add(crystal);
            if (corrupt)
            {
                // A hexed crystal: no light, and the chain snaps back to the start.
                _litValues.Add(0);
                _combo = 0;
                _sfx.PlayBounce(crystal.transform.position, 1f);
                if (_glow != null) _glow.Pulse(crystal.transform.position, _corruptGlow, 0.6f, 0.4f);
                _spark.SetHeat(0f);
                CrystalLit?.Invoke(crystal, -1);
                return;
            }
            // Chain value: its place in the chain, times the shot's Amp multiplier. Criticals double it and stack:
            // Gold, Focus (gaze; x3 with Third Eye), the shot's first hit (First Light), a hit after a ceiling
            // bounce (Skylight).
            Vector3 at = crystal.transform.position;
            bool focused = _focus != null && crystal == _focus.Focused;
            int crit = (crystal.Kind == CrystalKind.Gold ? 2 : 1) * (focused ? (Has(Relic.ThirdEye) ? 3 : 2) : 1);
            bool firstLight = !_anyHit && Has(Relic.FirstLight);
            bool relicCrit = firstLight || _skyCrit;
            if (relicCrit) crit *= 2;
            if (firstLight) TriggerRelic(Relic.FirstLight, at);
            if (_skyCrit) TriggerRelic(Relic.Skylight, at);
            if (focused && Has(Relic.ThirdEye)) TriggerRelic(Relic.ThirdEye, at);
            _skyCrit = false;
            _anyHit = true;
            int value = NextHitValue * crit;
            if (focused)
            {
                Debug.Log($"[Ricochet] Focus critical: value {NextHitValue} -> x{(Has(Relic.ThirdEye) ? 3 : 2)}");
                _sfx.PlayChord(at);
                if (_glow != null) _glow.Pulse(at, _focusGlow, 1.4f, 0.6f);
                if (_fx != null) _fx.Burst(at, _focusGlow);
            }
            if (relicCrit)
            {
                _sfx.PlayChord(at);
                if (_fx != null) _fx.Burst(at, Upgrades.RelicColor);
            }
            _litValues.Add(value);

            int points = _basePoints * value;
            LastShotScore += points;
            Score += points;
            _sfx.PlayComboNote(_combo, at);
            if (_popups != null) _popups.Show(at, points, _combo);
            // Each hit in a chain throws a little more light onto the room.
            if (_glow != null) _glow.Pulse(at, _hitGlow * (1f + 0.25f * Mathf.Min(_combo, 6)), 0.9f, 0.45f);
            _combo++;
            BestCombo = Mathf.Max(BestCombo, _combo);
            _spark.SetHeat(_combo / 6f); // the trail heats toward gold with the chain
            CrystalLit?.Invoke(crystal, _combo - 1);

            switch (crystal.Kind)
            {
                case CrystalKind.Gold:
                    _sfx.PlayChord(at); // a critical rings out
                    if (_glow != null) _glow.Pulse(at, _popGlow * 1.3f, 1.3f, 0.6f);
                    break;
                case CrystalKind.Amp:
                    _mult += Has(Relic.Resonance) ? 2 : 1;
                    if (Has(Relic.Resonance)) TriggerRelic(Relic.Resonance, at);
                    _sfx.PlayGuard(at);
                    if (_glow != null) _glow.Pulse(at, _ampGlow, 1.6f, 0.7f);
                    if (_fx != null) _fx.Burst(at, _ampGlow);
                    break;
                case CrystalKind.Prism:
                    _prismHit = true;
                    if (_glow != null) _glow.Pulse(at, _prismGlow, 1.8f, 0.8f);
                    break;
                case CrystalKind.Bomb:
                    Detonate(spark, at, crystal, _bombRadius * (Has(Relic.BigBang) ? _bigBangScale : 1f));
                    if (Has(Relic.BigBang)) TriggerRelic(Relic.BigBang, at);
                    break;
            }

            // The Bomb Spark bursts on its third clean hit, lighting everything around it.
            if (spark.Kind == SparkKind.Bomb && !spark.IsChild && !_sparkBurst && ++_sparkHits >= _sparkBombHits)
            {
                _sparkBurst = true;
                Debug.Log("[Ricochet] Bomb Spark burst");
                Detonate(spark, spark.transform.position, null,
                         _sparkBombRadius * (Has(Relic.BigBang) ? _bigBangScale : 1f));
                if (Has(Relic.BigBang)) TriggerRelic(Relic.BigBang, spark.transform.position);
            }
        }

        /// <summary>The chain value the next clean hit would carry (before criticals).</summary>
        public int NextHitValue => (1 + _combo + _carom) * _mult;

        /// <summary>A blast lights every clean crystal around it, each as the next hit in the chain.</summary>
        void Detonate(Spark spark, Vector3 at, Crystal bomb, float radius)
        {
            _sfx.PlayShieldHit(at); // a low boom
            if (_glow != null) _glow.Pulse(at, _bombGlow, radius * 3.5f, 0.9f);
            if (_fx != null) { _fx.Burst(at, _bombGlow); _fx.Burst(at, _shardColor); }
            var active = _board.Active;
            // Collect first, onto a shared stack: lighting a neighbour Bomb detonates it too (a chain reaction),
            // and that nested call pushes and pops its own segment above ours.
            int start = _blast.Count;
            float r2 = radius * radius;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c == bomb || c.IsLit || c.IsPopped || c.IsCorrupt) continue;
                if ((c.transform.position - at).sqrMagnitude <= r2) _blast.Add(c);
            }
            int end = _blast.Count;
            for (int i = start; i < end; i++)
                if (!_blast[i].IsLit) OnCrystalHit(spark, _blast[i]);
            _blast.RemoveRange(start, end - start);
        }

        void OnRoomBounced(Spark spark, Vector3 point, Vector3 normal)
        {
            var body = spark == _spark ? _sparkBody : spark.GetComponent<Rigidbody>();
            float strength = body.linearVelocity.magnitude / 6f;
            _sfx.PlayBounce(point, strength);
            if (_glow != null) _glow.Pulse(point, _bounceGlow * (0.35f * Mathf.Clamp01(strength)), 0.55f, 0.25f);
            if (!ShotInProgress) return;

            // Carom: every bank before the first hit raises the whole chain by one.
            if (!_anyHit && _carom < _maxCarom && Has(Relic.Carom))
            {
                _carom++;
                _sfx.PlayPullTick(_carom, point);
                if (_glow != null) _glow.Pulse(point, Upgrades.RelicColor * 1.2f, 0.7f, 0.35f);
                TriggerRelic(Relic.Carom, point);
            }
            if (spark.LastBounceLabel == Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels.CEILING && Has(Relic.Skylight))
            {
                _skyCrit = true;
                if (_glow != null) _glow.Pulse(point, Upgrades.RelicColor * 1.5f, 1f, 0.5f);
            }
            if (spark.LastBounceWasGrace)
            {
                _sfx.PlayChord(point); // Second Wind: the floor gives it back
                if (_glow != null) _glow.Pulse(point, _ampGlow, 1.2f, 0.6f);
                if (_fx != null) _fx.Burst(point, _ampGlow);
                TriggerRelic(Relic.SecondWind, point);
            }
            if (spark == _spark && spark.Kind == SparkKind.Splitter && !_split) Split(spark, point, normal);
        }

        /// <summary>Splitter: on its first bounce the Spark becomes three, fanning out along the surface.</summary>
        void Split(Spark parent, Vector3 point, Vector3 normal)
        {
            _split = true;
            Vector3 v = _sparkBody.linearVelocity;
            if (v.sqrMagnitude < 0.25f) return;
            Vector3 at = parent.transform.position + normal * 0.03f;
            for (int i = 0; i < _children.Length; i++)
            {
                var child = _children[i];
                float angle = (i == 0 ? -1f : 1f) * _splitAngle;
                child.gameObject.SetActive(true);
                child.SetKind(SparkKind.Splitter);
                child.Hold(at);
                child.FloorGrace = 0;
                child.Launch(Quaternion.AngleAxis(angle, normal) * v * 0.92f);
                _live++;
            }
            _sfx.PlayChord(point);
            if (_glow != null) _glow.Pulse(point, Upgrades.Color(SparkKind.Splitter) * 1.6f, 1.1f, 0.5f);
            if (_fx != null)
            {
                _fx.Burst(point, Upgrades.Color(SparkKind.Splitter), 2.2f);
                _fx.Burst(point, Color.white, 1.2f);
            }
            // A beat of slow time so the fork reads from the seat (never while the drama owns time).
            if (_warp != null && !_warp.IsWarped) _warp.Hold(0.25f, 0.12f);
            Debug.Log("[Ricochet] Splitter split");
        }

        /// <summary>Ghost slipped through furniture.</summary>
        void OnPhased(Spark spark, Vector3 point)
        {
            Color ghost = Upgrades.Color(SparkKind.Ghost);
            _sfx.PlaySwell(point);
            if (_glow != null) _glow.Pulse(point, ghost * 1.4f, 0.9f, 0.5f);
            if (_fx != null) _fx.Burst(point, ghost);
            Debug.Log("[Ricochet] Ghost phased through furniture");
        }

        void OnSparkDied(Spark spark)
        {
            if (spark != _spark) spark.gameObject.SetActive(false);
            if (--_live <= 0) StartCoroutine(EndShot());
        }

        IEnumerator EndShot()
        {
            Run?.AdvanceBag();
            // Peggle-style sequential clear of everything lit this shot.
            for (int i = 0; i < _litThisShot.Count; i++)
            {
                Vector3 at = _litThisShot[i].transform.position;
                _litThisShot[i].Pop();
                if (_glow != null) _glow.Pulse(at, _popGlow, 0.7f, 0.35f);
                if (_fx != null) _fx.Burst(at, _shardColor);
                CrystalPopped?.Invoke(at, _litValues[i]); // each hit carries its chain value in light (0 if hexed)
                yield return _popWait;
            }
            Debug.Log($"[Ricochet] Shot: {_litThisShot.Count} crystals, +{LastShotScore}, total {Score}" +
                      (_mult > 1 ? $", amp x{_mult}" : "") + (_prismHit ? ", prism" : ""));
            ShotInProgress = false;
            ShotScored?.Invoke(_litThisShot.Count, LastShotScore);

            if (_prismHit && !(WillRenewBoard?.Invoke() ?? false))
            {
                // Prism: the board renews itself in a rainbow wash before the turn continues.
                Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
                if (_glow != null) _glow.Pulse(center, _prismGlow, 3.5f, 1.2f);
                _sfx.PlayChord(center);
                yield return new WaitForSeconds(0.5f);
                NewBoard();
            }

            if (TurnGate != null)
                yield return TurnGate(); // the encounter resolves the turn (damage, the creature's move, board refresh)
            else if (_board.RemainingCount() == 0)
            {
                Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
                _sfx.PlayChord(center);
                if (_glow != null) _glow.Pulse(center, _popGlow * 1.5f, 3.5f, 1.4f); // the whole room lights up
                yield return new WaitForSeconds(1.2f);
                NewBoard();
            }
            ArmNext();
        }
    }
}
