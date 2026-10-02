using System;
using Confluence.Arena.Generation;
using Confluence.Arena.Navigation;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    public class LaneAndBoundaryTests
    {
        private static ArenaLayout Disc(float radius, params YawBox[] walls)
        {
            var layout = new ArenaLayout { Diameter = (int)(radius * 2f), Boundary = BoundaryContour.Circle(Vec3.Zero, radius) };
            layout.Solids.Add(new Solid
            {
                Id = 0,
                Kind = SolidKind.Floor,
                Box = new YawBox(new Vec3(0f, -1f, 0f), new Vec3(radius + 2f, 1f, radius + 2f), 0f),
                Anchored = true,
                Walkable = true,
            });
            for (int i = 0; i < walls.Length; i++)
            {
                layout.Solids.Add(new Solid { Id = i + 1, Kind = SolidKind.Wall, Box = walls[i], Anchored = true });
            }

            return layout;
        }

        [Fact]
        public void LongestLaneInAnOpenDiscIsItsDiameter()
        {
            ArenaLayout layout = Disc(10f);
            var meter = new LaneMeter(ArenaNav.Build(layout), layout);
            Assert.InRange(meter.Longest().Length, 19f, 20f);
            Assert.InRange(meter.Designated().Length, 19f, 20f);
        }

        [Fact]
        public void TallWallsInterruptLanesButLowCoverDoesNot()
        {
            var wall = new YawBox(new Vec3(0f, 1.5f, 0f), new Vec3(0.5f, 1.5f, 12f), 0f); // splits the disc east/west
            ArenaLayout layout = Disc(10f, wall);
            var meter = new LaneMeter(ArenaNav.Build(layout), layout);
            Assert.True(meter.Designated().IsEmpty || meter.Designated().Length < 0.5f); // the center is inside the wall

            var low = new YawBox(new Vec3(0f, 0.5f, 0f), new Vec3(0.5f, 0.5f, 12f), 0f);
            ArenaLayout lowLayout = Disc(10f, low);
            var lowMeter = new LaneMeter(ArenaNav.Build(lowLayout), lowLayout);
            Assert.InRange(lowMeter.Longest().Length, 19f, 20f); // 1 m cover does not block the 1.2-1.6 m band
        }

        [Fact]
        public void LaneSegmentsAreInterruptedByCrossingBoxes()
        {
            var lane = new FiringLane { A = new Vec2(-5f, 0f), B = new Vec2(5f, 0f) };
            Assert.True(LaneMeter.Interrupts(lane, new YawBox(new Vec3(0f, 1f, 0f), new Vec3(0.3f, 1f, 1f), 0f)));
            Assert.False(LaneMeter.Interrupts(lane, new YawBox(new Vec3(0f, 1f, 3f), new Vec3(0.3f, 1f, 1f), 0f)));
            Assert.False(LaneMeter.Interrupts(lane, new YawBox(new Vec3(7f, 1f, 0f), new Vec3(0.3f, 1f, 1f), 0f)));
        }

        [Theory]
        [InlineData(24)]
        [InlineData(32)]
        [InlineData(40)]
        [InlineData(48)]
        public void ContourIsIrregularHalfTurnSymmetricAndHitsTheTargetArea(int diameter)
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                SizeProfile profile = SizeProfile.For(diameter);
                BoundaryContour c = BoundaryBuilder.Build(new Rng(seed), profile);
                Assert.True(c.IsValid(out string reason), reason);
                int n = c.SampleCount;
                for (int i = 0; i < n / 2; i++)
                {
                    Assert.Equal(c.InitialRadii[i], c.InitialRadii[i + n / 2]);
                }

                Assert.InRange(c.AreaAt(0f), profile.TargetArea * 0.999f, profile.TargetArea * 1.001f);
                Assert.True(c.MaxRadiusAt(0f) - c.MinRadiusAt(0f) > 0.01f * profile.Radius); // irregular, not a circle
                Assert.True(c.MaxRadiusAt(0f) <= profile.Radius * (1f + 2f * ArenaRules.ContourIrregularity));
                Assert.Equal(GlobalRules.Boundary.FinalFloorRadius, c.RadiusAt(30f, GlobalRules.Boundary.ContractionEndSec), 4);
            }
        }

        [Fact]
        public void FloorSlabsCoverEverythingButTheCutouts()
        {
            var cut = new Rect2(-2f, -6f, 2f, -2f);
            var pieces = FloorSlabs.Pieces(10f, new[] { cut, cut.Mirrored() });
            float area = 0f;
            foreach (Rect2 r in pieces)
            {
                area += r.Width * r.Depth;
                Assert.False(r.Overlaps(cut));
                Assert.False(r.Overlaps(cut.Mirrored()));
            }

            Assert.Equal(400f - 32f, area, 2);
        }

        [Fact]
        public void SpawnPathBandsGrowWithSizeInsideTheGlobalRange()
        {
            float lastMin = 0f;
            float lastMax = 0f;
            foreach (int d in ArenaFixture.Diameters)
            {
                ArenaRules.SpawnPathBand(d, out float min, out float max);
                Assert.True(min >= GlobalRules.Arena.SpawnPathMin && max <= GlobalRules.Arena.SpawnPathMax);
                Assert.True(min > lastMin && max > lastMax);
                lastMin = min;
                lastMax = max;
            }
        }

        [Fact]
        public void FootprintDistanceAndOverlapAreExactForRotatedBoxes()
        {
            var a = new YawBox(new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f), 0f);
            var b = new YawBox(new Vec3(3f, 0f, 0f), new Vec3(1f, 1f, 1f), 0f);
            Assert.Equal(1f, Footprint2D.Distance(a, b), 4);
            var c = new YawBox(new Vec3(1.8f, 0f, 0f), new Vec3(1f, 1f, 0.2f), 45f);
            Assert.True(Footprint2D.Overlap(a, c));
            Assert.Equal(0f, Footprint2D.Distance(a, c));
            var d = new YawBox(new Vec3(0f, 0f, 3f), new Vec3(1f, 1f, 1f), 45f);
            Assert.Equal(3f - 1f - MathF.Sqrt(2f), Footprint2D.Distance(a, d), 3);
        }
    }
}
