using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Generation;
using Confluence.Arena.Navigation;
using Confluence.Arena.Props;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Prop placement (spec L662 "three to eight prop definitions, ordinarily two instances of each", at most 48
    /// instances; every prop's placement text: "Mirrored or resource-equivalent pair; never the only route, spawn
    /// floor, or final-circle support").
    /// </summary>
    public class PropPlacementTests
    {
        [Fact]
        public void EveryCataloguePropHasAPhysicalShape()
        {
            Assert.Equal(48, ArenaFixture.Catalogue.Props.Count);
            foreach (PropDef prop in ArenaFixture.Catalogue.Props)
            {
                Assert.True(PropShapes.TryGet(prop.Id, out PropShape shape), prop.Id);
                Assert.True(shape.SizeX > 0f && shape.SizeZ > 0f, prop.Id);
            }
        }

        [Fact]
        public void PropsComeInMirroredPairsWithinTheDefinitionAndInstanceCaps()
        {
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                ArenaLayout layout = arena.Layout;
                Assert.InRange(layout.Props.Count, 2 * GlobalRules.Arena.PropDefinitionsMin, GlobalRules.Arena.MaxPropInstances);
                foreach (IGrouping<string, PropPlacement> definition in layout.Props.GroupBy(p => p.PropId))
                {
                    Assert.Equal(GlobalRules.Arena.PropInstancesPerDefinition, definition.Count());
                }

                Assert.InRange(layout.Props.Select(p => p.PropId).Distinct().Count(), GlobalRules.Arena.PropDefinitionsMin, GlobalRules.Arena.PropDefinitionsMax);
                foreach (PropPlacement p in layout.Props)
                {
                    PropPlacement twin = layout.FindProp(p.MirrorId);
                    Assert.NotNull(twin);
                    Assert.NotSame(p, twin);
                    Assert.Equal(p.PropId, twin.PropId);
                    Assert.Equal(p.Id, twin.MirrorId);
                    Assert.True(Vec3.Distance(MirrorMath.Point(p.Position), twin.Position) < 1e-3f);
                    Assert.True(System.Math.Abs(CMath.DeltaAngle(MirrorMath.Yaw(p.YawDeg), twin.YawDeg)) < 0.01f);
                }
            }
        }

        [Fact]
        public void PropsNeverStandOnASpawnFloorOrTheFinalCircle()
        {
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                ArenaLayout layout = arena.Layout;
                foreach (PropPlacement p in layout.Props)
                {
                    PropShape shape = PropShapes.For(p.PropId);
                    YawBox footprint = shape.ClearanceBoxAt(p.Position, p.YawDeg);
                    foreach (SpawnPoint spawn in layout.Spawns)
                    {
                        Assert.False(Footprint2D.Overlap(footprint, spawn.Alcove), p.PropId + " overlaps a spawn alcove");
                    }

                    Assert.True(Vec3.DistanceXZ(p.Position, layout.Center) >= layout.FinalFloorRadius + shape.PlacementRadius, p.PropId + " on the final circle");
                }
            }
        }

        /// <summary>
        /// Props are obstacles to navigation, never supports: with every prop in place both spawns keep 2.5 m routes to
        /// the final floor and to each other, so no prop is the only route, and no walkable node stands on a prop.
        /// </summary>
        [Fact]
        public void PropsAreNeverTheOnlyRouteOrASupport()
        {
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                ArenaLayout layout = arena.Layout;
                ArenaNav nav = ArenaNav.Build(layout, new ArenaNavOptions { PropsAsObstacles = true });
                var survey = new ArenaSurvey(layout, nav);
                var ids = new HashSet<int>(layout.Solids.Select(s => s.Id));
                for (int node = 0; node < nav.NodeCount; node++)
                {
                    Assert.Contains(nav.NodeSupportSolidId(node), ids);
                }

                for (int side = 0; side < 2; side++)
                {
                    bool[] route = nav.Reachable(new[] { survey.SpawnNode(side) }, NavQuery.Route);
                    Assert.Contains(survey.FinalFloorNodes, n => route[n]);
                    Assert.True(route[survey.SpawnNode(1 - side)], layout.BiomeId + " spawns lack a 2.5 m route with props in place");
                }
            }
        }

        /// <summary>
        /// Every placed prop carries its collision volume in the layout (<see cref="PropPlacement.CollisionBox"/>,
        /// read by the Sim and by navigation): the body box of its shape where the prop blocks movement, nothing for
        /// floor-level props; mirrored twins carry exact images. The validator rejects a layout whose stored box
        /// disagrees with the shape.
        /// </summary>
        [Fact]
        public void PlacedPropsCarryTheirShapeCollisionBox()
        {
            int blocking = 0;
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                foreach (PropPlacement p in arena.Layout.Props)
                {
                    PropShape shape = PropShapes.For(p.PropId);
                    Assert.Equal(shape.BlocksMovement, p.HasCollision);
                    Assert.True(PropShapes.CollisionMatchesShape(p), p.PropId);
                    if (!p.HasCollision)
                    {
                        continue;
                    }

                    blocking++;
                    YawBox body = shape.BoxAt(p.Position, p.YawDeg);
                    Assert.True(Vec3.Distance(body.Center, p.CollisionBox.Center) < 1e-4f, p.PropId);
                    Assert.True(Vec3.Distance(body.HalfExtents, p.CollisionBox.HalfExtents) < 1e-4f, p.PropId);
                    YawBox twin = arena.Layout.FindProp(p.MirrorId).CollisionBox;
                    Assert.True(Vec3.Distance(MirrorMath.Point(p.CollisionBox.Center), twin.Center) < 1e-3f, p.PropId);
                    Assert.True(System.Math.Abs(CMath.DeltaAngle(MirrorMath.Yaw(p.CollisionBox.YawDeg), twin.YawDeg)) < 0.01f, p.PropId);
                }
            }

            Assert.True(blocking > 0);

            ArenaLayout moved = ArenaFixture.BiomeSamples.Select(a => a.Layout).First(l => l.Props.Any(p => p.HasCollision)).Clone();
            PropPlacement nudged = moved.Props.First(p => p.HasCollision);
            nudged.CollisionBox = new YawBox(nudged.CollisionBox.Center + new Vec3(0.5f, 0f, 0f), nudged.CollisionBox.HalfExtents, nudged.CollisionBox.YawDeg);
            ArenaValidationReport report = ArenaFixture.Generator.Validate(moved);
            Assert.Contains(report.Failures, f => f.StartsWith("props", System.StringComparison.Ordinal) && f.Contains("collision box"));
        }

        [Fact]
        public void PropChoiceFollowsTheBiomeMaterialBudget()
        {
            // Step 3 of the pipeline (spec L318): props are drawn by the biome's material budget, so the definitions
            // placed weigh more, on average, than a uniform draw from the catalogue would.
            double placed = 0;
            double uniform = 0;
            int picks = 0;
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                BiomeDef biome = ArenaFixture.Catalogue.Biome(arena.Layout.BiomeId);
                double mean = ArenaFixture.Catalogue.Props.Average(p => ArenaContent.PropWeight(p, biome));
                foreach (string id in arena.Layout.Props.Select(p => p.PropId).Distinct())
                {
                    placed += ArenaContent.PropWeight(ArenaFixture.Catalogue.Prop(id), biome);
                    uniform += mean;
                    picks++;
                }
            }

            Assert.True(picks > 0);
            Assert.True(placed > uniform * 1.1, "placed weight " + placed + " vs uniform " + uniform);
        }
    }
}
