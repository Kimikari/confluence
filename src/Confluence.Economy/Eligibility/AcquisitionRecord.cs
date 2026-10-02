using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Confluence.Core.Content;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// The acquisition-time truth value of every acquisition-only leaf (wizard, round_at_least, feat, always) in an
    /// augment's eligibility. "Wizard, round, and completed-feat prerequisites are acquisition-only" (spec L247, L602):
    /// after the draft choice those leaves are never re-read, so for example NOT(feat) cannot flip later. Ongoing
    /// leaves (equipment, owned augments, equipped spells) are re-evaluated at every finalization.
    /// </summary>
    public sealed class AcquisitionRecord
    {
        private readonly Dictionary<Predicate, bool> _values = new Dictionary<Predicate, bool>(ReferenceComparer<Predicate>.Instance);
        private readonly bool[] _ordered;

        private AcquisitionRecord(Predicate root, RoundInfo round, string wizardId, bool[] ordered)
        {
            Root = root;
            Round = round;
            WizardId = wizardId;
            _ordered = ordered;
            int i = 0;
            // Indexer, not Add: the shared AlwaysLeaf instance may occur more than once in one tree.
            foreach (var leaf in AcquisitionLeaves(root)) _values[leaf] = ordered[i++];
        }

        /// <summary>The predicate the values belong to (the augment's eligibility).</summary>
        public Predicate Root { get; }

        /// <summary>The draft round of acquisition (null for setup grants with no round, e.g. tests).</summary>
        public RoundInfo Round { get; }

        public string WizardId { get; }

        /// <summary>Recorded values of the acquisition-only leaves in pre-order, for serialization.</summary>
        public IReadOnlyList<bool> LeafValues => Array.AsReadOnly(_ordered);

        public bool TryGetRecorded(Predicate leaf, out bool value)
        {
            if (leaf != null && _values.TryGetValue(leaf, out value)) return true;
            value = false;
            return false;
        }

        /// <summary>Evaluates and stores every acquisition-only leaf of the predicate with the given leaf evaluator.</summary>
        public static AcquisitionRecord Capture(Predicate root, RoundInfo round, string wizardId, Func<Predicate, bool> evaluateLeaf)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (evaluateLeaf == null) throw new ArgumentNullException(nameof(evaluateLeaf));
            var values = new List<bool>();
            foreach (var leaf in AcquisitionLeaves(root)) values.Add(evaluateLeaf(leaf));
            return new AcquisitionRecord(root, round, wizardId, values.ToArray());
        }

        /// <summary>Restores a record from serialized values (see <see cref="LeafValues"/>).</summary>
        public static AcquisitionRecord FromValues(Predicate root, RoundInfo round, string wizardId, IReadOnlyList<bool> values)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (values == null) throw new ArgumentNullException(nameof(values));
            int expected = 0;
            foreach (var _ in AcquisitionLeaves(root)) expected++;
            if (values.Count != expected) throw new ArgumentException("Expected " + expected + " recorded leaf values.", nameof(values));
            var copy = new bool[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return new AcquisitionRecord(root, round, wizardId, copy);
        }

        /// <summary>Acquisition-only leaves in pre-order (spec L247).</summary>
        public static IEnumerable<Predicate> AcquisitionLeaves(Predicate root)
        {
            foreach (var node in root.DescendantsAndSelf())
                if (!node.IsComposite && PredicateKinds.IsAcquisitionOnly(node.Kind)) yield return node;
        }
    }

    /// <summary>Identity comparison for immutable AST nodes shared by a catalogue instance.</summary>
    internal sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
