using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;

namespace Confluence.Sim.Players
{
    /// <summary>An equipped item as the duel sees it: id, category, occupied slot, and tags.</summary>
    public sealed class EquippedItem
    {
        public EquippedItem(string id, EquipmentCategory category, EquipmentSlot slot, IReadOnlyList<string> tags = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Category = category;
            Slot = slot;
            Tags = tags ?? Array.Empty<string>();
        }

        public string Id { get; }
        public EquipmentCategory Category { get; }
        public EquipmentSlot Slot { get; }
        public IReadOnlyList<string> Tags { get; }

        public bool IsFocusOrWeapon => Category == EquipmentCategory.Wand || Category == EquipmentCategory.Staff || Category == EquipmentCategory.Weapon;

        /// <summary>The common melee profile this item supplies (spec L100).</summary>
        public MeleeProfile MeleeProfile => MeleeProfiles.For(Category, Tags);

        public static EquippedItem FromDefinition(EquipmentDef def, EquipmentSlot slot)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            return new EquippedItem(def.Id, def.Category, slot, def.Tags);
        }
    }

    /// <summary>An active augment (shared or transformation) with its rank. Stored for the content stages.</summary>
    public struct ActiveAugment
    {
        public string Id;
        public int Rank;

        public ActiveAugment(string id, int rank)
        {
            Id = id;
            Rank = rank;
        }
    }

    /// <summary>A round-start reserve shield from equipment or augments (spec L114, L579).</summary>
    public struct ReserveShieldGrant
    {
        public string SourceId;
        public float Amount;

        public ReserveShieldGrant(string sourceId, float amount)
        {
            SourceId = sourceId;
            Amount = amount;
        }
    }

    /// <summary>
    /// What one player brings into a duel: identity, wizard, the six equipped spells, equipment, active augments,
    /// the enabled active item, and resolved stat modifiers. Content stages fill the modifiers and shields; the Sim
    /// applies them under the global caps.
    /// </summary>
    public sealed class DuelPlayerSetup
    {
        public string PlayerId { get; set; }
        public string WizardId { get; set; }
        public List<string> EquippedSpellIds { get; set; } = new List<string>();
        public List<EquippedItem> Equipment { get; set; } = new List<EquippedItem>();
        public List<ActiveAugment> Augments { get; set; } = new List<ActiveAugment>();

        /// <summary>The single enabled equipment active (spec L575), or null.</summary>
        public string EnabledActiveItemId { get; set; }

        /// <summary>Spawn side 0 or 1; -1 uses the player index.</summary>
        public int SpawnSide { get; set; } = -1;

        public List<StatModifier> Modifiers { get; set; } = new List<StatModifier>();
        public List<ReserveShieldGrant> ReserveShields { get; set; } = new List<ReserveShieldGrant>();

        public EquippedItem ItemIn(EquipmentSlot slot)
        {
            for (int i = 0; i < Equipment.Count; i++)
            {
                if (Equipment[i].Slot == slot)
                {
                    return Equipment[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Melee profile of the main focus or sidearm. An empty slot uses the issued plain focus (spec L571), which has
        /// the focus profile.
        /// </summary>
        public MeleeProfile MeleeProfileFor(EquipmentSlot slot)
        {
            EquippedItem item = ItemIn(slot);
            return item != null && item.IsFocusOrWeapon ? item.MeleeProfile : MeleeProfile.Focus;
        }

        /// <summary>Source id for melee damage from a slot: the item id or the common-melee rule id.</summary>
        public string MeleeSourceFor(EquipmentSlot slot)
        {
            EquippedItem item = ItemIn(slot);
            return item != null && item.IsFocusOrWeapon ? item.Id : DamageSources.CommonMelee;
        }

        /// <summary>
        /// Builds a setup from catalogue records. Spells default to the wizard's first six (spec L292); equipment ids
        /// are placed in their first legal slot unless a slot is given.
        /// </summary>
        public static DuelPlayerSetup FromCatalogue(Catalogue catalogue, string playerId, string wizardId,
            IEnumerable<string> spellIds = null, IEnumerable<KeyValuePair<string, EquipmentSlot>> equipment = null,
            IEnumerable<ActiveAugment> augments = null)
        {
            if (catalogue == null)
            {
                throw new ArgumentNullException(nameof(catalogue));
            }

            WizardDef wizard = catalogue.Wizard(wizardId);
            var setup = new DuelPlayerSetup { PlayerId = playerId, WizardId = wizard.Id };
            setup.EquippedSpellIds.AddRange(spellIds ?? wizard.DefaultSpellIds);
            if (equipment != null)
            {
                foreach (KeyValuePair<string, EquipmentSlot> entry in equipment)
                {
                    EquipmentDef def = catalogue.Equipment(entry.Key);
                    setup.Equipment.Add(EquippedItem.FromDefinition(def, entry.Value));
                }
            }

            if (augments != null)
            {
                setup.Augments.AddRange(augments);
            }

            return setup;
        }
    }
}
