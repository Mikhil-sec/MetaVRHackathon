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
        [SerializeField] Oculus.Interaction.Input.Hand _leftHand;
        [SerializeField] Oculus.Interaction.Input.Hand _rightHand;

        [Header("Sling placement relative to the seated head")]
        [SerializeField] float _slingForward = 0.38f;
        [SerializeField] float _slingDown = 0.22f;

        [Header("Scoring")]
        [SerializeField] int _basePoints = 10;
        [SerializeField] float _popInterval = 0.07f;

        readonly List<Crystal> _litThisShot = new();
        int _combo;
        int _boardSeed = 1;

        public int Score { get; private set; }
        public int LastShotScore { get; private set; }
        public int BestCombo { get; private set; }

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
            Pose seat = _playArea.Seat;
            _sling.transform.position = seat.position + seat.forward * _slingForward + Vector3.down * _slingDown;

            if (_leftHand != null) _sling.AddInput(new HandPinchInput(_leftHand));
            if (_rightHand != null) _sling.AddInput(new HandPinchInput(_rightHand));
            if (PlayArea.IsDesktop) _sling.AddInput(new MousePinchInput(Camera.main, _sling.transform));

            NewBoard();
            _sling.Arm();
        }

        void NewBoard()
        {
            int count = _board.Generate(_playArea.Room, _playArea.Seat, _boardSeed++);
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
            _combo++;
            BestCombo = Mathf.Max(BestCombo, _combo);
        }

        void OnRoomBounced(Spark spark, Vector3 point, Vector3 normal)
        {
            _sfx.PlayBounce(point, spark.GetComponent<Rigidbody>().linearVelocity.magnitude / 6f);
        }

        void OnSparkDied(Spark spark) => StartCoroutine(EndShot());

        IEnumerator EndShot()
        {
            // Peggle-style sequential clear of everything lit this shot.
            for (int i = 0; i < _litThisShot.Count; i++)
            {
                _litThisShot[i].Pop();
                yield return new WaitForSeconds(_popInterval);
            }
            Debug.Log($"[Ricochet] Shot: {_litThisShot.Count} crystals, +{LastShotScore}, total {Score}");

            if (_board.RemainingCount() == 0)
            {
                _sfx.PlayChord(_playArea.Seat.position + _playArea.Seat.forward * 2f);
                yield return new WaitForSeconds(1.2f);
                NewBoard();
            }
            _sling.Arm();
        }
    }
}
