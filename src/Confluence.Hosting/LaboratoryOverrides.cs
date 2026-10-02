using System.Collections.Generic;
using System.Text;

namespace Confluence.Hosting
{
    /// <summary>
    /// Development and Laboratory deviations from the competitive rules. Laboratory sessions may use any catalogue
    /// content and are clearly labeled with no ranked rewards (spec L69); every override is part of the host settings
    /// fingerprint, so a modified match can never pass for a standard one (L71). <see cref="None"/> is the standard rules.
    /// </summary>
    public sealed class LaboratoryOverrides
    {
        public LaboratoryOverrides(bool bypassEconomyFloors = false, bool poolsUseWholeCatalogue = false)
        {
            BypassEconomyFloors = bypassEconomyFloors;
            PoolsUseWholeCatalogue = poolsUseWholeCatalogue;
        }

        /// <summary>Standard rules: implemented pools only and the match-start floors enforced (D-010).</summary>
        public static LaboratoryOverrides None { get; } = new LaboratoryOverrides();

        /// <summary>
        /// Start even when the available pools break the ten-per-rarity draft floor (L233) or cannot fill the shop
        /// (D-010, D-082). The gaps are still computed and logged.
        /// </summary>
        public bool BypassEconomyFloors { get; }

        /// <summary>
        /// Shop and draft pools draw from every catalogue definition instead of implemented ones only (Laboratory "any
        /// catalogue content", L69). Unimplemented definitions then change economy state but have no duel effect.
        /// </summary>
        public bool PoolsUseWholeCatalogue { get; }

        /// <summary>Any override is active: the session is Laboratory-labeled and never counts for rating.</summary>
        public bool IsModified => BypassEconomyFloors || PoolsUseWholeCatalogue;

        /// <summary>Canonical text recorded in the host settings fingerprint.</summary>
        public string CanonicalText()
        {
            if (!IsModified) return "standard";
            var parts = new List<string>();
            if (BypassEconomyFloors) parts.Add("bypass-economy-floors");
            if (PoolsUseWholeCatalogue) parts.Add("pools-whole-catalogue");
            var sb = new StringBuilder("laboratory:");
            sb.Append(string.Join(",", parts));
            return sb.ToString();
        }

        public override string ToString() => CanonicalText();
    }
}
