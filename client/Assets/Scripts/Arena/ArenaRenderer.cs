using System;
using System.Collections.Generic;
using Confluence.Client.Mapping;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Builds the presentation of any Confluence.Core ArenaLayout: solids (yaw boxes and ramp wedges) with
    /// pattern materials keyed by Solid.Material and Flags, material patches as ground decals, prop
    /// placeholders with interaction icons, spawn alcove wards, the final floor ring, the boundary contour,
    /// and the standardized overhead lighting (spec L320, L329, L664). Presentation only: nothing here feeds
    /// back into collision or rules (spec L134, DECISIONS D-003). <see cref="SetCombatTime"/> drives every
    /// time-dependent visual: boundary contraction (L48) and final-center cover warning/retirement (L678).
    /// Negative times are the 5 s alcove phase (L46): the spawn ward volumes are shown only then, because the
    /// alcoves open when combat begins at 0 (L47). Spawn floor markers stay visible at all times (L329).
    /// </summary>
    public sealed class ArenaRenderer : MonoBehaviour
    {
        public const float PatchLift = 0.015f;
        public const float PatchLayerStep = 0.004f;
        public const float FinalRingWidth = 0.24f;

        private readonly Dictionary<int, GameObject> _solidObjects = new Dictionary<int, GameObject>();
        private readonly List<RetiringSolid> _retiring = new List<RetiringSolid>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<SurfaceKind> _surfaces = new List<SurfaceKind>();
        private readonly List<GameObject> _props = new List<GameObject>();
        private readonly Dictionary<int, GameObject> _propById = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PropPlacement> _propPlacements = new Dictionary<int, PropPlacement>();
        private readonly List<GameObject> _wards = new List<GameObject>();
        private readonly List<int> _dynamicIds = new List<int>();
        private readonly HashSet<int> _liveDynamic = new HashSet<int>();
        private ArenaMaterialLibrary _materials;
        private bool _ownsMaterials;

        public ArenaLayout Layout { get; private set; }

        public Catalogue Catalogue { get; private set; }

        public ArenaMaterialLibrary Materials => _materials;

        public BoundaryView Boundary { get; private set; }

        public Transform SolidsRoot { get; private set; }
        public Transform PatchesRoot { get; private set; }
        public Transform PropsRoot { get; private set; }
        public Transform SpawnsRoot { get; private set; }
        public Transform MarkersRoot { get; private set; }
        public Transform LightingRoot { get; private set; }

        public Light OverheadLight { get; private set; }

        public int SolidCount => _solidObjects.Count;

        public int PatchCount => PatchesRoot == null ? 0 : PatchesRoot.childCount;

        public int PropCount => _props.Count;

        /// <summary>Earliest time <see cref="SetCombatTime"/> accepts: the start of the alcove phase (spec L46).</summary>
        public static float AlcoveStartSec => -GlobalRules.Round.AlcoveSec;

        /// <summary>True while the clock is in the alcove phase and the ward volumes are shown.</summary>
        public bool WardsClosed => CombatTimeSec < 0f;

        public float CombatTimeSec { get; private set; }

        /// <summary>Distinct surface kinds present (solids and patches), in enum order, for the preview legend.</summary>
        public IReadOnlyList<SurfaceKind> SurfacesPresent => _surfaces;

        /// <summary>Creates a renderer GameObject and builds the layout.</summary>
        public static ArenaRenderer Create(ArenaLayout layout, Catalogue catalogue, Transform parent = null)
        {
            var go = new GameObject("Arena " + (layout?.BiomeId ?? "layout"));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            ArenaRenderer renderer = go.AddComponent<ArenaRenderer>();
            renderer.Build(layout, catalogue);
            return renderer;
        }

        /// <summary>GameObject presenting a solid id, or null.</summary>
        public GameObject FindSolidObject(int solidId) => _solidObjects.TryGetValue(solidId, out GameObject go) ? go : null;

        public void Build(ArenaLayout layout, Catalogue catalogue = null, ArenaMaterialLibrary materials = null)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Clear();
            Layout = layout;
            Catalogue = catalogue;
            _ownsMaterials = materials == null;
            _materials = materials ?? new ArenaMaterialLibrary();

            SolidsRoot = Group("Solids");
            PatchesRoot = Group("Materials");
            PropsRoot = Group("Props");
            SpawnsRoot = Group("Spawns");
            MarkersRoot = Group("Markers");
            LightingRoot = Group("Lighting");

            BuildSolids();
            BuildPatches();
            BuildProps();
            BuildSpawns();
            BuildFinalFloor();
            BuildBoundary();
            BuildLighting();
            SetCombatTime(0f);
        }

        /// <summary>
        /// Applies a combat time in seconds (clamped to <see cref="AlcoveStartSec"/>) to the spawn wards (L46-L47),
        /// the boundary (L48), and final-center cover (L678).
        /// </summary>
        public void SetCombatTime(float timeSec)
        {
            CombatTimeSec = Mathf.Max(AlcoveStartSec, timeSec);
            if (Boundary != null)
            {
                Boundary.SetTime(Mathf.Max(0f, CombatTimeSec));
            }

            bool wardsClosed = WardsClosed;
            for (int i = 0; i < _wards.Count; i++)
            {
                _wards[i].SetActive(wardsClosed);
            }

            for (int i = 0; i < _retiring.Count; i++)
            {
                RetiringSolid r = _retiring[i];
                bool retired = CombatTimeSec >= GlobalRules.Boundary.FinalCoverRetireSec;    // spec L678: 85 s
                bool warned = CombatTimeSec >= GlobalRules.Boundary.FinalCoverWarningSec;    // spec L678: 83 s
                r.Renderer.gameObject.SetActive(!retired);
                r.Renderer.sharedMaterial = warned ? _materials.Warning(r.Kind) : _materials.Solid(r.Kind);
            }
        }

        /// <summary>
        /// Mirrors a Sim collision world's dynamic solid state after <see cref="SetCombatTime"/>: layout solids the
        /// duel destroyed are hidden (durability, spec L171), and temporary solids (spell walls) are shown as boxes in
        /// their material pattern while they exist. Prop bodies are dynamic solids too, but their placeholders show them
        /// (<see cref="SyncProps"/>), so solids of <paramref name="props"/> are skipped. Returns the number of temporary
        /// solids shown.
        /// </summary>
        public int SyncSolids(Confluence.Sim.World.CollisionWorld world, Confluence.Sim.Props.PropSystem props = null)
        {
            if (world == null || SolidsRoot == null)
            {
                return 0;
            }

            _liveDynamic.Clear();
            IReadOnlyList<Confluence.Sim.World.CollisionSolid> solids = world.Solids;
            for (int i = 0; i < solids.Count; i++)
            {
                Confluence.Sim.World.CollisionSolid solid = solids[i];
                if (!solid.Dynamic)
                {
                    if (!solid.Active && _solidObjects.TryGetValue(solid.Id, out GameObject layoutObject) && layoutObject.activeSelf)
                    {
                        layoutObject.SetActive(false);
                    }

                    continue;
                }

                if (!solid.Active || (props != null && props.IsPropSolid(solid.Id)))
                {
                    continue;
                }

                _liveDynamic.Add(solid.Id);
                if (!_solidObjects.ContainsKey(solid.Id))
                {
                    SurfaceKind kind = Surfaces.FromFlags(solid.Flags, SurfaceKind.Stone);
                    Mesh mesh = Track(solid.IsRamp ? ArenaMeshes.Ramp(solid.Ramp, "Temporary ramp " + solid.Id) : ArenaMeshes.Box(solid.Box, "Temporary solid " + solid.Id));
                    CreateMeshObject("Temporary solid " + solid.Id + " " + solid.SourceId, SolidsRoot, mesh, _materials.Solid(kind), out GameObject go);
                    CoreSpace.Pose(solid.Box, out Vector3 position, out Quaternion rotation);
                    go.transform.SetPositionAndRotation(position, rotation);
                    _solidObjects[solid.Id] = go;
                    _dynamicIds.Add(solid.Id);
                }
            }

            for (int i = _dynamicIds.Count - 1; i >= 0; i--)
            {
                int id = _dynamicIds[i];
                if (_liveDynamic.Contains(id))
                {
                    continue;
                }

                if (_solidObjects.TryGetValue(id, out GameObject gone))
                {
                    ArenaMaterialLibrary.DestroyObject(gone);
                    _solidObjects.Remove(id);
                }

                _dynamicIds.RemoveAt(i);
            }

            return _dynamicIds.Count;
        }

        /// <summary>
        /// Moves each prop placeholder with its Sim body (pushed props slide, spec L155, L173) and hides destroyed props
        /// (spec L171). Returns the number of props still shown.
        /// </summary>
        public int SyncProps(Confluence.Sim.Props.PropSystem props)
        {
            if (props == null)
            {
                return _props.Count;
            }

            int shown = 0;
            IReadOnlyList<Confluence.Sim.Props.PropBody> bodies = props.Props;
            for (int i = 0; i < bodies.Count; i++)
            {
                Confluence.Sim.Props.PropBody body = bodies[i];
                if (!_propById.TryGetValue(body.Id, out GameObject go) || !_propPlacements.TryGetValue(body.Id, out PropPlacement placement))
                {
                    continue;
                }

                if (body.Destroyed)
                {
                    if (go.activeSelf)
                    {
                        go.SetActive(false);
                    }

                    continue;
                }

                shown++;
                if (body.HasCollision && placement.HasCollision)
                {
                    Confluence.Core.Geometry.Vec3 moved = placement.Position + (body.Box.Center - placement.CollisionBox.Center);
                    go.transform.position = CoreSpace.ToUnity(moved);
                }
            }

            return shown;
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                ArenaMaterialLibrary.DestroyObject(transform.GetChild(i).gameObject);
            }

            _dynamicIds.Clear();
            _solidObjects.Clear();
            _retiring.Clear();
            _surfaces.Clear();
            _props.Clear();
            _propById.Clear();
            _propPlacements.Clear();
            _wards.Clear();
            Boundary = null;
            OverheadLight = null;
            Layout = null;
            ReleaseAssets();
        }

        // Children are destroyed with this GameObject; only the meshes, materials, and textures need releasing.
        private void OnDestroy() => ReleaseAssets();

        private void ReleaseAssets()
        {
            for (int i = 0; i < _meshes.Count; i++)
            {
                ArenaMaterialLibrary.DestroyObject(_meshes[i]);
            }

            _meshes.Clear();
            if (_ownsMaterials && _materials != null)
            {
                _materials.Dispose();
            }

            _materials = null;
        }

        // ---- Solids -------------------------------------------------------------------------------------------

        private void BuildSolids()
        {
            if (Layout.Solids == null)
            {
                return;
            }

            for (int i = 0; i < Layout.Solids.Count; i++)
            {
                Solid solid = Layout.Solids[i];
                if (solid == null)
                {
                    continue;
                }

                SurfaceKind kind = Surfaces.ForSolid(solid);
                NoteSurface(kind);
                Mesh mesh = Track(solid.IsRamp ? ArenaMeshes.Ramp(solid.Ramp, "Ramp " + solid.Id) : ArenaMeshes.Box(solid.Box, "Solid " + solid.Id));
                string objectName = "Solid " + solid.Id + " " + solid.Kind + " " + (solid.Material ?? kind.ToString());
                MeshRenderer meshRenderer = CreateMeshObject(objectName, SolidsRoot, mesh, _materials.Solid(kind), out GameObject go);
                CoreSpace.Pose(solid.Box, out Vector3 position, out Quaternion rotation);
                go.transform.SetPositionAndRotation(position, rotation);
                bool translucent = Surfaces.Style(kind).Translucent;
                meshRenderer.shadowCastingMode = translucent ? ShadowCastingMode.Off : ShadowCastingMode.On;
                _solidObjects[solid.Id] = go;
                if (solid.RetiresAtFinal)
                {
                    _retiring.Add(new RetiringSolid(meshRenderer, kind));
                }
            }
        }

        // ---- Material patches ----------------------------------------------------------------------------------

        private void BuildPatches()
        {
            if (Layout.Materials == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            int baseColorId = Shader.PropertyToID("_BaseColor");
            for (int i = 0; i < Layout.Materials.Count; i++)
            {
                MaterialPatch patch = Layout.Materials[i];
                if (patch == null)
                {
                    continue;
                }

                SurfaceKind kind = Surfaces.ForPatch(patch);
                NoteSurface(kind);
                var worldCenter = new Vector2(patch.Center.X, patch.Center.Y);
                Mesh mesh = Track(patch.IsCircle
                    ? ArenaMeshes.Disc(patch.Radius, worldCenter, 48, "Patch " + i)
                    : ArenaMeshes.Rectangle(new Vector2(patch.HalfSize.X, patch.HalfSize.Y), worldCenter, patch.YawDeg, "Patch " + i));
                string objectName = "Patch " + i + " " + kind + (patch.NaturalSource ? " (natural source)" : string.Empty);
                MeshRenderer meshRenderer = CreateMeshObject(objectName, PatchesRoot, mesh, _materials.Patch(kind), out GameObject go);
                float lift = PatchLift + PatchLayerStep * (i % 4);
                go.transform.SetPositionAndRotation(
                    new Vector3(patch.Center.X, patch.SurfaceY + lift, patch.Center.Y),
                    patch.IsCircle ? Quaternion.identity : CoreSpace.YawRotation(patch.YawDeg));
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;

                // More units read as a denser decal (units 1-4, spec L136); the pattern itself never changes.
                Color tint = Surfaces.Style(kind).Tint;
                int units = Surfaces.MaxUnits(patch);
                tint.a *= units <= 0 ? 0.6f : 0.55f + 0.45f * Mathf.Clamp01(units / (float)GlobalRules.World.MaxUnitsPerCell);
                block.Clear();
                block.SetColor(baseColorId, tint);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        // ---- Props ---------------------------------------------------------------------------------------------

        private void BuildProps()
        {
            if (Layout.Props == null)
            {
                return;
            }

            for (int i = 0; i < Layout.Props.Count; i++)
            {
                PropPlacement placement = Layout.Props[i];
                if (placement == null)
                {
                    continue;
                }

                PropDef def = null;
                Catalogue?.PropsById.TryGetValue(placement.PropId ?? string.Empty, out def);
                GameObject prop = PropPlaceholder.Build(placement, def, PropsRoot, _materials);
                _props.Add(prop);
                _propById[placement.Id] = prop;
                _propPlacements[placement.Id] = placement;
            }
        }

        // ---- Spawns, final floor, markers ------------------------------------------------------------------------

        private void BuildSpawns()
        {
            if (Layout.Spawns == null)
            {
                return;
            }

            for (int i = 0; i < Layout.Spawns.Length; i++)
            {
                SpawnPoint spawn = Layout.Spawns[i];
                if (spawn == null)
                {
                    continue;
                }

                Material ward = _materials.Ward(spawn.Side);
                var root = new GameObject("Spawn " + spawn.Side).transform;
                root.SetParent(SpawnsRoot, false);

                // Ward volume the player is confined to during the 5 s alcove phase (spec L46).
                if (spawn.Alcove.HalfExtents.X > 0f && spawn.Alcove.HalfExtents.Z > 0f)
                {
                    Mesh volume = Track(ArenaMeshes.Box(spawn.Alcove, "Ward " + spawn.Side));
                    MeshRenderer wardRenderer = CreateMeshObject("Ward volume", root, volume, ward, out GameObject wardGo);
                    CoreSpace.Pose(spawn.Alcove, out Vector3 p, out Quaternion q);
                    wardGo.transform.SetPositionAndRotation(p, q);
                    wardRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _wards.Add(wardGo);
                }

                // Floor marker and facing strip at the exact spawn position.
                Vector3 feet = CoreSpace.ToUnity(spawn.Position);
                Mesh disc = Track(ArenaMeshes.Disc(0.6f, Vector2.zero, 32, "Spawn disc"));
                CreateMeshObject("Spawn marker", root, disc, ward, out GameObject marker);
                marker.transform.position = feet + Vector3.up * 0.03f;
                Mesh strip = Track(ArenaMeshes.Rectangle(new Vector2(0.12f, 0.8f), Vector2.zero, 0f, "Spawn facing"));
                CreateMeshObject("Spawn facing", root, strip, ward, out GameObject facing);
                Quaternion yaw = CoreSpace.YawRotation(spawn.YawDeg);
                facing.transform.SetPositionAndRotation(feet + Vector3.up * 0.035f + yaw * new Vector3(0f, 0f, 1.3f), yaw);
            }
        }

        private void BuildFinalFloor()
        {
            float radius = Layout.FinalFloorRadius > 0f ? Layout.FinalFloorRadius : GlobalRules.Boundary.FinalFloorRadius;
            Vector3 center = CoreSpace.ToUnity(Layout.Center);
            float floorY = ArenaQueries.SurfaceHeightAt(Layout, center.x, center.z, center.y + GlobalRules.Movement.StepHeight);
            if (float.IsNaN(floorY))
            {
                floorY = center.y;
            }

            Mesh ring = Track(ArenaMeshes.Ring(radius - FinalRingWidth * 0.5f, radius + FinalRingWidth * 0.5f, 96, "Final floor ring"));
            MeshRenderer ringRenderer = CreateMeshObject("Final floor ring", MarkersRoot, ring, _materials.FinalRing(), out GameObject go);
            go.transform.position = new Vector3(center.x, floorY + 0.02f, center.z);
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildBoundary()
        {
            if (Layout.Boundary == null)
            {
                return;
            }

            var go = new GameObject("Boundary");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(Layout.Boundary.Center.X, 0f, Layout.Boundary.Center.Z);
            Boundary = go.AddComponent<BoundaryView>();
            Boundary.Initialize(Layout.Boundary, _materials.Boundary(), _materials.BoundaryLine());
        }

        // ---- Lighting (spec L320 "standardized overhead lighting") ------------------------------------------------

        private void BuildLighting()
        {
            var sun = new GameObject("Overhead light");
            sun.transform.SetParent(LightingRoot, false);
            sun.transform.rotation = Quaternion.Euler(ArenaLighting.OverheadPitchDeg, ArenaLighting.OverheadYawDeg, 0f);
            OverheadLight = sun.AddComponent<Light>();
            ArenaLighting.ConfigureOverhead(OverheadLight);
            ArenaLighting.ApplyAmbient();

            if (Layout.LightShafts == null)
            {
                return;
            }

            for (int i = 0; i < Layout.LightShafts.Count; i++)
            {
                LightShaft shaft = Layout.LightShafts[i];
                if (shaft == null)
                {
                    continue;
                }

                var go = new GameObject("Light shaft " + i);
                go.transform.SetParent(LightingRoot, false);
                go.transform.SetPositionAndRotation(CoreSpace.ToUnity(shaft.Position) + Vector3.up * ArenaLighting.ShaftHeight, Quaternion.Euler(90f, 0f, 0f));
                ArenaLighting.ConfigureShaft(go.AddComponent<Light>(), shaft.Radius);
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private Transform Group(string groupName)
        {
            var go = new GameObject(groupName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private Mesh Track(Mesh mesh)
        {
            _meshes.Add(mesh);
            return mesh;
        }

        private void NoteSurface(SurfaceKind kind)
        {
            if (_surfaces.Contains(kind))
            {
                return;
            }

            int index = 0;
            while (index < _surfaces.Count && _surfaces[index] < kind)
            {
                index++;
            }

            _surfaces.Insert(index, kind);
        }

        internal static MeshRenderer CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material, out GameObject go)
        {
            go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            return meshRenderer;
        }

        private readonly struct RetiringSolid
        {
            public readonly MeshRenderer Renderer;
            public readonly SurfaceKind Kind;

            public RetiringSolid(MeshRenderer renderer, SurfaceKind kind)
            {
                Renderer = renderer;
                Kind = kind;
            }
        }
    }
}
