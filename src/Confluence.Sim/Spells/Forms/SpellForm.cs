using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Sim.Spells
{
    /// <summary>When a delivery reads the caster's aim.</summary>
    public enum AimLock
    {
        /// <summary>The aim at release (aim stays live through the windup; spec L474).</summary>
        AtRelease,

        /// <summary>The aim at commitment ("fixed direction selected at windup", teleport destinations shown by the cue).</summary>
        AtCommit,
    }

    /// <summary>Order in which transformations edit a form (spec L286, L510, L543).</summary>
    public enum TransformStage
    {
        /// <summary>Chosen delivery or trajectory (shape, travel, split, bounce).</summary>
        Delivery = 1,

        /// <summary>Reaction or payload modifiers.</summary>
        Payload = 2,

        /// <summary>Equipment modifiers (trajectory modes, parameter modifiers).</summary>
        Equipment = 3,
    }

    /// <summary>
    /// A resolved spell for one duel: the record's numeric fields, its typed delivery and payload, the prose parameters
    /// it declared, applied transformations, and the cast timing the machine uses. Built once per player per duel in the
    /// spec order base, delivery/trajectory, payload/reaction, equipment, then global caps (spec L286); mastery is
    /// applied per cast at commitment. Immutable after resolution.
    /// </summary>
    public sealed class SpellForm
    {
        internal SpellForm(SpellFormBuilder b, CastableSpell castable)
        {
            SpellId = b.SpellId;
            WizardId = b.WizardId;
            Definition = b.Definition;
            IsSignature = b.IsSignature;
            Mana = b.Mana;
            CooldownSec = b.CooldownSec;
            CastTimeSec = b.CastTimeSec;
            Range = b.Range;
            Channel = b.Channel;
            Tags = Array.AsReadOnly(b.Tags.ToArray());
            Delivery = b.Delivery;
            Payload = b.Payload;
            Script = b.Script;
            AimLock = b.AimLock;
            Parameters = Array.AsReadOnly(b.Parameters.ToArray());
            AppliedTransforms = Array.AsReadOnly(b.AppliedTransforms.ToArray());
            Castable = castable;
        }

        /// <summary>Catalogue id of the spell, or the wizard id for a signature technique.</summary>
        public string SpellId { get; }

        public string WizardId { get; }

        /// <summary>The base record (null for a signature technique).</summary>
        public SpellDef Definition { get; }

        public bool IsSignature { get; }

        public float Mana { get; }
        public float CooldownSec { get; }
        public float CastTimeSec { get; }

        /// <summary>Maximum valid targeting or travel range in meters (spec L525).</summary>
        public float Range { get; }

        public DamageChannel Channel { get; }

        /// <summary>Structural tags after transformation deltas (D-012).</summary>
        public IReadOnlyList<string> Tags { get; }

        public DeliverySpec Delivery { get; }

        public PayloadSpec Payload { get; }

        public SpellScript Script { get; }

        public AimLock AimLock { get; }

        /// <summary>Every prose number the behavior and its transformations declared (D-011).</summary>
        public IReadOnlyList<ProseParameter> Parameters { get; }

        /// <summary>Applied transformation augment ids in application order.</summary>
        public IReadOnlyList<string> AppliedTransforms { get; }

        /// <summary>Cost and timing data for the cast machine (mana, cooldown, windup, recovery, channel, recast, cue).</summary>
        public CastableSpell Castable { get; }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The declared prose parameter with this name, or null.</summary>
        public ProseParameter Parameter(string name)
        {
            for (int i = Parameters.Count - 1; i >= 0; i--)
                if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal)) return Parameters[i];
            return null;
        }

        /// <summary>Whether ownership limits count this form's construct as a field (fields and traps, spec L128, L510).</summary>
        public bool CountsAsField => HasTag("field") || HasTag("trap");

        public override string ToString() => SpellId + " (" + (Delivery == null ? "no delivery" : Delivery.Kind.ToString()) + ")";
    }

    /// <summary>
    /// Mutable form under construction. The spell behavior fills it from the catalogue record, transformations edit it
    /// in stage order, and <see cref="Build"/> applies the global caps, derives the cast data, and freezes everything.
    /// </summary>
    public sealed class SpellFormBuilder
    {
        private bool _built;

        private SpellFormBuilder(string spellId, string wizardId, SpellDef definition, bool signature)
        {
            SpellId = spellId;
            WizardId = wizardId;
            Definition = definition;
            IsSignature = signature;
        }

        /// <summary>A form seeded with the record's numeric fields (mana, cooldown, cast time, range, channel; spec L525, D-011).</summary>
        public static SpellFormBuilder FromDefinition(SpellDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var b = new SpellFormBuilder(def.Id, def.WizardId, def, false)
            {
                Mana = (float)def.Mana,
                CooldownSec = (float)def.Cooldown,
                CastTimeSec = (float)def.CastTime,
                Range = (float)def.Range,
                Channel = def.HasValidChannel ? def.Channel : DamageChannel.None,
                KitIndex = (ContentIds.NumberOf(def.Id) - 1) % GlobalRules.Loadout.SpellsPerWizard,
            };
            b.Tags.AddRange(def.Tags);
            b.Payload.Channel = b.Channel;
            return b;
        }

        /// <summary>A signature technique of a wizard; its listed numbers are prose parameters of the wizard record (spec L472).</summary>
        public static SpellFormBuilder ForSignature(string wizardId)
        {
            if (string.IsNullOrEmpty(wizardId)) throw new ArgumentException("A wizard id is required.", nameof(wizardId));
            return new SpellFormBuilder(wizardId, wizardId, null, true) { KitIndex = -1 };
        }

        /// <summary>A form for tests and tools that has no catalogue record.</summary>
        public static SpellFormBuilder Synthetic(string spellId, string wizardId = null) =>
            new SpellFormBuilder(spellId, wizardId, null, false);

        public string SpellId { get; }
        public string WizardId { get; }
        public SpellDef Definition { get; }
        public bool IsSignature { get; }
        public int KitIndex { get; set; } = -1;

        public float Mana { get; set; }
        public float CooldownSec { get; set; }

        /// <summary>Intrinsic windup after preparation (spec L92, L525).</summary>
        public float CastTimeSec { get; set; }

        public float Range { get; set; }
        public DamageChannel Channel { get; set; }

        /// <summary>A record-stated movement factor while winding up or channeling (0-1 of walk speed); negative = class default.</summary>
        public float CastingMoveFactor { get; set; } = -1f;

        /// <summary>The record requires standing still while casting (spec L92).</summary>
        public bool Stationary { get; set; }

        /// <summary>A record-stated recovery in seconds; never below the class default (spec L92); 0 = default.</summary>
        public float RecoverySec { get; set; }

        /// <summary>Channel length after release (0 = none), at most 3 s (spec L92), and seconds between pulses.</summary>
        public float ChannelSec { get; set; }

        public float ChannelPulseSec { get; set; } = 0.5f;

        public RecastRule Recast { get; set; }

        /// <summary>The universal interrupt-refund tag (spec L90).</summary>
        public bool InterruptRefund { get; set; }

        public AimLock AimLock { get; set; }

        public List<string> Tags { get; } = new List<string>();

        public DeliverySpec Delivery { get; set; }

        public PayloadSpec Payload { get; set; } = new PayloadSpec();

        /// <summary>Custom hooks (recast detonations, conditional follow-ups) beyond the delivery primitive.</summary>
        public SpellScript Script { get; set; }

        public List<ProseParameter> Parameters { get; } = new List<ProseParameter>();

        public List<string> AppliedTransforms { get; } = new List<string>();

        public bool HasTag(string tag) => Tags.Contains(tag);

        /// <summary>Applies the global ceilings, derives the cast data, and freezes the form (spec L286 "then global caps").</summary>
        public SpellForm Build()
        {
            if (_built) throw new InvalidOperationException("A spell form is built once.");
            if (Delivery == null) throw new InvalidOperationException(SpellId + " declares no delivery.");
            _built = true;

            ChannelSec = Math.Max(0f, Math.Min(ChannelSec, GlobalRules.Casting.MaxChannelDurationSec)); // spec L92
            Payload.ApplyGlobalCaps();
            Delivery.ApplyGlobalCaps();
            Payload.Freeze();
            Delivery.Freeze();
            return new SpellForm(this, BuildCastable());
        }

        private CastableSpell BuildCastable()
        {
            var tags = Array.AsReadOnly(Tags.ToArray());
            CastWeight weight = CastableSpell.Classify(tags, CastTimeSec);
            int windup = Ticks.FromSeconds(CastTimeSec);
            CastableSpell cast = IsSignature
                ? CastableSpell.Signature(SpellId, WizardId, Mana, CooldownSec, CastTimeSec, weight, Payload.IsDamaging)
                : new CastableSpell(SpellId, Mana, Ticks.FromSeconds(CooldownSec), windup, weight, Payload.IsDamaging,
                    Delivery.Kind == DeliveryKind.Projectile, KitIndex, WizardId, tags);
            if (CastingMoveFactor >= 0f) cast = cast.WithMoveSpeedFactor(Math.Min(1f, CastingMoveFactor));
            if (Stationary) cast = cast.WithStationary(true);
            int recovery = Ticks.FromSeconds(RecoverySec);
            if (recovery > cast.RecoveryTicks) cast = cast.WithRecoveryTicks(recovery);
            if (ChannelSec > 0f) cast = cast.WithChannel(Ticks.FromSeconds(ChannelSec), Math.Max(1, Ticks.FromSeconds(ChannelPulseSec)));
            if (Recast != null) cast = cast.WithRecast(Recast);
            if (InterruptRefund || Tags.Contains(CastingRules.InterruptRefundTag)) cast = cast.WithInterruptRefund(true);
            return cast.WithAnticipatoryCue(RequiresCue());
        }

        /// <summary>
        /// Spec L96: a threat above 70 direct damage (the primary payload at its stated weak-point multiplier), a hard
        /// control payload, or a teleport destination always gets the 0.25 s anticipatory cue.
        /// </summary>
        private bool RequiresCue()
        {
            float threat = Payload.Direct * Payload.PrecisionMultiplier;
            return threat > GlobalRules.Casting.CueDirectDamageThreshold || Payload.AppliesHardControl || Delivery.RequiresCue;
        }
    }
}
