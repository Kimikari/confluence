using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Hosting.Content;
using Confluence.Sim.Content;

namespace Confluence.Hosting.Tests
{
    /// <summary>
    /// The ledger-versus-claims check of the Stage 2 + 3 batch 1 merge: the [ContentBehavior] registry the host reads
    /// (and the ledger tool scans) holds exactly the records the merged branches reported as implemented, no more and no
    /// less. A later merge adds its branch's reported records to <see cref="Claims"/>.
    /// </summary>
    public sealed class LedgerClaimsTests
    {
        /// <summary>Stage 1b (merged 2026-10-03): MOD-001..072, the two Fireball/Cinder Sweep spells, six proof records, two biomes.</summary>
        private static readonly string[] Stage1b = Range("MOD", 1, 72)
            .Concat(new[] { "SPL-009", "SPL-010", "EQP-051", "EQP-066", "EQP-096", "AUG-145", "AUG-169", "AUG-175", "BIO-001", "BIO-011" }).ToArray();

        /// <summary>bram-kit (worktree-wf_ac402310-4a4-1, d18f24d): WIZ-002, SPL-009..016, AUG-305..320.</summary>
        private static readonly string[] BramKit = new[] { "WIZ-002" }.Concat(Range("SPL", 9, 16)).Concat(Range("AUG", 305, 320)).ToArray();

        /// <summary>halen-kit (worktree-wf_ac402310-4a4-2, 674b382): WIZ-010, SPL-073..080, AUG-433..448.</summary>
        private static readonly string[] HalenKit = new[] { "WIZ-010" }.Concat(Range("SPL", 73, 80)).Concat(Range("AUG", 433, 448)).ToArray();

        /// <summary>augments-a (worktree-wf_ac402310-4a4-3, d6e11f0): the 75 shared augments of its report.</summary>
        private static readonly string[] AugmentsA = Numbers("AUG",
            1, 5, 6, 7, 9, 12, 13, 14, 17, 18, 19, 20, 21, 25, 26, 27, 28, 30, 31, 32, 33, 38, 39, 41, 42, 43, 44, 49, 50, 51, 52, 53,
            54, 55, 59, 60, 62, 65, 66, 67, 68, 70, 73, 74, 75, 76, 77, 78, 79, 80, 82, 83, 84, 85, 86, 87, 88, 89, 90, 92, 97, 98,
            99, 100, 101, 102, 103, 104, 105, 107, 108, 110, 115, 117, 125);

        /// <summary>net-protocol (worktree-wf_ac402310-4a4-4, 21f84e5) reported no content records.</summary>
        private static IEnumerable<string> Claims => Stage1b.Concat(BramKit).Concat(HalenKit).Concat(AugmentsA);

        [Fact]
        public void The_Registry_Holds_Exactly_The_Merged_Claims()
        {
            Assert.Equal(75, AugmentsA.Length);
            Assert.Equal(25, BramKit.Length);
            Assert.Equal(25, HalenKit.Length);
            var claimed = new SortedSet<string>(Claims, StringComparer.Ordinal);
            Assert.Equal(205, claimed.Count); // SPL-009 and SPL-010 were Stage 1b records that bram-kit completed

            ImplementedContent implemented = ImplementedContent.FromShippedAssemblies();
            Assert.Equal(claimed, implemented.Ids);
            Assert.DoesNotContain(implemented.Ids, id => !HostFixture.Catalogue.Exists(id));
            Assert.DoesNotContain(AugmentsA, id => HostFixture.Catalogue.Augment(id).IsTransformation);
            Assert.All(BramKit.Concat(HalenKit).Where(id => id.StartsWith("AUG-", StringComparison.Ordinal)),
                id => Assert.True(HostFixture.Catalogue.Augment(id).IsTransformation, id));

            // The Sim registry (every gameplay behavior outside the Arena's modules) constructs each claimed behavior.
            ContentRegistry registry = ContentRegistry.Default;
            Assert.Equal(claimed.Where(id => !id.StartsWith("MOD-", StringComparison.Ordinal)), registry.Ids.OrderBy(x => x, StringComparer.Ordinal));
        }

        private static IEnumerable<string> Range(string prefix, int from, int to)
        {
            for (int n = from; n <= to; n++) yield return Id(prefix, n);
        }

        private static string[] Numbers(string prefix, params int[] numbers) => numbers.Select(n => Id(prefix, n)).ToArray();

        private static string Id(string prefix, int n) => prefix + "-" + n.ToString("000", CultureInfo.InvariantCulture);
    }
}
