using System.Collections.Generic;

namespace Confluence.Core.Content
{
    public enum ValidationSeverity
    {
        /// <summary>The catalogue breaks a binding rule; gameplay must not start on it.</summary>
        Error,

        /// <summary>The data departs from a descriptive design target or totals statement; recorded in DECISIONS.md.</summary>
        Warning,
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string check, string id, string message)
        {
            Severity = severity;
            Check = check;
            Id = id;
            Message = message;
        }

        public ValidationSeverity Severity { get; }

        /// <summary>Stable check code from <see cref="ValidationChecks"/>.</summary>
        public string Check { get; }

        /// <summary>The record the issue concerns, or a scope such as "counts" or "augments".</summary>
        public string Id { get; }

        public string Message { get; }

        public bool IsError => Severity == ValidationSeverity.Error;

        public override string ToString() => Severity + " [" + Check + "] " + Id + ": " + Message;

        public static int CountErrors(IEnumerable<ValidationIssue> issues)
        {
            int n = 0;
            foreach (var issue in issues)
                if (issue.IsError) n++;
            return n;
        }
    }

    /// <summary>Check codes reported by <see cref="CatalogueValidator"/>.</summary>
    public static class ValidationChecks
    {
        public const string Counts = "counts";
        public const string RuleFlags = "rule-flags";
        public const string IdFormat = "id-format";
        public const string DuplicateId = "duplicate-id";
        public const string IdInterval = "id-interval";
        public const string IdOrder = "id-order";
        public const string SpellOwnership = "spell-ownership";
        public const string DefaultSpells = "default-spells";
        public const string SpellFields = "spell-fields";
        public const string SpellTags = "spell-tags";
        public const string EquipmentCategory = "equipment-category";
        public const string EquipmentSlot = "equipment-slot";
        public const string EquipmentTier = "equipment-tier";
        public const string UniqueGroup = "unique-group";
        public const string EquipmentActivation = "equipment-activation";
        public const string AugmentRarity = "augment-rarity";
        public const string AugmentCost = "augment-cost";
        public const string Transformation = "transformation";
        public const string TransformationsPerSpell = "transformations-per-spell";
        public const string TransformationRarity = "transformation-rarity";
        public const string SharedAugments = "shared-augments";
        public const string UnconditionalPool = "unconditional-pool";
        public const string CompoundPredicates = "compound-predicates";
        public const string PredicateReference = "predicate-reference";
        public const string PredicateValue = "predicate-value";
        public const string SelfReference = "self-reference";
        public const string DependencyCycle = "dependency-cycle";
        public const string ParentRarity = "parent-rarity";
        public const string ExclusiveGroup = "exclusive-group";
        public const string Build = "build";
        public const string BuildPlan = "build-plan";
        public const string NumericRange = "numeric-range";
        public const string Counterplay = "counterplay";
        public const string ArenaFields = "arena-fields";
    }
}
