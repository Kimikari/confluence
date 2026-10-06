using Confluence.Reactions;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.World.Materials
{
    /// <summary>
    /// Sees what the duel sends into its reaction world (spec L136, L145-L147, L163): every world deposit and direct
    /// impact as it is queued during tick-order step 4 (before Reactions applies it at step 5), and the result of each
    /// reaction step. Interactive props (IPropBehavior) and biome objects (IBiomeRuleBehavior: heat sinks, basins,
    /// hedges, dust beds) use it to react to the material and impacts that touch them. An observer may queue further
    /// requests from its callbacks (they apply in the same step, in the reaction world's canonical order). Observers run
    /// in registration order, which is the duel's construction order, so every replica sees the same sequence.
    /// </summary>
    public interface IMaterialObserver
    {
        /// <summary>A world deposit was queued (material, force, wind...), before translation into reaction requests.</summary>
        void OnDeposit(ReactionMaterials materials, in WorldDeposit deposit);

        /// <summary>
        /// A delivery's direct impact was queued; <paramref name="impact"/> keeps the struck solid id as the delivery
        /// reported it (a prop body or layout solid; -1 for the ground).
        /// </summary>
        void OnImpact(ReactionMaterials materials, in WorldImpact impact);

        /// <summary>The reaction world finished a step (its events, damage, and impulses are in <paramref name="result"/>).</summary>
        void OnStepped(ReactionMaterials materials, StepResult result);

        /// <summary>Folds the observer's own authoritative state into the duel hash.</summary>
        void Hash(ref StateHasher hasher);
    }
}
