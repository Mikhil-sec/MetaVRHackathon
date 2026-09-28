using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>Procedural flat-shaded crystal: a hexagonal bipyramid with a taller top point.</summary>
    public static class CrystalMesh
    {
        static Mesh s_Mesh;

        public static Mesh Get()
        {
            if (s_Mesh != null) return s_Mesh;

            const int sides = 6;
            const float radius = 0.5f, top = 1.1f, bottom = -0.45f;
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                ring[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }

            // Split vertices per face for hard edges.
            var verts = new Vector3[sides * 6];
            var tris = new int[sides * 6];
            int v = 0;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % sides];
                verts[v] = new Vector3(0f, top, 0f); verts[v + 1] = b; verts[v + 2] = a;
                verts[v + 3] = new Vector3(0f, bottom, 0f); verts[v + 4] = a; verts[v + 5] = b;
                v += 6;
            }
            for (int i = 0; i < tris.Length; i++) tris[i] = i;

            s_Mesh = new Mesh { name = "Crystal", vertices = verts, triangles = tris };
            s_Mesh.RecalculateNormals();
            s_Mesh.RecalculateBounds();
            return s_Mesh;
        }
    }
}
