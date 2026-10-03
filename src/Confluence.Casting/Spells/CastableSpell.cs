using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Casting
{
    /// <summary>
    /// The timing and cost data the cast machine needs for one equipped spell or signature. Built from the
    /// catalogue record (<see cref="FromDef"/>) and then adjusted by the simulation for transformations and records
    /// that state their own recovery, channel, stationary requirement, or recast (spec L92-L96, L525, L541).
    /// Immutable; the With* methods return adjusted copies.
    /// </summary>
    public sealed class CastableSpell
    {
        private static readonly IReadOnlyList<string> NoTags = Array.AsReadOnly(new string[0]);

        public CastableSpell(string id, float mana, int cooldownTicks, int windupTicks, CastWeight weight, bool isDamaging,
            bool isProjectile = false, int kitIndex = -1, string wizardId = null, IReadOnlyList<string> tags = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Spell id is required.", nameof(id));
            if (mana < 0f) throw new ArgumentOutOfRangeException(nameof(mana));
            if (cooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(cooldownTicks));
            if (windupTicks < 0) throw new ArgumentOutOfRangeException(nameof(windupTicks));
            Id = id;
            WizardId = wizardId;
            KitIndex = kitIndex;
            Mana = mana;
            CooldownTicks = cooldownTicks;
            WindupTicks = windupTicks;
            Weight = weight;
            IsDamaging = isDamaging;
            IsProjectile = isProjectile;
            Tags = tags ?? NoTags;
            RecoveryTicks = CastWeights.DefaultRecoveryTicks(weight);
            MoveSpeedFactor = CastWeights.MoveSpeedFactor(weight);
            Parameters = GrammarParameters.Empty;
        }

        private CastableSpell(CastableSpell s)
        {
            Id = s.Id;
            WizardId = s.WizardId;
            KitIndex = s.KitIndex;
            IsSignature = s.IsSignature;
            Mana = s.Mana;
            CooldownTicks = s.CooldownTicks;
            WindupTicks = s.WindupTicks;
            Weight = s.Weight;
            RecoveryTicks = s.RecoveryTicks;
            MoveSpeedFactor = s.MoveSpeedFactor;
            IsStationary = s.IsStationary;
            ChannelMaxTicks = s.ChannelMaxTicks;
            ChannelPulseTicks = s.ChannelPulseTicks;
            HasInterruptRefund = s.HasInterruptRefund;
            RequiresAnticipatoryCue = s.RequiresAnticipatoryCue;
            IsDamaging = s.IsDamaging;
            IsProjectile = s.IsProjectile;
            Recast = s.Recast;
            Tags = s.Tags;
            Parameters = s.Parameters;
        }

        public string Id { get; }

        /// <summary>Owning wizard, when known.</summary>
        public string WizardId { get; }

        /// <summary>Local index 0-7 in the owner's kit (fixes per-spell phrases, motifs, layouts); -1 for a signature.</summary>
        public int KitIndex { get; }

        /// <summary>Signature techniques use their listed activation and windup, never a grammar (spec L472).</summary>
        public bool IsSignature { get; private set; }

        /// <summary>Listed mana, paid at commitment before reductions (spec L90, L525).</summary>
        public float Mana { get; private set; }

        public int CooldownTicks { get; private set; }

        /// <summary>Intrinsic windup after preparation (the record's cast_time; 0 = instant; spec L92, L525).</summary>
        public int WindupTicks { get; private set; }

        public CastWeight Weight { get; private set; }

        /// <summary>Recovery after release or channel end; never shorter than the class default (spec L92).</summary>
        public int RecoveryTicks { get; private set; }

        /// <summary>Fraction of walk speed from commitment through windup and channel (spec L92).</summary>
        public float MoveSpeedFactor { get; private set; }

        /// <summary>The record requires standing still while casting; overrides the move factor (spec L92).</summary>
        public bool IsStationary { get; private set; }

        /// <summary>Maximum channel length after release (0 = not channeled), at most 3 s (spec L92).</summary>
        public int ChannelMaxTicks { get; private set; }

        /// <summary>Ticks between channel pulse events.</summary>
        public int ChannelPulseTicks { get; private set; } = 1;

        public bool IsChanneled => ChannelMaxTicks > 0;

        /// <summary>The universal interrupt-refund tag: interrupting the committed cast refunds 25% mana (spec L90).</summary>
        public bool HasInterruptRefund { get; private set; }

        /// <summary>Above 70 direct damage, hard control, or a teleport destination: 0.25 s minimum cue (spec L96).</summary>
        public bool RequiresAnticipatoryCue { get; private set; }

        /// <summary>Damaging spells take damage rewards, nondamaging ones efficiency where a grammar offers the choice.</summary>
        public bool IsDamaging { get; }

        public bool IsProjectile { get; }

        /// <summary>Recast allowance, or null when the spell has none (spec L94).</summary>
        public RecastRule Recast { get; private set; }

        /// <summary>Quick casts stay compatible with sprint; others suppress it while casting (spec L80).</summary>
        public bool IsSprintCompatible => Weight == CastWeight.Quick && !IsChanneled;

        /// <summary>Move factor actually applied while casting.</summary>
        public float CastingMoveSpeedFactor => IsStationary ? 0f : MoveSpeedFactor;

        /// <summary>Minimum ticks between the first visible cue and release.</summary>
        public int MinimumCueTicks => RequiresAnticipatoryCue ? CastingRules.AnticipatoryCueTicks : 0;

        public IReadOnlyList<string> Tags { get; }

        public GrammarParameters Parameters { get; private set; }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Base cast data for a catalogue spell. Mana, cooldown, and cast time come from the record (spec L525); the
        /// recovery class and cue requirement come from its tags and damage expression (spec L92, L96).
        /// </summary>
        public static CastableSpell FromDef(SpellDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            int windup = Ticks.FromSeconds(def.CastTime);
            var spell = new CastableSpell(def.Id, (float)def.Mana, Ticks.FromSeconds(def.Cooldown), windup, Classify(def.Tags, def.CastTime),
                def.IsDamaging, def.HasTag("projectile"), (ContentIds.NumberOf(def.Id) - 1) % GlobalRules.Loadout.SpellsPerWizard, def.WizardId, def.Tags);
            spell.HasInterruptRefund = def.HasTag(CastingRules.InterruptRefundTag);
            spell.RequiresAnticipatoryCue = MaxNumber(def.Damage) > GlobalRules.Casting.CueDirectDamageThreshold
                || HasAny(def.Tags, CastingRules.HardControlTags) || def.HasTag("teleport");
            return spell;
        }

        /// <summary>A signature technique: listed cost, cooldown, and windup; activation commits directly (spec L472).</summary>
        public static CastableSpell Signature(string id, string wizardId, float mana, double cooldownSec, double windupSec, CastWeight weight, bool isDamaging)
        {
            var spell = new CastableSpell(id, mana, Ticks.FromSeconds(cooldownSec), Ticks.FromSeconds(windupSec), weight, isDamaging, false, -1, wizardId);
            spell.IsSignature = true;
            return spell;
        }

        /// <summary>
        /// Recovery class from tags and windup (spec L92; quick/heavy threshold <see cref="CastingRules.QuickCastMaxWindupSec"/>).
        /// Summons and fields are major, control tags are control, then windup decides quick versus heavy.
        /// </summary>
        public static CastWeight Classify(IReadOnlyList<string> tags, double castTimeSec)
        {
            if (HasAny(tags, new[] { "summon", "field" })) return CastWeight.MajorFieldOrSummon;
            if (HasAny(tags, CastingRules.ControlTags)) return CastWeight.Control;
            return Ticks.FromSeconds(castTimeSec) <= CastingRules.QuickCastMaxWindupTicks ? CastWeight.Quick : CastWeight.Heavy;
        }

        public CastableSpell WithMana(float mana)
        {
            if (mana < 0f) throw new ArgumentOutOfRangeException(nameof(mana));
            return new CastableSpell(this) { Mana = mana };
        }

        public CastableSpell WithCooldownTicks(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            return new CastableSpell(this) { CooldownTicks = ticks };
        }

        public CastableSpell WithWindupTicks(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            return new CastableSpell(this) { WindupTicks = ticks };
        }

        /// <summary>Changes the class; recovery and move factor reset to the new class defaults.</summary>
        public CastableSpell WithWeight(CastWeight weight) =>
            new CastableSpell(this) { Weight = weight, RecoveryTicks = CastWeights.DefaultRecoveryTicks(weight), MoveSpeedFactor = CastWeights.MoveSpeedFactor(weight) };

        /// <summary>A record-stated recovery. Spec L92 lets a record state a longer recovery, never a shorter one.</summary>
        public CastableSpell WithRecoveryTicks(int ticks)
        {
            int floor = CastWeights.DefaultRecoveryTicks(Weight);
            if (ticks < floor) throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Recovery cannot be shorter than the class default " + floor + " ticks (spec L92).");
            return new CastableSpell(this) { RecoveryTicks = ticks };
        }

        public CastableSpell WithStationary(bool stationary) => new CastableSpell(this) { IsStationary = stationary };

        /// <summary>
        /// A record-stated movement factor while casting (for example SPL-007 "caster movement -20% while winding up").
        /// Spec L92 sets the class default; a record may state its own penalty, which this replaces (0-1 of walk speed).
        /// </summary>
        public CastableSpell WithMoveSpeedFactor(float factor)
        {
            if (!(factor >= 0f && factor <= 1f)) throw new ArgumentOutOfRangeException(nameof(factor));
            return new CastableSpell(this) { MoveSpeedFactor = factor };
        }

        /// <summary>A channel after release, clamped to the 3 s normal maximum (spec L92).</summary>
        public CastableSpell WithChannel(int maxTicks, int pulseTicks = 1)
        {
            if (maxTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxTicks));
            if (pulseTicks < 1) throw new ArgumentOutOfRangeException(nameof(pulseTicks));
            return new CastableSpell(this) { ChannelMaxTicks = Math.Min(maxTicks, CastingRules.MaxChannelTicks), ChannelPulseTicks = pulseTicks };
        }

        public CastableSpell WithInterruptRefund(bool refund) => new CastableSpell(this) { HasInterruptRefund = refund };

        public CastableSpell WithAnticipatoryCue(bool cue) => new CastableSpell(this) { RequiresAnticipatoryCue = cue };

        public CastableSpell WithRecast(RecastRule recast) => new CastableSpell(this) { Recast = recast };

        public CastableSpell WithParameters(GrammarParameters parameters) => new CastableSpell(this) { Parameters = parameters ?? GrammarParameters.Empty };

        public override string ToString() => Id;

        private static bool HasAny(IReadOnlyList<string> tags, string[] wanted)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
                for (int j = 0; j < wanted.Length; j++)
                    if (string.Equals(tags[i], wanted[j], StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Largest number written in a damage expression ("68; weak-point hit 85" gives 85).</summary>
        internal static double MaxNumber(string expression)
        {
            double max = 0.0;
            if (string.IsNullOrEmpty(expression)) return max;
            int i = 0;
            while (i < expression.Length)
            {
                if (!char.IsDigit(expression[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.')) i++;
                if (double.TryParse(expression.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > max)
                {
                    max = v;
                }
            }

            return max;
        }
    }
}
