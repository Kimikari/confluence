using System;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;
using Confluence.Sim.Players;

namespace Confluence.Client.Laboratory
{
    /// <summary>
    /// What a finalized Economy loadout brings into a Sim duel (spec L46 "opponent final loadouts become visible"):
    /// wizard, the six equipped spells in slot order, every equipped item in its slot, the active (non-dormant) augments
    /// with their ranks, and the one enabled active. Stat modifiers and reserve shields come from content behaviors
    /// that later stages register, so none are added here. The offline Laboratory uses this mapping; the server uses
    /// Confluence.Hosting's equivalent once that adapter lands (docs/ARCHITECTURE.md).
    /// </summary>
    public static class LoadoutDuelSetup
    {
        public static DuelPlayerSetup Build(Loadout loadout, string playerId)
        {
            if (loadout == null)
            {
                throw new ArgumentNullException(nameof(loadout));
            }

            var setup = new DuelPlayerSetup { PlayerId = playerId, WizardId = loadout.WizardId };
            setup.EquippedSpellIds.AddRange(loadout.EquippedSpellIds);
            for (int i = 0; i < GlobalRules.Loadout.EquipmentSlots; i++)
            {
                var slot = (EquipmentSlot)i;
                OwnedEquipment item = loadout.ItemIn(slot);
                if (item != null && !item.IsSpent)
                {
                    setup.Equipment.Add(EquippedItem.FromDefinition(item.Definition, slot));
                }
            }

            foreach (OwnedAugment augment in loadout.ActiveAugments)
            {
                setup.Augments.Add(new ActiveAugment(augment.Id, augment.Rank));
            }

            OwnedEquipment active = loadout.EnabledActive;
            setup.EnabledActiveItemId = active != null ? active.Id : null;
            return setup;
        }
    }
}
