using System;
using Confluence.Core.Rules;

namespace Confluence.Casting
{
    /// <summary>
    /// A spell's recast allowance (spec L94, L512). Window, count, extra payment, and added recovery come from the
    /// exact spell or augment; a missing window means "once during the original effect's remaining lifetime", which
    /// the simulation ends by calling <see cref="CastMachine.EndRecastWindow"/> when the effect expires.
    /// </summary>
    public sealed class RecastRule
    {
        public RecastRule(int maxRecasts = GlobalRules.Casting.DefaultRecastCount, int windowTicks = 0, float extraMana = GlobalRules.Casting.RecastExtraManaCost, int addedRecoveryTicks = 0)
        {
            if (maxRecasts < 1) throw new ArgumentOutOfRangeException(nameof(maxRecasts));
            if (windowTicks < 0) throw new ArgumentOutOfRangeException(nameof(windowTicks));
            if (extraMana < 0f) throw new ArgumentOutOfRangeException(nameof(extraMana));
            if (addedRecoveryTicks < 0) throw new ArgumentOutOfRangeException(nameof(addedRecoveryTicks));
            MaxRecasts = maxRecasts;
            WindowTicks = windowTicks;
            ExtraMana = extraMana;
            AddedRecoveryTicks = addedRecoveryTicks;
        }

        /// <summary>The spec default: one recast while the original effect lasts, no cost, no added recovery.</summary>
        public static RecastRule Default { get; } = new RecastRule();

        public int MaxRecasts { get; }

        /// <summary>Ticks after release during which recast is allowed; 0 = the original effect's remaining lifetime.</summary>
        public int WindowTicks { get; }

        public bool HasExplicitWindow => WindowTicks > 0;

        /// <summary>Further resource payment the record states for each recast; 0 by default (spec L94).</summary>
        public float ExtraMana { get; }

        public int AddedRecoveryTicks { get; }
    }
}
