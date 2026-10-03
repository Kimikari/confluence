using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Content;
using Confluence.Sim.Content.SpellBehaviors;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Spell form resolution: record numbers, transformation order and exclusivity, structural tags, caps, cues.</summary>
    public class SpellFormTests
    {
        /// <summary>A transformation that logs its application and adds one direct damage.</summary>
        private sealed class LoggingTransform : ISpellTransform
        {
            private readonly List<string> _log;
            private readonly string _name;

            public LoggingTransform(TransformStage stage, string name, List<string> log)
            {
                Stage = stage;
                _name = name;
                _log = log;
            }

            public TransformStage Stage { get; }

            public IReadOnlyList<ProseParameter> Parameters => Array.Empty<ProseParameter>();

            public void Apply(SpellFormBuilder form, int rank)
            {
                _log.Add(_name + "@" + rank);
                form.Payload.Direct += 1f;
            }
        }

        private static SpellDef Def(string id) => SpellTestKit.Catalogue.Spell(id);

        [Fact]
        public void A_Form_Takes_Mana_Cooldown_Windup_Range_And_Channel_From_The_Record()
        {
            SpellForm form = SpellFormResolver.Resolve(Def("SPL-009"), new Fireball());
            Assert.Equal(18f, form.Mana);
            Assert.Equal(5f, form.CooldownSec);
            Assert.Equal(0.35f, form.CastTimeSec, 4);
            Assert.Equal(30f, form.Range);
            Assert.Equal(DamageChannel.Thermal, form.Channel);
            Assert.Equal(DamageChannel.Thermal, form.Payload.Channel);
            Assert.True(form.HasTag("projectile"));

            CastableSpell c = form.Castable;
            Assert.Equal("SPL-009", c.Id);
            Assert.Equal(18f, c.Mana);
            Assert.Equal(Ticks.FromSeconds(5.0), c.CooldownTicks);
            Assert.Equal(Ticks.FromSeconds(0.35), c.WindupTicks);
            Assert.Equal(CastWeight.Quick, c.Weight);
            Assert.True(c.IsDamaging);
            Assert.True(c.IsProjectile);
            Assert.Equal(0, c.KitIndex);
        }

        [Fact]
        public void A_Resolved_Form_Is_Immutable()
        {
            SpellForm form = SpellFormResolver.Resolve(Def("SPL-009"), new Fireball());
            Assert.Throws<InvalidOperationException>(() => form.Payload.Direct = 1f);
            Assert.Throws<InvalidOperationException>(() => ((ProjectileDelivery)form.Delivery).Speed = 1f);
            Assert.Throws<InvalidOperationException>(() => form.Payload.AddDeposit(DepositKind.Water, 1, DepositPlacement.Impact));
            Assert.Throws<InvalidOperationException>(() => form.Payload.Deposits[0].Strength = 4);
        }

        [Fact]
        public void Transformations_Apply_By_Stage_Then_Ascending_Augment_Id()
        {
            var log = new List<string>();
            var transforms = new[]
            {
                new ActiveTransform("AUG-940", 1, new LoggingTransform(TransformStage.Equipment, "equipment", log)),
                new ActiveTransform("AUG-930", 2, new LoggingTransform(TransformStage.Payload, "payload", log)),
                new ActiveTransform("AUG-920", 1, new LoggingTransform(TransformStage.Delivery, "delivery-b", log)),
                new ActiveTransform("AUG-910", 1, new LoggingTransform(TransformStage.Delivery, "delivery-a", log)),
            };
            SpellForm form = SpellFormResolver.Resolve(Def("SPL-009"), new Fireball(), transforms);
            Assert.Equal(new[] { "delivery-a@1", "delivery-b@1", "payload@2", "equipment@1" }, log); // spec L286, L510
            Assert.Equal(new[] { "AUG-910", "AUG-920", "AUG-930", "AUG-940" }, form.AppliedTransforms);
            Assert.Equal(52f, form.Payload.Direct);
            Assert.True(form.Castable.RequiresAnticipatoryCue == false); // 52 is below the 70 cue threshold
        }

        [Fact]
        public void Two_Branches_Of_One_Exclusive_Group_Cannot_Coexist()
        {
            var log = new List<string>();
            var transforms = new[]
            {
                new ActiveTransform("AUG-950", 1, new LoggingTransform(TransformStage.Delivery, "split", log), "trajectory"),
                new ActiveTransform("AUG-951", 1, new LoggingTransform(TransformStage.Delivery, "ricochet", log), "trajectory"),
            };
            Assert.Throws<InvalidOperationException>(() => SpellFormResolver.Resolve(Def("SPL-009"), new Fireball(), transforms));
            Assert.Throws<InvalidOperationException>(() => SpellFormResolver.Resolve(Def("SPL-009"), new Fireball(),
                new[] { transforms[0], new ActiveTransform("AUG-950", 2, new LoggingTransform(TransformStage.Delivery, "again", log)) }));
        }

        [Fact]
        public void A_Declared_Structural_Delta_Changes_The_Forms_Tags()
        {
            StructuralDeltas.Register("AUG-960", "SPL-009", new[] { "trap", "field" }, new[] { "projectile" });
            var log = new List<string>();
            SpellForm form = SpellFormResolver.Resolve(Def("SPL-009"), new Fireball(),
                new[] { new ActiveTransform("AUG-960", 1, new LoggingTransform(TransformStage.Delivery, "trap", log)) });
            Assert.False(form.HasTag("projectile")); // D-012
            Assert.True(form.HasTag("trap"));
            Assert.True(form.CountsAsField);
        }

        [Fact]
        public void Global_Caps_Clamp_Channel_Push_Precision_Statuses_And_Deposit_Strength()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T70");
            b.Delivery = new ConeDelivery { HalfAngleDeg = 45f, Reach = 4f };
            b.ChannelSec = 5f;
            b.Payload.Direct = 10f;
            b.Payload.Push = 6f;
            b.Payload.PrecisionMultiplier = 2f;
            b.Payload.AddStatus(StatusKind.Stun, 2f);
            b.Payload.AddStatus(StatusKind.Slow, 6f, 0.5f);
            b.Payload.AddDeposit(DepositKind.Heat, 7, DepositPlacement.Footprint);
            SpellForm form = b.Build();
            Assert.Equal(CastingRules.MaxChannelTicks, form.Castable.ChannelMaxTicks);                       // spec L92
            Assert.Equal(GlobalRules.Terrain.PlayerKnockbackCap, form.Payload.Push);                          // spec L173
            Assert.Equal(GlobalRules.Damage.PrecisionMultiplierMax, form.Payload.PrecisionMultiplier);       // spec L110
            Assert.Equal(GlobalRules.Control.HardControlMaxSec, form.Payload.Statuses[0].DurationSec, 3);    // spec L116
            Assert.Equal(GlobalRules.Control.SlowMaxDurationSec, form.Payload.Statuses[1].DurationSec, 3);
            Assert.Equal(GlobalRules.Control.SlowCap, form.Payload.Statuses[1].Intensity, 3);
            Assert.Equal(GlobalRules.World.MaxUnitsPerCell, form.Payload.Deposits[0].Strength);              // spec L163
        }

        [Fact]
        public void The_Anticipatory_Cue_Covers_Big_Hits_Hard_Control_And_Teleports()
        {
            SpellForm Make(Action<SpellFormBuilder> edit)
            {
                SpellFormBuilder b = SpellTestKit.Form("SPL-T71");
                b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 30f };
                edit(b);
                return b.Build();
            }

            Assert.False(Make(b => b.Payload.Direct = 70f).Castable.RequiresAnticipatoryCue);
            Assert.True(Make(b => b.Payload.Direct = 71f).Castable.RequiresAnticipatoryCue); // spec L96: above 70 direct
            Assert.True(Make(b => b.Payload.AddStatus(StatusKind.Root, 0.5f)).Castable.RequiresAnticipatoryCue);
            Assert.False(Make(b => b.Payload.AddStatus(StatusKind.Slow, 1f, 0.2f)).Castable.RequiresAnticipatoryCue);
            Assert.True(Make(b => b.Delivery = new MovementDelivery { Mode = DeliveryKind.Teleport, Distance = 5f }).Castable.RequiresAnticipatoryCue);
        }

        [Fact]
        public void Mastery_Multiplies_Only_The_Primary_Direct_Payload_Within_Its_Cap()
        {
            SpellForm form = SpellFormResolver.Resolve(Def("SPL-009"), new Fireball());
            var cast = new CastRecord(form, 0, 0, new CastEventId(0, 1), MasteryResult.Mastered(MasteryReward.DirectDamage, 0.12f), 0.1f, 1, 1,
                default, default);
            Assert.Equal(1.15f, cast.DirectDamageMultiplier, 4); // 12% + 10% linked, clamped at 15% (spec L124, L480)
            Assert.Equal(1f, cast.ProjectileSpeedMultiplier);
            var efficiency = new CastRecord(form, 0, 0, new CastEventId(0, 2), MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.12f), 0f, 1, 1,
                default, default);
            Assert.Equal(1f, efficiency.DirectDamageMultiplier);
        }

        [Fact]
        public void A_Delivery_Without_Required_Numbers_Is_Refused_When_The_Form_Builds()
        {
            SpellFormBuilder noSpeed = SpellTestKit.Form("SPL-T72");
            noSpeed.Delivery = new ProjectileDelivery { MaxRange = 10f };
            Assert.Throws<InvalidOperationException>(() => noSpeed.Build());
            SpellFormBuilder noDelivery = SpellTestKit.Form("SPL-T73");
            Assert.Throws<InvalidOperationException>(() => noDelivery.Build());
        }
    }
}
