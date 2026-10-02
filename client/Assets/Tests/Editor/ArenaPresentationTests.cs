using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Confluence.Client.Arena;
using Confluence.Client.Content;
using Confluence.Client.Mapping;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Confluence.Client.Tests
{
    /// <summary>Shared catalogue for the presentation tests (loading takes about a second).</summary>
    internal static class TestCatalogue
    {
        private static Catalogue _catalogue;

        public static Catalogue Instance => _catalogue ?? (_catalogue = Catalogue.Load(Catalogue.FindDefaultPath()));
    }

    public sealed class CatalogueServiceTests
    {
        [Test]
        public void LoadsTheSuppliedCatalogueThroughCore()
        {
            var service = new CatalogueService();
            service.LoadNow();

            Assert.AreEqual(CatalogueLoadState.Loaded, service.State, service.Error);
            Catalogue loaded = service.Catalogue;
            Catalogue reference = TestCatalogue.Instance;
            Assert.AreEqual(reference.Wizards.Count, loaded.Wizards.Count);
            Assert.AreEqual(reference.Spells.Count, loaded.Spells.Count);
            Assert.AreEqual(reference.Props.Count, loaded.Props.Count);
            Assert.AreEqual(reference.Biomes.Count, loaded.Biomes.Count);
            Assert.IsTrue(loaded.PropsById.ContainsKey("PROP-001"));
            StringAssert.Contains("wizards", service.Describe());
        }

        [Test]
        public void StreamingAssetsCopyMatchesTheSpecification()
        {
            // tools/sync-unity.ps1 copies the catalogue unchanged; a stale copy would make the player disagree
            // with the server about content.
            string copy = Path.Combine(Application.streamingAssetsPath, CatalogueService.FileName);
            Assume.That(File.Exists(copy), "Run tools/sync-unity.ps1 first.");
            Assert.AreEqual(Sha256(Catalogue.FindDefaultPath()), Sha256(copy), "Stale catalogue copy; run tools/sync-unity.ps1.");
            Assert.AreEqual(copy, CatalogueService.ResolvePath());
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }
    }

    public sealed class CoreSpaceTests
    {
        [Test]
        public void YawMatchesCoreForward()
        {
            foreach (float yaw in new[] { 0f, 37f, 90f, -90f, 180f, 271f })
            {
                Vector3 unity = CoreSpace.YawRotation(yaw) * Vector3.forward;
                Vector3 core = CoreSpace.ToUnity(CMath.ForwardFromYaw(yaw));
                Assert.Less(Vector3.Distance(unity, core), 1e-4f, "yaw " + yaw);
            }

            // Positive yaw turns right: yaw 90 faces +X.
            Assert.Less(Vector3.Distance(CoreSpace.YawRotation(90f) * Vector3.forward, Vector3.right), 1e-5f);
        }

        [Test]
        public void PitchSignIsFlippedForUnityEulerX()
        {
            // Core pitch is elevation (positive looks up); Unity's euler X rotates positive downward.
            foreach (float pitch in new[] { -60f, -15f, 0f, 30f, 80f })
            {
                Vector3 unity = CoreSpace.ViewRotation(25f, pitch) * Vector3.forward;
                Vector3 core = CoreSpace.ToUnity(CMath.ForwardFromYawPitch(25f, pitch));
                Assert.Less(Vector3.Distance(unity, core), 1e-4f, "pitch " + pitch);
                Assert.AreEqual(-pitch, CoreSpace.UnityEulerXFromPitch(pitch));
                Assert.AreEqual(pitch, CoreSpace.PitchFromUnityEulerX(Mathf.Repeat(-pitch, 360f)), 1e-4f);
            }

            Assert.Greater((CoreSpace.ViewRotation(0f, 30f) * Vector3.forward).y, 0.49f);
        }

        [Test]
        public void PolarPointsFollowTheBoundaryContour()
        {
            BoundaryContour contour = SampleLayouts.CreateContour(Vec3.Zero, 64);
            foreach (float yaw in new[] { 0f, 45f, 133f, 270f })
            {
                Vector3 polar = ArenaMeshes.PolarXZ(contour.RadiusAt(yaw, 0f), yaw);
                Vector3 core = CoreSpace.ToUnity(contour.PointOnContour(yaw, 0f));
                Assert.Less(Vector3.Distance(polar, core), 1e-3f, "yaw " + yaw);
            }
        }
    }

    public sealed class SurfaceClassificationTests
    {
        [Test]
        public void SolidsClassifyByMaterialNameThenFlags()
        {
            Assert.AreEqual(SurfaceKind.Metal, Surfaces.ForSolid(new Solid { Material = "Metal" }));
            Assert.AreEqual(SurfaceKind.Stone, Surfaces.ForSolid(new Solid { Material = "slate" }));
            Assert.AreEqual(SurfaceKind.Glass, Surfaces.ForSolid(new Solid { Flags = MaterialFlags.Reflective }));
            Assert.AreEqual(SurfaceKind.Growth, Surfaces.ForSolid(new Solid { Flags = MaterialFlags.Growth | MaterialFlags.Combustible }));
            Assert.AreEqual(SurfaceKind.Wood, Surfaces.ForSolid(new Solid { Flags = MaterialFlags.Combustible }));
            Assert.AreEqual(SurfaceKind.Ice, Surfaces.ForSolid(new Solid { Flags = MaterialFlags.Ice }));
            Assert.AreEqual(SurfaceKind.Stone, Surfaces.ForSolid(new Solid()));
        }

        [Test]
        public void PatchesClassifyByLargestUnitCount()
        {
            Assert.AreEqual(SurfaceKind.Water, Surfaces.ForPatch(new MaterialPatch { Water = 2, Sand = 1 }));
            Assert.AreEqual(SurfaceKind.Sand, Surfaces.ForPatch(new MaterialPatch { Water = 1, Sand = 2 }));
            Assert.AreEqual(SurfaceKind.Water, Surfaces.ForPatch(new MaterialPatch { Water = 2, Sand = 2 }), "ties prefer water");
            Assert.AreEqual(SurfaceKind.Heat, Surfaces.ForPatch(new MaterialPatch { Heat = 1, Growth = 1 }));
            Assert.AreEqual(SurfaceKind.Metal, Surfaces.ForPatch(new MaterialPatch { Flags = MaterialFlags.Conductive }));
            Assert.AreEqual(3, Surfaces.MaxUnits(new MaterialPatch { Dust = 3, Frost = 1 }));
        }

        [Test]
        public void EverySurfaceHasADistinctLuminancePattern()
        {
            // Spec L371: palettes must preserve material patterns, so identity cannot rest on color. The textures
            // are grayscale; compare them pairwise after removing each one's mean brightness.
            int kinds = Surfaces.Count;
            var patterns = new float[kinds][];
            for (int k = 0; k < kinds; k++)
            {
                Texture2D texture = PatternTextures.Surface((SurfaceKind)k, 64);
                Color32[] pixels = texture.GetPixels32();
                UnityEngine.Object.DestroyImmediate(texture);
                var values = new float[pixels.Length];
                float mean = 0f;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Assert.AreEqual(pixels[i].r, pixels[i].g, "patterns are grayscale");
                    values[i] = pixels[i].r / 255f * (pixels[i].a / 255f);
                    mean += values[i];
                }

                mean /= values.Length;
                float variance = 0f;
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] -= mean;
                    variance += values[i] * values[i];
                }

                Assert.Greater(variance / values.Length, 1e-4f, (SurfaceKind)k + " has no visible pattern");
                patterns[k] = values;
            }

            for (int a = 0; a < kinds; a++)
            {
                for (int b = a + 1; b < kinds; b++)
                {
                    float difference = 0f;
                    for (int i = 0; i < patterns[a].Length; i++)
                    {
                        difference += Mathf.Abs(patterns[a][i] - patterns[b][i]);
                    }

                    Assert.Greater(difference / patterns[a].Length, 0.02f, (SurfaceKind)a + " vs " + (SurfaceKind)b);
                }
            }
        }
    }

    public sealed class SampleLayoutTests
    {
        [Test]
        public void SampleLayoutIsPointSymmetric()
        {
            ArenaLayout layout = SampleLayouts.CreatePreviewLayout(TestCatalogue.Instance);
            foreach (Solid solid in layout.Solids)
            {
                if (solid.MirrorId < 0)
                {
                    continue;
                }

                Solid twin = layout.FindSolid(solid.MirrorId);
                Assert.IsNotNull(twin, "twin of solid " + solid.Id);
                Assert.AreEqual(solid.Id, twin.MirrorId);
                YawBox rotated = solid.Box.RotatedAbout(layout.Center, 180f);
                Assert.Less(Vec3.Distance(rotated.Center, twin.Box.Center), 1e-4f, "solid " + solid.Id);
                Assert.AreEqual(solid.Material, twin.Material);
                Assert.AreEqual(solid.RampRise, twin.RampRise);
            }

            Assert.AreEqual(-layout.Spawns[0].Position.Z, layout.Spawns[1].Position.Z, 1e-5f);
            for (float yaw = 0f; yaw < 180f; yaw += 7.5f)
            {
                Assert.AreEqual(layout.Boundary.InitialRadiusAt(yaw), layout.Boundary.InitialRadiusAt(yaw + 180f), 1e-3f, "contour at " + yaw);
            }
        }

        [Test]
        public void SampleLayoutRespectsTheRulesItImitates()
        {
            ArenaLayout layout = SampleLayouts.CreatePreviewLayout(TestCatalogue.Instance);
            Assert.IsTrue(layout.Boundary.IsValid(out string reason), reason);
            Assert.AreEqual(GlobalRules.Boundary.FinalFloorRadius, layout.Boundary.RadiusAt(30f, GlobalRules.Boundary.ContractionEndSec), 1e-3f);
            Assert.LessOrEqual(layout.NaturalSourceCount(), 8, "spec L668");

            bool hasRamp = false;
            foreach (Solid solid in layout.Solids)
            {
                if (solid.IsRamp)
                {
                    hasRamp = true;
                    Assert.LessOrEqual(solid.Ramp.SlopeDeg, GlobalRules.Arena.RampMaxSlopeDeg, "spec L312");
                }
            }

            Assert.IsTrue(hasRamp);
            Assert.AreEqual(TestCatalogue.Instance.Biome(SampleLayouts.SampleBiomeId).EnvironmentRule, layout.EnvironmentRule);
            foreach (PropPlacement prop in layout.Props)
            {
                Assert.IsTrue(TestCatalogue.Instance.PropsById.ContainsKey(prop.PropId), prop.PropId);
            }
        }
    }

    public sealed class ArenaRendererTests
    {
        private ArenaRenderer _renderer;
        private ArenaLayout _layout;

        [SetUp]
        public void SetUp()
        {
            _layout = SampleLayouts.CreatePreviewLayout(TestCatalogue.Instance);
            _renderer = ArenaRenderer.Create(_layout, TestCatalogue.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            if (_renderer != null)
            {
                UnityEngine.Object.DestroyImmediate(_renderer.gameObject);
            }
        }

        [Test]
        public void BuildsOneObjectPerLayoutElement()
        {
            Assert.AreEqual(_layout.Solids.Count, _renderer.SolidCount);
            Assert.AreEqual(_layout.Materials.Count, _renderer.PatchCount);
            Assert.AreEqual(_layout.Props.Count, _renderer.PropCount);
            Assert.AreEqual(_layout.Spawns.Length, _renderer.SpawnsRoot.childCount);
            Assert.IsNotNull(_renderer.Boundary);
            Assert.IsNotNull(_renderer.MarkersRoot.Find("Final floor ring"));

            // Each prop placeholder is named after its catalogue record.
            foreach (Transform prop in _renderer.PropsRoot)
            {
                StringAssert.Contains("PROP-", prop.name);
            }
        }

        [Test]
        public void SolidsMatchTheirCoreBoxes()
        {
            foreach (Solid solid in _layout.Solids)
            {
                GameObject go = _renderer.FindSolidObject(solid.Id);
                Assert.IsNotNull(go, "solid " + solid.Id);
                Assert.Less(Vector3.Distance(go.transform.position, CoreSpace.ToUnity(solid.Box.Center)), 1e-4f);
                Vector3 forward = go.transform.rotation * Vector3.forward;
                Assert.Less(Vector3.Distance(forward, CoreSpace.ToUnity(CMath.ForwardFromYaw(solid.Box.YawDeg))), 1e-4f);

                Bounds bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                Vector3 size = CoreSpace.ToUnity(solid.Box.Size);
                Assert.Less(Vector3.Distance(bounds.size, size), 1e-3f, "solid " + solid.Id + " size");

                // Material identity comes from the surface classification (spec L371 patterns).
                Material material = go.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreSame(_renderer.Materials.Solid(Surfaces.ForSolid(solid)), material);
            }
        }

        [Test]
        public void RampMeshFollowsTheCoreSurface()
        {
            Solid ramp = SampleLayouts.SolidsOfKind(_layout, SolidKind.Ramp)[0];
            Mesh mesh = _renderer.FindSolidObject(ramp.Id).GetComponent<MeshFilter>().sharedMesh;
            Transform t = _renderer.FindSolidObject(ramp.Id).transform;
            RampShape shape = ramp.Ramp;

            // Every vertex on or below the walkable surface, and the surface reaches both edge heights.
            float low = float.MaxValue;
            float high = float.MinValue;
            foreach (Vector3 local in mesh.vertices)
            {
                Vector3 world = t.TransformPoint(local);
                Vector3 inset = Vector3.MoveTowards(world, t.position, 1e-3f);
                float surface = shape.SurfaceHeightAt(inset.x, inset.z);
                Assert.LessOrEqual(world.y, surface + 2e-3f);
                low = Mathf.Min(low, world.y);
                high = Mathf.Max(high, world.y);
            }

            Assert.AreEqual(shape.TopHeight, high, 1e-3f);
            Assert.AreEqual(shape.Bottom, low, 1e-3f);
        }

        [Test]
        public void BoundaryWallTracksTheContractingContour()
        {
            // Spec L48: the contour contracts from 55 s to the 8 m final floor at 85 s.
            MeshFilter wall = _renderer.Boundary.transform.Find("Wall").GetComponent<MeshFilter>();
            foreach (float time in new[] { 0f, 60f, 70f, GlobalRules.Boundary.ContractionEndSec })
            {
                _renderer.SetCombatTime(time);
                Vector3[] vertices = wall.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i += 14)
                {
                    Vector3 p = vertices[i]; // local to the boundary transform, which sits at the contour center
                    float yaw = CMath.YawOf(new Vec3(p.x, 0f, p.z));
                    float radius = new Vector2(p.x, p.z).magnitude;
                    Assert.AreEqual(_layout.Boundary.RadiusAt(yaw, time), radius, 2e-3f, "t=" + time + " yaw=" + yaw);
                }
            }

            Assert.AreEqual(GlobalRules.Boundary.FinalFloorRadius, _renderer.Boundary.CurrentRadiusAt(123f), 1e-3f);
            _renderer.SetCombatTime(0f);
            Assert.Greater(_renderer.Boundary.CurrentRadiusAt(123f), 14f);
        }

        [Test]
        public void FinalCenterCoverWarnsAt83AndRetiresAt85()
        {
            // Spec L678: map-owned final-center soft cover is marked at 83 s and retires at 85 s.
            Solid cover = null;
            foreach (Solid solid in _layout.Solids)
            {
                if (solid.RetiresAtFinal)
                {
                    cover = solid;
                    break;
                }
            }

            Assert.IsNotNull(cover);
            GameObject go = _renderer.FindSolidObject(cover.Id);
            SurfaceKind kind = Surfaces.ForSolid(cover);
            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();

            _renderer.SetCombatTime(82.9f);
            Assert.IsTrue(go.activeSelf);
            Assert.AreSame(_renderer.Materials.Solid(kind), meshRenderer.sharedMaterial);

            _renderer.SetCombatTime(GlobalRules.Boundary.FinalCoverWarningSec);
            Assert.IsTrue(go.activeSelf);
            Assert.AreSame(_renderer.Materials.Warning(kind), meshRenderer.sharedMaterial);

            _renderer.SetCombatTime(GlobalRules.Boundary.FinalCoverRetireSec);
            Assert.IsFalse(go.activeSelf);

            _renderer.SetCombatTime(0f);
            Assert.IsTrue(go.activeSelf, "scrubbing back restores the cover");
        }

        [Test]
        public void StandardizedOverheadLightPointsDown()
        {
            Light light = _renderer.OverheadLight;
            Assert.IsNotNull(light);
            Assert.AreEqual(LightType.Directional, light.type);
            Assert.Less((light.transform.rotation * Vector3.forward).y, -0.8f);
            Assert.AreEqual(_layout.LightShafts.Count + 1, _renderer.LightingRoot.childCount);
        }

        [Test]
        public void LegendListsTheSampleSurfaces()
        {
            var present = new List<SurfaceKind>(_renderer.SurfacesPresent);
            foreach (SurfaceKind kind in new[] { SurfaceKind.Stone, SurfaceKind.Metal, SurfaceKind.Wood, SurfaceKind.Glass, SurfaceKind.Growth, SurfaceKind.Water, SurfaceKind.Sand })
            {
                CollectionAssert.Contains(present, kind);
            }

            for (int i = 1; i < present.Count; i++)
            {
                Assert.Less(present[i - 1], present[i], "legend order is deterministic");
            }
        }

        [Test]
        public void RebuildingReleasesThePreviousPresentation()
        {
            int children = _renderer.transform.childCount;
            _renderer.Build(SampleLayouts.CreatePreviewLayout(TestCatalogue.Instance), TestCatalogue.Instance);
            Assert.AreEqual(children, _renderer.transform.childCount);
            Assert.AreEqual(_layout.Solids.Count, _renderer.SolidCount);
        }

        [Test]
        public void RendersALayoutWithoutCatalogueOrOptionalParts()
        {
            var minimal = new ArenaLayout { Diameter = 24, Center = Vec3.Zero, Boundary = BoundaryContour.Circle(Vec3.Zero, 12f) };
            minimal.Solids.Add(new Solid { Id = 7, Kind = SolidKind.Floor, Box = YawBox.OnFloor(new Vec3(0f, -0.5f, 0f), new Vec3(30f, 0.5f, 30f), 0f) });
            minimal.Props.Add(new PropPlacement { Id = 1, PropId = "PROP-UNKNOWN", Position = Vec3.Zero });
            ArenaRenderer renderer = ArenaRenderer.Create(minimal, null);
            try
            {
                Assert.AreEqual(1, renderer.SolidCount);
                Assert.AreEqual(1, renderer.PropCount);
                Assert.AreEqual(12f, renderer.Boundary.CurrentRadiusAt(10f), 1e-3f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            }
        }
    }
}
