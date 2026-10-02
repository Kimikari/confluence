using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Wind and steam or dust (spec L151, DECISIONS D-023).</summary>
    public class WindTests
    {
        /// <summary>A cloud of <paramref name="columns"/> cells along +X starting at cell (10, 24).</summary>
        private static ObscurationVolume Cloud(Harness h, int columns, VolumeKind kind = VolumeKind.Steam)
        {
            h.World.CreateVolume(new VolumeSpec
            {
                Kind = kind,
                Footprint = Footprint.Lane(h.CellPoint(10, 24).XZ(), h.CellPoint(10 + columns - 1, 24).XZ(), 0.5f),
                LifetimeTicks = Ticks.FromSeconds(8f),
                Owner = Harness.PlayerA,
                SourceEventId = 1,
            });
            h.Step();
            return h.OnlyVolume();
        }

        private static WindGust Gust(int strength, float seconds = 1f, float speed = 10f) => new WindGust
        {
            Direction = Vec2.UnitX,
            Strength = strength,
            DurationTicks = Ticks.FromSeconds(seconds),
            Speed = speed,
            Owner = Harness.PlayerB,
            SourceEventId = 2,
        };

        [Fact]
        public void Strength_One_Neither_Moves_Nor_Disperses_A_Cloud()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 12);
            h.World.Wind(Gust(1));
            StepLog log = h.StepSeconds(1f);
            Assert.Equal(Vec2.Zero, cloud.Offset);
            Assert.Equal(12, cloud.ColumnCount);
            Assert.Equal(0, log.Count(ReactionKind.WindDisperse));
        }

        [Fact]
        public void Strength_Two_Translates_At_Most_Three_Meters_Per_Second_And_Disperses_The_Front_Two_Meters_Once()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 12); // 6 m deep along the wind
            h.World.Wind(Gust(2, 1f, speed: 10f));  // requested 10 m/s
            StepResult first = h.Step();
            Assert.Equal(1, first.Count(ReactionKind.WindDisperse));
            Assert.Equal(8, cloud.ColumnCount); // the leading 2 m (four columns) are gone
            Assert.Equal(GlobalRules.World.WindTranslateMaxSpeed * Ticks.Dt, cloud.Offset.X, 4);

            StepLog log = h.StepFor(Ticks.FromSeconds(1f) - 1);
            Assert.Equal(0, log.Count(ReactionKind.WindDisperse)); // once per gust and volume
            Assert.Equal(8, cloud.ColumnCount);
            Assert.InRange(cloud.Offset.X, 2.99f, 3.001f);
            Assert.Equal(0f, cloud.Offset.Y, 5);

            // The gust has ended: the cloud stays put.
            h.StepFor(30);
            Assert.InRange(cloud.Offset.X, 2.99f, 3.001f);

            // The moved cloud occupies its translated columns.
            Vec3 movedTail = h.CellPoint(10, 24) + new Vec3(3f, 1f, 0f);
            Assert.True(cloud.ContainsPoint(movedTail));
            Assert.False(cloud.ContainsPoint(h.CellPoint(10, 24) + new Vec3(0f, 1f, 0f)));
        }

        [Fact]
        public void Combined_Gusts_Still_Move_A_Cloud_At_No_More_Than_Three_Meters_Per_Second()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 12);
            h.World.Wind(Gust(3, 1f, 3f));
            WindGust second = Gust(3, 1f, 3f);
            second.SourceEventId = 3;
            second.Direction = new Vec2(1f, 1f).Normalized();
            h.World.Wind(second);
            h.StepSeconds(1f);
            Assert.True(cloud.Offset.Length() <= GlobalRules.World.WindTranslateMaxSpeed + 1e-3f);
        }

        [Fact]
        public void A_Cloud_No_Deeper_Than_The_Slice_Is_Dispersed_Entirely()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 4, VolumeKind.Dust);
            h.World.Wind(Gust(2));
            StepResult r = h.Step();
            Assert.Empty(h.World.Volumes);
            Assert.Contains(r.VolumeEvents, e => e.VolumeId == cloud.Id && e.Kind == VolumeEventKind.Retired && e.Reason == VolumeRetireReason.Dispersed);
        }

        [Fact]
        public void A_Stated_Strength_One_Cloud_Mover_Obeys_Its_Distance_Bound_And_Never_Disperses()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 12);
            WindGust gust = Gust(1, 2f, 3f);
            gust.LocalCloudOverride = true; // BIO-011 / SPL-105 text moves clouds at strength 1 (D-023)
            gust.MaxDistance = 2f;
            h.World.Wind(gust);
            StepLog log = h.StepSeconds(2f);
            Assert.Equal(2f, cloud.Offset.X, 3);
            Assert.Equal(12, cloud.ColumnCount);
            Assert.Equal(0, log.Count(ReactionKind.WindDisperse));
        }

        [Fact]
        public void Wind_Ignores_Clouds_Outside_Its_Area_And_Unmovable_Volumes()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 6);
            WindGust local = Gust(2);
            local.Area = Footprint.Circle(h.CellPoint(40, 40).XZ(), 2f);
            h.World.Wind(local);
            h.StepSeconds(1f);
            Assert.Equal(Vec2.Zero, cloud.Offset);
            Assert.Equal(6, cloud.ColumnCount);

            var h2 = new Harness();
            ObscurationVolume shadow = Cloud(h2, 6, VolumeKind.ShadowConcealment);
            h2.World.Wind(Gust(3));
            h2.StepSeconds(1f);
            Assert.Equal(Vec2.Zero, shadow.Offset);
            Assert.Equal(6, shadow.ColumnCount);
        }

        [Fact]
        public void A_Corridor_Clear_Removes_Only_The_Columns_Inside_It()
        {
            var h = new Harness();
            ObscurationVolume cloud = Cloud(h, 12);
            h.World.ClearVolumes(new VolumeClear { Area = Footprint.Rectangle((h.CellPoint(15, 24).XZ() + h.CellPoint(16, 24).XZ()) * 0.5f, new Vec2(0.5f, 2f), 0f), SourceEventId = 4 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.VolumeCleared));
            Assert.Equal(10, cloud.ColumnCount); // a 1 m wide corridor removes two columns
            Assert.False(cloud.ContainsPoint(h.CellPoint(15, 24) + new Vec3(0f, 1f, 0f)));
        }
    }
}
