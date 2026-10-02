using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;

namespace Confluence.Economy.Loadouts
{
    /// <summary>
    /// One player's build: the selected wizard, the eight-spell exclusive kit with six equipped spells, eight
    /// equipment slots plus three reserve slots, the single enabled equipment active, and six augment slots.
    /// Every mutation validates the complete arrangement first and either applies fully or returns a failure code
    /// with nothing changed. Spells are never purchased; only the wizard's own kit can be equipped (spec L185, L292, L521).
    /// </summary>
    public sealed class Loadout
    {
        private const int SlotCount = GlobalRules.Loadout.EquipmentSlots;                 // spec L210
        private const int ReserveCapacity = GlobalRules.Economy.ReserveSlots;             // spec L210
        private const int SpellSlots = GlobalRules.Loadout.EquippedSpellSlots;            // spec L292
        private const int AugmentSlotCount = GlobalRules.Draft.AugmentSlots;              // spec L231

        private readonly OwnedEquipment[] _slots = new OwnedEquipment[SlotCount];
        private readonly List<OwnedEquipment> _reserve = new List<OwnedEquipment>(ReserveCapacity);
        private readonly string[] _equippedSpells = new string[SpellSlots];
        private readonly OwnedAugment[] _augments = new OwnedAugment[AugmentSlotCount];
        private int _enabledActiveId;
        private int _nextInstanceId = 1;

        public Loadout(Catalogue catalogue, string wizardId)
        {
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            Wizard = catalogue.Wizard(wizardId);
            KitSpellIds = Wizard.SpellIds;
            if (KitSpellIds.Count < SpellSlots) throw new ArgumentException(wizardId + " has fewer than " + SpellSlots + " spells.", nameof(wizardId));

            // "Equip six, defaulting to the first six" (spec L292).
            var defaults = IsLegalSpellSelection(Wizard.DefaultSpellIds) ? Wizard.DefaultSpellIds : FirstSix(KitSpellIds);
            for (int i = 0; i < SpellSlots; i++) _equippedSpells[i] = defaults[i];
        }

        private Loadout(Loadout source)
        {
            Catalogue = source.Catalogue;
            Wizard = source.Wizard;
            KitSpellIds = source.KitSpellIds;
            for (int i = 0; i < SlotCount; i++) _slots[i] = source._slots[i]?.Clone();
            foreach (var item in source._reserve) _reserve.Add(item.Clone());
            Array.Copy(source._equippedSpells, _equippedSpells, SpellSlots);
            for (int i = 0; i < AugmentSlotCount; i++) _augments[i] = source._augments[i]?.Clone();
            _enabledActiveId = source._enabledActiveId;
            _nextInstanceId = source._nextInstanceId;
            IsFrozen = source.IsFrozen;
            EligibilityProfile = source.EligibilityProfile;
        }

        public Catalogue Catalogue { get; }
        public WizardDef Wizard { get; }
        public string WizardId => Wizard.Id;

        /// <summary>The wizard's eight exclusive spells; the `spell` predicate leaf reads this kit (spec L239, L617).</summary>
        public IReadOnlyList<string> KitSpellIds { get; }

        /// <summary>The six equipped spells in slot order.</summary>
        public IReadOnlyList<string> EquippedSpellIds => Array.AsReadOnly((string[])_equippedSpells.Clone());

        /// <summary>Set while an augment draft is open: equipment and spells cannot change (spec L227).</summary>
        public bool IsFrozen { get; internal set; }

        /// <summary>The structural spell profile from the last dormancy resolution (spec L243, L543); null before the first.</summary>
        public StructuralProfile EligibilityProfile { get; internal set; }

        public Loadout Clone() => new Loadout(this);

        // ---- Spells ------------------------------------------------------------------------------

        public bool OwnsSpell(string spellId)
        {
            for (int i = 0; i < KitSpellIds.Count; i++)
                if (KitSpellIds[i] == spellId) return true;
            return false;
        }

        public bool IsSpellEquipped(string spellId) => Array.IndexOf(_equippedSpells, spellId) >= 0;

        /// <summary>Equips six distinct spells from the wizard's own kit, in the given slot order (spec L292).</summary>
        public TransactionCode SetEquippedSpells(IReadOnlyList<string> spellIds)
        {
            if (IsFrozen) return TransactionCode.LoadoutFrozen;
            var code = CheckSpellSelection(spellIds);
            if (code != TransactionCode.Success) return code;
            for (int i = 0; i < SpellSlots; i++) _equippedSpells[i] = spellIds[i];
            return TransactionCode.Success;
        }

        public TransactionCode CheckSpellSelection(IReadOnlyList<string> spellIds)
        {
            if (spellIds == null || spellIds.Count != SpellSlots) return TransactionCode.InvalidSpellSelection;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in spellIds)
            {
                if (id == null) return TransactionCode.InvalidSpellSelection;
                if (!OwnsSpell(id)) return TransactionCode.SpellNotInKit;
                if (!seen.Add(id)) return TransactionCode.DuplicateSpell;
            }
            return TransactionCode.Success;
        }

        private bool IsLegalSpellSelection(IReadOnlyList<string> ids) => CheckSpellSelection(ids) == TransactionCode.Success;

        private static IReadOnlyList<string> FirstSix(IReadOnlyList<string> kit)
        {
            var first = new string[SpellSlots];
            for (int i = 0; i < SpellSlots; i++) first[i] = kit[i];
            return first;
        }

        // ---- Equipment queries -------------------------------------------------------------------

        /// <summary>The purchased item in a slot, or null when the slot is empty (holding its issued item if it has one).</summary>
        public OwnedEquipment ItemIn(EquipmentSlot slot) => _slots[(int)slot];

        /// <summary>The slot holds only its plain issued equipment, or nothing for slots without issued gear (spec L202, L625).</summary>
        public bool HoldsIssued(EquipmentSlot slot) => _slots[(int)slot] == null && IssuedEquipment.HasIssued(slot);

        public IReadOnlyList<OwnedEquipment> Reserve => _reserve.AsReadOnly();

        /// <summary>Items in equipment slots, in slot order.</summary>
        public IReadOnlyList<OwnedEquipment> EquippedItems
        {
            get
            {
                var list = new List<OwnedEquipment>(SlotCount);
                foreach (var item in _slots)
                    if (item != null) list.Add(item);
                return list;
            }
        }

        /// <summary>Every held item: equipment slots in order, then reserve.</summary>
        public IReadOnlyList<OwnedEquipment> AllItems
        {
            get
            {
                var list = new List<OwnedEquipment>(SlotCount + ReserveCapacity);
                list.AddRange(EquippedItems);
                list.AddRange(_reserve);
                return list;
            }
        }

        public OwnedEquipment FindItem(int instanceId, out Placement placement)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != null && _slots[i].InstanceId == instanceId)
                {
                    placement = Placement.In((EquipmentSlot)i);
                    return _slots[i];
                }
            }
            foreach (var item in _reserve)
            {
                if (item.InstanceId == instanceId)
                {
                    placement = Placement.Reserve;
                    return item;
                }
            }
            placement = Placement.Reserve;
            return null;
        }

        /// <summary>The single enabled equipment active, or null (spec L210, L575).</summary>
        public OwnedEquipment EnabledActive
        {
            get
            {
                if (_enabledActiveId == 0) return null;
                var item = FindItem(_enabledActiveId, out var placement);
                return item != null && placement.IsSlot ? item : null;
            }
        }

        /// <summary>Whether this equipped item's activation key is live: it is the chosen enabled active.</summary>
        public bool IsActivationEnabled(OwnedEquipment item) => item != null && item.IsActive && item.InstanceId == _enabledActiveId;

        /// <summary>
        /// An equipped active item whose activation is disabled. Dormant actives are plainly labeled (spec L210) and
        /// supply no active-effect tag (spec L585).
        /// </summary>
        public bool IsDormantActive(OwnedEquipment item)
        {
            if (item == null || !item.IsActive) return false;
            FindItem(item.InstanceId, out var placement);
            return placement.IsSlot && !IsActivationEnabled(item);
        }

        /// <summary>Plain label for an equipped active item's state, or null for passive items and consumables.</summary>
        public string ActiveStatusLabel(OwnedEquipment item)
        {
            if (item == null || !item.IsActive) return null;
            FindItem(item.InstanceId, out var placement);
            if (placement.IsReserve) return "In reserve";
            if (IsActivationEnabled(item)) return "Enabled active";
            if (!SlotRules.SlotAllowsActivation(placement.Slot)) return "Dormant: a Sidearm cannot use its activation";
            return "Dormant: another active (or none) is chosen";
        }

        /// <summary>Unspent purchased consumable charges held in the Consumable slot and reserve (spec L628).</summary>
        public int HeldConsumableCharges
        {
            get
            {
                int total = 0;
                foreach (var item in AllItems)
                    if (item.IsConsumable && item.Charges > 0) total += item.Charges;
                return total;
            }
        }

        // ---- Equipment mutations -----------------------------------------------------------------

        /// <summary>
        /// Where a new copy of the definition would go: the requested placement, or automatically the first empty legal
        /// equipment slot without a definition or Unique-group conflict, else the reserve.
        /// </summary>
        public TransactionCode CheckAdd(EquipmentDef definition, Placement? requested, out Placement placement)
        {
            placement = Placement.Reserve;
            if (IsFrozen) return TransactionCode.LoadoutFrozen;
            if (definition == null) return TransactionCode.UnknownItem;
            if (!SlotRules.IsLegalDefinition(definition)) return TransactionCode.IllegalDefinition;
            var probe = new OwnedEquipment(0, definition, 0, 0);
            if (requested.HasValue)
            {
                placement = requested.Value;
                return CheckArrangementWith(probe, placement);
            }
            foreach (var slot in SlotRules.SlotsFor(definition))
            {
                if (_slots[(int)slot] != null) continue;
                if (CheckArrangementWith(probe, Placement.In(slot)) != TransactionCode.Success) continue;
                placement = Placement.In(slot);
                return TransactionCode.Success;
            }
            if (_reserve.Count < ReserveCapacity)
            {
                placement = Placement.Reserve;
                return TransactionCode.Success;
            }
            return TransactionCode.NoFreeSpace;
        }

        /// <summary>
        /// Adds a copy of the definition (the shop calls this after charging the price). A consumable carries one charge
        /// per purchase (spec L212). An active item placed where it can activate becomes the enabled active when none is.
        /// </summary>
        public TransactionCode AddItem(EquipmentDef definition, int paidPrice, Placement? requested, out OwnedEquipment item)
        {
            item = null;
            if (paidPrice < 0) throw new ArgumentOutOfRangeException(nameof(paidPrice));
            var code = CheckAdd(definition, requested, out var placement);
            if (code != TransactionCode.Success) return code;
            int charges = definition.IsConsumable ? GlobalRules.Economy.ConsumableCharges : 0;
            item = new OwnedEquipment(_nextInstanceId++, definition, paidPrice, charges);
            if (placement.IsReserve) _reserve.Add(item);
            else _slots[(int)placement.Slot] = item;
            AutoEnable(item, placement);
            return TransactionCode.Success;
        }

        /// <summary>
        /// Moves an item to a slot or the reserve. When the target slot is occupied the two items swap places; the whole
        /// resulting arrangement must be legal (slot categories, no definition in two slots, distinct Unique groups,
        /// reserve capacity).
        /// </summary>
        public TransactionCode Move(int instanceId, Placement target)
        {
            if (IsFrozen) return TransactionCode.LoadoutFrozen;
            var item = FindItem(instanceId, out var from);
            if (item == null) return TransactionCode.UnknownItem;
            if (from.Equals(target)) return TransactionCode.Success;

            var slots = (OwnedEquipment[])_slots.Clone();
            var reserve = new List<OwnedEquipment>(_reserve);
            if (from.IsReserve) reserve.Remove(item);
            else slots[(int)from.Slot] = null;

            if (target.IsReserve)
            {
                reserve.Add(item);
            }
            else
            {
                var occupant = slots[(int)target.Slot];
                slots[(int)target.Slot] = item;
                if (occupant != null)
                {
                    if (from.IsReserve) reserve.Add(occupant);
                    else slots[(int)from.Slot] = occupant;
                }
            }

            var code = ValidateArrangement(slots, reserve);
            if (code != TransactionCode.Success) return code;
            Array.Copy(slots, _slots, SlotCount);
            _reserve.Clear();
            _reserve.AddRange(reserve);
            ClearInvalidEnabledActive();
            AutoEnable(item, target);
            return TransactionCode.Success;
        }

        /// <summary>Removes an item (the shop refunds its sell value). Issued gear is not an item and cannot be removed.</summary>
        public TransactionCode RemoveItem(int instanceId, out OwnedEquipment removed)
        {
            removed = null;
            if (IsFrozen) return TransactionCode.LoadoutFrozen;
            var item = FindItem(instanceId, out var placement);
            if (item == null) return TransactionCode.UnknownItem;
            if (placement.IsReserve) _reserve.Remove(item);
            else _slots[(int)placement.Slot] = null;
            if (_enabledActiveId == instanceId) _enabledActiveId = 0;
            removed = item;
            return TransactionCode.Success;
        }

        /// <summary>
        /// Chooses the single enabled equipment active (spec L575 "The player chooses one enabled active in the shop;
        /// other active equipment is dormant"). Null disables every active. The item must be an equipped active item
        /// outside the Sidearm (spec L577).
        /// </summary>
        public TransactionCode SetEnabledActive(int? instanceId)
        {
            if (IsFrozen) return TransactionCode.LoadoutFrozen;
            if (!instanceId.HasValue)
            {
                _enabledActiveId = 0;
                return TransactionCode.Success;
            }
            var item = FindItem(instanceId.Value, out var placement);
            if (item == null) return TransactionCode.UnknownItem;
            if (!item.IsActive) return TransactionCode.NotAnActiveItem;
            if (placement.IsReserve || !SlotRules.SlotAllowsActivation(placement.Slot)) return TransactionCode.ActiveItemNotEnableable;
            _enabledActiveId = item.InstanceId;
            return TransactionCode.Success;
        }

        /// <summary>
        /// Spends the equipped consumable's charge on commitment (spec L212). Reported by the match after a duel; the
        /// spent item stays until sold for zero (spec L212 "A used consumable sells for zero and disappears").
        /// </summary>
        public TransactionCode SpendConsumableCharge()
        {
            var item = _slots[(int)EquipmentSlot.Consumable];
            if (item == null || item.Charges <= 0) return TransactionCode.NoConsumableCharge;
            item.Charges--;
            return TransactionCode.Success;
        }

        private TransactionCode CheckArrangementWith(OwnedEquipment probe, Placement placement)
        {
            var slots = (OwnedEquipment[])_slots.Clone();
            var reserve = new List<OwnedEquipment>(_reserve);
            if (placement.IsReserve) reserve.Add(probe);
            else
            {
                if (slots[(int)placement.Slot] != null) return TransactionCode.SlotOccupied;
                slots[(int)placement.Slot] = probe;
            }
            return ValidateArrangement(slots, reserve);
        }

        private static TransactionCode ValidateArrangement(OwnedEquipment[] slots, List<OwnedEquipment> reserve)
        {
            if (reserve.Count > ReserveCapacity) return TransactionCode.ReserveFull;
            for (int i = 0; i < SlotCount; i++)
            {
                var item = slots[i];
                if (item == null) continue;
                if (!SlotRules.Accepts((EquipmentSlot)i, item.Definition)) return TransactionCode.SlotRejectsItem;
                for (int j = 0; j < i; j++)
                {
                    var other = slots[j];
                    if (other == null) continue;
                    // "A definition cannot occupy both" Main Focus and Sidearm; two different trinkets (spec L210, L568).
                    if (other.Id == item.Id) return TransactionCode.DuplicateEquippedDefinition;
                    // "A nonempty Unique group cannot be equipped twice" (spec L210).
                    if (item.Definition.HasUniqueGroup && other.Definition.UniqueGroup == item.Definition.UniqueGroup)
                        return TransactionCode.UniqueGroupConflict;
                }
            }
            return TransactionCode.Success;
        }

        private void ClearInvalidEnabledActive()
        {
            if (_enabledActiveId == 0) return;
            var item = FindItem(_enabledActiveId, out var placement);
            if (item == null || placement.IsReserve || !SlotRules.SlotAllowsActivation(placement.Slot)) _enabledActiveId = 0;
        }

        // An active item placed where it can activate becomes the enabled choice when none is chosen yet; the player can
        // change or clear the choice with SetEnabledActive (DECISIONS: shop default for the active-item choice).
        private void AutoEnable(OwnedEquipment item, Placement placement)
        {
            if (_enabledActiveId != 0 || !item.IsActive || placement.IsReserve) return;
            if (SlotRules.SlotAllowsActivation(placement.Slot)) _enabledActiveId = item.InstanceId;
        }

        // ---- Augments ----------------------------------------------------------------------------

        /// <summary>The six augment slots; null entries are free.</summary>
        public IReadOnlyList<OwnedAugment> AugmentSlots => Array.AsReadOnly((OwnedAugment[])_augments.Clone());

        /// <summary>Owned augments in slot order.</summary>
        public IReadOnlyList<OwnedAugment> Augments
        {
            get
            {
                var list = new List<OwnedAugment>(AugmentSlotCount);
                foreach (var a in _augments)
                    if (a != null) list.Add(a);
                return list;
            }
        }

        /// <summary>Owned augments that are not dormant, in slot order.</summary>
        public IReadOnlyList<OwnedAugment> ActiveAugments
        {
            get
            {
                var list = new List<OwnedAugment>(AugmentSlotCount);
                foreach (var a in _augments)
                    if (a != null && a.IsActive) list.Add(a);
                return list;
            }
        }

        public OwnedAugment FindAugment(string augmentId)
        {
            foreach (var a in _augments)
                if (a != null && a.Id == augmentId) return a;
            return null;
        }

        public int AugmentSlotOf(string augmentId)
        {
            for (int i = 0; i < AugmentSlotCount; i++)
                if (_augments[i] != null && _augments[i].Id == augmentId) return i;
            return -1;
        }

        /// <summary>The first free augment slot, or -1 when all six are occupied.</summary>
        public int FirstFreeAugmentSlot
        {
            get
            {
                for (int i = 0; i < AugmentSlotCount; i++)
                    if (_augments[i] == null) return i;
                return -1;
            }
        }

        /// <summary>
        /// Whether a new definition may join this build: not already owned, owned by this wizard if a transformation, and
        /// no owned augment shares its nonempty exclusivity group (spec L286, L541). Slot capacity is checked separately.
        /// </summary>
        public TransactionCode CheckNewAugment(AugmentDef definition, int ignoreSlot = -1)
        {
            if (definition == null) return TransactionCode.UnknownItem;
            if (FindAugment(definition.Id) != null && AugmentSlotOf(definition.Id) != ignoreSlot) return TransactionCode.AugmentAlreadyOwned;
            if (!BelongsToThisWizard(definition)) return TransactionCode.WrongWizard;
            if (definition.HasExclusiveGroup)
            {
                for (int i = 0; i < AugmentSlotCount; i++)
                {
                    var owned = _augments[i];
                    if (owned == null || i == ignoreSlot) continue;
                    if (owned.Definition.ExclusiveGroup == definition.ExclusiveGroup) return TransactionCode.ExclusiveGroupConflict;
                }
            }
            return TransactionCode.Success;
        }

        /// <summary>A transformation is drafted only by its owner; a shared augment listing eligible wizards only by those.</summary>
        public bool BelongsToThisWizard(AugmentDef definition)
        {
            if (definition.IsTransformation) return definition.OwnerWizardId == WizardId;
            if (definition.EligibleWizards.Count == 0) return true;
            foreach (var id in definition.EligibleWizards)
                if (id == WizardId) return true;
            return false;
        }

        internal void PlaceAugment(int slot, OwnedAugment augment)
        {
            if (slot < 0 || slot >= AugmentSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            _augments[slot] = augment;
        }

        internal OwnedAugment AugmentAt(int slot) => slot >= 0 && slot < AugmentSlotCount ? _augments[slot] : null;

        // ---- Whole-build validation ----------------------------------------------------------

        /// <summary>
        /// Every invariant of a legal build, as plain-language problems (empty when legal): six distinct kit spells,
        /// slot categories, no definition equipped twice, distinct Unique groups, reserve capacity, a single valid
        /// enabled active, at most six augments, ranks 1-2, distinct definitions and exclusivity groups.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var spells = CheckSpellSelection(_equippedSpells);
            if (spells != TransactionCode.Success) problems.Add("Equipped spells: " + spells);
            var arrangement = ValidateArrangement(_slots, _reserve);
            if (arrangement != TransactionCode.Success) problems.Add("Equipment: " + arrangement);
            if (_enabledActiveId != 0)
            {
                var active = FindItem(_enabledActiveId, out var placement);
                if (active == null || !active.IsActive || placement.IsReserve || !SlotRules.SlotAllowsActivation(placement.Slot))
                    problems.Add("Enabled active is not an equipped active item outside the Sidearm.");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in Augments)
            {
                if (!seen.Add(a.Id)) problems.Add(a.Id + " is owned twice.");
                if (a.Rank < 1 || a.Rank > GlobalRules.Draft.MaxRank) problems.Add(a.Id + " has rank " + a.Rank + ".");
                if (a.Definition.HasExclusiveGroup && !groups.Add(a.Definition.ExclusiveGroup))
                    problems.Add(a.Id + " conflicts in exclusivity group '" + a.Definition.ExclusiveGroup + "'.");
                if (!BelongsToThisWizard(a.Definition)) problems.Add(a.Id + " belongs to another wizard.");
            }
            return problems;
        }
    }
}
