using System;
using Confluence.Core.Arena;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// Creates the authoritative duel world for one pairing and for its replays. This is the single place the host
    /// chooses the Sim seams (spell runtime, world materials), so the casting and reactions stages plug in here.
    /// Replays must use the same factory as the live duel.
    /// </summary>
    public interface ISimWorldFactory
    {
        DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog);
    }

    /// <summary>
    /// The Sim with its default seams (<c>NullSpellRuntime</c>, <c>NullWorldMaterials</c>) until the casting and
    /// reactions adapters exist: movement, guard, melee, boundary, falls, and the duel flow are fully authoritative;
    /// spells and materials do nothing yet.
    /// </summary>
    public sealed class SimWorldFactory : ISimWorldFactory
    {
        public static readonly SimWorldFactory Default = new SimWorldFactory();

        public DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var options = new DuelWorldOptions
            {
                AlcoveTicks = alcoveTicks,  // spec L46: 5 s, from Match's timings
                CombatTicks = combatTicks,  // spec L47: at most 90 s
                KeepEventLog = keepEventLog,
            };
            return new DuelWorld(layout, side0, side1, options);
        }
    }
}
