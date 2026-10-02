using Confluence.Core.Arena;
using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Visible legal boundary (spec L306 "not an invisible rectangular wall", L331): a translucent striped wall
    /// plus a ground line tracing <see cref="BoundaryContour.RadiusAt"/>. <see cref="SetTime"/> moves both as the
    /// contour contracts between 55 s and 85 s (spec L48, DECISIONS D-026). Both meshes are rewritten in place.
    /// </summary>
    public sealed class BoundaryView : MonoBehaviour
    {
        public const float WallHeight = 6f;
        public const float WallSink = 0.5f;
        public const float LineWidth = 0.25f;
        public const float LineLift = 0.04f;

        private BoundaryContour _contour;
        private Mesh _wallMesh;
        private Mesh _lineMesh;
        private Vector3[] _wallVertices;
        private Vector2[] _wallUvs;
        private Vector3[] _lineVertices;
        private Vector2[] _lineUvs;
        private int _segments;
        private float _lastTime = float.NaN;

        public BoundaryContour Contour => _contour;

        /// <summary>Combat time last applied.</summary>
        public float TimeSec { get; private set; }

        /// <summary>Radius of the wall along a yaw at the current time (for tests and HUD).</summary>
        public float CurrentRadiusAt(float yawDeg) => _contour == null ? 0f : _contour.RadiusAt(yawDeg, TimeSec);

        public void Initialize(BoundaryContour contour, Material wallMaterial, Material lineMaterial)
        {
            _contour = contour;
            _segments = Mathf.Max(96, contour.SampleCount * 2);
            _wallMesh = CreateChild("Wall", wallMaterial, out _);
            _lineMesh = CreateChild("GroundLine", lineMaterial, out _);
            AllocateStrip(_wallMesh, out _wallVertices, out _wallUvs);
            AllocateStrip(_lineMesh, out _lineVertices, out _lineUvs);
            _lastTime = float.NaN;
            SetTime(0f);
        }

        /// <summary>Updates the contour to a combat time in seconds (no work when the radii cannot have changed).</summary>
        public void SetTime(float timeSec)
        {
            if (_contour == null)
            {
                return;
            }

            float progress = _contour.ContractionProgress(timeSec);
            TimeSec = timeSec;
            if (!float.IsNaN(_lastTime) && Mathf.Approximately(progress, _contour.ContractionProgress(_lastTime)))
            {
                return;
            }

            _lastTime = timeSec;
            float centerY = _contour.Center.Y;
            float u = 0f;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= _segments; i++)
            {
                float yaw = i * 360f / _segments;
                float radius = _contour.RadiusAt(yaw, timeSec);
                Vector3 outer = ArenaMeshes.PolarXZ(radius, yaw);
                Vector3 inner = ArenaMeshes.PolarXZ(Mathf.Max(0f, radius - LineWidth), yaw);
                if (i > 0)
                {
                    u += Vector3.Distance(previous, outer);
                }

                previous = outer;
                _wallVertices[2 * i] = new Vector3(outer.x, centerY - WallSink, outer.z);
                _wallVertices[2 * i + 1] = new Vector3(outer.x, centerY + WallHeight, outer.z);
                _wallUvs[2 * i] = new Vector2(u, 0f);
                _wallUvs[2 * i + 1] = new Vector2(u, 1f);
                _lineVertices[2 * i] = new Vector3(inner.x, centerY + LineLift, inner.z);
                _lineVertices[2 * i + 1] = new Vector3(outer.x, centerY + LineLift, outer.z);
                _lineUvs[2 * i] = new Vector2(u, 0f);
                _lineUvs[2 * i + 1] = new Vector2(u, 1f);
            }

            Apply(_wallMesh, _wallVertices, _wallUvs);
            Apply(_lineMesh, _lineVertices, _lineUvs);
        }

        private Mesh CreateChild(string childName, Material material, out MeshRenderer meshRenderer)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Boundary " + childName };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            return mesh;
        }

        private void AllocateStrip(Mesh mesh, out Vector3[] vertices, out Vector2[] uvs)
        {
            int columns = _segments + 1;
            vertices = new Vector3[columns * 2];
            uvs = new Vector2[columns * 2];
            var triangles = new int[_segments * 6];
            for (int i = 0; i < _segments; i++)
            {
                int a = 2 * i;
                int t = 6 * i;
                triangles[t] = a;
                triangles[t + 1] = a + 1;
                triangles[t + 2] = a + 3;
                triangles[t + 3] = a;
                triangles[t + 4] = a + 3;
                triangles[t + 5] = a + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
        }

        private static void Apply(Mesh mesh, Vector3[] vertices, Vector2[] uvs)
        {
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            ArenaMaterialLibrary.DestroyObject(_wallMesh);
            ArenaMaterialLibrary.DestroyObject(_lineMesh);
        }
    }
}
