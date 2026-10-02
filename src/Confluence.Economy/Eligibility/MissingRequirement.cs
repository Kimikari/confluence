using System;
using Confluence.Core.Content;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// One unmet part of an eligibility predicate, with its plain-language card text (spec L237 "explained in plain
    /// language on the card", L641 "The UI always displays the missing requirement", L651 "exact missing-prerequisite
    /// list"). The node is a failing leaf, a failing NOT, or an ANY none of whose alternatives hold.
    /// </summary>
    public sealed class MissingRequirement
    {
        public MissingRequirement(Predicate node, string text)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public Predicate Node { get; }

        public string Text { get; }

        /// <summary>Wizard, round, and feat requirements can only be met at acquisition (spec L247).</summary>
        public bool IsAcquisitionOnly => Node.Timing == PredicateTiming.AcquisitionOnly;

        public override string ToString() => Text;
    }
}
