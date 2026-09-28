using UnityEngine;

namespace Ricochet.Room
{
    /// <summary>
    /// On desktop there is no passthrough, so the room mesh (RoomGlow, transparent on device) would leave an
    /// empty void. This gives it an opaque base color so the room is visible while testing in the Editor.
    /// The base stays clear until the play area is ready, the first point where the XR state is certain.
    /// </summary>
    public sealed class DesktopRoomPreview : MonoBehaviour
    {
        static readonly int RoomBaseColorId = Shader.PropertyToID("_RoomBaseColor");

        [SerializeField] PlayArea _playArea;
        // Dim, like the dimmed passthrough of CONCEPT section 6, so the game's light reads on the walls.
        [SerializeField] Color _previewColor = new(0.2f, 0.21f, 0.26f, 1f);

        void Awake() => Shader.SetGlobalColor(RoomBaseColorId, Color.clear);

        void OnEnable() => _playArea.Ready += OnReady;

        void OnDisable() => _playArea.Ready -= OnReady;

        void OnReady() => Shader.SetGlobalColor(RoomBaseColorId, PlayArea.IsDesktop ? _previewColor : Color.clear);

        void OnDestroy() => Shader.SetGlobalColor(RoomBaseColorId, Color.clear);
    }
}
