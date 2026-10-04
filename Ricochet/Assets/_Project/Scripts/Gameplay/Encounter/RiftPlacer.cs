using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Where the rift opens (CONCEPT section 5): the best clear patch of real wall inside the forward view
    /// (+/-25 degrees, docs/TECH_GUIDE.md section 7), away from doors and windows, with nothing standing in front of it.
    /// Falls back to a portal hanging in the air when no wall qualifies (a cluttered room, or no scene model).
    /// </summary>
    public static class RiftPlacer
    {
        public struct Placement
        {
            public Vector3 Position;
            public Vector3 Normal;     // points out of the wall, toward the player
            public bool OnWall;
            public string Info;
            /// <summary>Crystals are kept out of this radius around ExclusionCenter.</summary>
            public float ExclusionRadius;
            public Vector3 ExclusionCenter => Position + Normal * 0.25f;
        }

        // The clear zone is an ellipse shaped like the crack plus its glow: narrow and tall.
        const float ClearHalfWidth = 0.28f, ClearHalfHeight = 0.42f;
        const float HeroClearance = 0.75f;
        // Trophy crowns: a rift on top of one is allowed (it steps aside) but only when no other wall spot is clear.
        const float CrownClearance = 0.5f, CrownPenalty = 1.5f;
        // +/-24 is the critical-content band; +/-30 is a penalized last resort, still inside the Glasses' ~+/-35 view.
        static readonly float[] Yaws = { 0f, -6f, 6f, -12f, 12f, -18f, 18f, -24f, 24f, -30f, 30f };
        // Higher pitches look over furniture; 18 degrees stays inside the +/-20 degree vertical band.
        static readonly float[] Pitches = { 4f, 9f, 0f, 14f, -4f, 18f };

        /// <param name="avoid">A previous rift position to move away from (the next encounter opens elsewhere).</param>
        /// <param name="keepClear">The hero landing point: the guaranteed first shot keeps its cluster.</param>
        /// <param name="crowns">Trophy crowns on the walls, kept clear of when another spot is as good.</param>
        public static Placement Place(MRUKRoom room, Pose seat, Vector3? avoid = null, Vector3? keepClear = null,
                                      List<Vector3> crowns = null)
        {
            Vector3 eye = seat.position;
            Vector3 forward = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            var best = new Placement();
            float bestScore = float.NegativeInfinity;

            // Rejection tallies, reported when nothing qualifies (sweep diagnostics).
            int miss = 0, notWall = 0, angle = 0, range = 0, near = 0, blocked = 0, opening = 0;
            if (room != null)
            {
                foreach (float yaw in Yaws)
                foreach (float pitch in Pitches)
                {
                    Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * (Quaternion.AngleAxis(-pitch, right) * forward);
                    if (!room.Raycast(new Ray(eye, dir), 8f, out RaycastHit hit, out MRUKAnchor anchor)) { miss++; continue; }
                    if (anchor == null || (anchor.Label & MRUKAnchor.SceneLabels.WALL_FACE) == 0) { notWall++; continue; }
                    if (Vector3.Dot(hit.normal, -dir) < 0.6f) { angle++; continue; }  // faces the player
                    if (hit.distance < 1.2f || hit.distance > 6f) { range++; continue; }
                    if ((avoid.HasValue && (hit.point - avoid.Value).sqrMagnitude < 1.2f * 1.2f) ||
                        (keepClear.HasValue && (hit.point - keepClear.Value).sqrMagnitude < HeroClearance * HeroClearance)) { near++; continue; }
                    if (!Clear(room, eye, hit.point, hit.normal, anchor)) { blocked++; continue; }
                    if (NearOpening(room, hit.point)) { opening++; continue; }

                    float score = -Mathf.Abs(yaw) * 0.04f - Mathf.Abs(pitch - 6f) * 0.03f - Mathf.Abs(hit.distance - 2.8f) * 0.3f
                                  - (Mathf.Abs(yaw) > 25f ? 1f : 0f);
                    if (crowns != null)
                        foreach (var c in crowns)
                            if ((c - hit.point).sqrMagnitude < CrownClearance * CrownClearance) { score -= CrownPenalty; break; }
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = new Placement
                    {
                        Position = hit.point, Normal = hit.normal, OnWall = true, ExclusionRadius = 0.55f,
                        Info = $"wall yaw={yaw:F0} pitch={pitch:F0} d={hit.distance:F1}",
                    };
                }
            }
            if (best.OnWall) return best;
            // The same wall again beats a portal in the air.
            if (avoid.HasValue) return Place(room, seat, null, keepClear, crowns);

            // No clear wall: hang the rift in the air ahead, facing the player: up and to the side away from the hero
            // cluster so the guaranteed first shot keeps its target.
            float side = 0.3f;
            if (keepClear.HasValue && Vector3.Dot(keepClear.Value - eye, right) > 0f) side = -side;
            Vector3 p = eye + forward * 1.8f + Vector3.up * 0.35f + right * side;
            return new Placement
            {
                Position = p, Normal = -forward, OnWall = false, ExclusionRadius = 0.3f,
                Info = $"floating (miss={miss} notWall={notWall} angle={angle} range={range} near={near} blocked={blocked} opening={opening})",
            };
        }

        /// <summary>A ring of rays from the eye must all land on the same wall at the expected depth: no furniture in front.</summary>
        static bool Clear(MRUKRoom room, Vector3 eye, Vector3 center, Vector3 normal, MRUKAnchor wall)
        {
            Vector3 u = Vector3.Cross(normal, Vector3.up).normalized;
            if (u.sqrMagnitude < 0.5f) return false; // not a vertical surface
            Vector3 v = Vector3.Cross(u, normal);
            int blocked = 0; // a lamp or plant clipping one edge is fine; the creature floats in front anyway
            for (int i = 0; i < 12; i++)
            {
                // 8 points on the ellipse, then 4 halfway in.
                float a = i < 8 ? i * Mathf.PI * 0.25f : (i - 8) * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                float k = i < 8 ? 1f : 0.5f;
                Vector3 p = center + (u * (Mathf.Cos(a) * ClearHalfWidth) + v * (Mathf.Sin(a) * ClearHalfHeight)) * k;
                Vector3 to = p - eye;
                float dist = to.magnitude;
                if (!room.Raycast(new Ray(eye, to / dist), dist + 0.3f, out RaycastHit hit, out MRUKAnchor anchor)) return false;
                if (anchor != wall || Mathf.Abs(hit.distance - dist) > 0.08f)
                {
                    if (i >= 8 || ++blocked > 2) return false; // the inner ring must be fully clear
                }
            }
            return true;
        }

        static bool NearOpening(MRUKRoom room, Vector3 p)
        {
            const MRUKAnchor.SceneLabels openings = MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            foreach (var anchor in room.Anchors)
            {
                if ((anchor.Label & openings) == 0 || !anchor.PlaneRect.HasValue) continue;
                Rect rect = anchor.PlaneRect.Value;
                Vector3 local = anchor.transform.InverseTransformPoint(p);
                const float margin = 0.35f;
                if (Mathf.Abs(local.z) < 0.4f &&
                    local.x > rect.xMin - margin && local.x < rect.xMax + margin &&
                    local.y > rect.yMin - margin && local.y < rect.yMax + margin) return true;
            }
            return false;
        }
    }
}
