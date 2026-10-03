using System;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// Creates the authoritative duel world for one pairing and for its replays. This is the single place the host
    /// chooses the Sim seams (spell runtime, world materials, content behaviors). Replays must use the same factory as
    /// the live duel (<see cref="MatchHost.WorldFactory"/>).
    /// </summary>
    public interface ISimWorldFactory
    {
        DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog);
    }

    /// <summary>
    /// The full authoritative duel: the spell runtime over each player's catalogue loadout (wizard grammar, implemented
    /// spells and transformations, signature), the Sim's default reaction materials built from the arena layout (biome
    /// rule included), prop bodies with catalogue mass and durability, and the registered wizard passives, equipment,
    /// and shared augments attached through the effect framework.
    /// </summary>
    public sealed class SimWorldFactory : ISimWorldFactory
    {
        public SimWorldFactory(Catalogue catalogue, ContentRegistry content = null)
        {
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            Content = content ?? ContentRegistry.Default;
        }

        public Catalogue Catalogue { get; }

        /// <summary>The behaviors the duels run (the shipped Sim behaviors by default).</summary>
        public ContentRegistry Content { get; }

        public DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var options = new DuelWorldOptions
            {
                AlcoveTicks = alcoveTicks,  // spec L46: 5 s, from Match's timings
                CombatTicks = combatTicks,  // spec L47: at most 90 s
                KeepEventLog = keepEventLog,
                SpellRuntime = SpellRuntime.FromCatalogue(Catalogue, Content),
                Catalogue = Catalogue,
                Content = Content,
            };
            return new DuelWorld(layout, side0, side1, options);
        }
    }
}
