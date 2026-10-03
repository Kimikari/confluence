using System;
using System.Collections.Generic;
using Confluence.Client.ViewModels;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>Optional shop commands the build panel offers next to owned items (sell, reserve moves, spells, actives).</summary>
    public sealed class BuildPanelActions
    {
        public Action<int> Sell { get; set; }
        public Action<int> ToggleReserve { get; set; }
        public Action<string, string> SwapSpells { get; set; }
        public Action<int?> SetActive { get; set; }
    }

    /// <summary>
    /// The build panel (spec L355) from an <see cref="IBuildViewModel"/>: the eight equipment slots and three reserve
    /// slots, the eight exclusive spells (six equipped with slot numbers), six augment slots with dormant labels and
    /// their missing requirements (L641), the active item choice (L575), and final statistics with raw and effective
    /// values where a cap applies. With <see cref="BuildPanelActions"/> (the shop) items can be sold or moved and spells
    /// swapped: pick an unequipped spell, then the equipped spell it replaces.
    /// </summary>
    public sealed class BuildPanel
    {
        private readonly IBuildViewModel _model;
        private readonly BuildPanelActions _actions;
        private readonly VisualElement _content;
        private string _benchSpell;

        public BuildPanel(IBuildViewModel model, BuildPanelActions actions = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _actions = actions;
            Root = Ui.Element("cf-build");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-build-scroll");
            _content = scroll.contentContainer;
            Root.Add(scroll);
            _model.Changed += Refresh;
            Refresh();
        }

        public VisualElement Root { get; }

        public void Detach() => _model.Changed -= Refresh;

        public void Refresh()
        {
            _content.Clear();
            _content.Add(Ui.Label(_model.WizardName, "cf-build-title"));
            _content.Add(Ui.Muted("Signature: " + DuelHudModel.LeadName(_model.Signature) + "  |  Passive: " + DuelHudModel.LeadName(_model.Passive)));

            Section("Equipment slots");
            foreach (EquipmentSlotView slot in _model.Slots)
            {
                VisualElement row = Ui.Element("cf-build-row");
                row.Add(Ui.Label(slot.SlotName, "cf-build-key"));
                Label item = Ui.Label(slot.ItemName, slot.Empty || slot.Issued ? "cf-build-dim" : "cf-build-value");
                row.Add(item);
                if (!string.IsNullOrEmpty(slot.ActiveLabel))
                {
                    row.Add(Ui.Label(slot.ActiveLabel, slot.ActiveLabel.StartsWith("Dormant", StringComparison.Ordinal) ? "cf-build-dormant" : "cf-build-tag"));
                }

                if (_actions != null && slot.InstanceId > 0)
                {
                    int id = slot.InstanceId;
                    row.Add(Ui.Button("Sell " + slot.SellValue, () => _actions.Sell?.Invoke(id), "cf-button--tiny"));
                    row.Add(Ui.Button("To reserve", () => _actions.ToggleReserve?.Invoke(id), "cf-button--tiny"));
                }

                _content.Add(row);
            }

            Section("Reserve (" + _model.Reserve.Count + "/" + _model.ReserveCapacity + ")");
            if (_model.Reserve.Count == 0)
            {
                _content.Add(Ui.Muted("Empty."));
            }

            foreach (ReserveItemView item in _model.Reserve)
            {
                VisualElement row = Ui.Element("cf-build-row");
                row.Add(Ui.Label(item.ItemName, "cf-build-value"));
                row.Add(Ui.Label(item.SlotText, "cf-build-dim"));
                if (_actions != null)
                {
                    int id = item.InstanceId;
                    row.Add(Ui.Button("Sell " + item.SellValue, () => _actions.Sell?.Invoke(id), "cf-button--tiny"));
                    row.Add(Ui.Button("Equip", () => _actions.ToggleReserve?.Invoke(id), "cf-button--tiny"));
                }

                _content.Add(row);
            }

            Section("Active item: " + _model.EnabledActive);
            if (_actions != null && _model.ActiveChoices.Count > 1)
            {
                VisualElement choices = Ui.Element("cf-build-wrap");
                foreach (ActiveChoiceView choice in _model.ActiveChoices)
                {
                    int? id = choice.InstanceId;
                    Button button = Ui.Button(choice.Name, () => _actions.SetActive?.Invoke(id), "cf-button--tiny");
                    button.EnableInClassList("cf-button--selected", choice.Selected);
                    button.SetEnabled(choice.Enableable);
                    choices.Add(button);
                }

                _content.Add(choices);
            }

            Section("Spells (six of eight equipped)");
            if (_actions != null)
            {
                _content.Add(Ui.Muted(_benchSpell == null
                    ? "To change spells: pick an unequipped spell, then the equipped spell it replaces."
                    : "Now pick the equipped spell to replace (or the same spell to cancel)."));
            }

            foreach (SpellView spell in _model.Spells)
            {
                VisualElement row = Ui.Element("cf-build-row");
                row.Add(Ui.Label(spell.Equipped ? spell.SlotNumber.ToString() : "-", "cf-build-slotnum"));
                VisualElement text = Ui.Element("cf-build-spell");
                text.Add(Ui.Label(spell.Name + "  (" + spell.Id + ", " + spell.Role + ")", spell.Equipped ? "cf-build-value" : "cf-build-dim"));
                text.Add(Ui.Label(spell.CostText, "cf-build-small"));
                row.Add(text);
                if (_actions != null)
                {
                    string id = spell.Id;
                    bool equipped = spell.Equipped;
                    string label = equipped ? (_benchSpell != null ? "Replace" : string.Empty) : (_benchSpell == id ? "Cancel" : "Swap in");
                    if (label.Length > 0)
                    {
                        row.Add(Ui.Button(label, () => OnSpell(id, equipped), "cf-button--tiny"));
                    }
                }

                _content.Add(row);
            }

            Section("Augments (six slots)");
            foreach (AugmentSlotView slot in _model.Augments)
            {
                VisualElement row = Ui.Element("cf-build-row");
                row.Add(Ui.Label((slot.Slot + 1).ToString(), "cf-build-slotnum"));
                if (slot.Empty)
                {
                    row.Add(Ui.Label("Empty", "cf-build-dim"));
                    _content.Add(row);
                    continue;
                }

                VisualElement text = Ui.Element("cf-build-spell");
                string target = slot.IsTransformation ? " - transforms " + slot.TargetSpellName : string.Empty;
                text.Add(Ui.Label(slot.Name + " (" + slot.Rarity + ", rank " + slot.Rank + ")" + target, slot.Dormant ? "cf-build-dim" : "cf-build-value"));
                if (slot.Dormant)
                {
                    text.Add(Ui.Label("DORMANT", "cf-build-dormant"));
                    foreach (string missing in slot.Missing)
                    {
                        text.Add(Ui.Label("Missing: " + missing, "cf-build-small"));
                    }

                    foreach (string parent in slot.DormantParents)
                    {
                        text.Add(Ui.Label("Depends on dormant " + parent, "cf-build-small"));
                    }
                }

                row.Add(text);
                _content.Add(row);
            }

            Section("Final statistics (raw / effective after caps)");
            foreach (StatView stat in _model.Stats)
            {
                VisualElement row = Ui.Element("cf-build-row");
                row.Add(Ui.Label(stat.Name, "cf-build-key"));
                row.Add(Ui.Label(stat.Capped ? stat.Raw + " -> " + stat.Effective + " (capped)" : stat.Effective, stat.Capped ? "cf-build-dormant" : "cf-build-value"));
                _content.Add(row);
            }
        }

        private void OnSpell(string id, bool equipped)
        {
            if (!equipped)
            {
                _benchSpell = _benchSpell == id ? null : id;
                Refresh();
                return;
            }

            if (_benchSpell != null)
            {
                string bench = _benchSpell;
                _benchSpell = null;
                _actions.SwapSpells?.Invoke(id, bench);
            }
        }

        private void Section(string title) => _content.Add(Ui.Label(title, "cf-build-section"));
    }
}
