using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Deployables, summons, ground waves, and tethers.</summary>
    public class PlacedDeliveryTests
    {
        // ----- deployables ----------------------------------------------------------------------------------

        private static SpellForm Orb(bool recast = true, float lifetime = 3f)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T60", 10f, 1f, 0f, 30f, DamageChannel.Thermal, "trap");
            b.Delivery = new DeployableDelivery { Placement = DeployPlacement.Thrown, Speed = 18f, LifetimeSec = lifetime, DetonateOnRecast = recast };
            b.Payload.Splash = 25f;
            b.Payload.SplashRadius = 2f;
            if (recast) b.Recast = RecastRule.Default;
            return b.Build();
        }

        [Fact]
        public void A_Thrown_Orb_Waits_On_The_First_Surface_And_The_Recast_Detonates_It()
        {
            Solid wall = SpellTestKit.Wall(100, new Vec3(0f, 0f, 4.2f), new Vec3(3f, 2.5f, 0.2f));
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Orb()), layout: SpellTestKit.FlatWith(wall));
            TestWorlds.Place(w, 1, new Vec3(1f, 0f, 3.2f), 180f);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            DeployableEntity orb = SpellTestKit.Runtime(w).Entities.OfType<DeployableEntity>().Single();
            Assert.True(orb.Settled);
            Assert.InRange(orb.Position.Z, 3.8f, 4.1f);
            Assert.Empty(SpellTestKit.DamageTo(w, 1));

            w.Step(TestWorlds.A.With(InputButtons.Recast), TestWorlds.B);
            CastDuelEvent recast = SpellTestKit.CastEvents(w, CastEventKind.Recast).Single();
            Assert.Equal(SpellTestKit.CastEvents(w, CastEventKind.Committed).Single().CastId, recast.CastId); // spec L94
            Assert.Equal(25f, SpellTestKit.HealthLost(w, 1), 3);
            Assert.Empty(SpellTestKit.Runtime(w).Entities.OfType<DeployableEntity>());
            Assert.Single(SpellTestKit.CastEvents(w, CastEventKind.RecastWindowClosed));
        }

        [Fact]
        public void The_Default_Recast_Window_Closes_When_The_Effect_Ends()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(lifetime: 1f)));
            PlayerInput down = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, -3f));
            SpellTestKit.Cast(w, down);
            SpellTestKit.Run(w, 10, down);
            Assert.NotNull(SpellTestKit.Machine(w, 0).PendingRecastFor(0));
            SpellTestKit.Run(w, Ticks.FromSeconds(1.0) + 6, down);
            Assert.Null(SpellTestKit.Machine(w, 0).PendingRecastFor(0)); // spec L94: never after the effect has ended
            w.Step(TestWorlds.A.With(InputButtons.Recast), TestWorlds.B);
            Assert.Equal(RejectReason.NoPendingRecast, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
        }

        [Fact]
        public void A_Proximity_Trap_Warns_Before_It_Bursts_On_The_First_Entrant()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T61", 10f, 1f, 0f, 8f, DamageChannel.Arcane, "trap");
            b.Delivery = new DeployableDelivery { Placement = DeployPlacement.Placed, LifetimeSec = 6f, Health = 25f, TriggerRadius = 1f, WarningSec = 0.4f };
            b.Payload.Splash = 30f;
            b.Payload.SplashRadius = 1.5f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, 0f));
            SpellTestKit.Cast(w, aim);
            SpellTestKit.Run(w, 30);
            Assert.Empty(SpellTestKit.SpellEvents(w, SpellEventKind.DeployableWarning));

            TestWorlds.Place(w, 1, new Vec3(0.8f, 0f, 0f), 180f);
            w.Step(TestWorlds.A, TestWorlds.B);
            SpellEvent warning = SpellTestKit.SpellEvents(w, SpellEventKind.DeployableWarning).Single();
            Assert.Equal(Ticks.FromSeconds(0.4), warning.Ticks);
            SpellTestKit.Run(w, Ticks.FromSeconds(0.4) - 1);
            Assert.Empty(SpellTestKit.DamageTo(w, 1));
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Equal(30f, SpellTestKit.HealthLost(w, 1), 3);
        }

        // ----- summons --------------------------------------------------------------------------------------

        private static SpellForm Familiar(float lifetime = 6f)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T62", 10f, 0.1f, 0f, 6f, DamageChannel.Physical, "summon");
            b.Delivery = new SummonDelivery
            {
                Health = 40f,
                LifetimeSec = lifetime,
                Mobility = SummonMobility.Stationary,
                AttackRange = 4f,
                AttackIntervalSec = 0.5f,
                ProjectileSpeed = 18f,
            };
            b.Payload.Direct = 6f;
            return b.Build();
        }

        [Fact]
        public void A_Summon_Attacks_Only_A_Visible_Target_Within_Its_Reach()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Familiar()));
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, -1f));
            SpellTestKit.Cast(w, aim);
            SummonEntity summon = SpellTestKit.Runtime(w).Entities.OfType<SummonEntity>().Single();
            Assert.Equal(40f, summon.Owned.Health);
            SpellTestKit.Run(w, 60);
            Assert.Empty(SpellTestKit.DamageTo(w, 1)); // the target is 7 m away

            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 1.5f), 180f);
            SpellTestKit.Run(w, 60);
            Assert.True(SpellTestKit.DamageTo(w, 1).Count >= 1);
            Assert.All(SpellTestKit.DamageTo(w, 1), d => Assert.Equal(6f, d.Dealt, 3));
        }

        [Fact]
        public void A_Summon_Never_Fires_Through_Cover_And_Its_Body_Can_Be_Destroyed()
        {
            SpellFormBuilder bolt = SpellTestKit.Form("SPL-T63", 5f, 0.2f);
            bolt.Delivery = new ProjectileDelivery { Speed = 40f, MaxRange = 30f };
            bolt.Payload.Direct = 50f;
            Solid wall = SpellTestKit.Wall(101, new Vec3(0f, 0f, 0.5f), new Vec3(3f, 2.5f, 0.2f));
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Familiar()), SpellTestKit.Loadout(bolt.Build()), layout: SpellTestKit.FlatWith(wall));
            SpellTestKit.Cast(w, SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, -1f)));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 2f), 180f);
            SpellTestKit.Run(w, 60);
            Assert.Empty(SpellTestKit.DamageTo(w, 1)); // spec L527: visible targets only

            TestWorlds.Place(w, 1, new Vec3(4f, 0f, 1f), 180f);
            SummonEntity summon = SpellTestKit.Runtime(w).Entities.OfType<SummonEntity>().Single();
            PlayerInput shoot = SpellTestKit.Aim(w, 1, summon.BodyCenter);
            w.Step(TestWorlds.A, shoot.With(InputButtons.Primary));
            SpellTestKit.Run(w, 30, TestWorlds.A, shoot);
            Assert.Empty(SpellTestKit.Runtime(w).Entities.OfType<SummonEntity>());
            Assert.Contains(w.EventLog.OfType<FieldEvent>(), e => e.Change == FieldChange.Destroyed && e.Destroyer == 1);
            Assert.Equal(1, w.Player(1).Feats.SummonsDestroyed); // FEAT-015
        }

        [Fact]
        public void A_Third_Summon_Retires_The_Oldest()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Familiar()));
            for (int i = 0; i < 3; i++)
            {
                SpellTestKit.Cast(w, SpellTestKit.Aim(w, 0, new Vec3(i, 0f, -2f)));
                SpellTestKit.Run(w, 40);
            }

            Assert.Equal(GlobalRules.FieldsAndSummons.MaxSummonsPerPlayer, w.Owned.Count(0, OwnedEntityKind.Summon));
            Assert.Single(w.EventLog.OfType<FieldEvent>(), e => e.Change == FieldChange.Retired && e.EntityKind == OwnedEntityKind.Summon);
            Assert.Equal(2, SpellTestKit.Runtime(w).Entities.OfType<SummonEntity>().Count());
        }

        // ----- waves ----------------------------------------------------------------------------------------

        private static SpellForm Wave(float push = 2f)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T64", 10f, 1f, 0f, 12f, DamageChannel.Physical);
            b.Delivery = new WaveDelivery { Speed = 18f, Width = 2f, Length = 12f };
            b.Payload.Direct = 8f;
            b.Payload.Push = push;
            b.Payload.PushDirection = PushDirection.AlongDelivery;
            return b.Build();
        }

        [Fact]
        public void A_Ground_Wave_Pushes_Each_Target_Once_Along_Its_Travel()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Wave()));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 0f), 180f);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            Assert.Equal(8f, SpellTestKit.DamageTo(w, 1).Single().Dealt, 3);
            Assert.InRange(w.Player(1).Position.Z, 1.9f, 2.1f); // pushed 2 m along +Z, once
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.WaveEnded));
        }

        [Fact]
        public void Solid_Obstacles_Stop_Wave_Segments_Separately()
        {
            // A post blocks the right half of the 2 m front; the left half carries on.
            Solid post = SpellTestKit.Wall(102, new Vec3(0.55f, 0f, -3f), new Vec3(1f, 1f, 0.4f));
            DuelWorld left = SpellTestKit.Duel(SpellTestKit.Loadout(Wave(0f)), layout: SpellTestKit.FlatWith(post));
            TestWorlds.Place(left, 1, new Vec3(-0.6f, 0f, 0f), 180f);
            SpellTestKit.Cast(left);
            SpellTestKit.Run(left, 60);
            Assert.Equal(8f, SpellTestKit.HealthLost(left, 1), 3);
            Assert.NotEmpty(SpellTestKit.SpellEvents(left, SpellEventKind.WaveSegmentBlocked));

            DuelWorld right = SpellTestKit.Duel(SpellTestKit.Loadout(Wave(0f)), layout: SpellTestKit.FlatWith(post));
            TestWorlds.Place(right, 1, new Vec3(0.9f, 0f, 0f), 180f);
            SpellTestKit.Cast(right);
            SpellTestKit.Run(right, 60);
            Assert.Equal(0f, SpellTestKit.HealthLost(right, 1));
        }

        [Fact]
        public void An_Airborne_Target_Above_The_Wave_Avoids_It()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Wave()));
            TestWorlds.Place(w, 1, new Vec3(0f, 3f, -2f), 180f);
            w.Player(1).Motor.Grounded = false;
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 30);
            Assert.Empty(SpellTestKit.DamageTo(w, 1));
        }

        // ----- tethers --------------------------------------------------------------------------------------

        [Fact]
        public void A_Tether_Pulls_Its_Target_Toward_The_Caster_Unless_Control_Immunity_Protects_It()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T65", 10f, 1f, 0f, 12f, DamageChannel.Physical, "pull");
            b.Delivery = new TetherDelivery { Mode = TetherMode.PullTarget, Speed = 20f, PullDistance = 2f };
            SpellForm form = b.Build();
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            Assert.InRange(w.Player(1).Position.Z, 3.9f, 4.1f); // 2 m toward the caster
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.TetherAttached));

            DuelWorld immune = SpellTestKit.Duel(SpellTestKit.Loadout(form));
            immune.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-STUN", 0, 6));
            SpellTestKit.Run(immune, 8);
            SpellTestKit.Cast(immune);
            SpellTestKit.Run(immune, 60);
            Assert.Equal(6f, immune.Player(1).Position.Z, 2); // spec L116: no control during immunity
            Assert.Contains(immune.EventLog.OfType<MovementEvent>(), e => e.Change == MovementChange.KnockbackBlockedByImmunity);
        }

        [Fact]
        public void A_Reel_Attaches_To_Solid_Geometry_And_Stops_There()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T66", 10f, 1f, 0f, 10f, DamageChannel.Physical, "movement");
            b.Delivery = new TetherDelivery { Mode = TetherMode.ReelToAnchor, PullDistance = 12f, ReelDurationSec = 0.5f };
            SpellForm form = b.Build();
            Assert.Equal(GlobalRules.Modifiers.MaxDashDistance, ((TetherDelivery)form.Delivery).PullDistance);
            Solid anchor = SpellTestKit.Wall(103, new Vec3(0f, 0f, 0f), new Vec3(3f, 2.5f, 0.2f));
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form), layout: SpellTestKit.FlatWith(anchor));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            Assert.InRange(w.Player(0).Position.Z, -0.65f, -0.45f);
        }
    }
}
