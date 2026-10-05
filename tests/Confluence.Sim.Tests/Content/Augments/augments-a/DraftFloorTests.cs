using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Sim.Content;
using Xunit.Abstractions;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>
    /// The draft floor (spec L233, L596: ten legal alternatives per rarity) of the M1 wizards at their match-start loadout,
    /// counted through Economy's floor API over the records the shipped registry implements (this branch's augments-a
    /// records plus every other registered record).
    /// </summary>
    public class DraftFloorTests
    {
        private readonly ITestOutputHelper _out;

        public DraftFloorTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static readonly string[] AugmentsAFamilies =
        {
            "Casting mastery", "Projectile and beam shaping", "Fieldcraft and terrain", "Melee and guard", "Movement arts", "World reactions",
        };

        [Theory]
        [InlineData("WIZ-002", MatchMode.Conclave)]
        [InlineData("WIZ-010", MatchMode.Conclave)]
        [InlineData("WIZ-002", MatchMode.Duel)]
        [InlineData("WIZ-010", MatchMode.Duel)]
        public void Reports_The_Legal_Implemented_Augments_Per_Rarity_For_The_Default_Loadouts(string wizardId, MatchMode mode)
        {
            Catalogue cat = AKit.Catalogue;
            IContentAvailability available = ContentRegistry.Default.Availability;
            DraftFloorReport report = EconomyFloors.CheckDraftFloor(cat, available, wizardId, mode);

            // The augments-a share of the count: implemented shared augments of the six families.
            var ours = cat.Augments.Where(a => !a.IsTransformation && Array.IndexOf(AugmentsAFamilies, a.Family) >= 0 && available.IsAvailable(a.Id)).ToList();
            _out.WriteLine(report + " (augments-a implemented: " + ours.Count + ")");
            foreach (Rarity r in new[] { Rarity.Silver, Rarity.Gold, Rarity.Prismatic })
            {
                _out.WriteLine("  " + r + ": " + report.For(r).LegalAlternatives + " legal, augments-a implemented " + ours.Count(a => a.RarityEnum == r));
            }

            // Every unconditional augments-a record is legal for every wizard at the default loadout (spec L596).
            ContentRegistry reg = ContentRegistry.Default;
            foreach (AugmentDef a in ours.Where(a => a.IsUnconditional))
            {
                Assert.True(reg.Contains(a.Id));
            }

            foreach (Rarity r in new[] { Rarity.Silver, Rarity.Gold, Rarity.Prismatic })
            {
                int unconditional = ours.Count(a => a.RarityEnum == r && a.IsUnconditional);
                Assert.True(report.For(r).LegalAlternatives >= unconditional, wizardId + " " + r + ": " + report.For(r).LegalAlternatives + " < " + unconditional);
            }

            Assert.True(report.MeetsAll, report.ToString());
        }
    }
}
