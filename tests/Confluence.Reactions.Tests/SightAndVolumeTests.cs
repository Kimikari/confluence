using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Steam path-length sight blocking (spec L167) and volume owner bookkeeping (L128, L670).</summary>
    public class SightAndVolumeTests
    {
        /// <summary>Volume over cells x0..x1 of row z (cell (10, z) starts at world x = -7).</summary>
        private static void Cloud(Harness h, int x0, int x1, int z = 24, VolumeKind kind = VolumeKind.Steam, int owner = Harness.PlayerA, int source = 1)
        {
            h.World.CreateVolume(new VolumeSpec
            {
                Kind = kind,
                Footprint = Footprint.Lane(h.CellPoint(x0, z).XZ(), h.CellPoint(x1, z).XZ(), 0.5f),
                LifetimeTicks = Ticks.FromSeconds(3f),
                Owner = owner,
                SourceEventId = source,
            });
        }

        private static Vec3 RowPoint(Harness h, float x, int z = 24, float y = 1f) => new Vec3(x, y, h.Grid.CellCenter(0, z).Y);

        [Fact]
        public void Two_Meters_Of_Steam_Block_Acquisition_And_1_9_Meters_Do_Not()
        {
            var h = new Harness();
            Cloud(h, 10, 13); // world x -7 .. -5: exactly 2 m deep
            h.Step();

            Vec3 far = RowPoint(h, 3f);
            Assert.Equal(2f, h.World.SightPathLength(RowPoint(h, -11f), far), 4);
            Assert.True(h.World.BlocksAcquisition(RowPoint(h, -11f), far));

            Vec3 inside = RowPoint(h, -6.9f); // 0.1 m inside the cloud
            Assert.Equal(1.9f, h.World.SightPathLength(inside, far), 4);
            Assert.False(h.World.BlocksAcquisition(inside, far));

            // Above the 2 m rise, or beside the cloud, nothing is in the way.
            Assert.Equal(0f, h.World.SightPathLength(RowPoint(h, -11f, y: 2.5f), RowPoint(h, 3f, y: 2.5f)));
            Assert.Equal(0f, h.World.SightPathLength(RowPoint(h, -11f, 30), RowPoint(h, 3f, 30)));
        }

        [Fact]
        public void Path_Length_Is_Cumulative_Across_Clouds_And_Overlaps_Count_Once()
        {
            var h = new Harness();
            Cloud(h, 10, 11, source: 1); // 1 m
            Cloud(h, 16, 17, source: 2); // another 1 m, 2 m further on
            h.Step();
            Assert.Equal(2f, h.World.SightPathLength(RowPoint(h, -11f), RowPoint(h, 3f)), 4);
            Assert.True(h.World.BlocksAcquisition(RowPoint(h, -11f), RowPoint(h, 3f)));

            var h2 = new Harness();
            Cloud(h2, 10, 12, source: 1); // 1.5 m twice over the same cells
            Cloud(h2, 10, 12, 24, VolumeKind.Ash, Harness.PlayerB, 2);
            h2.Step();
            Assert.Equal(1.5f, h2.World.SightPathLength(RowPoint(h2, -11f), RowPoint(h2, 3f)), 4);
            Assert.False(h2.World.BlocksAcquisition(RowPoint(h2, -11f), RowPoint(h2, 3f)));

            // A diagonal line through a 2 x 2 m block measures its actual path.
            var h3 = new Harness();
            h3.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Dust, Footprint = Footprint.Rectangle(new Vec2(-6f, -6f), new Vec2(1f, 1f), 0f), LifetimeTicks = 60 });
            h3.Step();
            Assert.Equal(2f * System.MathF.Sqrt(2f), h3.World.SightPathLength(new Vec3(-9f, 1f, -9f), new Vec3(-3f, 1f, -3f)), 3);
        }

        [Fact]
        public void Illumination_Does_Not_Block_Sight_And_Steam_Never_Wets_Or_Harms_Bodies_Inside()
        {
            var h = new Harness();
            Cloud(h, 10, 20, 24, VolumeKind.Illumination);
            h.Step();
            Assert.Equal(0f, h.World.SightPathLength(RowPoint(h, -11f), RowPoint(h, 3f)));

            var h2 = new Harness();
            Cloud(h2, 10, 20);
            h2.Bodies.Add(ReactionBody.Player(1, RowPoint(h2, -6f, y: 0f)));
            StepLog log = h2.StepSeconds(1f);
            Assert.Empty(log.Damage);
            Assert.False(h2.World.IsWetAt(RowPoint(h2, -6f, y: 0f))); // steam deposits no water
            var inside = new List<ObscurationVolume>();
            h2.World.GetVolumesAt(RowPoint(h2, -6f), inside);
            Assert.Single(inside);
        }

        [Fact]
        public void Volume_Events_Carry_The_Owner_For_The_Three_Field_Cap()
        {
            var h = new Harness();
            Cloud(h, 10, 11, owner: Harness.PlayerA, source: 1);
            Cloud(h, 20, 21, owner: ReactionRules.MapOwner, source: 0);
            StepResult r = h.Step();
            Assert.Equal(2, r.VolumeEvents.Count);
            VolumeEvent mine = r.VolumeEvents[1];
            Assert.Equal(VolumeEventKind.Created, mine.Kind);
            Assert.Equal(Harness.PlayerA, mine.Owner);
            Assert.True(mine.CountsAsField);
            Assert.False(r.VolumeEvents[0].CountsAsField); // map-owned (source 0 sorts first) counts for nobody

            h.Step();
            Cloud(h, 30, 31, owner: Harness.PlayerA, source: 5);
            h.Step();
            var owned = new List<ObscurationVolume>();
            h.World.GetOwnedFieldVolumes(Harness.PlayerA, owned);
            Assert.Equal(2, owned.Count);
            Assert.True(owned[0].CreatedTick < owned[1].CreatedTick); // oldest first

            // Sim retires the oldest at the limit; the event arrives with the next step.
            Assert.True(h.World.RetireVolume(owned[0].Id, VolumeRetireReason.FieldLimit));
            Assert.Null(h.World.FindVolume(owned[0].Id));
            StepResult next = h.Step();
            VolumeEvent retired = Assert.Single(next.VolumeEvents);
            Assert.Equal(VolumeEventKind.Retired, retired.Kind);
            Assert.Equal(VolumeRetireReason.FieldLimit, retired.Reason);
            Assert.Equal(Harness.PlayerA, retired.Owner);
            Assert.False(h.World.RetireVolume(owned[0].Id));
        }

        [Fact]
        public void Player_Volumes_Last_At_Most_Eight_Seconds()
        {
            var h = new Harness();
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Ash, Footprint = Footprint.Circle(Vec2.Zero, 1f), LifetimeTicks = Ticks.FromSeconds(20f), Owner = Harness.PlayerA });
            h.Step();
            ObscurationVolume ash = h.OnlyVolume();
            Assert.Equal(Ticks.FromSeconds(GlobalRules.FieldsAndSummons.MaxLifetimeSec), ash.ExpiresTick - ash.CreatedTick);
        }

        [Fact]
        public void Condensing_A_Cloud_Returns_At_Most_Its_Consumed_Water_And_Consumes_It()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.Field(Material.Water, Footprint.Circle(p.XZ(), 1f), 2, 8f, Harness.PlayerB, 1);
            h.Step();
            h.Field(Material.Heat, Footprint.Circle(p.XZ(), 1f), 2, 0f, Harness.PlayerA, 2);
            h.Step();
            ObscurationVolume steam = h.OnlyVolume();
            int columns = steam.ColumnCount;
            Assert.Equal(2 * columns, steam.SourceWater); // two units steamed per cell
            Assert.Equal(0, h.World.UnitsAt(p).Water);

            h.World.Condense(new CondenseRequest { VolumeId = steam.Id, LifetimeTicks = 120, Owner = Harness.PlayerB, SourceEventId = 3 });
            StepResult r = h.Step();
            Assert.Empty(h.World.Volumes);
            Assert.Contains(r.VolumeEvents, e => e.Reason == VolumeRetireReason.Condensed);
            Assert.Equal(1, h.World.UnitsAt(p).Water); // bounded: one unit per column under the cloud
            Assert.Equal(columns, h.World.Ledger.CondensedWater);
            Assert.True(h.World.Ledger.CondensedWater <= h.World.Ledger.SteamWater);
        }
    }
}
