using System;
using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Ricochet.Room
{
    /// <summary>
    /// Owns the player's seat pose and the loaded room, and decides where the room comes from (MRUK's own
    /// load-on-startup is off):
    /// - Desktop (no XR): one of MRUK's room prefabs; the seat is chosen inside it, facing the most open direction,
    ///   and the rig moves there.
    /// - Device: the scanned scene model. With no scan, Space Setup is offered once (remembered); if there is still no
    ///   room, or scene permission is denied, the game plays in the Pocket Arena (CONCEPT section 5), never in a
    ///   made-up prefab room that would put invisible walls in the real one.
    /// On device the seat is wherever the head settles once head tracking is live (MRUK can load the room before the
    /// first tracked frame, when the head still sits at the origin).
    /// </summary>
    public sealed class PlayArea : MonoBehaviour
    {
        public const float SeatedEyeHeight = 1.15f;
        const float SettleSeconds = 0.4f;
        const float SettleTolerance = 0.02f;
        const string SpaceSetupOfferedKey = "ricochet.spaceSetupOffered";
        /// <summary>Editor only: PlayerPrefs flag that starts Play in the Pocket Arena (DevHooks.Pocket).</summary>
        public const string ForcePocketKey = "ricochet.forcePocket";

        [SerializeField] Transform _rigRoot;
        [SerializeField] Transform _head;

        public MRUKRoom Room { get; private set; }
        public Pose Seat { get; private set; }
        public bool IsReady { get; private set; }
        public static bool IsDesktop => !XRSettings.isDeviceActive;
        public static bool IsPocket => PocketArena.Active;

        public event Action Ready;
        /// <summary>
        /// The player re-centred (held the Meta button, e.g. after turning their chair): the seat moved to where they
        /// now sit and look. Seat-relative things (sling, HUD) follow; the room's content stays where it is.
        /// </summary>
        public event Action Reseated;

        public Transform Head => _head;

        void Start()
        {
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            StartCoroutine(LoadRoom());
        }

        void OnDestroy()
        {
            PocketArena.Deactivate();
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
            OVRManager.TrackingOriginChangePending -= OnOriginChangePending;
        }

        static readonly System.Collections.Generic.List<XRDisplaySubsystem> s_displays = new();

        static bool XrStarting()
        {
            if (XRSettings.isDeviceActive) return false;
            SubsystemManager.GetSubsystems(s_displays);
            foreach (var d in s_displays)
                if (!d.running) return true;
            return false;
        }

        IEnumerator LoadRoom()
        {
            var mruk = MRUK.Instance;
            // XR can start a few frames after Start (Meta XR Simulator in the Editor): an XR display that exists but
            // isn't running yet is not desktop. Deciding early loaded a prefab room in XR and moved the rig (session 8).
            for (float t = 0f; t < 5f && XrStarting(); t += Time.unscaledDeltaTime) yield return null;
            Debug.Log($"[Ricochet] Room source: {(IsDesktop ? "desktop" : "device")} (frame {Time.frameCount})");
#if UNITY_EDITOR
            if (PlayerPrefs.GetInt(ForcePocketKey, 0) == 1)
            {
                yield return LoadPocketArena("forced (DevHooks.Pocket)");
                yield break;
            }
#endif
            if (IsDesktop)
            {
                var prefabs = mruk.SceneSettings.RoomPrefabs;
                int index = mruk.SceneSettings.RoomIndex;
                if (index < 0 || index >= prefabs.Length) index = UnityEngine.Random.Range(0, prefabs.Length);
                _ = mruk.LoadSceneFromPrefab(prefabs[index]); // OnSceneLoaded takes it from here
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            bool? granted = null;
            yield return RequestScenePermission(result => granted = result);
            if (granted != true)
            {
                yield return LoadPocketArena("scene permission denied");
                yield break;
            }
#endif
            bool offer = PlayerPrefs.GetInt(SpaceSetupOfferedKey, 0) == 0;
            var load = mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: offer);
            while (!load.IsCompleted) yield return null;
            var result = load.IsCompletedSuccessfully ? load.Result : MRUK.LoadDeviceResult.Failure;
            if (result == MRUK.LoadDeviceResult.Success && mruk.GetCurrentRoom() != null) yield break; // OnSceneLoaded

            if (offer)
            {
                PlayerPrefs.SetInt(SpaceSetupOfferedKey, 1); // offered and declined: don't nag on every launch
                PlayerPrefs.Save();
            }
            yield return LoadPocketArena($"no scene model ({result})");
        }

        /// <summary>
        /// Builds the Pocket Arena around the settled seat and loads it as the room. MRUK world lock stays off: a
        /// synthetic room has no tracked anchors to lock to, and the arena should sit exactly where it was built.
        /// </summary>
        IEnumerator LoadPocketArena(string reason)
        {
            var mruk = MRUK.Instance;
            mruk.EnableWorldLock = false;
            Pose eye;
            if (IsDesktop)
            {
                // The rig's fallback head height lands a frame or two after Start, and PlaceDesktopSeat offsets the
                // rig by it (a prefab room takes that long to load anyway; the arena is instant).
                for (int f = 0; f < 10 && _head.localPosition.y < 0.5f; f++) yield return null;
                eye = new Pose(new Vector3(0f, SeatedEyeHeight, 0f), Quaternion.identity);
            }
            else
            {
                yield return WaitForSettledHead();
                eye = new Pose(_head.position, _head.rotation);
            }
            float floorY = _rigRoot != null ? _rigRoot.position.y : 0f; // floor-level tracking origin
            Debug.Log($"[Ricochet] Pocket Arena: {reason}");
            var layout = PocketArena.BuildLayout(eye, floorY);
            var load = mruk.LoadSceneFromPrefab(layout); // fires OnSceneLoaded
            while (!load.IsCompleted) yield return null;
            Destroy(layout);
        }

        void OnSceneLoaded()
        {
            Room = MRUK.Instance.GetCurrentRoom();
            if (IsDesktop)
            {
                if (Room != null) PlaceDesktopSeat();
                TakeSeat();
            }
            else if (IsPocket)
            {
                TakeSeat(); // the head already settled before the arena was built around it
            }
            else
            {
                StartCoroutine(SeatWhenTracked());
            }
        }

        IEnumerator SeatWhenTracked()
        {
            yield return WaitForSettledHead();
            TakeSeat();
        }

        IEnumerator WaitForSettledHead()
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
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Asks for the scene permission. A request made while another permission dialog is open fails silently,
        /// so it polls the grant and asks again every few seconds until it gets an answer.
        /// </summary>
        IEnumerator RequestScenePermission(Action<bool> done) => RequestPermission(OVRPermissionsRequester.ScenePermission, int.MaxValue, done);

        // Eye gaze (Focus, Look and Fire) on headsets that have eye tracking; head gaze plays fully without it.
        // Asked after the seat is taken, so it never lands while the scene or Space Setup dialog is open.
        IEnumerator RequestEyeTracking()
        {
            if (!OVRPlugin.eyeTrackingSupported) yield break;
            yield return RequestPermission(OVRPermissionsRequester.EyeTrackingPermission, 3,
                ok => Debug.Log($"[Ricochet] Eye tracking permission: {(ok ? "granted" : "not granted")}"));
        }

        IEnumerator RequestPermission(string permission, int maxAsks, Action<bool> done)
        {
            int answer = Permission.HasUserAuthorizedPermission(permission) ? 1 : 0;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answer = 1;
            callbacks.PermissionDenied += _ => answer = -1;
            float nextAsk = 0f;
            while (answer == 0)
            {
                if (Time.realtimeSinceStartup >= nextAsk)
                {
                    if (maxAsks-- <= 0) break; // a request made while another dialog is open fails silently
                    Permission.RequestUserPermission(permission, callbacks);
                    nextAsk = Time.realtimeSinceStartup + 5f;
                }
                yield return null;
                if (Permission.HasUserAuthorizedPermission(permission)) answer = 1;
            }
            done(answer == 1);
        }
#endif

        void TakeSeat()
        {
            Seat = SeatFromHead();
            IsReady = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(RequestEyeTracking());
#endif
            // Two ways a recenter arrives: an app/runtime request (RecenteredPose, the frame after), and the user's
            // system recenter (OpenXR reference-space change, announced before it applies).
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
            OVRManager.TrackingOriginChangePending += OnOriginChangePending;
            Ready?.Invoke();
        }

        Pose SeatFromHead()
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            return new Pose(_head.position, Quaternion.LookRotation(flatForward.normalized, Vector3.up));
        }

        void OnRecentered()
        {
            Debug.Log("[Ricochet] Recentered: the seat follows the player");
            Reseat();
        }

        float _reseatAt = -1f;

        // Pending: the new origin applies over the next frames; take the seat once it has (one re-seat per recenter).
        void OnOriginChangePending(OVRManager.TrackingOrigin origin, OVRPose? previous)
        {
            if (_reseatAt > 0f) return;
            _reseatAt = Time.realtimeSinceStartup + 0.35f;
            StartCoroutine(ReseatWhenSettled());
        }

        IEnumerator ReseatWhenSettled()
        {
            while (Time.realtimeSinceStartup < _reseatAt) yield return null;
            _reseatAt = -1f;
            Debug.Log("[Ricochet] Tracking origin changed: the seat follows the player");
            Reseat();
        }

        /// <summary>Takes the seat again from the head as it is now (a recenter; DevHooks for tests).</summary>
        public void Reseat()
        {
            if (!IsReady || _head == null) return;
            Seat = SeatFromHead();
            Reseated?.Invoke();
        }

        void PlaceDesktopSeat()
        {
            Pose eye = ChooseSeat(Room, 1);
            _rigRoot.SetPositionAndRotation(eye.position - Vector3.up * _head.localPosition.y, eye.rotation);
        }

        /// <summary>
        /// A plausible seated eye pose for rooms without a real player (desktop, room sweep): a free floor point
        /// with the most open space ahead, facing that way. Deterministic for a given room and seed.
        /// The Pocket Arena has its seat built in.
        /// </summary>
        public static Pose ChooseSeat(MRUKRoom room, int seed)
        {
            if (PocketArena.Active) return PocketArena.Seat;
            var saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            float floorY = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : 0f;

            Pose best = new(room.GetRoomBounds().center, Quaternion.identity);
            float bestScore = -1f;
            // Six candidate spots with somewhere to face; a cluttered room may need more draws to find them. MRUK
            // gives up on a draw after 1000 tries, so two empty draws in a row mean the room has no more to give.
            for (int candidate = 0, usable = 0, empty = 0; candidate < 40 && usable < 6; candidate++)
            {
                Vector3? sample = room.GenerateRandomPositionInRoom(0.6f, true);
                if (!sample.HasValue)
                {
                    if (++empty >= 2) break;
                    continue;
                }
                empty = 0;
                Vector3 eye = new(sample.Value.x, floorY + SeatedEyeHeight, sample.Value.z);
                bool counted = false;
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
                    if (score >= 0f && !counted) { counted = true; usable++; }
                    if (score > bestScore) { bestScore = score; best = new Pose(eye, Quaternion.LookRotation(dir, Vector3.up)); }
                }
            }

            UnityEngine.Random.state = saved;
            return best;
        }
    }
}
