using System;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Sim.Content;
using Xunit.Abstractions;

namespace Confluence.Sim.Tests.Content.AugmentsB
{
    /// <summary>
    /// The draft floor (spec L233, L596: ten legal alternatives per rarity) of the M1 wizards at their match-start loadout,
    /// counted through Economy's floor API over every record the shipped registry implements (augments-a, augments-b and
    /// the proof records), with the augments-b share and the augments-b records legal there but not implemented.
    /// </summary>
    public class DraftFloorBTests
    {
        private readonly ITestOutputHelper _out;

        public DraftFloorBTests(ITestOutputHelper output)
        {
            _out = output;
        }

        internal static readonly string[] AugmentsBFamilies =
        {
            "Resource and recovery", "Defense and commitment", "Information and deception", "Summon husbandry", "Build covenants", "Combo architectures",
        };

        [Theory]
        [InlineData("WIZ-002", MatchMode.Conclave)]
        [InlineData("WIZ-010", MatchMode.Conclave)]
        [InlineData("WIZ-002", MatchMode.Duel)]
        [InlineData("WIZ-010", MatchMode.Duel)]
        public void Reports_The_Legal_Implemented_Augments_Per_Rarity_For_The_Default_Loadouts(string wizardId, MatchMode mode)
        {
            Catalogue cat = Confluence.Sim.Tests.Content.AugmentsA.AKit.Catalogue;
            IContentAvailability available = ContentRegistry.Default.Availability;
            DraftFloorReport report = EconomyFloors.CheckDraftFloor(cat, available, wizardId, mode);

            var ours = cat.Augments.Where(a => !a.IsTransformation && Array.IndexOf(AugmentsBFamilies, a.Family) >= 0 && available.IsAvailable(a.Id)).ToList();
            _out.WriteLine(report + " (augments-b implemented: " + ours.Count + ")");
            var loadout = new Confluence.Economy.Loadouts.Loadout(cat, wizardId);
            foreach (Rarity r in new[] { Rarity.Silver, Rarity.Gold, Rarity.Prismatic })
            {
                var legal = Confluence.Economy.Augments.AugmentPool.Eligible(loadout, Confluence.Economy.RoundInfo.FirstDraftOf(mode, r), r, Confluence.Economy.FeatLedger.None, ContentAvailability.All);
                int legalOurs = legal.Count(o => !o.IsTransformation && Array.IndexOf(AugmentsBFamilies, o.Definition.Family) >= 0 && available.IsAvailable(o.Id));
                _out.WriteLine("  " + r + ": " + report.For(r).LegalAlternatives + " legal implemented (augments-b " + legalOurs + ")");
                var open = legal.Where(o => Array.IndexOf(AugmentsBFamilies, o.Definition.Family) >= 0 && !o.IsTransformation && !available.IsAvailable(o.Id)).Select(o => o.Id);
                _out.WriteLine("  " + r + " legal, not implemented (augments-b families): " + string.Join(" ", open));
            }

            Assert.True(report.MeetsAll, report.ToString());
        }
    }
}
