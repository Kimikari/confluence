using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Content
{
    public class FeatsTests
    {
        [Fact]
        public void DefinesFeat001To024InOrder()
        {
            Assert.Equal(24, Feats.All.Count);
            Assert.Equal(Feats.Count, Feats.All.Count);
            for (int i = 0; i < 24; i++) Assert.Equal("FEAT-" + (i + 1).ToString("D3"), Feats.All[i].Id);
            Assert.True(Feats.Exists("FEAT-001"));
            Assert.True(Feats.Exists("FEAT-024"));
            Assert.False(Feats.Exists("FEAT-025"));
            Assert.False(Feats.Exists("FEAT-000"));
            Assert.False(Feats.Exists(null));
            Assert.Equal("Deal 30 damage through bounded conductivity reactions", Feats.Get("FEAT-020").Description);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Feats.Get("FEAT-099"));
        }

        [Fact]
        public void MatchesTheSpecificationTableExactly()
        {
            // The feat table rows in the design (spec L253-L278) are the only lines starting with "| FEAT-".
            string design = Path.Combine(Path.GetDirectoryName(Catalogue.FindDefaultPath()), "confluence-design.md");
            var rows = File.ReadLines(design)
                .Select(line => Regex.Match(line, @"^\| (FEAT-\d{3}) \| (.+?) \|\s*$"))
                .Where(m => m.Success)
                .Select(m => (Id: m.Groups[1].Value, Description: m.Groups[2].Value))
                .ToList();
            Assert.Equal(24, rows.Count);
            Assert.Equal(rows, Feats.All.Select(f => (f.Id, f.Description)));
        }

        [Fact]
        public void EveryFeatReferencedByContentExists()
        {
            var referenced = TestCatalogue.Real.Augments.Select(a => a.Eligibility)
                .Concat(TestCatalogue.Real.Builds.Where(b => b.Eligibility != null).Select(b => b.Eligibility))
                .SelectMany(p => p.DescendantsAndSelf().OfType<FeatLeaf>())
                .Select(f => f.FeatId).Distinct().ToList();
            Assert.NotEmpty(referenced);
            Assert.All(referenced, id => Assert.True(Feats.Exists(id), id));
        }
    }

    public class StructuralDeltasTests : IDisposable
    {
        public StructuralDeltasTests()
        {
            StructuralDeltas.Clear();
        }

        public void Dispose()
        {
            StructuralDeltas.Clear();
        }

        [Fact]
        public void RegistersAndAppliesTagChanges()
        {
            var delta = StructuralDeltas.Register("AUG-TEST-1", "SPL-001", new[] { "trap", "field" }, new[] { "projectile" });
            Assert.True(StructuralDeltas.TryGet("AUG-TEST-1", out var found));
            Assert.Same(delta, found);
            Assert.Equal("SPL-001", found.TargetSpellId);
            Assert.Equal(new[] { "school:light", "damaging", "field", "trap" }, found.Apply(new[] { "school:light", "projectile", "damaging", "field" }));
            Assert.False(StructuralDeltas.TryGet("AUG-TEST-2", out var missing));
            Assert.Null(missing);
        }

        [Fact]
        public void IdenticalRegistrationIsIdempotentAndConflictsThrow()
        {
            var first = StructuralDeltas.Register("AUG-TEST-3", "SPL-002", new[] { "mobile" }, new[] { "stationary" });
            Assert.Same(first, StructuralDeltas.Register("AUG-TEST-3", "SPL-002", new[] { "mobile" }, new[] { "stationary" }));
            Assert.Throws<InvalidOperationException>(() => StructuralDeltas.Register("AUG-TEST-3", "SPL-002", new[] { "trap" }, null));
            Assert.Throws<ArgumentException>(() => StructuralDeltas.Register("", "SPL-002", null, null));
            Assert.Throws<ArgumentException>(() => StructuralDeltas.Register("AUG-TEST-4", "SPL-002", new[] { "" }, null));
        }

        [Fact]
        public void AllIsOrderedAndClearEmptiesTheRegistry()
        {
            StructuralDeltas.Register("AUG-TEST-9", "SPL-003", null, null);
            StructuralDeltas.Register("AUG-TEST-5", "SPL-003", new[] { "beam" }, null);
            Assert.Equal(new[] { "AUG-TEST-5", "AUG-TEST-9" }, StructuralDeltas.All.Select(d => d.AugmentId));
            Assert.Empty(StructuralDeltas.All[1].AddTags);
            StructuralDeltas.Clear();
            Assert.Empty(StructuralDeltas.All);
        }
    }

    public class ContentAvailabilityTests
    {
        [Fact]
        public void AllNoneAndFromIds()
        {
            Assert.True(ContentAvailability.All.IsAvailable("AUG-305"));
            Assert.False(ContentAvailability.None.IsAvailable("AUG-305"));
            var some = ContentAvailability.FromIds(new[] { "AUG-305", "EQP-001", null });
            Assert.True(some.IsAvailable("AUG-305"));
            Assert.True(some.IsAvailable("EQP-001"));
            Assert.False(some.IsAvailable("aug-305"));
            Assert.False(some.IsAvailable(null));
            Assert.Throws<ArgumentNullException>(() => ContentAvailability.FromIds(null));
        }
    }

    public class ContentAttributeTests
    {
        [Fact]
        public void VerifiesAttributeTargetsMethodsAndClassesAndRepeats()
        {
            var usage = typeof(VerifiesAttribute).GetCustomAttribute<AttributeUsageAttribute>();
            Assert.Equal(AttributeTargets.Method | AttributeTargets.Class, usage.ValidOn);
            Assert.True(usage.AllowMultiple);
            Assert.Equal(new[] { "AUG-305", "SPL-009" }, new VerifiesAttribute("AUG-305", "SPL-009").Ids);
            Assert.Empty(new VerifiesAttribute().Ids);
        }

        [Fact]
        public void ContentBehaviorAttributeTargetsClassesAndRepeats()
        {
            var usage = typeof(ContentBehaviorAttribute).GetCustomAttribute<AttributeUsageAttribute>();
            Assert.Equal(AttributeTargets.Class, usage.ValidOn);
            Assert.True(usage.AllowMultiple);
            Assert.Equal("SPL-009", new ContentBehaviorAttribute("SPL-009").Id);
            Assert.Throws<ArgumentNullException>(() => new ContentBehaviorAttribute(null));
        }
    }
}
