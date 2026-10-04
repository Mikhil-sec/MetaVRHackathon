using UnityEngine;

namespace Ricochet.Input
{
    /// <summary>
    /// The tracked hand meshes wear <c>Ricochet/HandLight</c> instead of the stock Interaction SDK hand material
    /// (docs/TECH_GUIDE.md section 6). On device the real hands show through passthrough, so the mesh only writes depth
    /// (a real hand in front of a crystal hides it). In the Editor (Meta XR Simulator, trailer renders) there are no
    /// real hands to see, so it draws a hand of light that fades out near the eye instead of smearing the near plane.
    /// </summary>
    public sealed class HandLook : MonoBehaviour
    {
        [SerializeField] Transform _rigRoot;
        [SerializeField] Material _handLight;

        Material _material;

        void Start()
        {
            if (_rigRoot == null || _handLight == null) return;
            _material = _handLight;
            if (!Application.isEditor)
            {
                _material = new Material(_handLight) { name = _handLight.name + " (depth only)" };
                _material.SetFloat("_ColorMask", 0f);
            }
            Apply();
            Invoke(nameof(Apply), 2f); // hand visuals that set themselves up late get it too
        }

        void Apply()
        {
            int n = 0;
            foreach (var r in _rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var current = r.sharedMaterial;
                // OculusHand (the tracked hands) and OculusHandWire (their synthetic, grab-constrained copies).
                if (current == null || current == _material || !current.name.StartsWith("OculusHand")) continue;
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                n++;
            }
            if (n > 0) Debug.Log($"[Ricochet] HandLook: {n} hand meshes{(Application.isEditor ? " (light)" : " (depth only)")}");
        }
    }
}
