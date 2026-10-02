using System;
using System.Collections.Generic;
using Confluence.Client.Laboratory;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Loadouts;
using Confluence.Sim.Players;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// <see cref="IBuildViewModel"/> over a local Confluence.Economy <see cref="Loadout"/> (offline Laboratory and
    /// tests). Dormancy and missing requirements are the ones the economy last resolved (spec L249, L587, L641); final
    /// statistics come from the Sim <see cref="StatSheet"/> the duel would build, with raw and effective values
    /// (spec L355). Call <see cref="Refresh"/> after any change to the loadout.
    /// </summary>
    public sealed class LoadoutBuildModel : IBuildViewModel
    {
        private readonly PlayerEconomy _player;
        private readonly List<EquipmentSlotView> _slots = new List<EquipmentSlotView>();
        private readonly List<ReserveItemView> _reserve = new List<ReserveItemView>();
        private readonly List<SpellView> _spells = new List<SpellView>();
        private readonly List<AugmentSlotView> _augments = new List<AugmentSlotView>();
        private readonly List<StatView> _stats = new List<StatView>();
        private readonly List<ActiveChoiceView> _activeChoices = new List<ActiveChoiceView>();

        public LoadoutBuildModel(PlayerEconomy player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            Refresh();
        }

        public event Action Changed;

        public Loadout Loadout => _player.Loadout;
        public string WizardName { get; private set; }
        public string WizardId => _player.Loadout.WizardId;
        public string Signature { get; private set; }
        public string Passive { get; private set; }
        public IReadOnlyList<EquipmentSlotView> Slots => _slots;
        public IReadOnlyList<ReserveItemView> Reserve => _reserve;
        public int ReserveCapacity => GlobalRules.Economy.ReserveSlots;
        public IReadOnlyList<SpellView> Spells => _spells;
        public IReadOnlyList<AugmentSlotView> Augments => _augments;
        public IReadOnlyList<StatView> Stats => _stats;
        public string EnabledActive { get; private set; }
        public IReadOnlyList<ActiveChoiceView> ActiveChoices => _activeChoices;

        public void Refresh()
        {
            Loadout loadout = _player.Loadout;
            Catalogue catalogue = loadout.Catalogue;
            WizardDef wizard = loadout.Wizard;
            WizardName = wizard.Name + ", " + wizard.Title;
            Signature = wizard.Signature;
            Passive = wizard.Passive;
            FillSlots(loadout);
            FillReserve(loadout);
            FillSpells(loadout, catalogue);
            FillAugments(loadout, catalogue);
            FillStats(loadout);
            FillActives(loadout);
            Changed?.Invoke();
        }

        private void FillSlots(Loadout loadout)
        {
            _slots.Clear();
            for (int i = 0; i < GlobalRules.Loadout.EquipmentSlots; i++)
            {
                var slot = (EquipmentSlot)i;
                OwnedEquipment item = loadout.ItemIn(slot);
                var view = new EquipmentSlotView { SlotName = ContentFormat.SlotName(slot) };
                if (item != null)
                {
                    view.InstanceId = item.InstanceId;
                    view.ItemId = item.Id;
                    view.ItemName = item.Definition.Name + (item.IsConsumable ? " (" + item.Charges + " charge)" : string.Empty);
                    view.ActiveLabel = loadout.ActiveStatusLabel(item);
                    view.SellValue = item.SellValue;
                    view.Effect = item.Definition.Effect;
                }
                else if (loadout.HoldsIssued(slot))
                {
                    view.Issued = true;
                    view.ItemName = IssuedEquipment.For(slot).Name + " (issued, no bonus)";
                }
                else
                {
                    view.Empty = true;
                    view.ItemName = "Empty";
                }

                _slots.Add(view);
            }
        }

        private void FillReserve(Loadout loadout)
        {
            _reserve.Clear();
            foreach (OwnedEquipment item in loadout.Reserve)
            {
                _reserve.Add(new ReserveItemView
                {
                    InstanceId = item.InstanceId,
                    ItemId = item.Id,
                    ItemName = item.Definition.Name,
                    SellValue = item.SellValue,
                    SlotText = item.Definition.Slot,
                });
            }
        }

        private void FillSpells(Loadout loadout, Catalogue catalogue)
        {
            _spells.Clear();
            IReadOnlyList<string> equipped = loadout.EquippedSpellIds;
            foreach (string id in loadout.KitSpellIds)
            {
                SpellDef spell = catalogue.Spell(id);
                int slot = IndexOf(equipped, id);
                _spells.Add(new SpellView
                {
                    Id = id,
                    Name = spell.Name,
                    Role = spell.Role,
                    CostText = ContentFormat.SpellCost(spell),
                    Equipped = slot >= 0,
                    SlotNumber = slot + 1,
                });
            }
        }

        private void FillAugments(Loadout loadout, Catalogue catalogue)
        {
            _augments.Clear();
            IReadOnlyList<OwnedAugment> slots = loadout.AugmentSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                OwnedAugment owned = slots[i];
                var view = new AugmentSlotView { Slot = i };
                if (owned != null)
                {
                    view.Empty = false;
                    view.Id = owned.Id;
                    view.Name = owned.Definition.Name;
                    view.Rank = owned.Rank;
                    view.Rarity = ContentFormat.RarityName(owned.OriginalRarity);
                    view.IsTransformation = owned.IsTransformation;
                    view.TargetSpellName = ContentFormat.SpellName(catalogue, owned.Definition.TargetSpellId);
                    view.Dormant = owned.IsDormant;
                    view.Missing = Texts(owned.MissingRequirements);
                    view.DormantParents = owned.DormantParents ?? Array.Empty<string>();
                }

                _augments.Add(view);
            }
        }

        private void FillStats(Loadout loadout)
        {
            _stats.Clear();
            DuelPlayerSetup setup = LoadoutDuelSetup.Build(loadout, "build");
            var sheet = new StatSheet();
            for (int i = 0; i < setup.Modifiers.Count; i++)
            {
                sheet.Add(setup.Modifiers[i]);
            }

            _stats.Add(new StatView { Name = "Health", Raw = Format(sheet.MaxHealth), Effective = Format(sheet.MaxHealth) });
            _stats.Add(new StatView { Name = "Mana", Raw = Format(GlobalRules.Resources.BaseMana), Effective = Format(GlobalRules.Resources.BaseMana) });
            _stats.Add(new StatView { Name = "Stamina", Raw = Format(GlobalRules.Resources.BaseStamina), Effective = Format(GlobalRules.Resources.BaseStamina) });
            foreach (StatLine line in sheet.Inspect())
            {
                if (line.Stat == StatKind.MaxHealthBonus)
                {
                    continue; // shown as Health above
                }

                _stats.Add(new StatView
                {
                    Name = ContentFormat.StatName(line),
                    Raw = ContentFormat.StatValue(line.Stat, line.Raw),
                    Effective = ContentFormat.StatValue(line.Stat, line.Effective),
                    Capped = line.Capped,
                });
            }
        }

        private void FillActives(Loadout loadout)
        {
            _activeChoices.Clear();
            OwnedEquipment enabled = loadout.EnabledActive;
            EnabledActive = enabled != null ? enabled.Definition.Name : "None";
            _activeChoices.Add(new ActiveChoiceView { InstanceId = null, Name = "No active", Selected = enabled == null });
            foreach (OwnedEquipment item in loadout.AllItems)
            {
                if (!item.IsActive)
                {
                    continue;
                }

                loadout.FindItem(item.InstanceId, out Placement placement);
                _activeChoices.Add(new ActiveChoiceView
                {
                    InstanceId = item.InstanceId,
                    Name = item.Definition.Name + " (" + placement + ")",
                    Selected = enabled != null && enabled.InstanceId == item.InstanceId,
                    Enableable = placement.IsSlot && SlotRules.SlotAllowsActivation(placement.Slot),
                });
            }
        }

        private static string Format(float value) => ContentFormat.Number(value);

        private static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static IReadOnlyList<string> Texts(IReadOnlyList<Confluence.Economy.Eligibility.MissingRequirement> missing)
        {
            if (missing == null || missing.Count == 0)
            {
                return Array.Empty<string>();
            }

            var texts = new string[missing.Count];
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i] = missing[i].Text;
            }

            return texts;
        }
    }
}
