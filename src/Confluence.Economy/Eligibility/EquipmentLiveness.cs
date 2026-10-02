using System;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// Which equipped items supply which live tags (spec L210, L239, L575, L585):
    /// a passive item supplies all its tags; an active item supplies its effect tags (including active_equipment) only
    /// while its activation is the enabled one; a disabled active keeps only its category identity tag (a wand is still a
    /// wand); a consumable supplies its tags only while it holds a purchased charge. A spent consumable is inert.
    /// </summary>
    public static class EquipmentLiveness
    {
        /// <summary>The item takes part in eligibility at all: every item except a spent consumable shell.</summary>
        public static bool IsPresent(OwnedEquipment item) => item != null && !item.IsSpent;

        /// <summary>The item's effect is live: present, and if it is an active item, its activation is enabled.</summary>
        public static bool HasLiveEffect(Loadout loadout, OwnedEquipment item)
        {
            if (!IsPresent(item)) return false;
            return !item.IsActive || loadout.IsActivationEnabled(item);
        }

        /// <summary>Whether this equipped item currently supplies the tag for equipped_tag counting (spec L615, L585).</summary>
        public static bool SuppliesTag(Loadout loadout, OwnedEquipment item, string tag)
        {
            if (!IsPresent(item) || !item.Definition.HasTag(tag)) return false;
            if (HasLiveEffect(loadout, item)) return true;
            // A disabled active "does not supply an active-effect tag" (spec L585); its category identity remains.
            return IsCategoryIdentityTag(item.Definition, tag);
        }

        /// <summary>The lower-case category name carried as a tag ("wand", "staff", "weapon").</summary>
        public static bool IsCategoryIdentityTag(EquipmentDef def, string tag)
        {
            if (!def.HasValidCategory) return false;
            string name = ContentText.CategoryName(def.Category).ToLowerInvariant().Replace(' ', '_');
            return string.Equals(name, tag, StringComparison.Ordinal);
        }

        public static bool IsCategory(OwnedEquipment item, EquipmentCategory category) =>
            IsPresent(item) && item.Definition.HasValidCategory && item.Definition.Category == category;
    }
}
