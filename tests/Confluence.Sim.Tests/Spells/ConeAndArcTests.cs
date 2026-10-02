using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Cones (angle, reach, per-target cover, footprint deposits) and spell melee arcs (swept volume and width).</summary>
    public class ConeAndArcTests
    {
        private static SpellForm Cone(float halfAngle = 45f, float reach = 4f, bool cover = true, DepositKind? deposit = null)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T20", 10f, 0.5f, 0f, reach);
            b.Delivery = new ConeDelivery { HalfAngleDeg = halfAngle, Reach = reach, CoverPerTarget = cover };
            b.Payload.Direct = 20f;
            if (deposit.HasValue) b.Payload.AddDeposit(deposit.Value, 1, DepositPlacement.Footprint);
            return b.Build();
        }

        private static float ConeHit(Vec3 targetFeet, ArenaLayout layout = null, SpellForm form = null)
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form ?? Cone()), layout: layout);
            TestWorlds.Place(w, 1, targetFeet, 180f);
            SpellTestKit.Cast(w);
            return SpellTestKit.HealthLost(w, 1);
        }

        [Fact]
        public void A_Cone_Strikes_Bodies_Inside_Its_Angle_And_Reach_Only()
        {
            Assert.Equal(20f, ConeHit(new Vec3(0f, 0f, -3f)), 3);                       // straight ahead, 3 m
            Assert.Equal(20f, ConeHit(new Vec3(2f, 0f, -4f)), 3);                       // 45 degrees off, 2.8 m
            Assert.Equal(0f, ConeHit(new Vec3(3f, 0f, -5f)));                           // ~72 degrees off
            Assert.Equal(0f, ConeHit(new Vec3(0f, 0f, -1.4f)));                         // 4.6 m away, reach 4 m + body
            Assert.Equal(20f, ConeHit(new Vec3(0f, 0f, -1.7f)), 3);                     // body edge inside the reach
            Assert.Equal(0f, ConeHit(new Vec3(0f, 0f, -8f)));                           // behind
        }

        [Fact]
        public void Each_Target_Checks_Cover_Separately()
        {
            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(80, new Vec3(0f, 0f, -4.5f), new Vec3(1f, 2.5f, 0.2f)));
            Assert.Equal(0f, ConeHit(new Vec3(0f, 0f, -3f), wall));                     // full cover between
            Assert.Equal(20f, ConeHit(new Vec3(1.5f, 0f, -3f), wall), 3);               // beside the same wall
            ArenaLayout low = SpellTestKit.FlatWith(SpellTestKit.Wall(81, new Vec3(0f, 0f, -4.5f), new Vec3(1f, 0.8f, 0.2f)));
            Assert.Equal(20f, ConeHit(new Vec3(0f, 0f, -3f), low), 3);                  // a low wall leaves the chest exposed
            Assert.Equal(20f, ConeHit(new Vec3(0f, 0f, -3f), wall, Cone(cover: false)), 3);
        }

        [Fact]
        public void A_Cone_Deposits_Over_Its_Ground_Footprint()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Cone(deposit: DepositKind.Heat)), materials: materials);
            SpellTestKit.Cast(w);
            WorldDeposit d = materials.Deposits.Single();
            Assert.Equal(FootprintShape.Cone, d.Footprint.Shape);
            Assert.Equal(4f, d.Footprint.Radius, 3);
            Assert.Equal(45f, d.Footprint.HalfAngleDeg, 3);
            Assert.True(d.Footprint.Contains(new Vec2(0f, -3f)));
            Assert.False(d.Footprint.Contains(new Vec2(0f, -9f)));
        }

        private static float ArcHit(Vec3 targetFeet, float arcHalf, float width, ArenaLayout layout = null)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T21", 10f, 0.5f, 0f, 2f);
            b.Delivery = new ArcDelivery { ArcHalfDeg = arcHalf, Reach = 2f, Width = width };
            b.Payload.Direct = 15f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()), layout: layout);
            TestWorlds.Place(w, 1, targetFeet, 180f);
            SpellTestKit.Cast(w);
            return SpellTestKit.HealthLost(w, 1);
        }

        [Fact]
        public void A_Thrust_Hits_Within_Its_Width_And_A_Slash_Sweeps_Its_Arc()
        {
            Assert.Equal(15f, ArcHit(new Vec3(0f, 0f, -4.5f), 0f, 0.5f), 3);
            Assert.Equal(0f, ArcHit(new Vec3(0.8f, 0f, -4.5f), 0f, 0.5f));             // 0.8 m aside: 0.4 + 0.25 short
            Assert.Equal(15f, ArcHit(new Vec3(0.8f, 0f, -4.5f), 0f, 1.0f), 3);          // "Wide 1 m thrust" reaches it
            Assert.Equal(15f, ArcHit(new Vec3(1.4f, 0f, -5.2f), 90f, 0.5f), 3);         // beside the caster, inside a 180-degree slash
            Assert.Equal(0f, ArcHit(new Vec3(0f, 0f, -3f), 0f, 0.5f));                  // beyond the 2 m reach
        }

        [Fact]
        public void A_Strike_Cannot_Pass_Solid_Geometry_And_Damages_Each_Target_Once()
        {
            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(82, new Vec3(0f, 0f, -5f), new Vec3(3f, 2.5f, 0.2f)));
            Assert.Equal(0f, ArcHit(new Vec3(0f, 0f, -4.4f), 60f, 0.5f, wall));
            Assert.Equal(15f, ArcHit(new Vec3(0f, 0f, -4.5f), 60f, 0.5f), 3);           // nine samples cross the body, one hit
        }
    }
}
