using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Room
{
    /// <summary>
    /// On desktop there is no passthrough, so the invisible room colliders would leave an empty void.
    /// This shows the EffectMesh with a debug material so the room is visible while testing in the Editor.
    /// </summary>
    public sealed class DesktopRoomPreview : MonoBehaviour
    {
        [SerializeField] EffectMesh _effectMesh;
        [SerializeField] Material _previewMaterial;

        void Start()
        {
            if (!PlayArea.IsDesktop) return;
            MRUK.Instance.RegisterSceneLoadedCallback(() => StartCoroutine(ShowNextFrame()));
        }

        // EffectMesh builds its meshes from the same scene-loaded event; wait until it has run.
        IEnumerator ShowNextFrame()
        {
            yield return null;
            _effectMesh.ToggleEffectMeshVisibility(true, new LabelFilter(), _previewMaterial);
        }
    }
}
