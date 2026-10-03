using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Tests.Effects;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Tests.Props
{
    /// <summary>Prop bodies in the duel: collision, mass classes, pushes, collision damage, destruction (spec L155, L171-L173).</summary>
    public class PropSystemTests
    {
        private static readonly Vec3 Crate = new Vec3(0.6f, 0.6f, 0.6f);

        private static DuelWorld WithProps(params PropPlacement[] props)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.AddRange(props);
            DuelWorld world = WorldDuels.Duel(layout);
            WorldDuels.Idle(world, 1);
            return world;
        }

        [Fact]
        public void Props_With_A_Collision_Box_Block_Movement_And_Others_Do_Not()
        {
            DuelWorld world = WithProps(
                WorldDuels.Prop(1, "PROP-006", new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f)),
                WorldDuels.Prop(2, "PROP-017", new Vec3(4f, 0f, 0f), new Vec3(1f, 1f, 1f), hasCollision: false));
            TestWorlds.Place(world, 1, new Vec3(4f, 0f, -6f), 0f);
            WorldDuels.Step(world, 180, WorldDuels.Walk(0f), WorldDuels.Walk(0f));

            Assert.InRange(world.Player(0).Position.Z, -1.0f, -0.85f); // stopped against the crate's face at z = -0.5
            Assert.True(world.Player(1).Position.Z > 1f);            // walked over the floor-level leaf bed
            Assert.True(world.Props.Find(1).HasCollision);
            Assert.False(world.Props.Find(2).HasCollision);
            Assert.True(world.Props.IsPropSolid(world.Props.Find(1).SolidId));
        }

        [Fact]
        public void A_Preview_Collision_World_Holds_The_Same_Prop_Bodies_As_A_Duel()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(1, "PROP-006", new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f)));
            layout.Props.Add(WorldDuels.Prop(2, "PROP-017", new Vec3(4f, 0f, 0f), new Vec3(1f, 1f, 1f), hasCollision: false));
            Confluence.Sim.World.CollisionWorld preview = Confluence.Sim.World.CollisionWorld.FromLayoutWithProps(layout);
            DuelWorld duel = WorldDuels.Duel(layout);

            Confluence.Sim.World.CollisionSolid body = preview.Solids.Single(s => s.Dynamic);
            Assert.Equal(layout.Props[0].CollisionBox.Center, body.Box.Center);
            Assert.Equal(layout.Props[0].CollisionBox.HalfExtents, body.Box.HalfExtents);
            Assert.Equal(duel.Props.Find(1).Solid.Box.Center, body.Box.Center);
            Assert.Equal(duel.Collision.Solids.Count, preview.Solids.Count);
        }

        [Fact]
        public void Mass_Class_And_Durability_Come_From_The_Catalogue()
        {
            DuelWorld world = WithProps(
                WorldDuels.Prop(1, "PROP-013", new Vec3(-6f, 0f, 0f), Crate),
                WorldDuels.Prop(2, "PROP-001", new Vec3(-3f, 0f, 0f), Crate),
                WorldDuels.Prop(3, "PROP-006", new Vec3(3f, 0f, 0f), Crate),
                WorldDuels.Prop(4, "PROP-002", new Vec3(6f, 0f, 0f), Crate));
            AssertProp(world.Props.Find(1), PropMass.Light, 10f, 30f);
            AssertProp(world.Props.Find(2), PropMass.Medium, 30f, 60f);
            AssertProp(world.Props.Find(3), PropMass.Heavy, 80f, 100f);
            Assert.Equal(PropMass.Anchored, world.Props.Find(4).Mass);
            Assert.True(world.Props.Find(4).Anchored);
            Assert.Equal(80f, world.Props.Find(4).MaxDurability, 3);

            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(1, "PROP-013", new Vec3(-6f, 0f, 0f), Crate));
            DuelWorld bare = WorldDuels.Duel(layout, withCatalogue: false);
            Assert.True(bare.Props.Find(1).Anchored); // unknown definitions never move
            Assert.Equal(0f, bare.Props.Find(1).MaxDurability, 3);
        }

        private static void AssertProp(PropBody prop, PropMass mass, float massValue, float durability)
        {
            Assert.Equal(mass, prop.Mass);
            Assert.Equal(massValue, prop.MassValue, 3);
            Assert.Equal(durability, prop.MaxDurability, 3);
            Assert.Equal(durability, prop.Durability, 3);
        }

        [Theory]
        [InlineData("PROP-013", 4f)]   // light: up to 4 m
        [InlineData("PROP-001", 2f)]   // medium: up to 2 m
        [InlineData("PROP-006", 0.5f)] // heavy: up to 0.5 m
        public void A_Force_Pushes_A_Loose_Prop_By_Its_Mass_Class_At_Most_Eight_Meters_Per_Second(string propId, float expected)
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, propId, new Vec3(-6f, 0f, 0f), Crate));
            PropBody prop = world.Props.Find(1);
            Vec3 start = prop.Position;
            Assert.True(WorldDuels.Materials(world).PushProp(1, new Vec3(1f, 0f, 0f), 0, 31));
            float maxStep = 0f;
            Vec3 last = start;
            for (int i = 0; i < 120; i++)
            {
                WorldDuels.Idle(world, 1);
                maxStep = System.Math.Max(maxStep, Vec3.Distance(prop.Position, last));
                last = prop.Position;
            }

            Assert.Equal(expected, prop.Position.X - start.X, 3);
            Assert.True(maxStep <= GlobalRules.Terrain.DamagingPropVelocityCap * Ticks.Dt + 1e-4f);
            Assert.False(prop.Moving);
            Assert.Equal(prop.Box.Center, world.Collision.Find(prop.SolidId).Box.Center); // collision moved with it
            List<PropEvent> events = WorldDuels.Events<PropEvent>(world);
            Assert.Equal(PropChange.Pushed, events[0].Change);
            Assert.Equal(PropChange.Stopped, events.Last().Change);
        }

        [Fact]
        public void Anchored_Props_Only_Shake()
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, "PROP-002", new Vec3(-6f, 0f, 0f), Crate));
            Vec3 start = world.Props.Find(1).Position;
            WorldDuels.Materials(world).PushProp(1, new Vec3(1f, 0f, 0f), 0, 31);
            WorldDuels.Idle(world, 60);
            Assert.Equal(start, world.Props.Find(1).Position);
            Assert.Equal(PropChange.Shaken, WorldDuels.Events<PropEvent>(world).Single().Change);
        }

        [Fact]
        public void A_Pushed_Prop_Stops_At_Walls_And_At_Unsupported_Edges()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(60, new Vec3(-4f, 0f, 0f), new Vec3(0.4f, 2f, 3f)));                      // wall face at x = -4.2
            layout.Solids.Add(TestWorlds.Box(61, new Vec3(8f, 0f, 0f), new Vec3(2f, 1f, 2f), SolidKind.Platform));     // platform top 1 m, x 7..9
            layout.Props.Add(WorldDuels.Prop(1, "PROP-013", new Vec3(-6f, 0f, 0f), Crate));
            layout.Props.Add(WorldDuels.Prop(2, "PROP-013", new Vec3(8f, 1f, 0f), Crate));
            DuelWorld world = WorldDuels.Duel(layout);
            WorldDuels.Materials(world).PushProp(1, new Vec3(1f, 0f, 0f), 0, 31);
            WorldDuels.Materials(world).PushProp(2, new Vec3(1f, 0f, 0f), 0, 32);
            WorldDuels.Idle(world, 120);

            PropBody walled = world.Props.Find(1);
            Assert.InRange(walled.Box.Center.X + walled.Box.HalfExtents.X, -4.4f, -4.2f + 1e-3f);
            PropBody ledge = world.Props.Find(2);
            Assert.InRange(ledge.Box.Center.X, 8.5f, 9f); // its center never passes the platform edge at x = 9
            Assert.Equal(1f, ledge.Box.Bottom, 3);       // props slide; they never fall
        }

        [Fact]
        public void A_Moving_Prop_Deals_Its_Class_Damage_Once_Per_Second_As_Prop_Damage_Without_Lifesteal_Or_Item_Effects()
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, "PROP-013", new Vec3(-2f, 0f, 6f), Crate));
            world.Player(0).Stats.Add(StatKind.Lifesteal, 0.1f, "EQP-TEST");
            RecordAction dealt = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt);
            RecordAction taken = EffectTests.Record(world, 1, EffectTrigger.DamageTaken);
            world.ApplyDamage(TestWorlds.Hit(world, 1, 0, 30f)); // room to heal
            WorldDuels.IdleSeconds(world, 0.6f);
            ReactionMaterials materials = WorldDuels.Materials(world);

            materials.PushProp(1, new Vec3(1f, 0f, 0f), 0, 41);
            WorldDuels.Idle(world, 30);
            DamageEvent hit = WorldDuels.Events<DamageEvent>(world).Single(d => d.Kind == DamageKind.Prop);
            Assert.Equal(0, hit.Attacker);
            Assert.Equal(1, hit.Target);
            Assert.Equal("PROP-013", hit.SourceId);
            Assert.Equal(ReactionTag.Prop, hit.Reaction);
            Assert.Equal(GlobalRules.Terrain.LightPropCollisionDamage, hit.Dealt, 3);
            Assert.Empty(WorldDuels.Events<HealEvent>(world));
            Assert.Empty(dealt.Fired);
            Assert.DoesNotContain(taken.Fired, t => t.DamageKind == DamageKind.Prop);
            Assert.Equal(1, world.Player(0).Feats.PropHits); // FEAT-021

            materials.PushProp(1, new Vec3(1f, 0f, 0f), 0, 42); // touching the player again within 1 s
            WorldDuels.Idle(world, 5);
            Assert.Single(WorldDuels.Events<DamageEvent>(world), d => d.Kind == DamageKind.Prop);
            Assert.Contains(WorldDuels.Events<PropEvent>(world), e => e.Change == PropChange.Collided && e.Amount == 0f);

            WorldDuels.IdleSeconds(world, 1f);
            materials.PushProp(1, new Vec3(1f, 0f, 0f), 0, 43);
            WorldDuels.Idle(world, 5);
            Assert.Equal(2, WorldDuels.Events<DamageEvent>(world).Count(d => d.Kind == DamageKind.Prop));
        }

        [Theory]
        [InlineData("PROP-001", 15f)] // medium
        [InlineData("PROP-006", 25f)] // heavy, at most 25 per impact (spec L155)
        public void Heavier_Props_Deal_Their_Class_Damage(string propId, float damage)
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, propId, new Vec3(-0.8f, 0f, 6f), Crate));
            WorldDuels.Materials(world).PushProp(1, new Vec3(1f, 0f, 0f), 1, 41); // ownership grants no exemption
            WorldDuels.Idle(world, 20);
            DamageEvent hit = WorldDuels.Events<DamageEvent>(world).Single();
            Assert.Equal(DamageKind.Prop, hit.Kind);
            Assert.Equal(damage, hit.Dealt, 3);
            Assert.Equal(1, hit.Attacker);
            Assert.Equal(0, world.Player(1).Feats.PropHits); // self-damage credits nothing
        }

        [Fact]
        public void A_Slow_Prop_Below_The_Impact_Threshold_Deals_No_Damage()
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, "PROP-013", new Vec3(-1.5f, 0f, 6f), Crate));
            WorldDuels.Materials(world).PushProp(1, new Vec3(1f, 0f, 0f), 0, 41, requestedSpeed: 2f);
            WorldDuels.Idle(world, 60);
            Assert.Empty(WorldDuels.Events<DamageEvent>(world));
            Assert.Contains(WorldDuels.Events<PropEvent>(world), e => e.Change == PropChange.Collided && e.TargetPlayer == 1);
        }

        [Fact]
        public void Strikes_Wear_A_Props_Durability_And_Destroying_It_Removes_Its_Collision()
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, "PROP-027", new Vec3(0f, 0f, -4.8f), new Vec3(0.6f, 1.5f, 0.6f))); // Glass Jug, durability 20
            PropBody jug = world.Props.Find(1);
            for (int i = 0; i < 2; i++)
            {
                world.Step(WorldDuels.Melee(0f), TestWorlds.B);
                WorldDuels.Idle(world, 60);
            }

            Assert.True(jug.Destroyed);
            Assert.False(world.Collision.Find(jug.SolidId).Active);
            PropEvent destroyed = WorldDuels.Events<PropEvent>(world).Single(e => e.Change == PropChange.Destroyed);
            Assert.Equal(0, destroyed.Player);
            Assert.Contains(WorldDuels.Events<PropEvent>(world), e => e.Change == PropChange.Damaged && e.Amount == GlobalRules.Melee.FocusDamage);

            WorldDuels.Step(world, 60, WorldDuels.Walk(0f), TestWorlds.B);
            Assert.True(world.Player(0).Position.Z > -4f); // the broken jug no longer blocks
        }

        [Fact]
        public void Props_Without_A_Body_Still_Take_Durability_Damage()
        {
            DuelWorld world = WithProps(WorldDuels.Prop(1, "PROP-017", new Vec3(4f, 0f, 0f), Crate, hasCollision: false)); // Dry Leaf Bed, 35
            Assert.False(world.Props.Damage(1, 20f, 0));
            Assert.Equal(15f, world.Props.Find(1).Durability, 3);
            Assert.True(world.Props.Damage(1, 20f, 0));
            Assert.True(world.Props.Find(1).Destroyed);
            Assert.False(world.Props.Damage(1, 5f, 0));
        }
    }
}
