using UnityEngine;

namespace Confluence.Client.Presentation
{
    /// <summary>Procedural meshes for combat feedback, in local space facing +Z (Core yaw 0).</summary>
    public static class ShapeMeshes
    {
        /// <summary>
        /// Vertical curved band: the part of a cylinder of this radius within +-halfAngle of +Z, from y0 to y1, double
        /// sided (the guard's frontal arc, spec L104).
        /// </summary>
        public static Mesh CylinderSector(float radius, float y0, float y1, float halfAngleDeg, int segments, string name)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, t) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = dir * radius + Vector3.up * y0;
                vertices[i * 2 + 1] = dir * radius + Vector3.up * y1;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, k = i * 12;
                triangles[k] = a; triangles[k + 1] = b; triangles[k + 2] = c;
                triangles[k + 3] = c; triangles[k + 4] = b; triangles[k + 5] = d;
                triangles[k + 6] = c; triangles[k + 7] = b; triangles[k + 8] = a;
                triangles[k + 9] = d; triangles[k + 10] = b; triangles[k + 11] = c;
            }

            return Build(name, vertices, uvs, triangles);
        }

        /// <summary>Flat horizontal sector between two radii within +-halfAngle of +Z, double sided (melee reach, spec L100).</summary>
        public static Mesh Sector(float innerRadius, float outerRadius, float halfAngleDeg, int segments, string name)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, t) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = dir * innerRadius;
                vertices[i * 2 + 1] = dir * outerRadius;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, k = i * 12;
                triangles[k] = a; triangles[k + 1] = b; triangles[k + 2] = c;
                triangles[k + 3] = c; triangles[k + 4] = b; triangles[k + 5] = d;
                triangles[k + 6] = c; triangles[k + 7] = b; triangles[k + 8] = a;
                triangles[k + 9] = d; triangles[k + 10] = b; triangles[k + 11] = c;
            }

            return Build(name, vertices, uvs, triangles);
        }

        private static Mesh Build(string name, Vector3[] vertices, Vector2[] uvs, int[] triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
