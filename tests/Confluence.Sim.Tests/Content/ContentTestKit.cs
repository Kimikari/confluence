using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content
{
    /// <summary>Duels with real catalogue loadouts (WIZ-002 Bram Cinder's grammar and registered spell behaviors).</summary>
    internal static class ContentTestKit
    {
        /// <summary>Player 0 plays Bram Cinder with the default kit (SPL-009 in slot 0, SPL-010 in slot 1); player 1 has no spells.</summary>
        public static DuelWorld BramVersusDummy(ArenaLayout layout = null, IWorldMaterials materials = null)
        {
            DuelPlayerSetup bram = DuelPlayerSetup.FromCatalogue(SpellTestKit.Catalogue, "A", "WIZ-002");
            DuelPlayerSetup dummy = TestWorlds.Setup("B");
            var runtime = SpellRuntime.FromCatalogue(SpellTestKit.Catalogue);
            return TestWorlds.Duel(layout, bram, dummy, 0, -1, runtime, materials);
        }

        /// <summary>
        /// WIZ-002 crest release from real input: hold primary for <paramref name="holdTicks"/> ticks (press tick
        /// included), then release. 27 ticks lands on the 0.45 s crest; 15 ticks is a normal release.
        /// </summary>
        public static void HoldAndRelease(DuelWorld world, int holdTicks, PlayerInput? aim = null, PlayerInput? other = null)
        {
            PlayerInput a = aim ?? TestWorlds.A;
            PlayerInput b = other ?? TestWorlds.B;
            for (int i = 0; i < holdTicks; i++) world.Step(a.With(InputButtons.Primary), b);
            world.Step(a, b);
        }

        public static CastDuelEvent LastCommit(DuelWorld world) => SpellTestKit.CastEvents(world, CastEventKind.Committed).Last();
    }
}
