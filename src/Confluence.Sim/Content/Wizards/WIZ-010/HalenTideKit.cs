using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Effects;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// WIZ-010 Halen Tide, "The Breathless Diver": the pressure-metering grammar lives in Confluence.Casting
    /// (<c>PressureMeterGrammar</c>, D-057); this kit behavior supplies the signature technique and the passive.
    /// <list type="bullet">
    /// <item>Signature, Undertow Mantle: "17 s cooldown, 20 mana, 0.25 s windup. Create a neutral 2 m radius Water eddy
    /// centered at the cast location for 3 s. Enemies and caster within it are slowed 15%; each body takes only 12 arcane
    /// damage on first entry. Water strength 1; one field allocation." Activated with its own binding, no grammar (spec
    /// L472). One gameplay field (spec L128), neutral: the caster is slowed and damaged like an enemy (L128 "Ownership ...
    /// does not grant immunity"; weakness "the eddy hinders him as well"). Field contact is area damage, never guarded
    /// (spec L104); each body takes the 12 at most once per cast however often it re-enters. Water 1 over the eddy for its
    /// lifetime, so heat makes steam and frost freezes it (counterplay "use Heat or Frost to alter its water").</item>
    /// <item>Passive, Pressure Reserve: "after spending at least 30 mana on one committed spell, gain 10 stamina, once per
    /// 6 s. Refunds and subsequent reaction costs cannot retrigger it." An effect-framework trigger on the paid
    /// commitment of an exclusive spell (the amount actually paid after reductions, spec L90, L124), with a 6 s internal
    /// cooldown inside the global proc budget (spec L126). Refunds are mana gains (no trigger); recast surcharges and
    /// other later costs are not commitments, so they never count.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HalenTideKit : IWizardKitBehavior
    {
        public const string Id = HalenRules.WizardId;

        public static readonly ProseParameter SignatureCooldown = ProseParameter.Of("signature_cooldown", 17, "17 s cooldown");
        public static readonly ProseParameter SignatureMana = ProseParameter.Of("signature_mana", 20, "20 mana");
        public static readonly ProseParameter SignatureWindup = ProseParameter.Of("signature_windup", 0.25, "0.25 s windup");
        public static readonly ProseParameter EddyRadius = ProseParameter.Of("eddy_radius", 2, "2 m radius Water eddy");
        public static readonly ProseParameter EddyLifetime = ProseParameter.Of("eddy_lifetime", 3, "centered at the cast location for 3 s");
        public static readonly ProseParameter EddySlow = ProseParameter.Percent("eddy_slow", 15, "slowed 15%");
        public static readonly ProseParameter EddyDamage = ProseParameter.Of("eddy_damage", 12, "12 arcane damage on first entry");
        public static readonly ProseParameter EddyWater = ProseParameter.Of("eddy_water", 1, "Water strength 1");
        public static readonly ProseParameter PassiveMana = ProseParameter.Of("passive_min_mana", 30, "at least 30 mana on one committed spell");
        public static readonly ProseParameter PassiveStamina = ProseParameter.Of("passive_stamina", 10, "gain 10 stamina");
        public static readonly ProseParameter PassiveInterval = ProseParameter.Of("passive_interval", 6, "once per 6 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[]
        {
            SignatureCooldown, SignatureMana, SignatureWindup, EddyRadius, EddyLifetime, EddySlow, EddyDamage, EddyWater,
            PassiveMana, PassiveStamina, PassiveInterval,
        };

        public IReadOnlyList<ProseParameter> Parameters => All;

        /// <summary>Signature parameters come from the wizard record's signature text.</summary>
        public static IReadOnlyList<ProseParameter> SignatureParameters => new[]
        {
            SignatureCooldown, SignatureMana, SignatureWindup, EddyRadius, EddyLifetime, EddySlow, EddyDamage, EddyWater,
        };

        /// <summary>Passive parameters come from the wizard record's passive text.</summary>
        public static IReadOnlyList<ProseParameter> PassiveParameters => new[] { PassiveMana, PassiveStamina, PassiveInterval };

        public void BuildSignature(SpellFormBuilder form)
        {
            form.Mana = SignatureMana.Float;
            form.CooldownSec = SignatureCooldown.Float;
            form.CastTimeSec = SignatureWindup.Float;
            form.Range = 0f; // centered on the caster (spec L525 self-centered actions use range zero)
            form.Channel = DamageChannel.Arcane;
            form.Payload.Channel = DamageChannel.Arcane;

            // A major field for recovery and movement while casting (spec L92), and the school it belongs to.
            form.Tags.Add("field");
            form.Tags.Add("water");
            form.Tags.Add("school:water");

            var slow = new PayloadSpec { Channel = DamageChannel.Arcane };
            slow.AddStatus(StatusKind.Slow, HalenRules.EddySlowDurationSec, EddySlow.Float);
            form.Delivery = new FieldDelivery
            {
                Shape = FieldShape.Circle,
                Radius = EddyRadius.Float,
                Placement = PlacementMode.AtCaster,
                LifetimeSec = EddyLifetime.Float,
                AffectsOwner = true, // "neutral": enemies and caster
                TickIntervalSec = HalenRules.EddySlowIntervalSec,
                EnterPayload = new PayloadSpec
                {
                    Channel = DamageChannel.Arcane,
                    Direct = EddyDamage.Float,
                    MaxPerTarget = EddyDamage.Float, // "each body takes only 12 arcane damage on first entry"
                    GuardEligible = false,
                },
                TickPayload = slow,
            };

            form.Payload.AddDeposit(DepositKind.Water, (int)EddyWater.Value, DepositPlacement.Footprint);
        }

        public void AttachPassive(IEffectHost host)
        {
            ContentArgs.Require(host);
            var reserve = new Effect(Id, EffectTrigger.CastCommitted)
            {
                Label = "Pressure Reserve",
                InternalCooldownTicks = Ticks.FromSeconds(PassiveInterval.Value),
                Condition = Conditions.When("the payment commits an exclusive spell", IsSpellCommitment),
            };
            reserve.Filter.MinAmount = PassiveMana.Float;
            reserve.Actions.Add(new GainResourceAction(EffectResource.Stamina, PassiveStamina.Float));
            host.Attach(reserve);
        }

        /// <summary>
        /// The trigger's payment is the commitment of one of the player's exclusive spells: its source is an SPL- record and
        /// the cast machine's current committed cast paid exactly that amount (a recast surcharge or a later cost is a
        /// separate, smaller payment and never matches).
        /// </summary>
        private static bool IsSpellCommitment(in EffectContext c)
        {
            string source = c.Trigger.SourceId;
            if (source == null || !source.StartsWith("SPL-", StringComparison.Ordinal)) return false;
            PlayerCaster caster = c.Self.Caster;
            if (caster == null || !caster.Machine.CurrentCastId.IsValid) return false;
            return string.Equals(caster.Machine.CurrentSpell?.Id, source, StringComparison.Ordinal)
                && Math.Abs(caster.Machine.CurrentPaidMana - c.Trigger.Amount) < 1e-3f;
        }
    }
}
