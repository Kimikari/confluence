using Confluence.Sim.Duel;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// Record-specific hooks beyond a delivery primitive (conditional follow-ups, recast effects that are not an
    /// entity's own detonation, on-hit riders). A behavior attaches one to its form; it is stateless and shared by all
    /// casts, so per-cast state lives on the <see cref="CastRecord"/> or the entities the hooks create.
    /// </summary>
    public abstract class SpellScript
    {
        /// <summary>
        /// The cast was committed and paid (tick-order step 3), before its windup: commitment-time telegraphs such as a
        /// fixed casting marker (spec L96, L549).
        /// </summary>
        public virtual void OnCommitted(SpellContext ctx, CastRecord cast)
        {
        }

        /// <summary>After the delivery released its effect (tick-order step 3).</summary>
        public virtual void OnReleased(SpellContext ctx, CastRecord cast)
        {
        }

        /// <summary>The selected spell's pending recast fired (spec L94); entity recasts run first.</summary>
        public virtual void OnRecast(SpellContext ctx, CastRecord cast)
        {
        }

        /// <summary>A payload of this cast damaged a player (the world already resolved it through the pipeline).</summary>
        public virtual void OnPlayerHit(SpellContext ctx, CastRecord cast, int target, DamageEvent hit)
        {
        }
    }
}
