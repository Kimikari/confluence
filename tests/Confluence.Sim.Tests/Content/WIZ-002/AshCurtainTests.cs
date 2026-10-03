using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>SPL-012 Ash Curtain and its transformations AUG-311 Ash Corridor and AUG-312 Ash Bellows.</summary>
    public class AshCurtainTests
    {
        private const string Ash = "SPL-012";

        private static ObscurationVolume Cloud(DuelWorld w) => BramKit.Reactions(w).Volumes.SingleOrDefault(v => v.Kind == VolumeKind.Ash);

        private static ObscurationVolume CastAt(DuelWorld w, Vec3 ground, int hold = BramKit.NormalHold)
        {
            PlayerInput aim = BramKit.AimAt(w, ground, BramKit.SlotOf(w, Ash));
            BramKit.Cast(w, Ash, hold, aim);
            BramKit.RunUntil(w, () => Cloud(w) != null, 60, aim);
            BramKit.Run(w, 2, aim);
            return Cloud(w);
        }

        private static Vec2 Center(ObscurationVolume v)
        {
            v.GetBounds(out Vec2 min, out Vec2 max);
            return (min + max) * 0.5f;
        }

        [Fact]
        [Verifies("SPL-012")]
        public void It_Creates_A_3_Meter_Opaque_Ash_Cloud_For_6_Seconds_At_The_Aimed_Ground_Point()
        {
            DuelWorld w = BramKit.Duel();
            ObscurationVolume cloud = CastAt(w, Vec3.Zero);
            Assert.NotNull(cloud);
            Assert.Equal(0, cloud.Owner);
            Assert.True(cloud.BlocksSight);
            Assert.True(cloud.WindMovable);
            Assert.Equal(0f, cloud.DamagePerSecond);
            Assert.Equal(Ticks.FromSeconds(6.0), cloud.ExpiresTick - cloud.CreatedTick);
            cloud.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 5.5f, 6.5f); // "3 m-radius"
            Assert.InRange(Center(cloud).Length(), 0f, 0.3f);
            Assert.Contains(w.Owned.Entities, e => e.Owner == 0 && e.DefinitionId == "CLOUD:Ash"); // one of the caster's fields
            Assert.Equal(100f - 20f, BramKit.Mana(w), 3);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks(Ash) > Ticks.FromSeconds(10.0)); // 11 s cooldown

            BramKit.RunUntil(w, () => Cloud(w) == null, 400);
            Assert.Null(Cloud(w));
            BramKit.Run(w, 2);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.FieldEnded), e => e.SpellId == Ash);
        }

        [Fact]
        [Verifies("SPL-012")]
        public void Nobody_Acquires_Through_It_It_Deals_No_Damage_And_Projectiles_Pass_Through()
        {
            DuelWorld w = BramKit.Duel();
            Assert.True(w.CanAcquire(0, 1));
            CastAt(w, Vec3.Zero);
            Assert.False(w.CanAcquire(0, 1)); // spec L167: two meters of ash block acquisition both ways
            Assert.False(w.CanAcquire(1, 0));
            Assert.Empty(BramKit.DamageTo(w, 1));

            // "ash does not absorb projectiles" / "shoot through": a Fireball through the cloud still hits.
            BramKit.Run(w, 30); // the field cast's 0.45 s recovery
            PlayerInput aim = BramKit.AimAt(w, BramKit.Chest(w, 1), 0);
            BramKit.Cast(w, 0, BramKit.NormalHold, aim);
            BramKit.Run(w, 60, aim);
            Assert.Equal(48f, BramKit.DamageTo(w, 1, "SPL-009").Single().Dealt, 3);
            Assert.NotNull(Cloud(w));
        }

        [Fact]
        [Verifies("SPL-012")]
        public void Wind_Displaces_It()
        {
            DuelWorld w = BramKit.Duel();
            ObscurationVolume cloud = CastAt(w, Vec3.Zero);
            Vec2 before = Center(cloud);
            BramKit.Materials(w).Wind(new WindGust { Direction = new Vec2(1f, 0f), Strength = 2, DurationTicks = 30 });
            BramKit.Run(w, 32);
            Assert.True(Center(cloud).X - before.X > 0.5f); // translated along the gust (spec L151)
        }

        [Fact]
        [Verifies("SPL-012")]
        public void Water_Under_It_Collapses_The_Cloud()
        {
            DuelWorld w = BramKit.Duel();
            ObscurationVolume cloud = CastAt(w, Vec3.Zero);
            w.QueueDeposit(new WorldDeposit { Kind = DepositKind.Water, Strength = 1, Position = new Vec3(1f, 0f, 0f), SourcePlayer = 1, SourceId = "SPL-T95", LifetimeTicks = 120 });
            BramKit.Run(w, 3);
            Assert.Null(Cloud(w));
            Assert.True(BramKit.Entity<AshCloudEntity>(w) == null);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.FieldEnded), e => e.SpellId == Ash && e.Amount == 1f);
            BramKit.Run(w, 2);
            Assert.DoesNotContain(w.Owned.Entities, e => e.DefinitionId == "CLOUD:Ash");
            Assert.True(w.CanAcquire(0, 1));
        }

        [Fact]
        [Verifies("SPL-012")]
        public void Its_16_Meter_Range_Bounds_The_Placement_And_A_Crest_Release_Saves_12_Percent_Mana()
        {
            DuelWorld w = BramKit.Duel(layout: TestWorlds.Flat(boundaryRadius: 30f, floorHalf: 30f));
            ObscurationVolume cloud = CastAt(w, new Vec3(0f, 0f, 16f), BramKit.CrestHold); // 22 m away
            Vec2 c = Center(cloud);
            Assert.InRange(Vec2.Distance(c, new Vec2(0f, -6f)), 14.5f, 16.3f);
            Assert.Equal(100f - 20f * 0.88f, BramKit.Mana(w), 3);
        }

        // ----- AUG-311 Ash Corridor ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 6f)]
        [InlineData(2, 8f)]
        [Verifies("AUG-311")]
        public void Ash_Corridor_Replaces_The_Cloud_With_A_2_Meter_Wide_Curtain_Across_The_Aim_For_5_Seconds(int rank, float length)
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-311", rank)));
            ObscurationVolume curtain = CastAt(w, Vec3.Zero);
            curtain.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, length - 0.6f, length + 0.6f); // across the +Z aim
            Assert.InRange(max.Y - min.Y, 1.5f, 2.6f);                  // "2 m-wide"
            Assert.Equal(Ticks.FromSeconds(5.0), curtain.ExpiresTick - curtain.CreatedTick);
            Assert.False(w.CanAcquire(0, 1)); // a crossing sight line passes two meters of ash
            Assert.Equal(12f, BramKit.Form(w, Ash).Range); // "Cast range 16 becomes 12 m"
        }

        [Fact]
        [Verifies("AUG-311")]
        public void Ash_Corridor_Has_A_12_Meter_Range_And_Narrow_Ends_To_Move_Around()
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-311")), TestWorlds.Flat(boundaryRadius: 30f, floorHalf: 30f));
            ObscurationVolume far = CastAt(w, new Vec3(0f, 0f, 10f)); // 16 m away: placed within 12 m
            Assert.InRange(Vec2.Distance(Center(far), new Vec2(0f, -6f)), 10.5f, 12.3f);

            DuelWorld v = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-311")));
            CastAt(v, Vec3.Zero);
            Assert.False(v.CanAcquire(1, 0));
            TestWorlds.Place(v, 1, new Vec3(5f, 0f, 3f), 225f); // "Move around narrow ends": the line passes beyond the 3 m half length
            Assert.True(v.CanAcquire(1, 0));
        }

        // ----- AUG-312 Ash Bellows -----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 3f)]
        [Verifies("AUG-312")]
        public void Ash_Bellows_Recast_Pushes_The_Cloud_In_The_Aimed_Direction_For_6_Mana_Without_Extending_It(int rank, float push)
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-312", rank)));
            int slot = BramKit.SlotOf(w, Ash);
            ObscurationVolume cloud = CastAt(w, Vec3.Zero);
            Assert.Equal(Ticks.FromSeconds(5.0), cloud.ExpiresTick - cloud.CreatedTick); // "cloud duration 6 becomes 5 s"
            int expires = cloud.ExpiresTick;
            Vec2 before = Center(cloud);
            float mana = BramKit.Mana(w);

            PlayerInput east = PlayerInput.Idle(90f).Selecting(slot); // the manually chosen direction: +X
            BramKit.Recast(w, slot, east);
            Assert.Equal(mana - 6f, BramKit.Mana(w), 3); // "Recast costs 6 mana"
            BramKit.Run(w, 80, east);
            Vec2 moved = Center(cloud) - before;
            Assert.InRange(moved.X, push - 0.3f, push + 0.3f);
            Assert.InRange(System.Math.Abs(moved.Y), 0f, 0.2f);
            Assert.Equal(expires, cloud.ExpiresTick); // "lifetime never extends"
            Assert.Contains(BramKit.Spell(w, SpellEventKind.CloudMoved), e => e.SpellId == Ash && e.Amount == push);

            // Once per cast (spec L94).
            BramKit.Recast(w, slot, east);
            Assert.Single(BramKit.Spell(w, SpellEventKind.CloudMoved));
        }

        [Fact]
        [Verifies("AUG-312")]
        public void Ash_Bellows_Counterplay_The_Clouds_Original_Location_Opens_After_A_Push()
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-312")));
            int slot = BramKit.SlotOf(w, Ash);
            CastAt(w, Vec3.Zero);
            var from = new Vec3(-2.25f, 1.2f, -6f);
            var to = new Vec3(-2.25f, 1.2f, 6f);
            Assert.False(w.HasClearSight(from, to)); // through the west of the original cloud
            PlayerInput east = PlayerInput.Idle(90f).Selecting(slot);
            BramKit.Recast(w, slot, east);
            BramKit.Run(w, 80, east);
            Assert.True(w.HasClearSight(from, to)); // that part of the original location is now open
        }
    }
}
