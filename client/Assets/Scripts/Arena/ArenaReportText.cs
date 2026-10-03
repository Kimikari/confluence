using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;

namespace Confluence.Client.Arena
{
    /// <summary>One validation metric as shown to people: label, formatted value, and the raw key.</summary>
    public readonly struct MetricLine
    {
        public MetricLine(string key, string label, string value)
        {
            Key = key;
            Label = label;
            Value = value;
        }

        public string Key { get; }
        public string Label { get; }
        public string Value { get; }

        public override string ToString() => Label + ": " + Value;
    }

    /// <summary>
    /// Plain-language text for a released arena: how it was released (attempt k of 8 or the prevalidated fallback,
    /// spec L323) and the validator's metrics (spec L310-L312, L321-L322). Metric keys come from Confluence.Arena's
    /// validator; unknown keys are shown verbatim so new checks appear without a client change.
    /// </summary>
    public static class ArenaReportText
    {
        private static readonly Dictionary<string, (string Label, string Unit, int Decimals)> Known =
            new Dictionary<string, (string, string, int)>(StringComparer.Ordinal)
            {
                { "spawn_path", ("Spawn-to-spawn path (16-34 m, L310)", "m", 1) },
                { "longest_lane", ("Longest firing lane (12-28 m, L310)", "m", 1) },
                { "designated_lane", ("Designated firing lane", "m", 1) },
                { "cover_fraction", ("Cover share of usable floor (18-30%, L310)", "%", 1) },
                { "cover_area", ("Cover area", "m2", 1) },
                { "usable_area", ("Usable area (within 10% of target, L306)", "m2", 1) },
                { "target_area", ("Tier target area", "m2", 1) },
                { "contour_area", ("Contour area", "m2", 1) },
                { "central_reach_0", ("Useful central cover, side 0 (2.0-3.5 s, L310)", "s", 2) },
                { "central_reach_1", ("Useful central cover, side 1", "s", 2) },
                { "center_path_0", ("Path to the center, side 0", "m", 1) },
                { "center_path_1", ("Path to the center, side 1", "m", 1) },
                { "approaches_0", ("Disjoint 2.5 m approaches, side 0 (L308, L312)", "", 0) },
                { "approaches_1", ("Disjoint 2.5 m approaches, side 1", "", 0) },
                { "lane_interrupting_approaches", ("Approaches interrupting the lane (>= 2, L310)", "", 0) },
                { "natural_sources", ("Natural material sources (<= 8, L668)", "", 0) },
                { "prop_definitions", ("Prop definitions", "", 0) },
                { "prop_instances", ("Prop instances", "", 0) },
                { "max_height", ("Highest walkable surface", "m", 2) },
                { "min_height", ("Lowest walkable surface", "m", 2) },
                { "modules", ("Modules", "", 0) },
                { "cover_modules", ("Cover modules", "", 0) },
                { "elevation_modules", ("Elevation modules", "", 0) },
                { "destructible_solids", ("Destructible solids", "", 0) },
                { "final_floor_nodes", ("Final-floor navigation cells", "", 0) },
                { "field_placements", ("Representative field placements tested (L322)", "", 0) },
                { "single_cover_tests", ("Single-cover inflation tests (L310)", "", 0) },
                { "biome_features", ("Biome rule features", "", 0) },
            };

        /// <summary>"Generated: attempt 3 of 8" or "Prevalidated fallback fallback:32:BIO-001:0 (8 seeds rejected)".</summary>
        public static string Release(ArenaLayout layout)
        {
            if (layout == null)
            {
                return "No arena.";
            }

            ArenaValidationReport report = layout.Report;
            int attempts = report != null ? report.AttemptsUsed : 0;
            if (layout.IsFallback)
            {
                return "Prevalidated fallback " + layout.FallbackId + " (all " + attempts + " seeds failed validation, spec L323)";
            }

            string validity = report != null && report.Valid ? "valid" : "NOT validated";
            return "Generated, " + validity + ", released on attempt " + attempts + " of " + Confluence.Core.Rules.GlobalRules.Arena.MaxRetrySeeds;
        }

        public static string Release(GeneratedArena arena) => arena == null ? "No arena." : Release(arena.Layout);

        /// <summary>The report's metrics, known keys first in a fixed reading order, then the rest by key.</summary>
        public static List<MetricLine> Metrics(ArenaValidationReport report)
        {
            var lines = new List<MetricLine>();
            if (report?.Metrics == null)
            {
                return lines;
            }

            var keys = new List<string>(report.Metrics.Keys);
            var order = new List<string>(Known.Keys);
            keys.Sort((a, b) =>
            {
                int ia = order.IndexOf(a);
                int ib = order.IndexOf(b);
                if (ia >= 0 && ib >= 0)
                {
                    return ia.CompareTo(ib);
                }

                if (ia >= 0 != ib >= 0)
                {
                    return ia >= 0 ? -1 : 1;
                }

                return string.CompareOrdinal(a, b);
            });

            for (int i = 0; i < keys.Count; i++)
            {
                lines.Add(Format(keys[i], report.Metrics[keys[i]]));
            }

            return lines;
        }

        public static MetricLine Format(string key, float value)
        {
            if (Known.TryGetValue(key, out var known))
            {
                float shown = known.Unit == "%" ? value * 100f : value;
                string number = shown.ToString("F" + known.Decimals, CultureInfo.InvariantCulture);
                string unit = known.Unit.Length == 0 ? string.Empty : known.Unit == "%" ? "%" : " " + known.Unit;
                return new MetricLine(key, known.Label, number + unit);
            }

            return new MetricLine(key, key, value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>The first failure of each rejected attempt, e.g. "1: cover 0.16 below 0.18".</summary>
        public static List<string> Rejections(GeneratedArena arena)
        {
            var lines = new List<string>();
            if (arena?.RejectedAttempts == null)
            {
                return lines;
            }

            for (int i = 0; i < arena.RejectedAttempts.Count; i++)
            {
                ArenaValidationReport r = arena.RejectedAttempts[i];
                string reason = r?.Failures != null && r.Failures.Count > 0 ? r.Failures[0] : "rejected";
                lines.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ": " + reason);
            }

            return lines;
        }
    }
}
