using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Self shields and buffs, and dash, leap, and teleport movement with their caps and riders.</summary>
    public class SelfAndMovementTests
    {
        // ----- shields and buffs ----------------------------------------------------------------------------

        private static SpellForm Shield(float capacity, float durationSec, float arcHalf = 0f)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T40", 10f, 1f, 0f, 0f, DamageChannel.Arcane, "defensive");
            b.Delivery = new ShieldDelivery { Capacity = capacity, DurationSec = durationSec, ArcHalfDeg = arcHalf };
            return b.Build();
        }

        [Fact]
        public void A_Self_Shield_Grants_Capacity_For_At_Most_Eight_Seconds()
        {
            SpellForm form = Shield(45f, 12f);
            Assert.Equal(8f, ((ShieldDelivery)form.Delivery).DurationSec);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form));
            SpellTestKit.Cast(w);
            Assert.Equal(45f, w.Player(0).Shields.Total);
            SpellTestKit.Run(w, Ticks.FromSeconds(8.0));
            Assert.Equal(0f, w.Player(0).Shields.Total); // spec L114
        }

        [Fact]
        public void A_Directional_Shield_Protects_Its_Front_Arc_And_Exposes_Its_Back()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Shield(45f, 4f, 50f)));
            SpellTestKit.Cast(w);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 20f)); // from the front
            Assert.Equal(25f, w.Player(0).Shields.Total, 3);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -9f), 0f);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 20f)); // from behind (spec L549)
            Assert.Equal(25f, w.Player(0).Shields.Total, 3);
            Assert.Equal(20f, SpellTestKit.HealthLost(w, 0), 3);
        }

        [Fact]
        public void A_Buff_Reduces_Incoming_Projectile_Damage_And_Holds_Its_Stat_Modifiers_While_It_Lasts()
        {
            SpellFormBuilder buff = SpellTestKit.Form("SPL-T41", 10f, 1f, 0f, 0f, DamageChannel.Arcane, "defensive");
            var delivery = new BuffDelivery { DurationSec = 2f, ProjectileDamageReduction = 0.12f };
            delivery.AddModifier(StatKind.MovementBonus, -0.15f);
            buff.Delivery = delivery;

            SpellFormBuilder bolt = SpellTestKit.Form("SPL-T42", 10f, 0.5f);
            bolt.Delivery = new ProjectileDelivery { Speed = 40f, MaxRange = 30f };
            bolt.Payload.Direct = 50f;
            SpellFormBuilder cone = SpellTestKit.Form("SPL-T43", 10f, 0.5f, 0f, 4f);
            cone.Delivery = new ConeDelivery { HalfAngleDeg = 45f, Reach = 4f };
            cone.Payload.Direct = 50f;

            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(buff.Build()), SpellTestKit.Loadout(bolt.Build(), cone.Build()));
            SpellTestKit.Cast(w);
            Assert.Equal(-0.15f, w.Player(0).Stats.Raw(StatKind.MovementBonus), 4);
            w.Step(TestWorlds.A, TestWorlds.B.With(InputButtons.Primary));
            SpellTestKit.Run(w, 30);
            Assert.Equal(44f, SpellTestKit.TotalDealt(w, 0, "SPL-T42"), 3); // 50 x (1 - 12%)

            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);
            w.Step(TestWorlds.A, TestWorlds.B.Selecting(1));
            SpellTestKit.Run(w, 10, TestWorlds.A, TestWorlds.B.Selecting(1));
            w.Step(TestWorlds.A, TestWorlds.B.Selecting(1).With(InputButtons.Primary));
            Assert.Equal(50f, SpellTestKit.TotalDealt(w, 0, "SPL-T43"), 3); // not a projectile

            SpellTestKit.Run(w, Ticks.FromSeconds(2.0));
            Assert.Equal(0f, w.Player(0).Stats.Raw(StatKind.MovementBonus));
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.BuffEnded));
        }

        // ----- movement -------------------------------------------------------------------------------------

        private static SpellForm Move(DeliveryKind mode, float distance, float durationSec = 0f, float contact = 0f, bool stopOnContact = false,
            float burst = 0f, float maxRise = 0f, DashDirection direction = DashDirection.Forward)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T50", 10f, 1f, 0f, distance, DamageChannel.Physical, mode == DeliveryKind.Teleport ? "teleport" : "dash");
            var delivery = new MovementDelivery
            {
                Mode = mode,
                Distance = distance,
                DurationSec = durationSec,
                ContactRadius = contact,
                StopOnContact = stopOnContact,
                MaxRise = maxRise,
                Direction = direction,
            };
            if (burst > 0f) delivery.EndpointBurst = new PayloadSpec { Splash = burst, SplashRadius = 1.5f, Channel = DamageChannel.Physical };
            b.Delivery = delivery;
            b.Payload.Direct = contact > 0f ? 12f : 0f;
            return b.Build();
        }

        [Fact]
        public void A_Dash_Is_Capped_At_Eight_Meters_And_Travels_Along_The_Aim()
        {
            SpellForm form = Move(DeliveryKind.Dash, 12f, 0.4f);
            Assert.Equal(GlobalRules.Modifiers.MaxDashDistance, ((MovementDelivery)form.Delivery).Distance);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form), layout: TestWorlds.Flat(boundaryRadius: 19f));
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            PlayerInput right = TestWorlds.Face(90f);
            SpellTestKit.Cast(w, right);
            SpellTestKit.Run(w, 40, right);
            Assert.Equal(8f, w.Player(0).Position.X, 1);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.MovementEnded));
        }

        [Fact]
        public void A_Dash_Stops_At_Solid_Geometry_And_At_The_Legal_Boundary()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Move(DeliveryKind.Dash, 8f, 0.3f)),
                layout: SpellTestKit.FlatWith(SpellTestKit.Wall(90, new Vec3(0f, 0f, -2f), new Vec3(4f, 2.5f, 0.2f))));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 40);
            Assert.InRange(w.Player(0).Position.Z, -2.6f, -2.45f);

            DuelWorld edge = SpellTestKit.Duel(SpellTestKit.Loadout(Move(DeliveryKind.Dash, 8f, 0.3f)), layout: TestWorlds.Flat(boundaryRadius: 10f));
            TestWorlds.Place(edge, 0, new Vec3(0f, 0f, -6f), 180f);
            PlayerInput back = TestWorlds.Face(180f);
            SpellTestKit.Cast(edge, back);
            SpellTestKit.Run(edge, 40, back);
            Assert.True(edge.Layout.Boundary.Contains(edge.Player(0).Position, edge.CombatSeconds));
            Assert.Empty(SpellTestKit.DamageTo(edge, 0)); // self dashes stop at the edge without damage (spec L51)
        }

        [Fact]
        public void A_Charge_Damages_The_First_Contact_Once_Ends_There_And_Bursts_At_Its_Endpoint()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Move(DeliveryKind.Dash, 8f, 0.4f, contact: 0.3f, stopOnContact: true, burst: 9f)));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -2f), 180f);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 40);
            var hits = SpellTestKit.DamageTo(w, 1);
            Assert.Equal(DamageKind.Direct, hits[0].Kind);
            Assert.Equal(12f, hits[0].Dealt, 3);
            Assert.Equal(DamageKind.Splash, hits[1].Kind); // endpoint burst
            Assert.Equal(2, hits.Count);
            Assert.True(w.Player(0).Position.Z < -2.7f); // stopped at the contact instead of travelling 8 m
        }

        [Fact]
        public void A_Teleport_Needs_The_Anticipatory_Cue_Lands_On_A_Legal_Point_And_Is_Refused_Inside_Collision()
        {
            SpellForm form = Move(DeliveryKind.Teleport, 5f);
            Assert.True(form.Castable.RequiresAnticipatoryCue); // spec L96
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form));
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, -2f));
            SpellTestKit.Cast(w, aim);
            SpellEvent telegraph = SpellTestKit.SpellEvents(w, SpellEventKind.Telegraph).Single();
            Assert.InRange(telegraph.Position.Z, -2.2f, -1.9f);
            SpellTestKit.Run(w, 20, aim);
            CastDuelEvent cue = SpellTestKit.CastEvents(w, CastEventKind.CueStarted).Single();
            CastDuelEvent release = SpellTestKit.CastEvents(w, CastEventKind.Released).Single();
            Assert.True(release.Tick - cue.Tick >= Ticks.FromSeconds(GlobalRules.Casting.AnticipatoryCueMinSec));
            Assert.InRange(w.Player(0).Position.Z, -2.2f, -1.9f);

            // Aimed at a wall face, the destination would overlap the wall: refused, no movement (spec L51, L124).
            DuelWorld blocked = SpellTestKit.Duel(SpellTestKit.Loadout(Move(DeliveryKind.Teleport, 5f)),
                layout: SpellTestKit.FlatWith(SpellTestKit.Wall(91, new Vec3(0f, 0f, -3f), new Vec3(3f, 2.5f, 0.2f))));
            SpellTestKit.Cast(blocked);
            SpellTestKit.Run(blocked, 20);
            Assert.Single(SpellTestKit.SpellEvents(blocked, SpellEventKind.MovementRefused));
            Assert.Equal(-6f, blocked.Player(0).Position.Z, 2);
            Assert.Equal(90f, blocked.Player(0).Resources.Mana); // the commitment stays spent (spec L90)
        }

        [Fact]
        public void A_Leap_Arcs_Onto_A_Higher_Point_Within_Its_Rise()
        {
            Solid platform = TestWorlds.Box(92, new Vec3(0f, 0f, 0f), new Vec3(4f, 1.5f, 4f), Confluence.Core.Arena.SolidKind.Platform);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Move(DeliveryKind.Leap, 6f, 0.8f, maxRise: 2f)), layout: SpellTestKit.FlatWith(platform));
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 1.5f, -0.5f));
            SpellTestKit.Cast(w, aim);
            SpellTestKit.Run(w, 90, TestWorlds.A);
            Assert.Equal(1.5f, w.Player(0).Position.Y, 1);
            Assert.True(w.Player(0).Motor.Grounded);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.MovementEnded));
        }
    }
}
