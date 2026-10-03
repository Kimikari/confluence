using System.Collections.Generic;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;
using UnityEngine;
using UnityEngine.Rendering;
using Material = UnityEngine.Material;

namespace Confluence.Client.Presentation
{
    /// <summary>
    /// Draws the state of a Confluence.Reactions world. Ground cells with material units (spec L136: 0.5 m cells, 0-4
    /// units) become tiles whose size grows with the unit count, colored by the cell's leading state: burning growth,
    /// ice, charge, then the material with the most units. Obscuration volumes (steam, ash, mist, dust, smoke, shadow,
    /// venom, light; spec L134, L167) become the hull of their 0.5 m columns from base to top, moved by their wind
    /// offset. The volumes are server-owned state; the drawing only illustrates them (L134). Temporary art
    /// (docs/notes/client.md): flat translucent colors, no particles.
    /// </summary>
    public sealed class ReactionViews
    {
        /// <summary>Leading state of a cell, in drawing priority order.</summary>
        public enum CellLook
        {
            Burning,
            Ice,
            Charged,
            Water,
            Heat,
            Frost,
            Dust,
            Sand,
            Growth,
            Venom,
        }

        public const float TileLift = 0.04f;

        private const int EmptyHash = 17;

        private static readonly int LookCount = System.Enum.GetValues(typeof(CellLook)).Length;

        private readonly Transform _root;
        private readonly ArenaMaterialLibrary _materials;
        private readonly ArenaLayout _layout;
        private readonly Dictionary<int, float> _surfaceY = new Dictionary<int, float>();
        private readonly Layer[] _layers;
        private readonly Dictionary<int, VolumeView> _volumes = new Dictionary<int, VolumeView>();
        private readonly List<int> _seen = new List<int>();
        private readonly List<int> _xs = new List<int>();
        private readonly List<int> _zs = new List<int>();
        private readonly List<DrawnCell> _cells = new List<DrawnCell>();
        private readonly int[] _lookCounts;
        private int _cellsHash = EmptyHash;

        public ReactionViews(Transform parent, ArenaMaterialLibrary materials, ArenaLayout layout)
        {
            _materials = materials;
            _layout = layout;
            _root = new GameObject("Reaction state").transform;
            _root.SetParent(parent, false);
            _layers = new Layer[LookCount];
            _lookCounts = new int[LookCount];
        }

        public Transform Root => _root;

        /// <summary>Cells drawn as tiles after the last <see cref="Sync"/>.</summary>
        public int CellCount { get; private set; }

        public int VolumeCount => _volumes.Count;

        /// <summary>Cells per leading state after the last <see cref="Sync"/>.</summary>
        public int CountOf(CellLook look) => _lookCounts[(int)look];

        public GameObject FindVolume(int volumeId) => _volumes.TryGetValue(volumeId, out VolumeView v) ? v.Root : null;

        public static Color ColorOf(CellLook look)
        {
            switch (look)
            {
                case CellLook.Burning: return new Color(1f, 0.32f, 0.06f, 0.85f);
                case CellLook.Ice: return new Color(0.86f, 0.96f, 1f, 0.8f);
                case CellLook.Charged: return new Color(1f, 0.96f, 0.35f, 0.85f);
                case CellLook.Water: return new Color(0.25f, 0.55f, 1f, 0.6f);
                case CellLook.Heat: return new Color(1f, 0.5f, 0.15f, 0.6f);
                case CellLook.Frost: return new Color(0.75f, 0.92f, 1f, 0.6f);
                case CellLook.Dust: return new Color(0.72f, 0.62f, 0.45f, 0.6f);
                case CellLook.Sand: return new Color(0.95f, 0.85f, 0.5f, 0.6f);
                case CellLook.Growth: return new Color(0.3f, 0.78f, 0.3f, 0.6f);
                default: return new Color(0.62f, 0.3f, 0.82f, 0.6f);
            }
        }

        public static Color ColorOf(VolumeKind kind)
        {
            switch (kind)
            {
                case VolumeKind.Steam: return new Color(0.95f, 0.95f, 0.95f, 0.35f);
                case VolumeKind.Ash: return new Color(0.24f, 0.24f, 0.25f, 0.75f);
                case VolumeKind.Mist: return new Color(0.8f, 0.9f, 1f, 0.35f);
                case VolumeKind.Dust: return new Color(0.75f, 0.65f, 0.45f, 0.55f);
                case VolumeKind.Smoke: return new Color(0.4f, 0.4f, 0.42f, 0.55f);
                case VolumeKind.ShadowConcealment: return new Color(0.08f, 0.05f, 0.12f, 0.7f);
                case VolumeKind.VenomCloud: return new Color(0.45f, 0.8f, 0.25f, 0.45f);
                case VolumeKind.Illumination: return new Color(1f, 0.95f, 0.6f, 0.18f);
                default: return new Color(1f, 1f, 0.85f, 0.25f);
            }
        }

        /// <summary>Matches the drawing to the world: rebuilds the cell tiles when any cell changed, and each volume whose columns moved.</summary>
        public void Sync(ReactionWorld world)
        {
            if (world == null)
            {
                Clear();
                return;
            }

            SyncCells(world);
            SyncVolumes(world);
        }

        public void Clear()
        {
            foreach (Layer layer in _layers)
            {
                layer?.Destroy();
            }

            System.Array.Clear(_layers, 0, _layers.Length);
            foreach (VolumeView v in _volumes.Values)
            {
                v.Destroy();
            }

            _volumes.Clear();
            System.Array.Clear(_lookCounts, 0, _lookCounts.Length);
            CellCount = 0;
            _cellsHash = EmptyHash;
        }

        // ---- cells ---------------------------------------------------------------------------------------------

        private void SyncCells(ReactionWorld world)
        {
            IReadOnlyList<int> active = world.ActiveCells;
            _cells.Clear();
            int hash = EmptyHash;
            for (int i = 0; i < active.Count; i++)
            {
                if (Classify(world, active[i], out CellLook look, out int strength))
                {
                    _cells.Add(new DrawnCell { Index = active[i], Look = look, Strength = strength });
                    hash = (hash * 31 + active[i]) * 31 + (int)look * 8 + strength;
                }
            }

            if (hash == _cellsHash)
            {
                return;
            }

            _cellsHash = hash;
            var quads = new List<Vector3>[LookCount];
            System.Array.Clear(_lookCounts, 0, _lookCounts.Length);
            CellCount = _cells.Count;
            CellGrid grid = world.Grid;
            foreach (DrawnCell cell in _cells)
            {
                int k = (int)cell.Look;
                Vec2 c = grid.CellCenter(cell.Index);
                float half = CellGrid.CellSize * 0.5f * (0.4f + 0.6f * cell.Strength / 4f);
                float y = SurfaceY(cell.Index, c) + TileLift + k * 0.002f;
                List<Vector3> list = quads[k] ?? (quads[k] = new List<Vector3>());
                list.Add(new Vector3(c.X - half, y, c.Y - half));
                list.Add(new Vector3(c.X - half, y, c.Y + half));
                list.Add(new Vector3(c.X + half, y, c.Y + half));
                list.Add(new Vector3(c.X + half, y, c.Y - half));
                _lookCounts[k]++;
            }

            for (int k = 0; k < LookCount; k++)
            {
                if (quads[k] == null)
                {
                    _layers[k]?.Destroy();
                    _layers[k] = null;
                    continue;
                }

                if (_layers[k] == null)
                {
                    _layers[k] = new Layer(_root, "Cells " + (CellLook)k, _materials.Glow(ColorOf((CellLook)k)));
                }

                _layers[k].SetQuads(quads[k]);
            }
        }

        /// <summary>The cell's leading state and its strength 1-4, or false for a cell with nothing to draw.</summary>
        public static bool Classify(ReactionWorld world, int index, out CellLook look, out int strength)
        {
            CellUnits u = world.UnitsAtCell(index);
            Vec2 c = world.Grid.CellCenter(index);
            var point = new Vec3(c.X, 0f, c.Y);
            if (world.IsBurningAt(point))
            {
                look = CellLook.Burning;
                strength = Mathf.Clamp(u.Growth, 1, 4);
                return true;
            }

            if (world.HasIceAt(point))
            {
                look = CellLook.Ice;
                strength = 4;
                return true;
            }

            if (world.IsChargedAt(point))
            {
                look = CellLook.Charged;
                strength = Mathf.Clamp(u.Water, 1, 4);
                return true;
            }

            look = CellLook.Water;
            strength = u.Water;
            Lead(u.Heat, CellLook.Heat, ref look, ref strength);
            Lead(u.Frost, CellLook.Frost, ref look, ref strength);
            Lead(u.Dust, CellLook.Dust, ref look, ref strength);
            Lead(u.Sand, CellLook.Sand, ref look, ref strength);
            Lead(u.Growth, CellLook.Growth, ref look, ref strength);
            Lead(u.Venom, CellLook.Venom, ref look, ref strength);
            strength = Mathf.Min(strength, 4);
            return strength > 0;
        }

        private static void Lead(int units, CellLook candidate, ref CellLook look, ref int strength)
        {
            if (units > strength)
            {
                look = candidate;
                strength = units;
            }
        }

        // Natural units lie on their material patch's surface; anything else is drawn on the floor (stacked surfaces
        // share one 2D cell, docs/notes/reactions.md).
        private float SurfaceY(int index, Vec2 center)
        {
            if (_surfaceY.TryGetValue(index, out float y))
            {
                return y;
            }

            y = 0f;
            if (_layout?.Materials != null)
            {
                foreach (MaterialPatch patch in _layout.Materials)
                {
                    if (patch.ContainsXZ(center))
                    {
                        y = Mathf.Max(y, patch.SurfaceY);
                    }
                }
            }

            _surfaceY[index] = y;
            return y;
        }

        // ---- volumes -------------------------------------------------------------------------------------------

        private void SyncVolumes(ReactionWorld world)
        {
            _seen.Clear();
            IReadOnlyList<ObscurationVolume> volumes = world.Volumes;
            for (int i = 0; i < volumes.Count; i++)
            {
                ObscurationVolume volume = volumes[i];
                if (volume.ColumnCount == 0)
                {
                    continue;
                }

                _seen.Add(volume.Id);
                _xs.Clear();
                _zs.Clear();
                volume.Columns.GetColumns(_xs, _zs);
                int hash = Hash(volume);
                if (!_volumes.TryGetValue(volume.Id, out VolumeView view))
                {
                    view = new VolumeView(_root, volume, _materials.Glow(ColorOf(volume.Kind)));
                    _volumes.Add(volume.Id, view);
                }
                else if (view.Hash == hash)
                {
                    continue;
                }

                view.Hash = hash;
                view.Rebuild(volume, _xs, _zs);
            }

            if (_seen.Count == _volumes.Count)
            {
                return;
            }

            var gone = new List<int>();
            foreach (int id in _volumes.Keys)
            {
                if (!_seen.Contains(id))
                {
                    gone.Add(id);
                }
            }

            foreach (int id in gone)
            {
                _volumes[id].Destroy();
                _volumes.Remove(id);
            }
        }

        private int Hash(ObscurationVolume volume)
        {
            int hash = volume.ColumnCount;
            hash = hash * 31 + volume.Offset.X.GetHashCode();
            hash = hash * 31 + volume.Offset.Y.GetHashCode();
            hash = hash * 31 + volume.BaseY.GetHashCode();
            hash = hash * 31 + volume.Height.GetHashCode();
            for (int i = 0; i < _xs.Count; i++)
            {
                hash = hash * 31 + _xs[i] * 7919 + _zs[i];
            }

            return hash;
        }

        private struct DrawnCell
        {
            public int Index;
            public CellLook Look;
            public int Strength;
        }

        // One translucent mesh of tiles for one leading state.
        private sealed class Layer
        {
            private readonly GameObject _object;
            private readonly Mesh _mesh;

            public Layer(Transform parent, string name, Material material)
            {
                _object = new GameObject(name);
                _object.transform.SetParent(parent, false);
                _mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer renderer = _object.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            public void SetQuads(List<Vector3> corners)
            {
                var triangles = new int[corners.Count / 4 * 6];
                for (int q = 0, v = 0; v < corners.Count; q += 6, v += 4)
                {
                    triangles[q] = v;
                    triangles[q + 1] = v + 1;
                    triangles[q + 2] = v + 2;
                    triangles[q + 3] = v;
                    triangles[q + 4] = v + 2;
                    triangles[q + 5] = v + 3;
                }

                _mesh.Clear();
                _mesh.SetVertices(corners);
                _mesh.triangles = triangles;
                _mesh.RecalculateBounds();
            }

            public void Destroy()
            {
                ArenaMaterialLibrary.DestroyObject(_mesh);
                ArenaMaterialLibrary.DestroyObject(_object);
            }
        }

        // The outer hull of one volume's columns: top and bottom of every column, sides only toward empty neighbours.
        private sealed class VolumeView
        {
            private readonly Mesh _mesh;

            public VolumeView(Transform parent, ObscurationVolume volume, Material material)
            {
                Root = new GameObject("Volume " + volume.Id + " " + volume.Kind);
                Root.transform.SetParent(parent, false);
                _mesh = new Mesh { name = Root.name, indexFormat = IndexFormat.UInt32 };
                Root.AddComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer renderer = Root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            public GameObject Root { get; }

            public int Hash { get; set; }

            public void Rebuild(ObscurationVolume volume, List<int> xs, List<int> zs)
            {
                var vertices = new List<Vector3>(xs.Count * 8);
                var triangles = new List<int>(xs.Count * 18);
                float h = CellGrid.CellSize * 0.5f;
                float y0 = volume.BaseY;
                float y1 = volume.TopY;
                for (int i = 0; i < xs.Count; i++)
                {
                    int x = xs[i];
                    int z = zs[i];
                    Vec2 c = volume.ColumnCenter(x, z);
                    Quad(vertices, triangles, new Vector3(c.X - h, y1, c.Y - h), new Vector3(c.X - h, y1, c.Y + h), new Vector3(c.X + h, y1, c.Y + h), new Vector3(c.X + h, y1, c.Y - h));
                    Quad(vertices, triangles, new Vector3(c.X - h, y0, c.Y - h), new Vector3(c.X + h, y0, c.Y - h), new Vector3(c.X + h, y0, c.Y + h), new Vector3(c.X - h, y0, c.Y + h));
                    if (!volume.Columns.Contains(x - 1, z))
                    {
                        Side(vertices, triangles, new Vector3(c.X - h, 0f, c.Y - h), new Vector3(c.X - h, 0f, c.Y + h), y0, y1);
                    }

                    if (!volume.Columns.Contains(x + 1, z))
                    {
                        Side(vertices, triangles, new Vector3(c.X + h, 0f, c.Y + h), new Vector3(c.X + h, 0f, c.Y - h), y0, y1);
                    }

                    if (!volume.Columns.Contains(x, z - 1))
                    {
                        Side(vertices, triangles, new Vector3(c.X + h, 0f, c.Y - h), new Vector3(c.X - h, 0f, c.Y - h), y0, y1);
                    }

                    if (!volume.Columns.Contains(x, z + 1))
                    {
                        Side(vertices, triangles, new Vector3(c.X - h, 0f, c.Y + h), new Vector3(c.X + h, 0f, c.Y + h), y0, y1);
                    }
                }

                _mesh.Clear();
                _mesh.SetVertices(vertices);
                _mesh.SetTriangles(triangles, 0);
                _mesh.RecalculateBounds();
            }

            public void Destroy()
            {
                ArenaMaterialLibrary.DestroyObject(_mesh);
                ArenaMaterialLibrary.DestroyObject(Root);
            }

            private static void Side(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float y0, float y1)
            {
                Quad(v, t, new Vector3(a.x, y0, a.z), new Vector3(a.x, y1, a.z), new Vector3(b.x, y1, b.z), new Vector3(b.x, y0, b.z));
            }

            // One winding: the feedback material culls nothing (ArenaMaterialLibrary.Glow).
            private static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                v.Add(a);
                v.Add(b);
                v.Add(c);
                v.Add(d);
                t.Add(i);
                t.Add(i + 1);
                t.Add(i + 2);
                t.Add(i);
                t.Add(i + 2);
                t.Add(i + 3);
            }
        }
    }
}
