using UnityEngine;
using UnityEngine.InputSystem;

namespace Ricochet.Room
{
    /// <summary>Right-mouse-drag look for Editor play mode without XR. Rotates the rig root (the XR camera stays at identity).</summary>
    public sealed class DesktopLook : MonoBehaviour
    {
        [SerializeField] Transform _rigRoot;
        [SerializeField] float _degreesPerPixel = 0.15f;

        float _pitch;

        void Update()
        {
            if (!PlayArea.IsDesktop) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed) return;

            Vector2 delta = mouse.delta.ReadValue() * _degreesPerPixel;
            _pitch = Mathf.Clamp(_pitch - delta.y, -70f, 70f);
            Vector3 euler = _rigRoot.eulerAngles;
            _rigRoot.rotation = Quaternion.Euler(_pitch, euler.y + delta.x, 0f);
        }
    }
}
