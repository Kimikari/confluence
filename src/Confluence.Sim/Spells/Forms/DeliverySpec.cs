using Confluence.Core.Geometry;

namespace Confluence.Sim.Spells
{
    /// <summary>How a spell reaches the world (spec L535 delivery tags; L527 projectile, beam, and summon rules).</summary>
    public enum DeliveryKind
    {
        Projectile,
        Ray,
        Cone,
        Arc,
        Field,
        Shield,
        Buff,
        Dash,
        Leap,
        Teleport,
        Deployable,
        Summon,
        Wave,
        Tether,
    }

    /// <summary>
    /// The typed delivery of a spell form. At release (tick-order step 3) it resolves its effect or spawns a
    /// <see cref="SpellEntity"/> that steps in step 4; channeled deliveries also act on each channel pulse. Everything
    /// goes through the <see cref="SpellContext"/> and the public DuelWorld APIs, so caps, budgets, and events stay
    /// central. Numbers are edited while the form resolves and frozen afterwards.
    /// </summary>
    public abstract class DeliverySpec : SpellSpec
    {
        public abstract DeliveryKind Kind { get; }

        /// <summary>The windup finished: resolve the effect or spawn its entity.</summary>
        public abstract void Release(SpellContext ctx, CastRecord cast);

        /// <summary>One channel pulse after release (channeled beams and cones; spec L92).</summary>
        public virtual void OnChannelPulse(SpellContext ctx, CastRecord cast, int pulse)
        {
        }

        public virtual void OnChannelEnded(SpellContext ctx, CastRecord cast)
        {
        }

        /// <summary>
        /// Where the delivery would land with the caster's current aim, for the anticipatory cue (spec L96, L549):
        /// placed effects and teleports show their destination, directed effects their aim point at range.
        /// </summary>
        public virtual Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.AimPoint(caster, form.Range);

        /// <summary>The delivery itself needs the 0.25 s anticipatory cue (a teleport destination, spec L96).</summary>
        public virtual bool RequiresCue => false;

        /// <summary>Clamps the delivery's numbers to the global ceilings (spec L92, L114, L124, L128); local text may be stricter.</summary>
        internal virtual void ApplyGlobalCaps()
        {
        }

        /// <summary>Delivery direction: the aim at release, or the aim fixed at commitment ("fixed direction selected at windup").</summary>
        protected static Vec3 AimDirection(SpellContext ctx, CastRecord cast) =>
            cast.Form.AimLock == AimLock.AtCommit ? cast.CommitDirection : ctx.AimDirection(cast.Caster);

        /// <summary>The payload this delivery applies: its own override, else the form's primary payload.</summary>
        protected static PayloadSpec PayloadOf(CastRecord cast, PayloadSpec own) => own ?? cast.Form.Payload;

        /// <summary>Caps a nested payload (field ticks, trap bursts) together with the delivery.</summary>
        internal static void CapNested(PayloadSpec payload)
        {
            if (payload == null) return;
            payload.ApplyGlobalCaps();
        }
    }
}
