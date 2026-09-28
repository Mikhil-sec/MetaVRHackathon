using UnityEngine;
using UnityEngine.InputSystem;

namespace Ricochet.Input
{
    /// <summary>
    /// Desktop stand-in for a hand, used in Editor play mode without XR.
    /// Left-drag behaves like pulling a slingshot: dragging down-left aims up-right,
    /// and drag length maps to pull depth toward the player.
    /// </summary>
    public sealed class MousePinchInput : IPinchInput
    {
        const float PixelsPerMeter = 900f;
        const float DepthPerLateral = 1.4f;

        readonly Camera _camera;
        readonly Transform _anchor;
        Vector2 _pressPosition;
        bool _wasPressed;

        public MousePinchInput(Camera camera, Transform anchor)
        {
            _camera = camera;
            _anchor = anchor;
        }

        public bool IsTracked => Mouse.current != null;
        public float ReachScale => float.PositiveInfinity;
        public bool IsHand => false;
        public float PinchStrength => Mouse.current != null && Mouse.current.leftButton.isPressed ? 1f : 0f;

        public bool IsPinching
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null) return false;
                bool pressed = mouse.leftButton.isPressed;
                if (pressed && !_wasPressed) _pressPosition = mouse.position.ReadValue();
                _wasPressed = pressed;
                return pressed;
            }
        }

        public Vector3 PinchPoint
        {
            get
            {
                var mouse = Mouse.current;
                Vector2 drag = (mouse.position.ReadValue() - _pressPosition) / PixelsPerMeter;
                Transform cam = _camera.transform;
                Vector3 lateral = cam.right * drag.x + cam.up * drag.y;
                Vector3 back = -cam.forward * (drag.magnitude * DepthPerLateral);
                return _anchor.position + lateral + back;
            }
        }
    }
}
