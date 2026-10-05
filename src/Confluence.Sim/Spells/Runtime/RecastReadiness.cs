using System.Collections.Generic;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A live entity whose cast's recast will act on something that does not exist yet (an orb still in flight before
    /// it waits on a surface, a projectile that plants the marker its recast bursts, a reservoir not yet charged). While
    /// such an entity lives and no live entity of the same cast <see cref="SpellEntity.AcceptsRecast"/>, the cast has no
    /// pending recast target: the press is refused as NoPendingRecast and spends no recast use (spec L94 "does nothing
    /// when none exists").
    /// </summary>
    public interface IAwaitsRecastTarget
    {
        /// <summary>True while the recast target this entity leads to is still on its way.</summary>
        bool AwaitingRecastTarget { get; }
    }

    /// <summary>Whether a cast's recast would act now (see <see cref="IAwaitsRecastTarget"/>).</summary>
    public static class RecastReadiness
    {
        /// <summary>
        /// True unless some live entity of <paramref name="cast"/> awaits its target and none accepts the recast. Casts
        /// without awaiting entities keep the framework's behavior (the recast always counts).
        /// </summary>
        public static bool HasTarget(CastRecord cast, IReadOnlyList<SpellEntity> entities)
        {
            if (cast == null) return true;
            bool awaiting = false;
            for (int i = 0; i < entities.Count; i++)
            {
                SpellEntity entity = entities[i];
                if (!entity.Alive || entity.Cast != cast) continue;
                if (entity.AcceptsRecast) return true;
                if (entity is IAwaitsRecastTarget a && a.AwaitingRecastTarget) awaiting = true;
            }

            return !awaiting;
        }
    }
}
