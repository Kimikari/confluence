using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// WIZ-010's signature (Undertow Mantle) and passive (Pressure Reserve), attached through the kit behavior and the
    /// effect framework in a real duel.
    /// </summary>
    public class HalenTideKitTests
    {
        private static List<EffectEvent> Stamina(HalenDuel d) =>
            d.Events<EffectEvent>().Where(e => e.Change == EffectChange.StaminaGained && e.SourceId == HalenTideKit.Id).ToList();

        [Fact]
        [Verifies("WIZ-010")]
        public void The_Registry_Supplies_The_Kit_And_A_Halen_Loadout_Uses_It()
        {
            Assert.True(ContentRegistry.Default.TryGetKit(HalenTideKit.Id, out IWizardKitBehavior kit));
            Assert.IsType<HalenTideKit>(kit);
            HalenDuel d = HalenTestKit.Duel();
            Assert.IsType<Confluence.Casting.Grammars.PressureMeterGrammar>(d.P0.Caster.Loadout.Grammar);
            Assert.Equal(new[] { "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077", "SPL-078" },
                d.P0.Caster.Loadout.Spells.Select(s => s.SpellId));
            SpellForm signature = d.P0.Caster.Loadout.Signature;
            Assert.NotNull(signature);
            Assert.True(signature.IsSignature);
            Assert.Equal(HalenTideKit.Id, signature.SpellId);
            Assert.Single(d.World.Effects.Host(0).Effects, e => e.SourceId == HalenTideKit.Id && e.Trigger == EffectTrigger.CastCommitted);
        }

        // ----- Undertow Mantle ----------------------------------------------------------------------------------

        [Fact]
        [Verifies("WIZ-010")]
        public void Undertow_Mantle_Costs_20_Cools_17_Seconds_And_Releases_After_A_Quarter_Second()
        {
            HalenDuel d = HalenTestKit.Duel();
            CastDuelEvent commit = d.Signature();
            Assert.NotNull(commit);
            Assert.Equal(HalenTideKit.Id, commit.SpellId);
            Assert.Equal(MissReason.NotApplicable, commit.Cast.Mastery.Miss); // no grammar for signatures (spec L472)
            Assert.Equal(20f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(17.0), d.P0.Caster.CooldownRemainingTicks(HalenTideKit.Id));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            SpellEvent eddy = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.25), eddy.Tick);
            Assert.Equal(2f, eddy.Amount, 3);
            Assert.Equal(Ticks.FromSeconds(3.0), eddy.Ticks);
            Assert.Equal(0f, Vec3.DistanceXZ(eddy.Position, d.P0.Position), 2); // centered at the cast location
            OwnedEntity field = d.World.Owned.Entities.Single();                 // one field allocation
            Assert.Equal(OwnedEntityKind.Field, field.Kind);
            Assert.True(field.Neutral);
            Assert.Equal(Ticks.FromSeconds(GlobalRules.Casting.RecoveryMajorFieldOrSummonSec), d.P0.Caster.Loadout.Signature.Castable.RecoveryTicks);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Undertow_Mantle_Slows_Enemies_And_The_Caster_15_Percent_And_Damages_Each_Body_12_Once()
        {
            HalenDuel d = HalenTestKit.Duel();
            TestWorlds.Place(d.World, 1, d.P0.Position + new Vec3(0f, 0f, 1.2f), 180f);
            d.Signature();
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            d.Run(2);
            Assert.Equal(0.15f, d.P0.Statuses.StrongestSlow, 3); // the eddy hinders him as well
            Assert.Equal(0.15f, d.P1.Statuses.StrongestSlow, 3);
            DamageEvent mine = d.DamageTo(0, HalenTideKit.Id).Single();
            DamageEvent theirs = d.DamageTo(1, HalenTideKit.Id).Single();
            foreach (DamageEvent e in new[] { mine, theirs })
            {
                Assert.Equal(12f, e.Dealt, 3);
                Assert.Equal(DamageChannel.Arcane, e.Resolution.Channels.Single().Channel);
                Assert.Equal(DamageKind.Splash, e.Kind); // field contact: never guarded (spec L104)
            }

            // Leaving and re-entering deals nothing more: "each body takes only 12 arcane damage on first entry".
            TestWorlds.Place(d.World, 1, d.P0.Position + new Vec3(0f, 0f, 4f), 180f);
            d.Run(20);
            Assert.Equal(0f, d.P1.Statuses.StrongestSlow, 3); // the slow ends once outside
            TestWorlds.Place(d.World, 1, d.P0.Position + new Vec3(0f, 0f, 1.2f), 180f);
            d.Run(20);
            Assert.Single(d.DamageTo(1, HalenTideKit.Id));
            Assert.Equal(0.15f, d.P1.Statuses.StrongestSlow, 3);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Undertow_Mantle_Damages_A_Late_Entrant_And_Ends_After_3_Seconds()
        {
            // Counterplay "attack from outside the eddy": a body that stays out is never touched.
            HalenDuel d = HalenTestKit.Duel();
            d.Signature();
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            d.Run(30);
            Assert.Empty(d.DamageTo(1, HalenTideKit.Id));
            TestWorlds.Place(d.World, 1, d.P0.Position + new Vec3(1.5f, 0f, 0f), 180f);
            d.Run(3);
            Assert.Equal(12f, d.DamageTo(1, HalenTideKit.Id).Single().Dealt, 3);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldEnded).Any(), Ticks.FromSeconds(3.0));
            Assert.Single(d.SpellEvents(SpellEventKind.FieldEnded));
            d.Run(20);
            Assert.Equal(0f, d.P1.Statuses.StrongestSlow, 3);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Undertow_Mantle_Lays_Water_1_That_Heat_And_Frost_Alter()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.Signature();
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            d.Step();
            Vec2 c = d.SpellEvents(SpellEventKind.FieldCreated).Single().Position.XZ();
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(Footprint.Circle(c, 2f), cells);
            Assert.All(cells, i => Assert.Equal(1, d.Reactions.UnitsAtCell(i).Water)); // "Water strength 1"

            // Counterplay "use Heat or Frost to alter its water".
            d.Deposit(Material.Frost, 2, Footprint.Circle(c + new Vec2(1f, 0f), 0.6f));
            d.Deposit(Material.Heat, 1, Footprint.Circle(c - new Vec2(1f, 0f), 0.6f));
            d.Step();
            Assert.True(d.Reactions.HasIceAt((c + new Vec2(1f, 0f)).ToXZ()));
            Assert.Contains(d.Reactions.Volumes, v => v.Kind == Confluence.Reactions.Volumes.VolumeKind.Steam);
        }

        // ----- Pressure Reserve -------------------------------------------------------------------------------

        [Fact]
        [Verifies("WIZ-010")]
        public void Pressure_Reserve_Gives_10_Stamina_After_A_30_Mana_Commitment()
        {
            HalenDuel d = HalenTestKit.With(DeepglassPrison.Id);
            d.P0.Resources.TrySpendStamina(50f, d.World.Tick);
            d.AimAtOther();
            CastDuelEvent commit = d.Cast(0);
            Assert.Equal(32f, commit.Cast.Mana, 3);
            EffectEvent gain = Stamina(d).Single();
            Assert.Equal(10f, gain.Amount, 3);
            Assert.Equal(commit.Tick, gain.Tick);
            Assert.Equal(60f, d.P0.Resources.Stamina, 3);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Pressure_Reserve_Ignores_Cheaper_Casts_And_The_Mastered_Discount_Below_30()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { "SPL-073", DeepglassPrison.Id }));
            d.P0.Resources.TrySpendStamina(50f, d.World.Tick);
            d.AimAtOther();
            d.Cast(0);                                           // 16 mana
            d.Run(30);
            CastDuelEvent mastered = d.Cast(1, mastery: true);   // 32 * 0.85 = 27.2 actually paid (spec L124)
            Assert.True(mastered.Cast.Mastery.IsMastered);
            Assert.Equal(27.2f, mastered.Cast.Mana, 3);
            d.Run(5);
            Assert.Empty(Stamina(d));
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Pressure_Reserve_Fires_At_Most_Once_Per_6_Seconds_And_Refunds_Never_Retrigger_It()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { "SPL-073", DeepglassPrison.Id }));
            d.P0.Resources.TrySpendStamina(90f, d.World.Tick);
            d.AimAtOther();

            // A drawback cost increase makes the next Pressure Jet cost 16 + 20 = 36 (paid after the 70% floor, spec L124).
            AddNextCastIncrease(d, 20f);
            d.Step();
            CastDuelEvent jet = d.Cast(0);
            Assert.Equal(36f, jet.Cast.Mana, 3);
            Assert.Single(Stamina(d));

            d.Run(60);
            CastDuelEvent prison = d.Cast(1);                    // 32 paid one second later: inside the 6 s interval
            Assert.Equal(32f, prison.Cast.Mana, 3);
            Assert.Single(Stamina(d));

            // A refund of the paid commitment is a mana gain, not a payment: it never retriggers the passive.
            ResourceEvent payment = d.Events<ResourceEvent>().Last(r => r.Change == ResourceChange.ManaSpent && r.SourceId == DeepglassPrison.Id);
            Assert.True(d.World.Effects.RefundCast(0, payment.Id, 5f, "AUG-TEST") > 0f);
            d.Run(2);
            Assert.Single(Stamina(d));

            // Six seconds after the first, a 36-mana jet triggers it again.
            d.RunUntil(() => d.P0.Caster.CooldownRemainingTicks("SPL-073") == 0 && d.World.Tick - jet.Tick > Ticks.FromSeconds(6.0), Ticks.FromSeconds(7.0));
            d.P0.Resources.TrySpendStamina(50f, d.World.Tick); // stamina regenerated meanwhile; make room for the gain
            AddNextCastIncrease(d, 20f);
            d.Step();
            d.Cast(0);
            Assert.Equal(2, Stamina(d).Count);
            Assert.Equal(2, d.Events<EffectEvent>().Count(e => e.Change == EffectChange.Activated && e.SourceId == HalenTideKit.Id));
        }

        private static void AddNextCastIncrease(HalenDuel d, float mana)
        {
            var increase = new Effect("AUG-TEST", EffectTrigger.Periodic) { PeriodTicks = 1, MaxActivations = 1 };
            increase.Actions.Add(new NextCastCostAction(-mana, 0f, 0));
            d.World.Effects.Host(0).Attach(increase);
        }

        // ----- prose parameters (D-011) -------------------------------------------------------------------------

        [Fact]
        [Verifies("WIZ-010")]
        public void Every_Kit_Parameter_Quotes_The_Wizard_Record()
        {
            WizardDef wizard = HalenTestKit.Catalogue.Wizard(HalenTideKit.Id);
            foreach (var p in HalenTideKit.SignatureParameters)
            {
                Assert.Contains(p.Quote, wizard.Signature);
                Assert.Contains(p.WrittenText, p.Quote);
            }

            foreach (var p in HalenTideKit.PassiveParameters)
            {
                Assert.Contains(p.Quote, wizard.Passive);
                Assert.Contains(p.WrittenText, p.Quote);
            }

            Assert.Equal(HalenTideKit.SignatureParameters.Count + HalenTideKit.PassiveParameters.Count, new HalenTideKit().Parameters.Count);
        }
    }
}
