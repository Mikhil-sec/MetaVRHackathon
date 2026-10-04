using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Audio
{
    /// <summary>
    /// Bounces should sound like *your* room (docs/TECH_GUIDE.md section 8): the Meta XR Audio spatializer's shoebox
    /// room (early reflections plus reverb) is sized and placed from the loaded room's world bounds, the scanned room
    /// or the Pocket Arena, once per room. Meta's <c>MetaXRAudioRoomAcousticProperties</c> re-sends the room every
    /// frame with freshly allocated arrays; this sends it once, allocation-free.
    /// </summary>
    public sealed class RoomAcoustics : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] float _clutterPerItem = 0.06f; // furniture diffuses the reverb

        // Per-band reflection coefficients (Meta's presets): drywall walls, acoustic-tile ceiling, carpeted floor.
        static readonly float[] Drywall = { 0.721240044f, 0.927690148f, 0.934302270f, 0.910105407f };
        static readonly float[] CeilingTile = { 0.488168418f, 0.361475229f, 0.339595377f, 0.498946249f };
        static readonly float[] Carpet = { 0.987633705f, 0.905486643f, 0.583110571f, 0.351053834f };

        readonly float[] _walls = new float[6 * 4]; // [Left, Right, Ceiling, Floor, Front, Back] x 4 bands
        readonly float[] _clutter = new float[4];

        void OnEnable()
        {
            _playArea.Ready += Apply;
            if (_playArea.IsReady) Apply();
        }

        void OnDisable() => _playArea.Ready -= Apply;

        void Apply()
        {
            var room = _playArea.Room;
            if (room == null) return;
            Bounds bounds = room.GetRoomBounds();
            Vector3 size = Vector3.Max(bounds.size, new Vector3(1.5f, 2f, 1.5f));
            for (int w = 0; w < 6; w++)
            {
                float[] material = w == 2 ? CeilingTile : w == 3 ? Carpet : Drywall;
                for (int b = 0; b < 4; b++) _walls[w * 4 + b] = material[b];
            }
            int furniture = 0; // volumes only: doors, windows and wall art do not diffuse a room
            var anchors = room.Anchors;
            for (int i = 0; i < anchors.Count; i++) if (anchors[i].VolumeBounds.HasValue) furniture++;
            float factor = Mathf.Clamp(0.15f + _clutterPerItem * furniture, 0.15f, 0.8f);
            float clutter = factor;
            for (int b = 3; b >= 0; b--)
            {
                _clutter[b] = factor;
                factor *= 0.5f; // clutter matters less at low frequencies (Meta's mapping)
            }
            var native = MetaXRAudioNativeInterface.Interface;
            int result = native.SetAdvancedBoxRoomParameters(size.x, size.y, size.z, false, bounds.center, _walls);
            native.SetRoomClutterFactor(_clutter);
            Debug.Log($"[Ricochet] Room acoustics: {size.x:F1} x {size.y:F1} x {size.z:F1} m, {furniture} volumes, clutter {clutter:F2} (result {result})");
        }
    }
}
