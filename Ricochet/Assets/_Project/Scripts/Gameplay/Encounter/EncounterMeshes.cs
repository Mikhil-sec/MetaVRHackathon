using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>Procedural meshes for the encounter: the rift's crack and the creature's faceted body.</summary>
    public static class EncounterMeshes
    {
        static Mesh s_rift, s_creature;

        /// <summary>
        /// A jagged vertical crack, 1 unit tall, about 1 unit wide at its widest: a zigzag seam with a lens-shaped
        /// width profile. Three vertices per row (edge, seam, edge) so the shader can glow across it (uv.x).
        /// </summary>
        public static Mesh Rift()
        {
            if (s_rift != null) return s_rift;
            const int rows = 17;
            var rng = new System.Random(19);
            var verts = new Vector3[rows * 3];
            var uvs = new Vector2[rows * 3];
            for (int r = 0; r < rows; r++)
            {
                float t = r / (rows - 1f);
                float y = t - 0.5f;
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.18f * Mathf.Sin(Mathf.PI * t);
                // Max(0, ...): sin(pi) comes out a hair negative, and a fractional power of a negative is NaN.
                float half = 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.8f) * (0.7f + 0.6f * (float)rng.NextDouble());
                verts[r * 3] = new Vector3(x - half, y, 0f);
                verts[r * 3 + 1] = new Vector3(x, y, 0f);
                verts[r * 3 + 2] = new Vector3(x + half, y, 0f);
                uvs[r * 3] = new Vector2(0f, t);
                uvs[r * 3 + 1] = new Vector2(0.5f, t);
                uvs[r * 3 + 2] = new Vector2(1f, t);
            }
            var tris = new int[(rows - 1) * 12];
            int k = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                int a = r * 3, b = a + 3;
                for (int c = 0; c < 2; c++)
                {
                    tris[k++] = a + c; tris[k++] = b + c; tris[k++] = a + c + 1;
                    tris[k++] = a + c + 1; tris[k++] = b + c; tris[k++] = b + c + 1;
                }
            }
            s_rift = new Mesh { name = "Rift", vertices = verts, uv = uvs, triangles = tris };
            s_rift.RecalculateBounds();
            return s_rift;
        }

        /// <summary>
        /// A faceted teardrop, about 1 unit tall: a once-subdivided icosahedron pulled into a point at the top,
        /// flat-shaded (split vertices) so the rim light catches the facets.
        /// </summary>
        public static Mesh Creature()
        {
            if (s_creature != null) return s_creature;
            float p = (1f + Mathf.Sqrt(5f)) / 2f;
            var ico = new[]
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var verts = new Vector3[faces.Length * 4];
            int v = 0;
            for (int f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = Shape(ico[faces[f]]), b = Shape(ico[faces[f + 1]]), c = Shape(ico[faces[f + 2]]);
                Vector3 ab = Shape((ico[faces[f]] + ico[faces[f + 1]]) * 0.5f);
                Vector3 bc = Shape((ico[faces[f + 1]] + ico[faces[f + 2]]) * 0.5f);
                Vector3 ca = Shape((ico[faces[f + 2]] + ico[faces[f]]) * 0.5f);
                Vector3[] sub = { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca };
                for (int i = 0; i < sub.Length; i += 3)
                {
                    // Unity's front face is the one Cross(b - a, c - a) points at: make every facet face outward.
                    Vector3 x = sub[i], y = sub[i + 1], z = sub[i + 2];
                    if (Vector3.Dot(Vector3.Cross(y - x, z - x), x + y + z) < 0f) (y, z) = (z, y);
                    verts[v++] = x; verts[v++] = y; verts[v++] = z;
                }
            }
            var tris = new int[verts.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            s_creature = new Mesh { name = "Creature", vertices = verts, triangles = tris };
            s_creature.RecalculateNormals();
            s_creature.RecalculateBounds();
            return s_creature;
        }

        // Unit sphere, then a teardrop: narrower and pulled up toward the crown, a little flattened front to back.
        static Vector3 Shape(Vector3 p)
        {
            p = p.normalized * 0.5f;
            float up = Mathf.Clamp01(p.y / 0.5f);
            float taper = 1f - 0.55f * up * up;
            return new Vector3(p.x * taper, p.y * (1f + 0.45f * up), p.z * taper * 0.85f);
        }
    }
}
