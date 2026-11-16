using UnityEngine;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// Deterministically generates procedural 3D meshes (Pyramid agent, 3D Arrow, VR Studio Grid)
    /// at runtime or Editor build time so SpatialMAS requires zero external .fbx/.obj files.
    /// </summary>
    public static class ProceduralMeshFactory
    {
        private static Mesh s_PyramidMesh;
        private static Mesh s_DiamondMesh;
        private static Mesh s_ArrowMesh;

        /// <summary>
        /// Creates a flat-shaded 4-sided Pyramid mesh centered at the origin with unit bounds (-0.5 to +0.5).
        /// </summary>
        public static Mesh GetOrCreatePyramidMesh()
        {
            if (s_PyramidMesh != null) return s_PyramidMesh;

            var mesh = new Mesh { name = "SpatialMAS_Pyramid" };

            Vector3 apex = new Vector3(0f, 0.5f, 0f);
            Vector3 bl = new Vector3(-0.5f, -0.5f, -0.5f);
            Vector3 br = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 fr = new Vector3(0.5f, -0.5f, 0.5f);
            Vector3 fl = new Vector3(-0.5f, -0.5f, 0.5f);

            // Duplicate vertices per face for crisp flat-shaded normals
            Vector3[] vertices =
            {
                // Front face
                fl, apex, fr,
                // Right face
                fr, apex, br,
                // Back face
                br, apex, bl,
                // Left face
                bl, apex, fl,
                // Bottom quad (2 triangles)
                bl, fl, fr,
                bl, fr, br
            };

            int[] triangles = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                triangles[i] = i;
            }

            Vector2[] uvs = new Vector2[vertices.Length];
            for (int i = 0; i < 12; i += 3)
            {
                uvs[i] = new Vector2(0f, 0f);
                uvs[i + 1] = new Vector2(0.5f, 1f);
                uvs[i + 2] = new Vector2(1f, 0f);
            }
            for (int i = 12; i < vertices.Length; i++)
            {
                uvs[i] = new Vector2(0.5f, 0.5f);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            s_PyramidMesh = mesh;
            return s_PyramidMesh;
        }

        /// <summary>Creates a flat-shaded octahedron with unit bounds.</summary>
        public static Mesh GetOrCreateDiamondMesh()
        {
            if (s_DiamondMesh != null) return s_DiamondMesh;

            Vector3 top = Vector3.up * 0.5f;
            Vector3 bottom = -top;
            Vector3[] rim = { Vector3.right * 0.5f, Vector3.forward * 0.5f, Vector3.left * 0.5f, Vector3.back * 0.5f };
            var vertices = new Vector3[24];
            var triangles = new int[vertices.Length];
            for (int i = 0; i < rim.Length; i++)
            {
                Vector3 next = rim[(i + 1) % rim.Length];
                int face = i * 6;
                vertices[face] = rim[i];
                vertices[face + 1] = top;
                vertices[face + 2] = next;
                vertices[face + 3] = rim[i];
                vertices[face + 4] = next;
                vertices[face + 5] = bottom;
            }
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;

            s_DiamondMesh = new Mesh { name = "SpatialMAS_Diamond", vertices = vertices, triangles = triangles };
            s_DiamondMesh.RecalculateNormals();
            s_DiamondMesh.RecalculateBounds();
            return s_DiamondMesh;
        }

        /// <summary>
        /// Creates a unit-length 3D Arrow mesh oriented along +Z (from Z=0 to Z=1) with unit width.
        /// </summary>
        public static Mesh GetOrCreateArrowMesh()
        {
            if (s_ArrowMesh != null) return s_ArrowMesh;

            var mesh = new Mesh { name = "SpatialMAS_Arrow3D" };
            const int segments = 12;
            const float shaftRadius = 0.22f;
            const float headRadius = 0.50f;
            const float shaftLength = 0.72f;

            var verts = new System.Collections.Generic.List<Vector3>(128);
            var tris = new System.Collections.Generic.List<int>(256);

            // Shaft cylinder (z = 0 to shaftLength)
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

                Vector3 p0Bottom = new Vector3(Mathf.Cos(a0) * shaftRadius, Mathf.Sin(a0) * shaftRadius, 0f);
                Vector3 p1Bottom = new Vector3(Mathf.Cos(a1) * shaftRadius, Mathf.Sin(a1) * shaftRadius, 0f);
                Vector3 p0Top = new Vector3(Mathf.Cos(a0) * shaftRadius, Mathf.Sin(a0) * shaftRadius, shaftLength);
                Vector3 p1Top = new Vector3(Mathf.Cos(a1) * shaftRadius, Mathf.Sin(a1) * shaftRadius, shaftLength);

                int idx = verts.Count;
                verts.Add(p0Bottom);
                verts.Add(p0Top);
                verts.Add(p1Top);
                verts.Add(p1Bottom);

                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
                tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
            }

            // Cone head (z = shaftLength to 1.0)
            Vector3 tip = new Vector3(0f, 0f, 1.0f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

                Vector3 c0 = new Vector3(Mathf.Cos(a0) * headRadius, Mathf.Sin(a0) * headRadius, shaftLength);
                Vector3 c1 = new Vector3(Mathf.Cos(a1) * headRadius, Mathf.Sin(a1) * headRadius, shaftLength);

                int idx = verts.Count;
                verts.Add(c0);
                verts.Add(tip);
                verts.Add(c1);

                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            s_ArrowMesh = mesh;
            return s_ArrowMesh;
        }
    }
}
