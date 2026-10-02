using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;

namespace Confluence.Tools.Ledger
{
    public enum LedgerStatus
    {
        Unstarted,
        Implemented,
        ImplementedTestsNotRun,
        ImplementedTestsFailing,
        Verified,
    }

    public sealed record TestReference(string Test, string Assembly, TestOutcome Outcome);

    public sealed class LedgerEntry
    {
        public LedgerEntry(ContentDef record, IReadOnlyList<string> implementedBy, IReadOnlyList<TestReference> tests)
        {
            Record = record;
            ImplementedBy = implementedBy;
            Tests = tests;
            Status = Decide(implementedBy.Count > 0, tests);
        }

        public ContentDef Record { get; }
        public IReadOnlyList<string> ImplementedBy { get; }
        public IReadOnlyList<TestReference> Tests { get; }
        public LedgerStatus Status { get; }

        /// <summary>
        /// Verified requires an implementation and at least one verifying test, all of which ran and passed.
        /// Tests without an implementation leave the record unstarted.
        /// </summary>
        public static LedgerStatus Decide(bool implemented, IReadOnlyList<TestReference> tests)
        {
            if (!implemented) return LedgerStatus.Unstarted;
            if (tests.Count == 0) return LedgerStatus.Implemented;
            if (tests.Any(t => t.Outcome == TestOutcome.Failed)) return LedgerStatus.ImplementedTestsFailing;
            if (tests.Any(t => t.Outcome == TestOutcome.NotRun)) return LedgerStatus.ImplementedTestsNotRun;
            return LedgerStatus.Verified;
        }
    }

    /// <summary>Per-ID implementation and verification status for every catalogue record.</summary>
    public sealed class Ledger
    {
        public static readonly IReadOnlyList<LedgerStatus> AllStatuses = (LedgerStatus[])Enum.GetValues(typeof(LedgerStatus));

        public Catalogue Catalogue { get; init; }
        public IReadOnlyList<LedgerEntry> Entries { get; init; }
        public IReadOnlyDictionary<string, LedgerEntry> ById { get; init; }
        public IReadOnlyList<string> TestAssemblies { get; init; }
        public IReadOnlyList<string> TrxFiles { get; init; }
        public int VerifyingTestCount { get; init; }

        /// <summary>IDs named by attributes that are outside per-ID tracking (feats, grammar and global systems).</summary>
        public IReadOnlyList<string> OutOfScopeIds { get; init; }

        /// <summary>Catalogue-shaped IDs named by attributes that do not exist in the catalogue.</summary>
        public IReadOnlyList<string> UnknownIds { get; init; }

        public IReadOnlyList<string> Warnings { get; init; }

        public static string Slug(LedgerStatus status)
        {
            switch (status)
            {
                case LedgerStatus.Unstarted: return "unstarted";
                case LedgerStatus.Implemented: return "implemented";
                case LedgerStatus.ImplementedTestsNotRun: return "implemented-tests-not-run";
                case LedgerStatus.ImplementedTestsFailing: return "implemented-tests-failing";
                case LedgerStatus.Verified: return "verified";
                default: throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        public int Count(ContentType? type, LedgerStatus? status)
        {
            return Entries.Count(e => (type == null || e.Record.Type == type) && (status == null || e.Status == status));
        }

        public static Ledger Build(
            Catalogue catalogue,
            IReadOnlyDictionary<string, List<string>> behaviors,
            IReadOnlyList<VerifyingTest> tests,
            TrxResults results,
            IReadOnlyList<string> testAssemblies,
            List<string> warnings)
        {
            var testsById = new Dictionary<string, List<TestReference>>(StringComparer.Ordinal);
            var outOfScope = new SortedSet<string>(StringComparer.Ordinal);
            var unknown = new SortedSet<string>(StringComparer.Ordinal);

            void Classify(string id)
            {
                if (catalogue.Exists(id)) return;
                if (ContentIds.TryParse(id, out _, out _)) unknown.Add(id);
                else outOfScope.Add(id);
            }

            foreach (var test in tests)
            {
                var outcome = results.OutcomeOf(test.ClassName, test.Method);
                foreach (var id in test.Ids)
                {
                    Classify(id);
                    if (!testsById.TryGetValue(id, out var list)) testsById.Add(id, list = new List<TestReference>());
                    list.Add(new TestReference(test.Key, test.Assembly, outcome));
                }
            }
            foreach (var id in behaviors.Keys) Classify(id);

            var entries = new List<LedgerEntry>();
            foreach (var record in catalogue.AllRecords())
            {
                var implementedBy = behaviors.TryGetValue(record.Id, out var types) ? (IReadOnlyList<string>)types : Array.Empty<string>();
                var refs = testsById.TryGetValue(record.Id, out var list)
                    ? list.OrderBy(t => t.Test, StringComparer.Ordinal).ToList()
                    : new List<TestReference>();
                entries.Add(new LedgerEntry(record, implementedBy, refs));
            }

            return new Ledger
            {
                Catalogue = catalogue,
                Entries = entries,
                ById = entries.ToDictionary(e => e.Record.Id, StringComparer.Ordinal),
                TestAssemblies = testAssemblies,
                TrxFiles = results.Files,
                VerifyingTestCount = tests.Count,
                OutOfScopeIds = outOfScope.ToList(),
                UnknownIds = unknown.ToList(),
                Warnings = warnings,
            };
        }
    }
}
