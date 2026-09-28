using System;
using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.XR;

namespace Ricochet.Room
{
    /// <summary>
    /// Owns the player's seat pose and the loaded room.
    /// On device the seat is wherever the head settles once the room has loaded and head tracking is live
    /// (MRUK can load the room before the first tracked frame, when the head still sits at the origin).
    /// On desktop (no XR) it picks a seat inside the room, facing the most open direction, and moves the rig there.
    /// </summary>
    public sealed class PlayArea : MonoBehaviour
    {
        const float SeatedEyeHeight = 1.15f;
        const float SettleSeconds = 0.4f;
        const float SettleTolerance = 0.02f;

        [SerializeField] Transform _rigRoot;
        [SerializeField] Transform _head;

        public MRUKRoom Room { get; private set; }
        public Pose Seat { get; private set; }
        public bool IsReady { get; private set; }
        public static bool IsDesktop => !XRSettings.isDeviceActive;

        public event Action Ready;

        public Transform Head => _head;

        void Start()
        {
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }

        void OnSceneLoaded()
        {
            Room = MRUK.Instance.GetCurrentRoom();
            if (IsDesktop)
            {
                if (Room != null) PlaceDesktopSeat();
                TakeSeat();
            }
            else
            {
                StartCoroutine(SeatWhenTracked());
            }
        }

        IEnumerator SeatWhenTracked()
        {
            float settled = 0f;
            Vector3 last = _head.localPosition;
            while (settled < SettleSeconds)
            {
                yield return null;
                Vector3 now = _head.localPosition;
                bool tracked = OVRManager.isHmdPresent && OVRManager.hasInputFocus && now.sqrMagnitude > 0.01f;
                settled = tracked && (now - last).sqrMagnitude < SettleTolerance * SettleTolerance ? settled + Time.deltaTime : 0f;
                last = now;
            }
            TakeSeat();
        }

        void TakeSeat()
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            Seat = new Pose(_head.position, Quaternion.LookRotation(flatForward.normalized, Vector3.up));
            IsReady = true;
            Ready?.Invoke();
        }

        void PlaceDesktopSeat()
        {
            Pose eye = ChooseSeat(Room, 1);
            _rigRoot.SetPositionAndRotation(eye.position - Vector3.up * _head.localPosition.y, eye.rotation);
        }

        /// <summary>
        /// A plausible seated eye pose for rooms without a real player (desktop, room sweep): a free floor point
        /// with the most open space ahead, facing that way. Deterministic for a given room and seed.
        /// </summary>
        public static Pose ChooseSeat(MRUKRoom room, int seed)
        {
            var saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            float floorY = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : 0f;

            Pose best = new(room.GetRoomBounds().center, Quaternion.identity);
            float bestScore = -1f;
            for (int candidate = 0; candidate < 6; candidate++)
            {
                Vector3 floor = room.GenerateRandomPositionInRoom(0.6f, true) ?? room.GetRoomBounds().center;
                Vector3 eye = new(floor.x, floorY + SeatedEyeHeight, floor.z);
                for (int i = 0; i < 16; i++)
                {
                    Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                    // Openness across the whole forward view (+/-30 deg), capped at 4 m: a real player faces into the
                    // room, not down a corridor that happens to have one long sightline.
                    float score = 0f;
                    for (int k = -3; k <= 3; k++)
                    {
                        Vector3 ray = Quaternion.Euler(0f, k * 10f, 0f) * dir;
                        float free = room.Raycast(new Ray(eye, ray), 4f, out RaycastHit hit) ? hit.distance : 4f;
                        // Nobody sits with furniture in their lap: the sling (below eye level) needs clear space ahead.
                        if (k == 0 && (room.Raycast(new Ray(eye + Vector3.down * 0.25f, ray), 1f, out _) ||
                                       room.IsPositionInSceneVolume(eye, 0.1f) ||
                                       room.IsPositionInSceneVolume(eye + ray * 0.42f + Vector3.down * 0.15f, 0.1f)))
                        {
                            score = -1f;
                            break;
                        }
                        score += free;
                    }
                    if (score > bestScore) { bestScore = score; best = new Pose(eye, Quaternion.LookRotation(dir, Vector3.up)); }
                }
            }

            UnityEngine.Random.state = saved;
            return best;
        }
    }
}
