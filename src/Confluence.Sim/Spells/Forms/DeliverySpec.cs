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
    /// The typed delivery of a spell form. A delivery resolves or spawns its world entity at release (tick-order step 3)
    /// and drives channel pulses; everything it does goes through the <see cref="SpellContext"/> and the public
    /// DuelWorld APIs, so caps, budgets, and events stay central.
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
        /// Where the delivery would land with the caster's current aim, for cue and telegraph events (spec L96, L549):
        /// placed effects and teleports show their destination, directed effects their aim point at range.
        /// </summary>
        public virtual Vec3 PredictTarget(SpellContext ctx, CastRecord cast, int caster) => ctx.AimPoint(caster, cast.Form.Range);

        /// <summary>The delivery itself needs the 0.25 s anticipatory cue (a teleport destination, spec L96).</summary>
        public virtual bool RequiresCue => false;

        /// <summary>Clamps the delivery's numbers to the global ceilings (spec L124, L128, L114, L92); local text may be stricter.</summary>
        internal virtual void ApplyGlobalCaps()
        {
        }
    }
}
