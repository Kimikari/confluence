using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Mesh construction for Core arena primitives. Meshes are built in the solid's local frame (the GameObject
    /// carries the YawBox center and yaw), so collision and visuals share exactly the same box. UVs are in
    /// meters (materials scale them per surface), offset by the box center so neighboring floor pieces
    /// continue each other's pattern.
    /// </summary>
    public static class ArenaMeshes
    {
        /// <summary>Box with flat-shaded faces for a YawBox's half extents.</summary>
        public static Mesh Box(YawBox box, string name = "Box")
        {
            var b = new MeshBuffer();
            Vector3 h = ToUnity(box.HalfExtents);
            Vector2 offset = new Vector2(box.Center.X, box.Center.Z);
            float y0 = box.Center.Y;

            // +Y top and -Y bottom: UV from (x, z).
            b.Quad(new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), Vector3.up, p => new Vector2(p.x + offset.x, p.z + offset.y));
            b.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down, p => new Vector2(p.x + offset.x, p.z + offset.y));

            // +-Z faces: UV from (x, y); +-X faces: UV from (z, y).
            b.Quad(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward, p => new Vector2(-p.x + offset.x, p.y + y0));
            b.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, -h.y, -h.z), Vector3.back, p => new Vector2(p.x + offset.x, p.y + y0));
            b.Quad(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z), Vector3.right, p => new Vector2(p.z + offset.y, p.y + y0));
            b.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), Vector3.left, p => new Vector2(-p.z + offset.y, p.y + y0));
            return b.ToMesh(name);
        }

        /// <summary>
        /// Ramp wedge matching Core's RampShape: the walkable surface rises from the local -Z edge (height
        /// Top - Rise) to the +Z edge (box top); the solid fills down to the box bottom.
        /// </summary>
        public static Mesh Ramp(RampShape ramp, string name = "Ramp")
        {
            var b = new MeshBuffer();
            Vector3 h = ToUnity(ramp.Box.HalfExtents);
            float lowTop = h.y - ramp.Rise;
            Vector2 offset = new Vector2(ramp.Box.Center.X, ramp.Box.Center.Z);
            float y0 = ramp.Box.Center.Y;
            Vector3 slopeNormal = ToUnity(ramp.Box.DirectionToLocal(ramp.SurfaceNormal));
            float slopeLength = Mathf.Sqrt(4f * h.z * h.z + ramp.Rise * ramp.Rise);

            // Walkable slope: UV x across, y along the surface.
            b.Quad(new Vector3(-h.x, lowTop, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, lowTop, -h.z), slopeNormal,
                p => new Vector2(p.x + offset.x, (p.z + h.z) / (2f * h.z) * slopeLength + offset.y));

            // Bottom.
            b.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down, p => new Vector2(p.x + offset.x, p.z + offset.y));

            // High end (+Z), full height.
            b.Quad(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward, p => new Vector2(-p.x + offset.x, p.y + y0));

            // Low end (-Z), only where the wedge has thickness there.
            if (lowTop > -h.y + 1e-4f)
            {
                b.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, lowTop, -h.z), new Vector3(h.x, lowTop, -h.z), new Vector3(h.x, -h.y, -h.z), Vector3.back, p => new Vector2(p.x + offset.x, p.y + y0));
            }

            // Side faces: the cross-section quadrilateral (or triangle) on each side.
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * h.x;
                Vector3 n = s > 0 ? Vector3.right : Vector3.left;
                Vector3 p0 = new Vector3(x, -h.y, -h.z);
                Vector3 p1 = new Vector3(x, -h.y, h.z);
                Vector3 p2 = new Vector3(x, h.y, h.z);
                Vector3 p3 = new Vector3(x, lowTop, -h.z);
                System.Func<Vector3, Vector2> uv = p => new Vector2(s * p.z + offset.y, p.y + y0);
                b.Triangle(p0, p1, p2, n, uv);
                if (lowTop > -h.y + 1e-4f)
                {
                    b.Triangle(p0, p2, p3, n, uv);
                }
            }

            return b.ToMesh(name);
        }

        /// <summary>Flat rectangle in the XZ plane centered on the origin (y = 0), UVs in world meters around worldCenter.</summary>
        public static Mesh Rectangle(Vector2 halfSize, Vector2 worldCenter, float yawDeg, string name = "Rectangle")
        {
            var b = new MeshBuffer();
            Quaternion rotation = Quaternion.Euler(0f, yawDeg, 0f);
            b.Quad(new Vector3(-halfSize.x, 0f, -halfSize.y), new Vector3(-halfSize.x, 0f, halfSize.y), new Vector3(halfSize.x, 0f, halfSize.y), new Vector3(halfSize.x, 0f, -halfSize.y), Vector3.up,
                p =>
                {
                    Vector3 world = rotation * p;
                    return new Vector2(world.x + worldCenter.x, world.z + worldCenter.y);
                });
            return b.ToMesh(name);
        }

        /// <summary>Flat disc in the XZ plane, UVs in world meters around worldCenter.</summary>
        public static Mesh Disc(float radius, Vector2 worldCenter, int segments = 48, string name = "Disc")
        {
            var b = new MeshBuffer();
            Vector3 center = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = PolarXZ(radius, i * 360f / segments);
                Vector3 c = PolarXZ(radius, (i + 1) * 360f / segments);
                b.Triangle(center, a, c, Vector3.up, p => new Vector2(p.x + worldCenter.x, p.z + worldCenter.y));
            }

            return b.ToMesh(name);
        }

        /// <summary>Flat annulus in the XZ plane; UV x runs along the circumference in meters, y across the band.</summary>
        public static Mesh Ring(float innerRadius, float outerRadius, int segments = 96, string name = "Ring")
        {
            var b = new MeshBuffer();
            float circumference = 2f * Mathf.PI * outerRadius;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 360f / segments;
                float a1 = (i + 1) * 360f / segments;
                float u0 = circumference * i / segments;
                float u1 = circumference * (i + 1) / segments;
                b.QuadWithUv(
                    PolarXZ(innerRadius, a0), new Vector2(u0, 0f),
                    PolarXZ(outerRadius, a0), new Vector2(u0, 1f),
                    PolarXZ(outerRadius, a1), new Vector2(u1, 1f),
                    PolarXZ(innerRadius, a1), new Vector2(u1, 0f),
                    Vector3.up);
            }

            return b.ToMesh(name);
        }

        /// <summary>Point at a Core yaw (clockwise from +Z) and radius on the XZ plane.</summary>
        public static Vector3 PolarXZ(float radius, float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r) * radius, 0f, Mathf.Cos(r) * radius);
        }

        private static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>Accumulates flat-shaded triangles; winding is fixed from each face's intended outward normal.</summary>
        private sealed class MeshBuffer
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, System.Func<Vector3, Vector2> uv)
            {
                QuadWithUv(a, uv(a), b, uv(b), c, uv(c), d, uv(d), normal);
            }

            public void QuadWithUv(Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc, Vector3 d, Vector2 ud, Vector3 normal)
            {
                int i = Add(a, ua, normal);
                Add(b, ub, normal);
                Add(c, uc, normal);
                Add(d, ud, normal);
                AddTriangle(i, i + 1, i + 2, normal);
                AddTriangle(i, i + 2, i + 3, normal);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, System.Func<Vector3, Vector2> uv)
            {
                int i = Add(a, uv(a), normal);
                Add(b, uv(b), normal);
                Add(c, uv(c), normal);
                AddTriangle(i, i + 1, i + 2, normal);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }

            private int Add(Vector3 position, Vector2 uv, Vector3 normal)
            {
                _vertices.Add(position);
                _normals.Add(normal);
                _uvs.Add(uv);
                return _vertices.Count - 1;
            }

            // Unity treats clockwise triangles (seen from outside) as front faces; for those, cross(b - a, c - a)
            // points outward. Flip any triangle whose winding disagrees with the intended normal.
            private void AddTriangle(int a, int b, int c, Vector3 normal)
            {
                Vector3 cross = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
                if (Vector3.Dot(cross, normal) < 0f)
                {
                    int t = b;
                    b = c;
                    c = t;
                }

                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }
        }
    }
}
