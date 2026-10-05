using System;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// WIZ-002's signature, Kiln Vent: "14 s cooldown, 25 mana, 0.35 s windup. Project a visible 4 m cone that deals 28
    /// thermal damage and pushes enemies 1.5 m. It contributes Heat strength 1 to affected world props; it does not
    /// create a persistent fire field." Counterplay: "punish the cone's windup. Move sideways out of the cone."
    /// </summary>
    public class KilnVentTests
    {
        private const string Vent = "WIZ-002";
        private static readonly Vec3 Bram = new Vec3(0f, 0f, -6f); // facing +Z

        private static DuelWorld Duel(Vec3 target, ArenaLayout layout = null)
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(), layout);
            TestWorlds.Place(w, 1, target, 180f);
            return w;
        }

        private static void VentAndResolve(DuelWorld w, PlayerInput? other = null)
        {
            BramKit.Signature(w, null, other);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ConeReleased).Any(e => e.SpellId == Vent), 40, null, other);
            BramKit.Run(w, 1, null, other);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Costs_25_Mana_Cools_Down_14_Seconds_And_Releases_After_A_0_35_Second_Windup()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f));
            VentAndResolve(w);
            Confluence.Sim.Casting.CastDuelEvent commit = BramKit.Casts(w, CastEventKind.Committed).Single();
            Assert.Equal(Vent, commit.Cast.SpellId);
            Assert.Equal(100f - 25f, BramKit.Mana(w), 3);
            Confluence.Sim.Casting.CastDuelEvent release = BramKit.Casts(w, CastEventKind.Released).Single();
            Assert.Equal(Ticks.FromSeconds(0.35), release.Tick - commit.Tick);
            Assert.Equal(Ticks.FromSeconds(14.0), w.Player(0).Caster.CooldownRemainingTicks(Vent) + (w.Tick - commit.Tick));

            // A second press inside the cooldown is refused; the vent is ready again 14 s after commitment.
            BramKit.Signature(w);
            Assert.Single(BramKit.Casts(w, CastEventKind.Committed));
            BramKit.RunUntil(w, () => w.Player(0).Caster.CooldownRemainingTicks(Vent) == 0, Ticks.FromSeconds(15.0));
            Assert.InRange(w.Tick, commit.Tick + Ticks.FromSeconds(14.0) - 1, commit.Tick + Ticks.FromSeconds(14.0) + 1);
            BramKit.Signature(w);
            Assert.Equal(2, BramKit.Casts(w, CastEventKind.Committed).Count());
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Is_Visible_From_Its_Press_And_Deals_28_Thermal_In_Its_4_Meter_Cone()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f));
            VentAndResolve(w);
            // "Project a visible 4 m cone": the cue is announced at the press, before the windup resolves (spec L96).
            Confluence.Sim.Casting.CastDuelEvent cue = BramKit.Casts(w, CastEventKind.CueStarted).Single();
            Assert.True(cue.Tick < BramKit.Casts(w, CastEventKind.Released).Single().Tick);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.Telegraph), e => e.SpellId == Vent);
            SpellEvent cone = BramKit.Spell(w, SpellEventKind.ConeReleased).Single();
            Assert.Equal(4f, cone.Amount, 3);

            DamageEvent hit = BramKit.DamageTo(w, 1, Vent).Single();
            Assert.Equal(28f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels.Single().Channel);
            Assert.Equal(DamageKind.Direct, hit.Kind);

            // A signature has no grammar puzzle: there is no crest bonus to gain (spec L472).
            Assert.Equal(MasteryReward.None, BramKit.Casts(w, CastEventKind.Committed).Single().Cast.Mastery.Reward);
        }

        [Theory]
        [InlineData(0f, -1.4f, false)]   // 4.6 m ahead: the body edge is beyond the 4 m reach
        [InlineData(3.5f, -4.5f, false)] // ~67 degrees off the aim: out of the cone sideways
        [InlineData(1.5f, -3.5f, true)]  // ~31 degrees: inside
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Counterplay_Moving_Sideways_Or_Out_Of_Reach_Avoids_It(float x, float z, bool hit)
        {
            DuelWorld w = Duel(new Vec3(x, 0f, z));
            VentAndResolve(w);
            Assert.Equal(hit ? 28f : 0f, BramKit.Dealt(w, 1, Vent), 3);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Counterplay_Leaving_The_Cone_During_The_Windup_Avoids_It()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3.5f));
            BramKit.Signature(w);
            TestWorlds.Place(w, 1, new Vec3(4f, 0f, -4.5f), 180f); // sidestepped during the 0.35 s windup
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ConeReleased).Any(), 40);
            BramKit.Run(w, 1);
            Assert.Empty(BramKit.DamageTo(w, 1, Vent));
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Pushes_Enemies_1_5_Meters_Away_From_Bram()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3.5f));
            Vec3 before = w.Player(1).Position;
            VentAndResolve(w);
            MovementEvent push = SpellTestKit.Events<MovementEvent>(w).Single(m => m.Change == MovementChange.KnockbackStarted && m.Player == 1);
            Assert.Equal(1.5f, push.Amount, 3);
            Assert.Equal(Vent, push.SourceId);
            BramKit.Run(w, 30);
            Vec3 after = w.Player(1).Position;
            Assert.Equal(1.5f, after.Z - before.Z, 1); // straight away from Bram along +Z
            Assert.Equal(0f, after.X - before.X, 2);
            Assert.True(w.Player(0).Position.Z > -6.1f && w.Player(0).Position.Z < -5.9f); // Bram is not pushed
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Push_Is_Hard_Control_So_Control_Immunity_Stops_It_But_Not_The_Damage()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3.5f));
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "TEST", -1, 1));
            BramKit.Run(w, 3); // the stun expires and leaves control immunity (spec L116)
            Assert.True(w.Player(1).Statuses.IsControlImmune(w.Tick + 1));
            Vec3 before = w.Player(1).Position;
            VentAndResolve(w);
            Assert.Equal(28f, BramKit.Dealt(w, 1, Vent), 3);
            Assert.Contains(SpellTestKit.Events<MovementEvent>(w), m => m.Change == MovementChange.KnockbackBlockedByImmunity && m.Player == 1);
            BramKit.Run(w, 30);
            Assert.Equal(before.Z, w.Player(1).Position.Z, 2);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Is_Reduced_By_A_Frontal_Guard_And_Stopped_By_Cover()
        {
            DuelWorld guarded = Duel(new Vec3(0f, 0f, -3.5f));
            PlayerInput guard = TestWorlds.B.With(InputButtons.Guard);
            BramKit.Run(guarded, 2, null, guard);
            VentAndResolve(guarded, guard);
            DamageEvent hit = BramKit.DamageTo(guarded, 1, Vent).Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard);
            Assert.Equal(14f, hit.Dealt, 3); // the frontal guard halves direct hits (spec L104)

            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(120, new Vec3(0f, 0f, -4.6f), new Vec3(1.2f, 2.5f, 0.2f)));
            DuelWorld covered = Duel(new Vec3(0f, 0f, -3.5f), wall);
            VentAndResolve(covered);
            Assert.Empty(BramKit.DamageTo(covered, 1, Vent));
        }

        // PROP-001 Copper Barrel over thin water (one unit), with more thin water on open ground in the cone.
        private static ArenaLayout WithBarrel(Vec3 at)
        {
            ArenaLayout layout = BramKit.FlatWith(BramKit.Water(at.XZ(), 0.4f, 0.4f, 1), BramKit.Water(new Vec2(1.5f, -4.5f), 0.3f, 0.3f, 1));
            layout.Props.Add(WorldDuels.Prop(1, "PROP-001", at, new Vec3(0.8f, 1f, 0.8f)));
            return layout;
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Gives_Heat_1_To_Props_In_The_Cone_And_Leaves_No_Fire_Field()
        {
            var at = new Vec3(0f, 0f, -3.5f);
            DuelWorld w = Duel(new Vec3(5f, 0f, 6f), WithBarrel(at));
            Assert.True(BramKit.Reactions(w).UnitsAt(at).Water > 0);
            VentAndResolve(w);
            BramKit.Run(w, 2);
            SpellEvent heated = BramKit.Spell(w, SpellEventKind.Heated).Single(e => e.SpellId == Vent);
            Assert.Equal(1, heated.EntityId);
            Assert.Equal(0, BramKit.Reactions(w).UnitsAt(at).Water); // "Heat strength 1" under the prop dried its one water unit

            // The cone itself deposits nothing: thin water on open ground in the cone stays, and the vent created no
            // field; the only field is the steam cloud that the prop heat and its water formed (a reaction product).
            Assert.True(BramKit.Reactions(w).UnitsAt(new Vec3(1.5f, 0f, -4.5f)).Water > 0);
            Assert.All(w.Owned.Entities, o => Assert.Equal(MaterialRules.CloudFieldPrefix + "Steam", o.DefinitionId));
            Assert.DoesNotContain(BramKit.Runtime(w).Entities, e => e.Cast != null && e.Cast.SpellId == Vent && e.Alive);

            // A recorded run shows exactly one heat deposit of strength 1, instant, on the prop's footprint only.
            var recorded = new RecordingMaterials();
            DuelWorld r = BramKit.Duel(BramKit.Bram(), WithBarrel(at), materials: recorded);
            TestWorlds.Place(r, 1, new Vec3(5f, 0f, 6f), 180f);
            VentAndResolve(r);
            WorldDeposit heat = recorded.Deposits.Single();
            Assert.Equal(DepositKind.Heat, heat.Kind);
            Assert.Equal(1, heat.Strength);
            Assert.Equal(0, heat.LifetimeTicks);
            Assert.True(heat.Footprint.Contains(at.XZ()));
            Assert.False(heat.Footprint.Contains(new Vec2(1.5f, -4.5f)));

            // A prop outside the cone is untouched.
            DuelWorld beside = Duel(new Vec3(5f, 0f, 6f), WithBarrel(new Vec3(-3.5f, 0f, -5.5f)));
            VentAndResolve(beside);
            Assert.DoesNotContain(BramKit.Spell(beside, SpellEventKind.Heated), e => e.SpellId == Vent);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Kiln_Vent_Prose_Parameters_Quote_The_Wizard_Record()
        {
            // D-011: every number of the signature and passive exists only in the record's prose.
            WizardDef wizard = BramKit.Catalogue.Wizard("WIZ-002");
            string text = wizard.Signature + " | " + wizard.Passive;
            var kit = new BramCinderKit();
            Assert.Equal(10, kit.Parameters.Count);
            foreach (ProseParameter p in kit.Parameters)
            {
                Assert.True(text.Contains(p.Quote, StringComparison.Ordinal), "\"" + p.Quote + "\" is not in the record");
                Assert.True(p.Quote.Contains(p.WrittenText, StringComparison.Ordinal), "\"" + p.Quote + "\" does not contain " + p.WrittenText);
            }

            SpellForm vent = w0().Player(0).Caster.Loadout.Signature;
            Assert.True(vent.IsSignature);
            Assert.Equal(25f, vent.Castable.Mana);
            Assert.Equal(Ticks.FromSeconds(14.0), vent.Castable.CooldownTicks);
            Assert.Equal(Ticks.FromSeconds(0.35), vent.Castable.WindupTicks);
            Assert.True(vent.Castable.RequiresAnticipatoryCue);
            Assert.Equal(28f, vent.Payload.Direct);
            Assert.Equal(1.5f, vent.Payload.Push);
            Assert.Equal(DamageChannel.Thermal, vent.Payload.Channel);
        }

        private static DuelWorld w0() => BramKit.Duel();
    }
}
