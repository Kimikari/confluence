using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Tests
{
    /// <summary>Frost and water (spec L145), heat and ice (L146), force and brittle ice (L147).</summary>
    public class IceTests
    {
        private static Harness WaterCell(out Vec3 p, int water = 1)
        {
            var h = new Harness();
            p = h.CellPoint(24, 24);
            h.Field(Material.Water, Footprint.Point(p.XZ()), water, 8f, Harness.PlayerB, 1);
            h.Step();
            return h;
        }

        [Fact]
        public void Two_Frost_Consume_One_Water_And_Form_A_Five_Second_Tile_With_45_Durability()
        {
            Harness h = WaterCell(out Vec3 p, water: 2);
            h.Point(Material.Frost, p, 2, 0, Harness.PlayerA, 2);
            StepResult r = h.Step();

            Assert.Equal(1, r.Count(ReactionKind.IceFormed));
            Assert.True(h.World.HasIceAt(p));
            Assert.Equal(GlobalRules.World.IceTileDurability, h.World.IceDurabilityAt(p));
            CellUnits u = h.World.UnitsAt(p);
            Assert.Equal(1, u.Water); // one of the two water units froze
            Assert.Equal(0, u.Frost);
            Assert.Equal(2, h.World.Ledger.Consumed(Material.Frost));
            Assert.Equal(1, h.World.Ledger.Consumed(Material.Water));
            Assert.False(h.World.IsWetAt(p)); // frozen water is not liquid
            Assert.Equal(SurfaceKind.Ice, h.World.SurfaceAt(p).Kind);

            // The tile lasts 5 s from formation (tick 1), then vanishes.
            StepLog log = h.StepFor(Ticks.FromSeconds(GlobalRules.World.IceLifetimeSec) - 1);
            Assert.True(h.World.HasIceAt(p));
            log.Add(h.Step());
            Assert.False(h.World.HasIceAt(p));
            Assert.Equal(1, log.Count(ReactionKind.IceExpired));
        }

        [Fact]
        public void One_Frost_Unit_Is_Not_Enough_And_A_Tile_Forms_Only_Once()
        {
            Harness h = WaterCell(out Vec3 p);
            h.Point(Material.Frost, p, 1, 30, Harness.PlayerA, 2);
            h.Step();
            Assert.False(h.World.HasIceAt(p));
            Assert.Equal(1, h.World.UnitsAt(p).Frost);

            // Four frost on two water: one tile; the leftover frost does not form a second tile on the same cell.
            Harness h2 = WaterCell(out Vec3 q, water: 2);
            h2.Point(Material.Frost, q, 4, 30, Harness.PlayerA, 2);
            StepResult r = h2.Step();
            Assert.Equal(1, r.Count(ReactionKind.IceFormed));
            Assert.Equal(2, h2.World.UnitsAt(q).Frost);
            Assert.Equal(1, h2.World.UnitsAt(q).Water);
        }

        [Fact]
        public void Two_Heat_Melt_A_Tile_Into_One_Water_Unit()
        {
            Harness h = WaterCell(out Vec3 p);
            h.Point(Material.Frost, p, 2, 0, Harness.PlayerA, 2);
            h.Step();
            Assert.Equal(0, h.World.UnitsAt(p).Water);

            h.Point(Material.Heat, p, 1, 30, Harness.PlayerB, 3);
            h.Step();
            Assert.True(h.World.HasIceAt(p)); // one heat unit is not enough
            Assert.Empty(h.World.Volumes);    // and makes no steam on ice

            h.Point(Material.Heat, p, 1, 0, Harness.PlayerB, 4);
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.IceMelted));
            Assert.False(h.World.HasIceAt(p));
            Assert.Equal(GlobalRules.World.MeltWaterProduced, h.World.UnitsAt(p).Water);
            Assert.Equal(0, h.World.UnitsAt(p).Heat);
            Assert.Empty(h.World.Volumes);
            Assert.Equal(1, h.World.Ledger.Produced(Material.Water));
        }

        [Fact]
        public void Further_Heat_After_A_Melt_Makes_Steam()
        {
            Harness h = WaterCell(out Vec3 p);
            h.Point(Material.Frost, p, 2, 0, Harness.PlayerA, 2);
            h.Step();
            h.Point(Material.Heat, p, 3, 0, Harness.PlayerB, 3);
            StepResult r = h.Step();

            Assert.Equal(1, r.Count(ReactionKind.IceMelted));
            Assert.Equal(1, r.Count(ReactionKind.Steam));
            Assert.Equal(0, h.World.UnitsAt(p).Water); // the melted unit became steam
            Assert.Equal(1, h.OnlyVolume().SourceWater);
        }

        [Fact]
        public void Weak_Hits_Wear_Durability_Without_Shards()
        {
            Harness h = WaterCell(out Vec3 p);
            h.Point(Material.Frost, p, 2, 0, Harness.PlayerA, 2);
            h.Step();
            h.Bodies.Add(ReactionBody.Player(1, p + new Vec3(0.5f, 0f, 0f)));

            float dealt = 0f;
            for (int i = 0; i < 2; i++)
            {
                h.World.Impact(new DirectImpact { Point = p, Direction = Vec3.Right, DirectDamage = 19f, Owner = Harness.PlayerA, SourceEventId = 10 + i });
                dealt += 19f;
                StepResult r = h.Step();
                Assert.Equal(1, r.Count(ReactionKind.IceWorn));
                Assert.Equal(GlobalRules.World.IceTileDurability - dealt, h.World.IceDurabilityAt(p), 3);
            }

            h.World.Impact(new DirectImpact { Point = p, Direction = Vec3.Right, DirectDamage = 19f, Owner = Harness.PlayerA, SourceEventId = 20 });
            StepResult last = h.Step();
            Assert.False(h.World.HasIceAt(p)); // 57 >= 45: worn through
            Assert.Equal(1, last.Count(ReactionKind.IceShattered));
            Assert.Equal(0, last.Count(ReactionKind.ShardCone));
            Assert.Empty(last.Damage);
        }

        [Fact]
        public void A_20_Damage_Impact_Breaks_One_Tile_And_Emits_One_Shard_Cone()
        {
            var h = new Harness();
            Vec3 a = h.CellPoint(24, 24);
            Vec3 b = h.CellPoint(25, 24);
            h.Field(Material.Water, Footprint.Lane(a.XZ(), b.XZ(), 0.5f), 1, 8f, Harness.PlayerB, 1);
            h.Step();
            h.Field(Material.Frost, Footprint.Lane(a.XZ(), b.XZ(), 0.5f), 2, 0f, Harness.PlayerB, 2);
            h.Step();
            Assert.True(h.World.HasIceAt(a) && h.World.HasIceAt(b));

            h.Bodies.Add(ReactionBody.Player(1, a + new Vec3(0.9f, 0f, 0f)));  // in the cone ahead
            h.Bodies.Add(ReactionBody.Player(2, a + new Vec3(-1.0f, 0f, 0f))); // behind the impact
            h.Bodies.Add(ReactionBody.Player(3, a + new Vec3(2.0f, 0f, 0f)));  // beyond 1 m (plus radius)
            h.World.Impact(new DirectImpact { Point = a, Direction = Vec3.Right, DirectDamage = 20f, Owner = Harness.PlayerA, SourceEventId = 30 });
            StepResult r = h.Step();

            Assert.False(h.World.HasIceAt(a));
            Assert.True(h.World.HasIceAt(b)); // only the struck tile breaks
            Assert.Equal(1, r.Count(ReactionKind.ShardCone));
            ContactDamage hit = Assert.Single(r.Damage);
            Assert.Equal(1, hit.BodyId);
            Assert.Equal(GlobalRules.World.ShardDamage, hit.Amount);
            Assert.Equal(DamageChannel.Physical, hit.Channel);
            Assert.Equal(Harness.PlayerA, hit.Owner);
            Assert.Equal(30, hit.SourceEventId);
            Assert.True(hit.IsSecondary);

            // The cone fires once per impact: nothing more on later steps.
            Assert.Empty(h.StepFor(10).Damage);
        }

        [Fact]
        public void Brittle_Cover_Solids_Shatter_Only_On_20_Damage()
        {
            var layout = TestArenas.Empty();
            layout.Solids.Add(new Solid
            {
                Id = 5,
                Kind = SolidKind.Cover,
                Box = YawBox.OnFloor(new Vec3(0f, 0f, 0f), new Vec3(1f, 2f, 0.5f), 0f),
                Flags = MaterialFlags.Brittle,
                Durability = 45f,
            });
            var h = new Harness(new ReactionWorld(layout));
            h.World.Impact(new DirectImpact { Point = new Vec3(0f, 1f, -0.25f), Direction = Vec3.Forward, DirectDamage = 15f, SolidId = 5, SourceEventId = 1 });
            Assert.Equal(0, h.Step().Count(ReactionKind.IceShattered));
            h.World.Impact(new DirectImpact { Point = new Vec3(0f, 1f, -0.25f), Direction = Vec3.Forward, DirectDamage = 25f, SolidId = 5, SourceEventId = 2 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.IceShattered));
            Assert.Equal(1, r.Count(ReactionKind.ShardCone));
        }
    }
}
