using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Room
{
    /// <summary>
    /// The no-scan fallback (CONCEPT section 5): a glass chamber built around the seat, open toward the player.
    /// It loads as a synthetic MRUK room, so the board generator, rift placer, EffectMesh colliders and RoomGlow all
    /// work on it unchanged. RoomGlow draws it as tinted glass with lit seams and a fine grid, fading out toward the
    /// player so it reads as a stage in front of you rather than a box around you (the arena globals set here).
    /// The layout is in the seat's floor frame: x right, y up from the floor, z forward from the eye.
    /// </summary>
    public static class PocketArena
    {
        public const string RoomKey = "PocketArena";
        public const float HalfWidth = 1.45f, BackZ = -0.8f, Height = 2.4f;
        /// <summary>Front wall distance ahead of the eye. Settable for tuning sweeps; the furniture keeps its place relative to it.</summary>
        public static float FrontZ = 2.6f;
        const float RevealStart = 0.15f, RevealEnd = 1.2f;

        // Seat-sized furniture: a low altar for table-top crystals and two pillars for bank shots.
        // z is measured back from the front wall.
        static readonly (MRUKAnchor.SceneLabels label, Vector3 center, Vector3 size)[] VolumeLayout =
        {
            (MRUKAnchor.SceneLabels.TABLE, new Vector3(0f, 0.225f, -1.4f), new Vector3(0.9f, 0.45f, 0.42f)),
            (MRUKAnchor.SceneLabels.STORAGE, new Vector3(-1.0f, 0.75f, -0.7f), new Vector3(0.32f, 1.5f, 0.32f)),
            (MRUKAnchor.SceneLabels.STORAGE, new Vector3(1.0f, 0.75f, -0.7f), new Vector3(0.32f, 1.5f, 0.32f)),
        };

        static IEnumerable<(MRUKAnchor.SceneLabels label, Vector3 center, Vector3 size)> Volumes
        {
            get
            {
                foreach (var (label, c, size) in VolumeLayout) yield return (label, new Vector3(c.x, c.y, FrontZ + c.z), size);
            }
        }

        /// <summary>RoomGlow's base on device: faint blue glass that dims the passthrough behind it a little.</summary>
        public static readonly Color GlassColor = new(0.30f, 0.42f, 0.66f, 0.14f);
        static readonly Color SeamColor = new(0.55f, 0.75f, 1.6f, 1f);

        const int MaxBoxes = 4; // must match RoomGlow.shader
        static readonly int WorldToLocalId = Shader.PropertyToID("_ArenaWorldToLocal");
        static readonly int BoxCenterId = Shader.PropertyToID("_ArenaBoxC");
        static readonly int BoxHalfId = Shader.PropertyToID("_ArenaBoxH");
        static readonly int ParamsId = Shader.PropertyToID("_ArenaParams");
        static readonly int ColorId = Shader.PropertyToID("_ArenaColor");

        public static bool Active { get; private set; }
        /// <summary>The seated eye pose the arena was built around.</summary>
        public static Pose Seat { get; private set; }
        /// <summary>The floor point under the eye, facing the seat's way: the layout frame.</summary>
        public static Pose Frame { get; private set; }

        /// <summary>
        /// A room prefab stand-in (named children, as MRUK's room prefabs) laid out around the eye, ready for
        /// MRUK.LoadSceneFromPrefab. Activates the arena (shader globals, seat). Destroy it once the room has loaded.
        /// </summary>
        public static GameObject BuildLayout(Pose eye, float floorY)
        {
            Vector3 flat = Vector3.ProjectOnPlane(eye.rotation * Vector3.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            var yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Seat = new Pose(eye.position, yaw);
            Frame = new Pose(new Vector3(eye.position.x, floorY, eye.position.z), yaw);

            var root = new GameObject(RoomKey);
            float midZ = (FrontZ + BackZ) * 0.5f, depth = FrontZ - BackZ;
            // Walls: centred, +z (forward) pointing out of the room, scale = (width, height), as in MRUK's prefabs.
            Wall(root, new Vector3(0f, 0f, FrontZ), Vector3.forward, HalfWidth * 2f);
            Wall(root, new Vector3(HalfWidth, 0f, midZ), Vector3.right, depth);
            Wall(root, new Vector3(0f, 0f, BackZ), Vector3.back, HalfWidth * 2f);
            Wall(root, new Vector3(-HalfWidth, 0f, midZ), Vector3.left, depth);
            foreach (var (label, center, size) in Volumes)
            {
                var t = new GameObject(label.ToString()).transform;
                t.SetParent(root.transform, false);
                t.SetPositionAndRotation(ToWorld(center), yaw);
                t.localScale = size; // volumes: pivot centred, y up
            }

            // MRUK reads a room prefab's poses in tracking space, so cancel the tracking space's pose (the rig is
            // moved on desktop, and could be anywhere on device): the room then lands where it was built.
            var rig = Object.FindAnyObjectByType<OVRCameraRig>(); // the rig MRUK reads (OVRCameraRig.Instance)
            Transform space = rig != null ? rig.trackingSpace : null;
            if (space != null)
            {
                var inverse = Matrix4x4.TRS(space.position, space.rotation, Vector3.one).inverse;
                root.transform.SetPositionAndRotation(inverse.GetPosition(), inverse.rotation);
            }

            Active = true;
            SetGlobals();
            return root;
        }

        static void Wall(GameObject root, Vector3 baseCenter, Vector3 outward, float width)
        {
            var t = new GameObject(MRUKAnchor.SceneLabels.WALL_FACE.ToString()).transform;
            t.SetParent(root.transform, false);
            t.SetPositionAndRotation(ToWorld(baseCenter + Vector3.up * (Height * 0.5f)),
                Frame.rotation * Quaternion.LookRotation(outward, Vector3.up));
            t.localScale = new Vector3(width, Height, 1f);
        }

        public static Vector3 ToWorld(Vector3 local) => Frame.position + Frame.rotation * local;
        public static Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Frame.rotation) * (world - Frame.position);
        public static Quaternion ToWorld(Quaternion local) => Frame.rotation * local;
        public static Quaternion ToLocal(Quaternion world) => Quaternion.Inverse(Frame.rotation) * world;

        static void SetGlobals()
        {
            var centers = new Vector4[MaxBoxes];
            var halves = new Vector4[MaxBoxes];
            float midZ = (FrontZ + BackZ) * 0.5f;
            centers[0] = new Vector4(0f, Height * 0.5f, midZ);
            halves[0] = new Vector4(HalfWidth, Height * 0.5f, (FrontZ - BackZ) * 0.5f);
            int n = 1;
            foreach (var (_, center, size) in Volumes)
            {
                if (n == MaxBoxes) break;
                centers[n] = center;
                halves[n] = size * 0.5f;
                n++;
            }
            Shader.SetGlobalMatrix(WorldToLocalId, Matrix4x4.TRS(Frame.position, Frame.rotation, Vector3.one).inverse);
            Shader.SetGlobalVectorArray(BoxCenterId, centers);
            Shader.SetGlobalVectorArray(BoxHalfId, halves);
            Shader.SetGlobalVector(ParamsId, new Vector4(n, RevealStart, RevealEnd, 1f));
            Shader.SetGlobalColor(ColorId, SeamColor);
        }

        public static void Deactivate()
        {
            Active = false;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }
    }
}
