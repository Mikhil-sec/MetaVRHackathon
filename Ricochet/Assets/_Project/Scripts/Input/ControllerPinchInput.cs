using UnityEngine;

namespace Ricochet.Input
{
    /// <summary>
    /// Touch controller as a pinch source: index trigger or grip counts as a pinch (with hysteresis),
    /// and the pinch point sits just ahead of the grip, where the fingers close.
    /// Hands stay the primary input; this keeps the game playable for anyone who picks up controllers.
    /// </summary>
    public sealed class ControllerPinchInput : IPinchInput
    {
        const float PressThreshold = 0.55f;
        const float ReleaseThreshold = 0.35f;
        static readonly Vector3 TipOffset = new(0f, -0.01f, 0.045f);

        readonly OVRInput.Controller _controller;
        readonly Transform _trackingSpace;
        bool _pressed;

        public ControllerPinchInput(OVRInput.Controller controller, Transform trackingSpace)
        {
            _controller = controller;
            _trackingSpace = trackingSpace;
        }

        public bool IsTracked =>
            OVRInput.IsControllerConnected(_controller) && OVRInput.GetControllerPositionTracked(_controller);

        // Controllers have no fingertip to aim with, so they reach a little further than a pinch.
        public float ReachScale => 1.5f;
        public bool IsHand => false;

        public float PinchStrength => IsTracked
            ? Mathf.Max(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _controller),
                        OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _controller))
            : 0f;

        public bool IsPinching
        {
            get
            {
                if (!IsTracked) return _pressed = false;
                float squeeze = Mathf.Max(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _controller),
                                          OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _controller));
                _pressed = squeeze >= (_pressed ? ReleaseThreshold : PressThreshold);
                return _pressed;
            }
        }

        public Vector3 PinchPoint
        {
            get
            {
                Vector3 local = OVRInput.GetLocalControllerPosition(_controller) +
                                OVRInput.GetLocalControllerRotation(_controller) * TipOffset;
                return _trackingSpace != null ? _trackingSpace.TransformPoint(local) : local;
            }
        }
    }
}
