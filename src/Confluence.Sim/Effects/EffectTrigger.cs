using System;
using Confluence.Core.Rules;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Status;
using SurfaceKind = Confluence.Sim.World.SurfaceKind;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// What makes an effect fire (spec L433 "trigger"). Sim systems raise these: duel events are translated at the
    /// dispatch point, state changes (sprint, selection, surfaces, volumes, melee misses) are polled there, and the
    /// spell runtime raises <see cref="CastReleased"/> through <see cref="EffectSystem.RaiseCastReleased"/>.
    /// </summary>
    public enum EffectTrigger
    {
        None,

        /// <summary>The duel's round starts: players are placed in their alcoves (spec L46, L579).</summary>
        RoundStart,

        /// <summary>The alcoves open (spec L47).</summary>
        CombatStart,

        /// <summary>Every <see cref="Effect.PeriodTicks"/> of combat time.</summary>
        Periodic,

        /// <summary>A paid cast commitment (mana paid, spec L90).</summary>
        CastCommitted,

        /// <summary>A cast released its delivery (raised by the spell runtime).</summary>
        CastReleased,

        /// <summary>The selected spell slot changed (spec L106: no swap windup).</summary>
        SpellSelectionChanged,

        /// <summary>The player's original direct hit (spell impact or common melee) damaged the opponent.</summary>
        DirectHitDealt,

        /// <summary>An original direct hit damaged the player.</summary>
        DirectHitTaken,

        /// <summary>Any original damage reached the player (spell, splash, melee, status pulse, fall).</summary>
        DamageTaken,

        /// <summary>The player's shields absorbed original damage.</summary>
        ShieldAbsorbed,

        /// <summary>A perfect guard blocked a hit (spec L104).</summary>
        PerfectGuard,

        /// <summary>A guard blocked a hit (perfect or ordinary).</summary>
        GuardBlocked,

        GuardStarted,
        Dodge,
        Jump,

        /// <summary>The player landed after a drop (amount = drop height).</summary>
        Land,

        SprintStarted,
        SprintStopped,
        MeleeHit,

        /// <summary>A common melee strike finished its live window without hitting the opponent.</summary>
        MeleeMiss,

        StatusApplied,
        StatusRemoved,

        /// <summary>The player created a gameplay field (spell field or a counted reaction cloud, spec L128).</summary>
        FieldCreated,

        /// <summary>The player destroyed a prop.</summary>
        PropDestroyed,

        /// <summary>The player destroyed a destructible cover solid that is not a prop.</summary>
        CoverDestroyed,

        EnteredVolume,
        LeftVolume,
        EnteredSurface,
        LeftSurface,
    }

    /// <summary>One raised trigger, with the facts conditions and filters read.</summary>
    public struct TriggerEvent
    {
        public EffectTrigger Trigger;

        /// <summary>The player whose effects are evaluated.</summary>
        public int Player;

        /// <summary>The counterpart: attacker, target, status applier, or -1.</summary>
        public int Other;

        public int Tick;

        /// <summary>The duel event the trigger came from, or 0.</summary>
        public int EventId;

        /// <summary>Original target-hit or cast event for the proc budget (spec L126); 0 = this trigger alone.</summary>
        public int OriginEventId;

        /// <summary>Primary damage channel of a hit (first part), or None.</summary>
        public DamageChannel Channel;

        /// <summary>Every channel of a split hit (bit per <see cref="DamageChannel"/> value).</summary>
        public int ChannelMask;

        /// <summary>Damage dealt, shield absorbed, mana paid, drop height: the trigger's own quantity.</summary>
        public float Amount;

        /// <summary>Attacker-to-target distance of a hit (spec L535 range wording), or 0.</summary>
        public float Range;

        public string SourceId;
        public DamageKind DamageKind;
        public bool IsSpellHit;
        public StatusKind Status;
        public SurfaceKind Surface;
        public VolumeKind Volume;
        public int VolumeId;

        public bool HasChannel(DamageChannel channel) => channel == DamageChannel.None || (ChannelMask & ChannelBit(channel)) != 0;

        public static int ChannelBit(DamageChannel channel) => 1 << (int)channel;

        public override string ToString() => Trigger + " p" + Player + " other " + Other + " t" + Tick + " amount " + Amount;
    }

    /// <summary>
    /// Restricts which raised triggers an effect reacts to (spec L433 "target filter"): channel, hit kind, range,
    /// amount, status, surface, volume, and source. Unset fields accept anything.
    /// </summary>
    public sealed class EffectFilter
    {
        /// <summary>Hit channel that must be present (None = any).</summary>
        public DamageChannel Channel { get; set; } = DamageChannel.None;

        /// <summary>Only exclusive-spell hits (spec L102: common melee is not a spell).</summary>
        public bool SpellHitsOnly { get; set; }

        /// <summary>Only common melee hits.</summary>
        public bool MeleeOnly { get; set; }

        public float MinRange { get; set; } = float.NaN;
        public float MaxRange { get; set; } = float.NaN;
        public float MinAmount { get; set; } = float.NaN;
        public StatusKind? Status { get; set; }
        public SurfaceKind? Surface { get; set; }
        public VolumeKind? Volume { get; set; }

        /// <summary>Exact source id (spell, item, status definition), or null for any.</summary>
        public string SourceId { get; set; }

        public bool Matches(in TriggerEvent e)
        {
            if (!e.HasChannel(Channel))
            {
                return false;
            }

            if (SpellHitsOnly && !e.IsSpellHit)
            {
                return false;
            }

            if (MeleeOnly && e.DamageKind != DamageKind.Melee)
            {
                return false;
            }

            if (!float.IsNaN(MinRange) && !(e.Range >= MinRange))
            {
                return false;
            }

            if (!float.IsNaN(MaxRange) && !(e.Range <= MaxRange))
            {
                return false;
            }

            if (!float.IsNaN(MinAmount) && !(e.Amount >= MinAmount))
            {
                return false;
            }

            if (Status.HasValue && e.Status != Status.Value)
            {
                return false;
            }

            if (Surface.HasValue && e.Surface != Surface.Value)
            {
                return false;
            }

            if (Volume.HasValue && e.Volume != Volume.Value)
            {
                return false;
            }

            return SourceId == null || string.Equals(SourceId, e.SourceId, StringComparison.Ordinal);
        }
    }
}
