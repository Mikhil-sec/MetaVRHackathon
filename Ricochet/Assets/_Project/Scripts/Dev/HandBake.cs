#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Input;
using Ricochet.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Ricochet.Dev
{
    /// <summary>
    /// Editor, XR Play only: bakes the tracked right hand (the simulator's synthetic hand) into the onboarding ghost
    /// hand (GhostHandDemo). With the hand pinching the Spark at the sling call Capture("pinch"); open the fingers
    /// without moving the wrist and call Capture("open"); then Save() writes <see cref="AssetPath"/>.
    /// Vertices are stored around the pinch point in the sling's frame (no scale), which is how the demo poses it:
    /// open pose in POSITION/NORMAL, pinch pose in TEXCOORD1/2, and TEXCOORD3.x = 0 at the wrist .. 1 at the fingertips.
    /// </summary>
    public static class HandBake
    {
        public const string AssetPath = "Assets/_Project/Art/Meshes/GhostHand.asset";

        static Vector3[] s_pinchV, s_pinchN, s_openV, s_openN;
        static List<Vector3> s_reach;
        static int[] s_tris;
        static Vector3 s_origin;
        static Quaternion s_frame;
        static SkinnedMeshRenderer s_skin;

        /// <summary>The skinned hand meshes under the camera rig, nearest the right wrist first.</summary>
        public static string List()
        {
            var sb = new StringBuilder();
            if (!RightHand(out var hand, out var wrist)) return "no tracked right hand";
            sb.Append("wrist ").Append(wrist.ToString("F3")).Append('\n');
            foreach (var r in Candidates(wrist))
                sb.Append($"{Vector3.Distance(r.bounds.center, wrist):F3} m  {Path(r.transform)}  verts={r.sharedMesh.vertexCount} enabled={r.enabled}\n");
            return sb.ToString();
        }

        public static string Capture(string pose)
        {
            if (!RightHand(out var hand, out var wrist)) return "no tracked right hand";
            var list = Candidates(wrist);
            if (list.Count == 0) return "no skinned hand mesh near the right wrist";
            var skin = list[0];
            var sling = Object.FindAnyObjectByType<Sling>();
            if (pose == "pinch")
            {
                if (!hand.GetJointPose(HandJointId.HandThumbTip, out var thumb) || !hand.GetJointPose(HandJointId.HandIndexTip, out var index))
                    return "no fingertip joints";
                s_origin = (thumb.position + index.position) * 0.5f;
                s_frame = sling.transform.rotation;
                s_skin = skin;
                Bake(skin, out s_pinchV, out s_pinchN);
                s_tris = skin.sharedMesh.triangles;
                return $"pinch baked from {Path(skin.transform)}: {s_pinchV.Length} verts, pinch point {s_origin:F3}, gap {Vector3.Distance(thumb.position, index.position):F3} m";
            }
            if (pose == "open")
            {
                if (s_skin == null) return "capture the pinch first";
                if (skin != s_skin) return "a different hand mesh is nearest now: " + Path(skin.transform);
                Bake(skin, out s_openV, out s_openN);
                // Reach: from the wrist toward the middle fingertip, measured on the open hand.
                hand.GetJointPose(HandJointId.HandMiddleTip, out var tip);
                Vector3 w = Local(wrist), axis = Local(tip.position) - w;
                s_reach = new List<Vector3>(s_openV.Length);
                for (int i = 0; i < s_openV.Length; i++)
                    s_reach.Add(new Vector3(Mathf.Clamp01(Vector3.Dot(s_openV[i] - w, axis) / axis.sqrMagnitude), 0f, 0f));
                return $"open baked: {s_openV.Length} verts, wrist {w:F3} (pinch frame), hand length {axis.magnitude:F3} m";
            }
            return "pose is pinch or open";
        }

        public static string Save()
        {
            if (s_pinchV == null || s_openV == null || s_pinchV.Length != s_openV.Length) return "capture both poses first";
            var mesh = new Mesh { name = "GhostHand" };
            mesh.SetVertices(s_openV);
            mesh.SetNormals(s_openN);
            mesh.SetUVs(1, s_pinchV);
            mesh.SetUVs(2, s_pinchN);
            mesh.SetUVs(3, s_reach);
            mesh.SetTriangles(s_tris, 0);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            foreach (var p in s_pinchV) bounds.Encapsulate(p);
            mesh.bounds = bounds;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = "GhostHand";
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(mesh, AssetPath);
            AssetDatabase.SaveAssets();
            return $"saved {AssetPath}: {mesh.vertexCount} verts, {s_tris.Length / 3} tris, bounds {bounds.size:F3}";
        }

        static void Bake(SkinnedMeshRenderer skin, out Vector3[] verts, out Vector3[] normals)
        {
            var baked = new Mesh();
            skin.BakeMesh(baked, true); // the renderer's local space with its scale applied: world = position + rotation * v
            verts = baked.vertices;
            normals = baked.normals;
            Transform t = skin.transform;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = Local(t.position + t.rotation * verts[i]);
                normals[i] = Quaternion.Inverse(s_frame) * (t.rotation * normals[i]);
            }
            Object.DestroyImmediate(baked);
        }

        static Vector3 Local(Vector3 world) => Quaternion.Inverse(s_frame) * (world - s_origin);

        static bool RightHand(out Hand hand, out Vector3 wrist)
        {
            foreach (var h in Object.FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (h.Handedness != Handedness.Right || !h.IsTrackedDataValid) continue;
                if (!h.GetJointPose(HandJointId.HandWristRoot, out var pose)) continue;
                hand = h;
                wrist = pose.position;
                return true;
            }
            hand = null;
            wrist = default;
            return false;
        }

        static List<SkinnedMeshRenderer> Candidates(Vector3 wrist)
        {
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var list = new List<SkinnedMeshRenderer>();
            if (rig == null) return list;
            foreach (var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (r.sharedMesh != null && r.sharedMesh.vertexCount > 300 && Vector3.Distance(r.bounds.center, wrist) < 0.25f)
                    list.Add(r);
            list.Sort((a, b) => Vector3.Distance(a.bounds.center, wrist).CompareTo(Vector3.Distance(b.bounds.center, wrist)));
            return list;
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
#endif
