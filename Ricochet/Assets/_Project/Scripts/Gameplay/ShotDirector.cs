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

        [Header("Scoring")]
        [SerializeField] int _basePoints = 10;
        [SerializeField] float _popInterval = 0.07f;

        readonly List<Crystal> _litThisShot = new();
        WaitForSeconds _popWait;
        Rigidbody _sparkBody;
        int _combo;
        int _boardSeed = 1;

        public int Score { get; private set; }
        public int LastShotScore { get; private set; }
        public int BestCombo { get; private set; }

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

            NewBoard();
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

        void NewBoard()
        {
            int count = _board.Generate(_playArea.Room, _playArea.Seat, _boardSeed++, HeroShot(_playArea.Seat));
            Debug.Log($"[Ricochet] Board generated: {count} crystals in room '{_playArea.Room.name}'");
        }

        void OnLaunched(Vector3 velocity)
        {
            _combo = 0;
            LastShotScore = 0;
            _litThisShot.Clear();
            _sfx.PlayLaunch(_spark.transform.position);
        }

        void OnCrystalHit(Spark spark, Crystal crystal)
        {
            if (crystal.IsLit) return;
            crystal.Light();
            _litThisShot.Add(crystal);

            int points = _basePoints * (1 + _combo);
            LastShotScore += points;
            Score += points;
            _sfx.PlayComboNote(_combo, crystal.transform.position);
            if (_popups != null) _popups.Show(crystal.transform.position, points, _combo);
            // Each hit in a chain throws a little more light onto the room.
            if (_glow != null) _glow.Pulse(crystal.transform.position, _hitGlow * (1f + 0.25f * Mathf.Min(_combo, 6)), 0.9f, 0.45f);
            _combo++;
            BestCombo = Mathf.Max(BestCombo, _combo);
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
                yield return _popWait;
            }
            Debug.Log($"[Ricochet] Shot: {_litThisShot.Count} crystals, +{LastShotScore}, total {Score}");

            if (_board.RemainingCount() == 0)
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
