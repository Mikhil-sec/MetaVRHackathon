using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.XR;

namespace Ricochet.Room
{
    /// <summary>
    /// Owns the player's seat pose and the loaded room.
    /// On device the seat is wherever the head is when the room loads.
    /// On desktop (no XR) it picks a seat inside the room, facing the most open direction, and moves the rig there.
    /// </summary>
    public sealed class PlayArea : MonoBehaviour
    {
        const float SeatedEyeHeight = 1.15f;

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
            if (IsDesktop && Room != null) PlaceDesktopSeat();

            Vector3 flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            Seat = new Pose(_head.position, Quaternion.LookRotation(flatForward.normalized, Vector3.up));
            IsReady = true;
            Ready?.Invoke();
        }

        /// <summary>Seat at a free floor point, facing the direction with the most open space.</summary>
        void PlaceDesktopSeat()
        {
            Vector3 seatFloor = Room.GenerateRandomPositionInRoom(0.6f, true) ?? Room.GetRoomBounds().center;
            seatFloor.y = Room.FloorAnchor != null ? Room.FloorAnchor.transform.position.y : 0f;
            Vector3 eye = seatFloor + Vector3.up * SeatedEyeHeight;

            Vector3 bestDir = Vector3.forward;
            float bestDist = -1f;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float dist = Room.Raycast(new Ray(eye, dir), 20f, out RaycastHit hit) ? hit.distance : 20f;
                if (dist > bestDist) { bestDist = dist; bestDir = dir; }
            }

            _rigRoot.SetPositionAndRotation(eye - Vector3.up * _head.localPosition.y, Quaternion.LookRotation(bestDir, Vector3.up));
        }
    }
}
