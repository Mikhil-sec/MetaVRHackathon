using System.Collections;
using System.Collections.Generic;
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

        [Header("Scoring")]
        [SerializeField] int _basePoints = 10;
        [SerializeField] float _popInterval = 0.07f;

        readonly List<Crystal> _litThisShot = new();
        readonly List<int> _litValues = new();
        WaitForSeconds _popWait;
        Rigidbody _sparkBody;
        int _combo;
        int _boardSeed = 1;

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

        /// <summary>Optional: runs once the room is ready, before the first board and arming (the encounter intro).</summary>
        public System.Func<IEnumerator> Intro;
        /// <summary>Optional: runs after each shot is scored, before the sling re-arms (the encounter's turn).</summary>
        public System.Func<IEnumerator> TurnGate;
        /// <summary>Optional: adjusts a freshly generated board before it is announced; returns the crystal count.</summary>
        public System.Func<int> BoardShaper;
        /// <summary>Chain units of light banked this shot (each hit adds its chain value: 1, 2, 3...).</summary>
        public int ShotLight => LastShotScore / Mathf.Max(1, _basePoints);

        void Awake()
        {
            _popWait = new WaitForSeconds(_popInterval);
            _sparkBody = _spark.GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            _playArea.Ready += OnRoomReady;
            _spark.CrystalHit += OnCrystalHit;
            _spark.RoomBounced += OnRoomBounced;
            _spark.Died += OnSparkDied;
            _sling.Launched += OnLaunched;
        }

        void OnDisable()
        {
            _playArea.Ready -= OnRoomReady;
            _spark.CrystalHit -= OnCrystalHit;
            _spark.RoomBounced -= OnRoomBounced;
            _spark.Died -= OnSparkDied;
            _sling.Launched -= OnLaunched;
        }

        void OnRoomReady()
        {
            _sling.transform.position = SlingPosition(_playArea.Seat);

            if (_leftHand != null) _sling.AddInput(new HandPinchInput(_leftHand));
            if (_rightHand != null) _sling.AddInput(new HandPinchInput(_rightHand));
            _sling.AddInput(new ControllerPinchInput(OVRInput.Controller.LTouch, _trackingSpace));
            _sling.AddInput(new ControllerPinchInput(OVRInput.Controller.RTouch, _trackingSpace));
            if (PlayArea.IsDesktop) _sling.AddInput(new MousePinchInput(Camera.main, _sling.transform));

            if (Intro != null) StartCoroutine(IntroThenArm());
            else
            {
                NewBoard();
                _sling.Arm();
            }
        }

        IEnumerator IntroThenArm()
        {
            yield return Intro(); // the intro owns board generation
            _sling.Arm();
        }

        const float HeroPull = 0.8f;
        const float HeroPitchUp = 3f;

        /// <summary>The relaxed straight-ahead shot a first-time player makes; the board guarantees it a cluster.</summary>
        public BoardGenerator.HeroShot HeroShot(Pose seat) => new()
        {
            Origin = SlingPosition(seat),
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

        void NewBoard()
        {
            int count = _board.Generate(_playArea.Room, _playArea.Seat, _boardSeed++, HeroShot(_playArea.Seat));
            if (BoardShaper != null) count = BoardShaper();
            Debug.Log($"[Ricochet] Board generated: {count} crystals in room '{_playArea.Room.name}'");
            BoardGenerated?.Invoke();
        }

        void OnLaunched(Vector3 velocity)
        {
            _combo = 0;
            LastShotScore = 0;
            ShotInProgress = true;
            _litThisShot.Clear();
            _litValues.Clear();
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
                if (_spark.Ribbon != null) _spark.Ribbon.SetHeat(0f);
                CrystalLit?.Invoke(crystal, -1);
                return;
            }
            _litValues.Add(1 + _combo);

            int points = _basePoints * (1 + _combo);
            LastShotScore += points;
            Score += points;
            _sfx.PlayComboNote(_combo, crystal.transform.position);
            if (_popups != null) _popups.Show(crystal.transform.position, points, _combo);
            // Each hit in a chain throws a little more light onto the room.
            if (_glow != null) _glow.Pulse(crystal.transform.position, _hitGlow * (1f + 0.25f * Mathf.Min(_combo, 6)), 0.9f, 0.45f);
            _combo++;
            BestCombo = Mathf.Max(BestCombo, _combo);
            if (_spark.Ribbon != null) _spark.Ribbon.SetHeat(_combo / 6f); // the trail heats toward gold with the chain
            CrystalLit?.Invoke(crystal, _combo - 1);
        }

        void OnRoomBounced(Spark spark, Vector3 point, Vector3 normal)
        {
            float strength = _sparkBody.linearVelocity.magnitude / 6f;
            _sfx.PlayBounce(point, strength);
            if (_glow != null) _glow.Pulse(point, _bounceGlow * (0.35f * Mathf.Clamp01(strength)), 0.55f, 0.25f);
        }

        void OnSparkDied(Spark spark) => StartCoroutine(EndShot());

        IEnumerator EndShot()
        {
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
            Debug.Log($"[Ricochet] Shot: {_litThisShot.Count} crystals, +{LastShotScore}, total {Score}");
            ShotInProgress = false;
            ShotScored?.Invoke(_litThisShot.Count, LastShotScore);

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
            _sling.Arm();
        }
    }
}
