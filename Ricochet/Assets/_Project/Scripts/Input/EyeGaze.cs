using UnityEngine;
using UnityEngine.InputSystem;

namespace Ricochet.Input
{
    /// <summary>
    /// The one gaze ray every look-to-act feature reads (Focus, Look and Fire, the eye toggle, reward picks).
    /// Eye gaze when the device has it (OpenXR <c>XR_EXT_eye_gaze_interaction</c>, the "Eye Gaze Interaction Profile"
    /// feature; Quest Pro, or the simulator's injected eye gaze), head gaze otherwise (Quest 3/3S, desktop):
    /// docs/TECH_GUIDE.md section 6, every gaze feature is fully playable on the fallback.
    /// The eye ray is lightly smoothed while fixating (tremor would flicker cone edges) and snaps on a saccade
    /// (a deliberate look must land at once). A blink holds the last eye direction, relative to the head, briefly.
    /// Zero allocations per frame; computed once per frame however many features ask.
    /// </summary>
    public static class EyeGaze
    {
        const float SaccadeDeg = 6f;     // a jump this big is a new look: no smoothing
        const float SmoothTime = 0.05f;  // fixation smoothing time constant (s)
        const float BlinkHold = 0.3f;    // keep the last eye direction this long through a tracking dropout (s)

        static InputAction _rotation, _tracked;
        static int _frame = -1;
        static Vector3 _localDir = Vector3.forward; // smoothed eye direction in the head's frame
        static float _lastEyeTime = -10f;
        static bool _hadEye;

        /// <summary>True when this frame's gaze comes from the eyes (or a blink hold), false for head gaze.</summary>
        public static bool IsEyes { get; private set; }

        /// <summary>The gaze ray from the head. Returns false only when there is no head.</summary>
        public static bool Ray(Transform head, out Vector3 origin, out Vector3 dir)
        {
            if (head == null) { origin = Vector3.zero; dir = Vector3.forward; return false; }
            if (_frame != Time.frameCount) Step(head);
            origin = head.position;
            dir = IsEyes ? head.rotation * _localDir : head.forward;
            return true;
        }

        static void Step(Transform head)
        {
            _frame = Time.frameCount;
            if (_rotation == null) Create();

            float now = RealTime.Now;
            if (_tracked.IsPressed() && head.parent != null)
            {
                // The pose is in the tracking space, which is the head's parent (OVRCameraRig/TrackingSpace).
                Quaternion world = head.parent.rotation * _rotation.ReadValue<Quaternion>();
                Vector3 local = Quaternion.Inverse(head.rotation) * (world * Vector3.forward);
                // Eyes rotate at most ~50 deg off the head's forward; anything wider is a bad sample.
                if (local.sqrMagnitude > 0.5f && local.z > 0.6f * local.magnitude)
                {
                    local.Normalize();
                    if (!_hadEye || Vector3.Angle(local, _localDir) > SaccadeDeg) _localDir = local;
                    else _localDir = Vector3.Slerp(_localDir, local, 1f - Mathf.Exp(-RealTime.DeltaTime / SmoothTime));
                    if (!_hadEye) Debug.Log("[Ricochet] Gaze source: eyes");
                    _hadEye = true;
                    _lastEyeTime = now;
                }
            }
            IsEyes = now - _lastEyeTime < BlinkHold;
        }

        static void Create()
        {
            _rotation = new InputAction("EyeGazeRotation", InputActionType.Value, "<EyeGaze>/pose/rotation", expectedControlType: "Quaternion");
            _tracked = new InputAction("EyeGazeTracked", InputActionType.Button, "<EyeGaze>/pose/isTracked");
            _rotation.Enable();
            _tracked.Enable();
        }
    }
}
