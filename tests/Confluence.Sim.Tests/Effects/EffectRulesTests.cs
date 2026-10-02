using System;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Tests.World;

namespace Confluence.Sim.Tests.Effects
{
    /// <summary>
    /// Conditions, stacking, internal cooldowns, the proc budget (spec L126), cast pricing and refunds (L90, L124),
    /// hit bonuses, and conditional modifiers of the effect framework (L433).
    /// </summary>
    public class EffectRulesTests
    {
        private static DuelWorld Ready()
        {
            DuelWorld world = WorldDuels.Duel();
            WorldDuels.Idle(world, 1);
            return world;
        }

        private static void Hit(DuelWorld world, float amount = 5f, int origin = 0, DamageKind kind = DamageKind.Direct, int generation = 0)
        {
            DamageRequest hit = TestWorlds.Hit(world, 0, 1, amount, kind);
            hit.OriginEventId = origin;
            hit.Generation = generation;
            world.ApplyDamage(hit);
        }

        // ----- proc budget (spec L126) -------------------------------------------------------------------------

        [Fact]
        public void An_Effect_Triggers_At_Most_Once_Per_Half_Second()
        {
            DuelWorld world = Ready();
            RecordAction taken = EffectTests.Record(world, 1, EffectTrigger.DamageTaken);
            Hit(world);
            WorldDuels.Idle(world, 10);
            Hit(world);
            WorldDuels.Idle(world, 10);
            Assert.Single(taken.Fired);
            WorldDuels.Idle(world, 15);
            Hit(world);
            WorldDuels.Idle(world, 1);
            Assert.Equal(2, taken.Fired.Count);
        }

        [Fact]
        public void An_Effect_Triggers_Once_Per_Original_Hit_Event_Even_Across_Beam_Ticks()
        {
            DuelWorld world = Ready();
            RecordAction taken = EffectTests.Record(world, 1, EffectTrigger.DamageTaken);
            for (int i = 0; i < 4; i++)
            {
                Hit(world, 3f, origin: 900); // one beam: every tick shares its origin event
                WorldDuels.IdleSeconds(world, 0.6f);
            }

            Assert.Single(taken.Fired);
        }

        [Fact]
        public void Secondary_Environment_Prop_And_Rule_Damage_Trigger_Nothing()
        {
            DuelWorld world = Ready();
            RecordAction taken = EffectTests.Record(world, 1, EffectTrigger.DamageTaken);
            RecordAction dealt = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt);
            Hit(world, 4f, generation: 1);
            WorldDuels.IdleSeconds(world, 0.6f);
            Hit(world, 4f, kind: DamageKind.Environment);
            WorldDuels.IdleSeconds(world, 0.6f);
            Hit(world, 4f, kind: DamageKind.Prop);
            WorldDuels.IdleSeconds(world, 0.6f);
            Hit(world, 4f, kind: DamageKind.Boundary);
            WorldDuels.IdleSeconds(world, 0.6f);
            Hit(world, 4f, kind: DamageKind.Recall);
            WorldDuels.IdleSeconds(world, 0.6f);
            Assert.Empty(taken.Fired);
            Assert.Empty(dealt.Fired);

            Hit(world, 4f, kind: DamageKind.Fall); // fall damage is original physical damage (spec L175)
            WorldDuels.Idle(world, 1);
            Assert.Single(taken.Fired);
            Assert.Empty(dealt.Fired);
        }

        [Fact]
        public void Effect_Outputs_Are_The_Single_Secondary_Generation_And_Raise_No_Further_Triggers()
        {
            DuelWorld world = Ready();
            var onShield = new RecordAction();
            Effect ward = new Effect("AUG-TEST", EffectTrigger.DamageTaken).With(new GrantShieldAction(10f, 60));
            world.Effects.Host(1).Attach(ward);
            world.Effects.Host(1).Attach(new Effect("AUG-TEST", EffectTrigger.ShieldAbsorbed).With(onShield));
            Hit(world);
            WorldDuels.Idle(world, 1);
            Assert.Equal(10f, world.Player(1).Shields.Total, 3);
            Assert.Empty(onShield.Fired);
            Assert.Equal(1, world.Effects.Host(1).ActivationCount(ward));
        }

        [Fact]
        public void Internal_Cooldowns_And_Activation_Limits_Hold()
        {
            DuelWorld world = Ready();
            RecordAction icd = EffectTests.Record(world, 1, EffectTrigger.DamageTaken, e => e.InternalCooldownTicks = Ticks.FromSeconds(2f));
            RecordAction once = EffectTests.Record(world, 1, EffectTrigger.DamageTaken, e => e.MaxActivations = 1);
            for (int i = 0; i < 6; i++)
            {
                Hit(world);
                WorldDuels.IdleSeconds(world, 0.6f);
            }

            Assert.Equal(2, icd.Fired.Count); // at 0 s and 2.4 s
            Assert.Single(once.Fired);
        }

        [Fact]
        public void Conditions_Gate_Activation()
        {
            DuelWorld world = Ready();
            RecordAction low = EffectTests.Record(world, 1, EffectTrigger.DamageTaken, e => e.Condition = Conditions.HealthFractionBelow(0.5f));
            Hit(world, 100f);
            WorldDuels.IdleSeconds(world, 0.6f);
            Assert.Empty(low.Fired);
            Hit(world, 100f);
            WorldDuels.Idle(world, 1);
            Assert.Single(low.Fired);
        }

        // ----- stacking --------------------------------------------------------------------------------------

        private static (DuelWorld world, Effect effect) Buffing(StackingRule rule, int maxStacks = 1, float durationSec = 2f)
        {
            DuelWorld world = Ready();
            var effect = new Effect("AUG-TEST", EffectTrigger.DamageTaken) { Stacking = rule, MaxStacks = maxStacks };
            effect.Actions.Add(new ModifyStatsAction(Ticks.FromSeconds(durationSec), new StatModifier(StatKind.Resistance, 0.05f, "AUG-TEST", DamageChannel.Physical)));
            world.Effects.Host(1).Attach(effect);
            return (world, effect);
        }

        [Fact]
        public void Refresh_Keeps_One_Instance_And_Restarts_Its_Duration()
        {
            (DuelWorld world, Effect effect) = Buffing(StackingRule.Refresh);
            Hit(world);
            WorldDuels.IdleSeconds(world, 1.5f);
            Hit(world);
            WorldDuels.IdleSeconds(world, 1.5f);
            Assert.Equal(1, world.Effects.Host(1).BuffInstances(effect));
            Assert.Equal(0.05f, world.Player(1).Stats.Resistance(DamageChannel.Physical), 4); // 3 s after the first hit, still active
            WorldDuels.IdleSeconds(world, 0.6f);
            Assert.Equal(0, world.Effects.Host(1).BuffInstances(effect));
            Assert.Equal(0f, world.Player(1).Stats.Resistance(DamageChannel.Physical), 4);
        }

        [Fact]
        public void Stack_Adds_Instances_Up_To_The_Limit_Under_The_Global_Cap()
        {
            (DuelWorld world, Effect effect) = Buffing(StackingRule.Stack, maxStacks: 3, durationSec: 8f);
            world.Player(1).Stats.Add(StatKind.Resistance, 0.25f, "EQP-TEST", DamageChannel.Physical);
            for (int i = 0; i < 5; i++)
            {
                Hit(world);
                WorldDuels.IdleSeconds(world, 0.55f);
            }

            Assert.Equal(3, world.Effects.Host(1).BuffInstances(effect));
            Assert.Equal(0.40f, world.Player(1).Stats.Raw(StatKind.Resistance, DamageChannel.Physical), 4);
            Assert.Equal(GlobalRules.Damage.ChannelResistanceCap, world.Player(1).Stats.Resistance(DamageChannel.Physical), 4);
        }

        [Fact]
        public void Keep_Existing_Ignores_Activations_While_Active_And_Replace_Starts_Fresh()
        {
            (DuelWorld keep, Effect kept) = Buffing(StackingRule.KeepExisting);
            Hit(keep);
            WorldDuels.IdleSeconds(keep, 1.5f);
            Hit(keep);
            WorldDuels.IdleSeconds(keep, 0.6f); // 2.1 s after the first: the ignored second hit did not extend it
            Assert.Equal(0, keep.Effects.Host(1).BuffInstances(kept));

            (DuelWorld replace, Effect replaced) = Buffing(StackingRule.Replace);
            Hit(replace);
            WorldDuels.IdleSeconds(replace, 1.5f);
            Hit(replace);
            WorldDuels.IdleSeconds(replace, 0.6f);
            Assert.Equal(1, replace.Effects.Host(1).BuffInstances(replaced));
        }

        // ----- conditional modifiers ---------------------------------------------------------------------------

        [Fact]
        public void A_Conditional_Modifier_Applies_Only_While_Its_Condition_Holds()
        {
            DuelWorld world = Ready();
            world.Effects.Host(1).Attach(new ConditionalModifier(Conditions.Airborne, new StatModifier(StatKind.PassiveReduction, -0.02f, "AUG-099")));
            Assert.Equal(1f, world.Player(1).Stats.PassiveMultiplier(DamageChannel.Physical), 4);
            world.Step(TestWorlds.A, WorldDuels.Jump(180f));
            WorldDuels.Idle(world, 5);
            Assert.False(world.Player(1).Motor.Grounded);
            Assert.Equal(1.02f, world.Player(1).Stats.PassiveMultiplier(DamageChannel.Physical), 4);
            WorldDuels.Idle(world, 60);
            Assert.Equal(1f, world.Player(1).Stats.PassiveMultiplier(DamageChannel.Physical), 4);
        }

        // ----- cast pricing and refunds (spec L90, L124) ------------------------------------------------------

        [Fact]
        public void Next_Cast_Bonuses_Reduce_One_Cast_Within_The_Seventy_Percent_Floor()
        {
            DuelWorld world = Ready();
            world.Player(0).Stats.Add(StatKind.CostReduction, 0.2f, "EQP-TEST");
            var bonus = new Effect("EQP-TEST2", EffectTrigger.Dodge);
            bonus.Actions.Add(new NextCastCostAction(4f, 0f, Ticks.FromSeconds(3f)));
            world.Effects.Host(0).Attach(bonus);
            world.Step(WorldDuels.Dodge(0f), TestWorlds.B);

            CastQuote quote = CastQuote.Of("SPL-009", 20f);
            CastPrice price = world.Effects.PriceCast(0, quote);
            Assert.Equal(4f, price.StatReduction, 4);
            Assert.Equal(4f, price.BonusReduction, 4);
            Assert.True(price.FloorBound);
            Assert.Equal(14f, price.Paid, 4); // at least 70% of 20 is paid

            Assert.True(world.Effects.TryCommitCast(0, quote, out CastReceipt receipt));
            Assert.Equal(86f, world.Player(0).Resources.Mana, 3);
            Assert.Equal(16f, world.Effects.PriceCast(0, quote).Paid, 4); // the bonus was consumed; the stat remains
        }

        [Fact]
        public void Cost_Drawbacks_Add_On_Top_And_Delayed_Bonuses_Wait()
        {
            DuelWorld world = Ready();
            var effect = new Effect("EQP-003", EffectTrigger.SpellSelectionChanged);
            effect.Actions.Add(new NextCastCostAction(3f, 0f, Ticks.FromSeconds(4f), delayTicks: Ticks.FromSeconds(1f)));
            world.Effects.Host(0).Attach(effect);
            var drawback = new Effect("EQP-003", EffectTrigger.CastCommitted);
            drawback.Actions.Add(new NextCastCostAction(-2f, 0f, 0, new CastFilter { SpellId = "SPL-009" }));
            world.Effects.Host(0).Attach(drawback);

            world.Step(WorldDuels.Select(0f, 1), TestWorlds.B);
            Assert.Equal(20f, world.Effects.PriceCast(0, CastQuote.Of("SPL-010", 20f)).Paid, 4); // not yet: "after 1 s"
            WorldDuels.Step(world, 60, WorldDuels.Select(0f, 1), TestWorlds.B);
            Assert.Equal(17f, world.Effects.PriceCast(0, CastQuote.Of("SPL-010", 20f)).Paid, 4);

            Assert.True(world.Effects.TryCommitCast(0, CastQuote.Of("SPL-009", 20f), out _));
            WorldDuels.Step(world, 1, WorldDuels.Select(0f, 1), TestWorlds.B);
            Assert.Equal(22f, world.Effects.PriceCast(0, CastQuote.Of("SPL-009", 20f)).Paid, 4); // "repeated casts ... cost 2 more"
            Assert.Equal(20f, world.Effects.PriceCast(0, CastQuote.Of("SPL-010", 20f)).Paid, 4);
        }

        [Fact]
        public void Refunds_Are_At_Most_A_Quarter_Of_The_Payment_And_Once_Per_Cast()
        {
            DuelWorld world = Ready();
            Assert.True(world.Effects.TryCommitCast(0, CastQuote.Of("SPL-009", 40f), out CastReceipt receipt));
            Assert.Equal(60f, world.Player(0).Resources.Mana, 3);
            Assert.Equal(10f, world.Effects.RefundCast(0, receipt.PaymentEventId, 30f, "AUG-008"), 3);
            Assert.Equal(0f, world.Effects.RefundCast(0, receipt.PaymentEventId, 5f, "AUG-008"), 3);
            Assert.Equal(70f, world.Player(0).Resources.Mana, 3);
            Assert.Equal(0f, world.Effects.RefundCast(1, receipt.PaymentEventId, 5f, "AUG-008"), 3); // not their cast
        }

        // ----- hit bonuses -----------------------------------------------------------------------------------

        [Fact]
        public void Next_Hit_Bonuses_Apply_To_One_Original_Hit_And_Damage_Stats_To_Every_One()
        {
            DuelWorld world = Ready();
            world.Player(0).Stats.Add(StatKind.SpellDamageBonus, 0.1f, "EQP-TEST");
            var primed = new Effect("AUG-TEST", EffectTrigger.Jump);
            primed.Actions.Add(new NextHitDamageAction(0.5f, 0f, Ticks.FromSeconds(3f), new HitFilter { SpellHitsOnly = true }));
            world.Effects.Host(0).Attach(primed);
            world.Step(WorldDuels.Jump(0f), TestWorlds.B);

            Hit(world, 10f, kind: DamageKind.Environment); // secondary damage neither uses nor consumes the bonus
            Assert.Equal(10f, LastDealt(world), 3);
            Hit(world, 10f);
            Assert.Equal(16.5f, LastDealt(world), 3); // 10 x 1.1 x 1.5
            Hit(world, 10f);
            Assert.Equal(11f, LastDealt(world), 3);
            Assert.Equal(0, world.Effects.Host(0).PendingHitBonuses);
        }

        private static float LastDealt(DuelWorld world) => world.TickEvents.OfType<DamageEvent>().Last().Dealt;

        // ----- actions and validation ------------------------------------------------------------------------

        [Fact]
        public void Triggered_Shields_Last_At_Most_Eight_Seconds_And_Reserve_Shields_Need_Round_Start()
        {
            DuelWorld world = Ready();
            world.Effects.Host(1).Attach(new Effect("AUG-TEST", EffectTrigger.DamageTaken).With(new GrantShieldAction(30f, Ticks.FromSeconds(20f))));
            Hit(world, 1f);
            WorldDuels.Idle(world, 1);
            Assert.Equal(30f, world.Player(1).Shields.Total, 3);
            WorldDuels.IdleSeconds(world, GlobalRules.Shields.MaxTimedDurationSec);
            Assert.Equal(0f, world.Player(1).Shields.Total, 3);

            var reserveOnHit = new Effect("AUG-TEST", EffectTrigger.DamageTaken).With(new GrantShieldAction(10f, 0, reserve: true));
            Assert.Throws<InvalidOperationException>(() => world.Effects.Host(1).Attach(reserveOnHit));
        }

        [Fact]
        public void Resource_Gains_Never_Exceed_The_Pool_Maximum()
        {
            DuelWorld world = Ready();
            world.Effects.Host(0).Attach(new Effect("AUG-102", EffectTrigger.Dodge).With(new GainResourceAction(EffectResource.Stamina, 40f)));
            world.Step(WorldDuels.Dodge(0f), TestWorlds.B);
            Assert.Equal(world.Player(0).Resources.MaxStamina, world.Player(0).Resources.Stamina, 3); // 75 + 40 capped at 100
            Assert.Contains(world.TickEvents.OfType<EffectEvent>(), e => e.Change == EffectChange.StaminaGained && e.Amount == 25f);
        }

        [Fact]
        public void Effects_Declare_The_Secondary_Events_Their_Actions_Produce()
        {
            DuelWorld world = Ready();
            var shieldOnly = new Effect("AUG-TEST", EffectTrigger.Jump) { PermittedSecondary = SecondaryEvents.Shield };
            shieldOnly.Actions.Add(new GainResourceAction(EffectResource.Mana, 5f));
            Assert.Throws<InvalidOperationException>(() => world.Effects.Host(0).Attach(shieldOnly));
            Assert.Throws<InvalidOperationException>(() => world.Effects.Host(0).Attach(new Effect("AUG-TEST", EffectTrigger.Jump)));
            Assert.Throws<InvalidOperationException>(() => world.Effects.Host(0).Attach(new Effect("AUG-TEST", EffectTrigger.Periodic).With(new CueAction("x"))));

            var cue = new Effect("AUG-TEST", EffectTrigger.Jump).With(new CueAction("ward-glyph"));
            Assert.Equal(SecondaryEvents.Cue, cue.PermittedSecondary);
            world.Effects.Host(0).Attach(cue);
            world.Step(WorldDuels.Jump(0f), TestWorlds.B);
            Assert.Contains(world.TickEvents.OfType<EffectEvent>(), e => e.Change == EffectChange.Cue && e.Cue == "ward-glyph");
            Assert.Contains(world.TickEvents.OfType<EffectEvent>(), e => e.Change == EffectChange.Activated && e.EffectKey == cue.Key);
        }
    }
}
