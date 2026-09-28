using UnityEngine;
using UnityEngine.XR;

namespace Ricochet
{
    /// <summary>Disables this GameObject when an XR device is active (desktop debug helpers: light, room preview).</summary>
    public sealed class DesktopOnly : MonoBehaviour
    {
        void Start()
        {
            if (XRSettings.isDeviceActive) gameObject.SetActive(false);
        }
    }
}
