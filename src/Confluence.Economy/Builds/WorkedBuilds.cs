using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Builds
{
    /// <summary>One free selection of a worked build's draft plan.</summary>
    public sealed class PlannedPick
    {
        public PlannedPick(int round, string augmentId, int rank)
        {
            Round = round;
            AugmentId = augmentId;
            Rank = rank;
        }

        /// <summary>Scheduled Conclave round of the draft.</summary>
        public int Round { get; }
        public string AugmentId { get; }

        /// <summary>The rank this choice reaches: 1 for a new definition, 2 for its upgrade.</summary>
        public int Rank { get; }

        public override string ToString() => "Round " + Round + ": " + AugmentId + " rank " + Rank;
    }

    /// <summary>
    /// Worked build examples as concrete loadouts (spec L689-L717). A build lists the target loadout, not a purchase
    /// order; its equipment cost excludes issued items (L697).
    /// </summary>
    public static class WorkedBuilds
    {
        /// <summary>
        /// Parses draft plan lines such as "Round 1: AUG-149 First Savings rank 1 (free)" (BLD-001..036). Empty when the
        /// build has no draft plan; unparseable lines are reported as problems.
        /// </summary>
        public static IReadOnlyList<PlannedPick> ParseDraftPlan(BuildDef build, out IReadOnlyList<string> problems)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var picks = new List<PlannedPick>();
            var issues = new List<string>();
            foreach (var line in build.DraftPlan)
            {
                if (TryParsePick(line, out var pick)) picks.Add(pick);
                else issues.Add(build.Id + ": cannot read draft plan line '" + line + "'.");
            }
            problems = issues.AsReadOnly();
            return picks.AsReadOnly();
        }

        public static bool TryParsePick(string line, out PlannedPick pick)
        {
            pick = null;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("Round ", StringComparison.Ordinal)) return false;
            int colon = line.IndexOf(':');
            if (colon < 0) return false;
            if (!int.TryParse(line.Substring(6, colon - 6).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int round)) return false;
            string rest = line.Substring(colon + 1).Trim();
            int space = rest.IndexOf(' ');
            string id = space < 0 ? rest : rest.Substring(0, space);
            if (!ContentIds.IsWellFormed(id, ContentType.Augment)) return false;
            int rankAt = rest.LastIndexOf(" rank ", StringComparison.Ordinal);
            if (rankAt < 0) return false;
            string rankText = rest.Substring(rankAt + 6).Trim();
            int end = 0;
            while (end < rankText.Length && char.IsDigit(rankText[end])) end++;
            if (end == 0 || !int.TryParse(rankText.Substring(0, end), NumberStyles.None, CultureInfo.InvariantCulture, out int rank)) return false;
            pick = new PlannedPick(round, id, rank);
            return true;
        }

        /// <summary>
        /// The build's loadout: its wizard, its six equipped spells, and its equipment placed into legal slots (items with the
        /// fewest legal slots first, so a staff takes the Main Focus before a wand). No augments are granted.
        /// include, when given, keeps only some items (for example those whose tier is unlocked by a round).
        /// </summary>
        public static Loadout CreateLoadout(Catalogue catalogue, BuildDef build, out IReadOnlyList<string> problems,
            Func<EquipmentDef, bool> include = null)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (build == null) throw new ArgumentNullException(nameof(build));
            var issues = new List<string>();
            var loadout = new Loadout(catalogue, build.WizardId);
            var spells = loadout.SetEquippedSpells(build.EquippedSpellIds);
            if (spells != TransactionCode.Success) issues.Add(build.Id + ": equipped spells rejected (" + spells + ").");

            var items = new List<EquipmentDef>();
            foreach (var id in build.EquipmentIds)
            {
                if (!catalogue.TryGetEquipment(id, out var def))
                {
                    issues.Add(build.Id + ": " + id + " is not shared equipment.");
                    continue;
                }
                if (include == null || include(def)) items.Add(def);
            }
            var order = new List<int>();
            for (int i = 0; i < items.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byOptions = SlotRules.SlotsFor(items[a]).Count.CompareTo(SlotRules.SlotsFor(items[b]).Count);
                return byOptions != 0 ? byOptions : a.CompareTo(b);
            });

            foreach (int i in order)
            {
                var def = items[i];
                bool placed = false;
                var last = TransactionCode.NoFreeSpace;
                foreach (var slot in SlotRules.SlotsFor(def))
                {
                    if (loadout.ItemIn(slot) != null) continue;
                    last = loadout.AddItem(def, def.Price, Placement.In(slot), out _);
                    if (last == TransactionCode.Success)
                    {
                        placed = true;
                        break;
                    }
                }
                if (!placed) issues.Add(build.Id + ": " + def.Id + " has no legal equipment slot (" + last + ").");
            }
            problems = issues.AsReadOnly();
            return loadout;
        }

        /// <summary>Total purchase price of the listed equipment (issued items are free, spec L697).</summary>
        public static int EquipmentCost(Catalogue catalogue, BuildDef build)
        {
            int total = 0;
            foreach (var id in build.EquipmentIds)
                if (catalogue.TryGetEquipment(id, out var def)) total += def.Price;
            return total;
        }
    }
}
