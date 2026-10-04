using System.Collections.Generic;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Procedural flat-shaded crystals, one silhouette per <see cref="CrystalKind"/> so the types read without color
    /// (accessibility, CONCEPT section 2): Normal is a tall hexagonal spike, Gold a wide brilliant-cut gem with a flat
    /// table, Bomb a spiked mine, Amp a three-tier stepped tower, Prism a bevelled triangular bar. All span roughly
    /// y -0.45..1.1 around a centre near y 0.3 (CrystalGlass maps height from that range; the collider is the same
    /// sphere for every kind, so physics and the sweep never see the shape).
    /// </summary>
    public static class CrystalMesh
    {
        static readonly Mesh[] s_Meshes = new Mesh[5];

        public static Mesh Get() => Get(CrystalKind.Normal);

        public static Mesh Get(CrystalKind kind)
        {
            int i = (int)kind;
            if (s_Meshes[i] != null) return s_Meshes[i];
            var b = new Builder();
            switch (kind)
            {
                case CrystalKind.Gold: Gem(b); break;
                case CrystalKind.Bomb: Mine(b); break;
                case CrystalKind.Amp: Tower(b); break;
                case CrystalKind.Prism: Bar(b); break;
                default: Spike(b); break;
            }
            s_Meshes[i] = b.Build(kind == CrystalKind.Normal ? "Crystal" : "Crystal" + kind);
            return s_Meshes[i];
        }

        /// <summary>Normal: a hexagonal bipyramid with a taller top point.</summary>
        static void Spike(Builder b)
        {
            const int sides = 6;
            var top = new Vector3(0f, 1.1f, 0f);
            var bottom = new Vector3(0f, -0.45f, 0f);
            var inside = new Vector3(0f, 0.3f, 0f);
            for (int i = 0; i < sides; i++)
            {
                Vector3 p = Ring(i, sides, 0.5f, 0f, 0f), q = Ring(i + 1, sides, 0.5f, 0f, 0f);
                b.Tri(top, p, q, inside);
                b.Tri(bottom, p, q, inside);
            }
        }

        /// <summary>Gold: a brilliant cut. Flat table, star and bezel facets in the crown, a pointed pavilion.</summary>
        static void Gem(Builder b)
        {
            const int sides = 8;
            const float girdleY = 0.32f, tableY = 0.76f;
            var culet = new Vector3(0f, -0.45f, 0f);
            var tableCentre = new Vector3(0f, tableY, 0f);
            var inside = new Vector3(0f, 0.3f, 0f);
            for (int i = 0; i < sides; i++)
            {
                Vector3 g0 = Ring(i, sides, 0.64f, girdleY, 0f), g1 = Ring(i + 1, sides, 0.64f, girdleY, 0f);
                Vector3 t0 = Ring(i, sides, 0.36f, tableY, 0.5f), tPrev = Ring(i - 1, sides, 0.36f, tableY, 0.5f);
                b.Tri(g0, g1, t0, inside);        // bezel facet, pointing up
                b.Tri(tPrev, t0, g0, inside);     // star facet, pointing down
                b.Tri(tableCentre, tPrev, t0, inside);
                b.Tri(culet, g0, g1, inside);     // pavilion
            }
        }

        /// <summary>Bomb: a sea mine. An icosahedron's 12 vertices pulled out into spikes over a small core.</summary>
        static void Mine(Builder b)
        {
            const float phi = 1.618034f, tip = 0.68f, valley = 0.3f;
            var centre = new Vector3(0f, 0.3f, 0f);
            Vector3[] v =
            {
                new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
                new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
                new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int i = 0; i < v.Length; i++) v[i] = centre + v[i].normalized * tip;
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]], c = v[f[i + 1]], d = v[f[i + 2]];
                // The face centre sinks to the core: each face becomes a valley between three spikes.
                Vector3 m = centre + ((a + c + d) / 3f - centre).normalized * valley;
                b.Tri(a, c, m, centre);
                b.Tri(c, d, m, centre);
                b.Tri(d, a, m, centre);
            }
        }

        /// <summary>Amp: three stacked tiers, each narrower and twisted 30 degrees, read as "more, more, more".</summary>
        static void Tower(Builder b)
        {
            const int sides = 6;
            // (bottom y, bottom radius, top y, top radius; top radius 0 = apex, twist in sides)
            Tier(b, sides, -0.45f, 0.54f, 0.14f, 0.22f, 0f);
            Tier(b, sides, 0.08f, 0.45f, 0.58f, 0.16f, 0.5f);
            Tier(b, sides, 0.52f, 0.34f, 1.1f, 0f, 0f);
        }

        static void Tier(Builder b, int sides, float y0, float r0, float y1, float r1, float twist)
        {
            var inside = new Vector3(0f, (y0 + y1) * 0.5f, 0f);
            var under = new Vector3(0f, y0, 0f);
            var apex = new Vector3(0f, y1, 0f);
            for (int i = 0; i < sides; i++)
            {
                Vector3 p0 = Ring(i, sides, r0, y0, twist), q0 = Ring(i + 1, sides, r0, y0, twist);
                if (r1 > 0f)
                {
                    Vector3 p1 = Ring(i, sides, r1, y1, twist), q1 = Ring(i + 1, sides, r1, y1, twist);
                    b.Tri(p0, q0, q1, inside);
                    b.Tri(p0, q1, p1, inside);
                }
                else b.Tri(p0, q0, apex, inside);
                // The flat underside: a ledge under the overhang (a cap at the very bottom).
                b.Tri(under, p0, q0, under + Vector3.up * 0.1f);
            }
        }

        /// <summary>Prism: a triangular bar lying across the crystal's axis, with bevelled ends.</summary>
        static void Bar(Builder b)
        {
            const float half = 0.66f, bevel = 0.5f, shrink = 0.62f;
            var inside = new Vector3(0f, 0.3f, 0f);
            // Cross-section: apex up, centroid at y 0.3.
            Vector3[] tri = { new(0f, 1.02f, 0f), new(0f, -0.06f, 0.64f), new(0f, -0.06f, -0.64f) };
            var body0 = new Vector3[3];
            var body1 = new Vector3[3];
            var end0 = new Vector3[3];
            var end1 = new Vector3[3];
            for (int k = 0; k < 3; k++)
            {
                Vector3 c = tri[k], s = inside + (tri[k] - inside) * shrink;
                body0[k] = c + Vector3.left * bevel;
                body1[k] = c + Vector3.right * bevel;
                end0[k] = s + Vector3.left * half;
                end1[k] = s + Vector3.right * half;
            }
            for (int k = 0; k < 3; k++)
            {
                int n = (k + 1) % 3;
                b.Tri(body0[k], body0[n], body1[n], inside);
                b.Tri(body0[k], body1[n], body1[k], inside);
                b.Tri(body0[k], body0[n], end0[n], inside);   // bevels
                b.Tri(body0[k], end0[n], end0[k], inside);
                b.Tri(body1[k], body1[n], end1[n], inside);
                b.Tri(body1[k], end1[n], end1[k], inside);
            }
            b.Tri(end0[0], end0[1], end0[2], inside);
            b.Tri(end1[0], end1[1], end1[2], inside);
        }

        static Vector3 Ring(int i, int sides, float radius, float y, float offset)
        {
            float a = (i + offset) * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
        }

        /// <summary>Flat-shaded triangles (vertices split per face for hard edges), each wound to face away from a
        /// reference point inside the solid it belongs to, so non-convex shapes (spikes, tiers) shade correctly.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _verts = new();

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) (b, c) = (c, b);
                _verts.Add(a);
                _verts.Add(b);
                _verts.Add(c);
            }

            public Mesh Build(string name)
            {
                var tris = new int[_verts.Count];
                for (int i = 0; i < tris.Length; i++) tris[i] = i;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
