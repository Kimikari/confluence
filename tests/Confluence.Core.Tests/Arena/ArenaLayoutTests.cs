using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Arena
{
    public class ArenaLayoutTests
    {
        private static ArenaLayout Populated()
        {
            var layout = new ArenaLayout
            {
                Seed = 0xABCDEFUL,
                Round = 3,
                Diameter = 32,
                BiomeId = "BIO-001",
                EnvironmentRule = "Every 8 s a rainfall pulse adds one water unit to the gutter beds.",
                Center = new Vec3(0f, 0f, 0f),
                Boundary = BoundaryContour.Circle(Vec3.Zero, 16f),
            };

            layout.Modules.Add(new ModuleInstance { ModuleId = "MOD-001", Position = new Vec3(0f, 0f, 6f), YawDeg = 0f, Side = 0, MirrorIndex = 1 });
            layout.Modules.Add(new ModuleInstance { ModuleId = "MOD-001", Position = new Vec3(0f, 0f, -6f), YawDeg = 180f, Side = 1, MirrorIndex = 0 });
            layout.Solids.Add(new Solid
            {
                Id = 1,
                Kind = SolidKind.Cover,
                Box = YawBox.OnFloor(new Vec3(2f, 0f, 3f), new Vec3(2f, 1.2f, 0.5f), 15f),
                Material = "stone",
                Flags = MaterialFlags.Anchored,
                Durability = 80f,
                ModuleIndex = 0,
                MirrorId = 2,
            });
            layout.Solids.Add(new Solid
            {
                Id = 2,
                Kind = SolidKind.Ramp,
                Box = YawBox.OnFloor(new Vec3(-2f, 0f, -3f), new Vec3(2f, 1f, 3f), 195f),
                RampRise = 1f,
                Walkable = true,
                Anchored = true,
                MirrorId = 1,
            });
            layout.Materials.Add(new MaterialPatch { Center = new Vec2(4f, 0f), HalfSize = new Vec2(1f, 2f), Water = 3, NaturalSource = true, Flags = MaterialFlags.Water, RefillRule = "BIO-001 rainfall", MirrorId = 1 });
            layout.Materials.Add(new MaterialPatch { Center = new Vec2(-4f, 0f), Radius = 1.5f, Heat = 2 });
            layout.Props.Add(new PropPlacement { Id = 10, PropId = "PROP-001", Position = new Vec3(5f, 0f, 5f), YawDeg = 45f, MirrorId = 11 });
            layout.Spawns[0].Position = new Vec3(0f, 0f, 14f);
            layout.Spawns[0].YawDeg = 180f;
            layout.Spawns[0].Alcove = YawBox.OnFloor(new Vec3(0f, 0f, 14f), new Vec3(3f, 3f, 3f), 0f);
            layout.Spawns[1].Position = new Vec3(0f, 0f, -14f);
            layout.LightShafts.Add(new LightShaft { Position = new Vec3(3f, 6f, 0f), Radius = 1f, MirrorId = 1 });
            layout.Gusts.Add(new GustEvent { TimeSec = 12f, DurationSec = 2f, Direction = new Vec2(1f, 0f), Strength = 1, Center = Vec2.Zero, Radius = 0f });
            layout.Report.Valid = true;
            layout.Report.AttemptsUsed = 2;
            layout.Report.SetMetric("spawnPath", 24.5f);
            return layout;
        }

        [Fact]
        public void Defaults_Match_The_Contract()
        {
            var layout = new ArenaLayout();
            Assert.Null(layout.FallbackId);
            Assert.False(layout.IsFallback);
            Assert.Equal(2, layout.Spawns.Length);
            Assert.Equal(0, layout.Spawns[0].Side);
            Assert.Equal(1, layout.Spawns[1].Side);
            Assert.Equal(4f, layout.FinalFloorRadius);
            Assert.Equal(GlobalRules.Boundary.FinalFloorDiameter / 2f, layout.FinalFloorRadius);
            Assert.NotNull(layout.Boundary);
            Assert.NotNull(layout.Report);
            Assert.Empty(layout.Modules);
            Assert.Empty(layout.Solids);
            Assert.Equal(-1, new Solid().MirrorId);
            Assert.Equal(-1, new Solid().ModuleIndex);
            Assert.True(new Solid().BlocksSight);
            Assert.Equal(-1, new ModuleInstance().Side);
        }

        [Fact]
        public void Clone_Is_Deep_And_Equal()
        {
            ArenaLayout original = Populated();
            ArenaLayout copy = original.Clone();

            Assert.Equal(original.Seed, copy.Seed);
            Assert.Equal(original.BiomeId, copy.BiomeId);
            Assert.Equal(original.Solids.Count, copy.Solids.Count);
            Assert.Equal(original.Solids[0].Box.Center, copy.Solids[0].Box.Center);
            Assert.Equal(original.Spawns[0].Alcove.HalfExtents, copy.Spawns[0].Alcove.HalfExtents);
            Assert.Equal(24.5f, copy.Report.Metrics["spawnPath"]);

            Assert.NotSame(original.Modules, copy.Modules);
            Assert.NotSame(original.Solids[0], copy.Solids[0]);
            Assert.NotSame(original.Materials[0], copy.Materials[0]);
            Assert.NotSame(original.Props[0], copy.Props[0]);
            Assert.NotSame(original.Spawns, copy.Spawns);
            Assert.NotSame(original.Spawns[0], copy.Spawns[0]);
            Assert.NotSame(original.Boundary, copy.Boundary);
            Assert.NotSame(original.Boundary.InitialRadii, copy.Boundary.InitialRadii);
            Assert.NotSame(original.LightShafts[0], copy.LightShafts[0]);
            Assert.NotSame(original.Gusts[0], copy.Gusts[0]);
            Assert.NotSame(original.Report.Failures, copy.Report.Failures);
            Assert.NotSame(original.Report.Metrics, copy.Report.Metrics);

            // Destruction or edits in one pair's copy never leak into another (spec L34).
            copy.Solids[0].Durability = 0f;
            copy.Solids.RemoveAt(1);
            copy.Materials[0].Water = 0;
            copy.Props[0].Position = Vec3.Zero;
            copy.Spawns[0].YawDeg = 0f;
            copy.Boundary.InitialRadii[0] = 5f;
            copy.Report.AddFailure("edited");
            copy.Modules[0].ModuleId = "MOD-002";

            Assert.Equal(80f, original.Solids[0].Durability);
            Assert.Equal(2, original.Solids.Count);
            Assert.Equal(3, original.Materials[0].Water);
            Assert.Equal(new Vec3(5f, 0f, 5f), original.Props[0].Position);
            Assert.Equal(180f, original.Spawns[0].YawDeg);
            Assert.Equal(16f, original.Boundary.InitialRadii[0]);
            Assert.True(original.Report.Valid);
            Assert.Empty(original.Report.Failures);
            Assert.False(copy.Report.Valid);
            Assert.Equal("MOD-001", original.Modules[0].ModuleId);
        }

        [Fact]
        public void Lookups_And_Counts()
        {
            ArenaLayout layout = Populated();
            Assert.Same(layout.Solids[1], layout.FindSolid(2));
            Assert.Null(layout.FindSolid(99));
            Assert.Same(layout.Props[0], layout.FindProp(10));
            Assert.Null(layout.FindProp(11));
            Assert.Equal(1, layout.NaturalSourceCount());
            Assert.True(layout.Report.TryGetMetric("spawnPath", out float path));
            Assert.Equal(24.5f, path);
        }

        [Fact]
        public void Solid_Ramp_And_Destructibility()
        {
            ArenaLayout layout = Populated();
            Solid cover = layout.Solids[0];
            Solid ramp = layout.Solids[1];
            Assert.True(cover.IsDestructible);
            Assert.False(cover.IsRamp);
            Assert.True(ramp.IsRamp);
            Assert.False(ramp.IsDestructible);
            Assert.Equal(1f, ramp.Ramp.Rise);
            Assert.Equal(ramp.Box.Top, ramp.Ramp.TopHeight);
            Assert.True(ramp.Ramp.SlopeDeg < GlobalRules.Arena.RampMaxSlopeDeg);
        }

        [Fact]
        public void Material_Patch_Containment()
        {
            var rect = new MaterialPatch { Center = new Vec2(4f, 0f), HalfSize = new Vec2(1f, 2f), YawDeg = 90f };
            // Rotated 90 degrees the 2 x 4 patch spans 4 m along world X and 2 m along Z.
            Assert.True(rect.ContainsXZ(new Vec2(5.9f, 0.9f)));
            Assert.False(rect.ContainsXZ(new Vec2(4f, 1.5f)));
            Assert.Equal(8f, rect.Area);

            var circle = new MaterialPatch { Center = new Vec2(-4f, 0f), Radius = 1.5f };
            Assert.True(circle.IsCircle);
            Assert.True(circle.ContainsXZ(new Vec2(-4f, 1.4f)));
            Assert.False(circle.ContainsXZ(new Vec2(-4f, 1.6f)));
            Assert.Equal(MathF.PI * 2.25f, circle.Area, 4);
        }

        [Fact]
        public void Material_Patch_Records_Sand_And_Growth_Units()
        {
            // BIO-007: "Two 2 m beds start with Sand 2 Water 1"; BIO-002: "root beds start with Heat 1 and Growth 1" (D-025).
            var silt = new MaterialPatch { Radius = 1f, Sand = 2, Water = 1, Flags = MaterialFlags.Sand | MaterialFlags.Water, NaturalSource = true };
            var orchard = new MaterialPatch { Radius = 1f, Heat = 1, Growth = 1, Flags = MaterialFlags.Growth | MaterialFlags.Combustible, NaturalSource = true };
            Assert.True(silt.HasValidUnits);
            Assert.True(orchard.HasValidUnits);

            MaterialPatch copy = silt.Clone();
            copy.Sand = 0;
            Assert.Equal(2, silt.Sand);

            Assert.False(new MaterialPatch { Growth = GlobalRules.World.MaxUnitsPerCell + 1 }.HasValidUnits);
            Assert.False(new MaterialPatch { Sand = -1 }.HasValidUnits);
            Assert.True(new MaterialPatch { Water = 4, Heat = 4, Frost = 4, Dust = 4, Sand = 4, Growth = 4 }.HasValidUnits);
        }

        [Fact]
        public void Report_Failure_Marks_Invalid()
        {
            var report = new ArenaValidationReport { Valid = true };
            report.AddFailure("spawn path 40 m exceeds 34 m");
            Assert.False(report.Valid);
            Assert.Single(report.Failures);
            ArenaValidationReport copy = report.Clone();
            copy.Failures.Clear();
            Assert.Single(report.Failures);
        }
    }
}
