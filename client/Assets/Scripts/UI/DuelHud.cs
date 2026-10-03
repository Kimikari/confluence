using System.Collections.Generic;
using System.Globalization;
using Confluence.Client.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// The in-combat HUD (spec L347-L349) rendered from an <see cref="IHudViewModel"/>: health with temporary shield,
    /// mana and stamina bars; the selected spell with its state; six slot cells; signature, enabled active, and
    /// consumable cells; status icons that name hard-control immunity, wet, damage over time, and the healing budget
    /// with text glyphs (never color alone, spec L371); the round timer, boundary cue (L48, L51), and the final-15 s
    /// equal/ahead/behind judgement (L53). Picking is ignored so the HUD never steals mouse input; it scales with the
    /// panel (scalable HUD, L371).
    /// </summary>
    public sealed class DuelHud
    {
        private readonly IHudViewModel _model;
        private readonly Label _timer;
        private readonly Label _phase;
        private readonly Label _boundary;
        private readonly Label _judgement;
        private readonly Label _notice;
        private readonly VisualElement _statusRow;
        private readonly Bar _health;
        private readonly Bar _mana;
        private readonly Bar _stamina;
        private readonly Label _selected;
        private readonly List<SlotCell> _slots = new List<SlotCell>();
        private readonly ActionCell _signature;
        private readonly ActionCell _active;
        private readonly ActionCell _consumable;
        private readonly Label _combat;

        public DuelHud(IHudViewModel model, string sessionLabel, string controlsHint)
        {
            _model = model;
            Root = Ui.Element("cf-duel-hud");
            Root.pickingMode = PickingMode.Ignore;

            VisualElement top = Ui.Element("cf-duel-top");
            _timer = Ui.Label(string.Empty, "cf-duel-timer");
            _phase = Ui.Label(string.Empty, "cf-duel-phase");
            _boundary = Ui.Label(string.Empty, "cf-duel-boundary");
            _judgement = Ui.Label(string.Empty, "cf-duel-judgement");
            top.Add(_timer);
            top.Add(_phase);
            top.Add(_boundary);
            top.Add(_judgement);
            Root.Add(top);

            VisualElement corner = Ui.Element("cf-duel-corner");
            if (!string.IsNullOrEmpty(sessionLabel))
            {
                corner.Add(Ui.Label(sessionLabel, "cf-lab-label"));
            }

            _notice = Ui.Label(string.Empty, "cf-duel-notice");
            corner.Add(_notice);
            _statusRow = Ui.Element("cf-status-row");
            corner.Add(_statusRow);
            Root.Add(corner);

            if (!string.IsNullOrEmpty(controlsHint))
            {
                VisualElement help = Ui.Element("cf-duel-help");
                help.Add(Ui.Label(controlsHint, "cf-duel-help-text"));
                Root.Add(help);
            }

            VisualElement bars = Ui.Element("cf-duel-bars");
            _health = new Bar(bars, "Health", "cf-bar-fill--health", withShield: true);
            _mana = new Bar(bars, "Mana", "cf-bar-fill--mana", withShield: false);
            _stamina = new Bar(bars, "Stamina", "cf-bar-fill--stamina", withShield: false);
            Root.Add(bars);

            VisualElement spells = Ui.Element("cf-duel-spells");
            _selected = Ui.Label(string.Empty, "cf-duel-selected");
            spells.Add(_selected);
            VisualElement slotRow = Ui.Element("cf-slot-row");
            for (int i = 0; i < 6; i++)
            {
                var cell = new SlotCell(i + 1);
                _slots.Add(cell);
                slotRow.Add(cell.Root);
            }

            spells.Add(slotRow);
            Root.Add(spells);

            VisualElement actions = Ui.Element("cf-duel-actions");
            _combat = Ui.Label(string.Empty, "cf-duel-combat");
            actions.Add(_combat);
            _signature = new ActionCell(actions);
            _active = new ActionCell(actions);
            _consumable = new ActionCell(actions);
            Root.Add(actions);

            Root.Add(Ui.Element("cf-crosshair"));
            DamageLayer = Ui.Element("cf-damage-layer");
            DamageLayer.pickingMode = PickingMode.Ignore;
            Root.Add(DamageLayer);
            IgnorePicking(Root);
        }

        public VisualElement Root { get; }

        /// <summary>Full-screen layer for confirmed-hit damage numbers (see Presentation.DamageNumbers).</summary>
        public VisualElement DamageLayer { get; }

        public void Refresh()
        {
            IHudViewModel m = _model;
            _timer.text = m.TimerText;
            _phase.text = m.PhaseText;
            _boundary.text = m.BoundaryText ?? string.Empty;
            _boundary.EnableInClassList("cf-duel-boundary--outside", m.Boundary == BoundaryCue.Outside);
            _boundary.EnableInClassList("cf-duel-boundary--contracting", m.Boundary == BoundaryCue.Contracting);
            _judgement.style.display = m.JudgementVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _judgement.text = m.Judgement > 0 ? "AHEAD on health" : m.Judgement < 0 ? "BEHIND on health" : "EVEN on health";
            _judgement.EnableInClassList("cf-duel-judgement--ahead", m.Judgement > 0);
            _judgement.EnableInClassList("cf-duel-judgement--behind", m.Judgement < 0);
            _notice.text = m.Notice ?? string.Empty;
            _notice.style.display = string.IsNullOrEmpty(m.Notice) ? DisplayStyle.None : DisplayStyle.Flex;

            _health.Set(m.Health, m.MaxHealth, m.Shield);
            _mana.Set(m.Mana, m.MaxMana, 0f);
            _stamina.Set(m.Stamina, m.MaxStamina, 0f);
            _selected.text = m.SelectedSpellLine;
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Set(i < m.Slots.Count ? m.Slots[i] : null);
            }

            _signature.Set(m.Signature);
            _active.Set(m.ActiveItem);
            _consumable.Set(m.Consumable);
            string combat = m.GuardText;
            if (!string.IsNullOrEmpty(m.MeleeText))
            {
                combat = string.IsNullOrEmpty(combat) ? m.MeleeText : combat + "\n" + m.MeleeText;
            }

            _combat.text = combat ?? string.Empty;
            RefreshStatuses(m.Statuses);
        }

        private void RefreshStatuses(IReadOnlyList<StatusIconView> statuses)
        {
            while (_statusRow.childCount > statuses.Count)
            {
                _statusRow.RemoveAt(_statusRow.childCount - 1);
            }

            while (_statusRow.childCount < statuses.Count)
            {
                VisualElement icon = Ui.Element("cf-status-icon");
                icon.pickingMode = PickingMode.Ignore;
                icon.Add(Ui.Label(string.Empty, "cf-status-glyph"));
                icon.Add(Ui.Label(string.Empty, "cf-status-text"));
                IgnorePicking(icon);
                _statusRow.Add(icon);
            }

            for (int i = 0; i < statuses.Count; i++)
            {
                StatusIconView s = statuses[i];
                VisualElement icon = _statusRow[i];
                ((Label)icon[0]).text = s.Glyph;
                string detail = s.Label;
                if (s.Seconds >= 0f)
                {
                    detail += " " + s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                }

                if (!string.IsNullOrEmpty(s.Value))
                {
                    detail += " " + s.Value;
                }

                ((Label)icon[1]).text = detail;
                foreach (StatusIconKind kind in System.Enum.GetValues(typeof(StatusIconKind)))
                {
                    icon.EnableInClassList("cf-status-icon--" + kind.ToString().ToLowerInvariant(), kind == s.Kind);
                }
            }
        }

        private static void IgnorePicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in element.Children())
            {
                IgnorePicking(child);
            }
        }

        private sealed class Bar
        {
            private readonly VisualElement _fill;
            private readonly VisualElement _shield;
            private readonly Label _text;
            private readonly string _name;

            public Bar(VisualElement parent, string name, string fillClass, bool withShield)
            {
                _name = name;
                VisualElement row = Ui.Element("cf-bar-row");
                _text = Ui.Label(name, "cf-bar-label");
                row.Add(_text);
                VisualElement track = Ui.Element("cf-bar-track");
                _fill = Ui.Element("cf-bar-fill", fillClass);
                track.Add(_fill);
                if (withShield)
                {
                    _shield = Ui.Element("cf-bar-fill", "cf-bar-fill--shield");
                    track.Add(_shield);
                }

                row.Add(track);
                parent.Add(row);
            }

            public void Set(float value, float max, float shield)
            {
                float safeMax = max > 0f ? max : 1f;
                float fraction = Mathf.Clamp01(value / safeMax);
                _fill.style.width = Length.Percent(fraction * 100f);
                string text = _name + " " + Mathf.CeilToInt(Mathf.Max(0f, value)) + "/" + Mathf.RoundToInt(max);
                if (_shield != null)
                {
                    // The shield follows the health it protects; with too little room it overlays the bar's end, so a
                    // shield on full health is still drawn (spec L347).
                    float shieldFraction = Mathf.Clamp01(shield / safeMax);
                    _shield.style.left = Length.Percent(Mathf.Min(fraction, 1f - shieldFraction) * 100f);
                    _shield.style.width = Length.Percent(shieldFraction * 100f);
                    _shield.style.display = shield > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                    if (shield > 0f)
                    {
                        text += "  +" + Mathf.CeilToInt(shield) + " shield";
                    }
                }

                _text.text = text;
            }
        }

        private sealed class SlotCell
        {
            private readonly Label _number;
            private readonly Label _name;
            private readonly Label _state;

            public SlotCell(int number)
            {
                Root = Ui.Element("cf-slot");
                _number = Ui.Label(number.ToString(CultureInfo.InvariantCulture), "cf-slot-number");
                _name = Ui.Label(string.Empty, "cf-slot-name");
                _state = Ui.Label(string.Empty, "cf-slot-state");
                Root.Add(_number);
                Root.Add(_name);
                Root.Add(_state);
            }

            public VisualElement Root { get; }

            public void Set(HudSlotView slot)
            {
                Root.EnableInClassList("cf-slot--selected", slot != null && slot.Selected);
                Root.EnableInClassList("cf-slot--unavailable", slot == null || slot.State != SlotState.Ready);
                _name.text = slot?.Name ?? string.Empty;
                _state.text = slot == null ? string.Empty : StateText(slot);
            }

            private static string StateText(HudSlotView slot)
            {
                switch (slot.State)
                {
                    case SlotState.Cooldown:
                        string cd = slot.CooldownSec.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                        return slot.Charges >= 0 ? cd + " x" + slot.Charges : cd; // spec L349: seconds and charges
                    case SlotState.NoMana: return "no mana";
                    case SlotState.Blocked: return "blocked";
                    case SlotState.Casting: return "casting";
                    case SlotState.Empty: return string.Empty;
                    default: return slot.Charges >= 0 ? "x" + slot.Charges : ContentFormat.Number(slot.ManaCost) + " mana";
                }
            }
        }

        private sealed class ActionCell
        {
            private readonly VisualElement _root;
            private readonly Label _key;
            private readonly Label _name;
            private readonly Label _detail;

            public ActionCell(VisualElement parent)
            {
                _root = Ui.Element("cf-action");
                _key = Ui.Label(string.Empty, "cf-action-key");
                _name = Ui.Label(string.Empty, "cf-action-name");
                _detail = Ui.Label(string.Empty, "cf-action-detail");
                _root.Add(_key);
                VisualElement text = Ui.Element("cf-action-text");
                text.Add(_name);
                text.Add(_detail);
                _root.Add(text);
                parent.Add(_root);
            }

            public void Set(HudActionView action)
            {
                _key.text = action?.Key ?? string.Empty;
                _name.text = action?.Name ?? string.Empty;
                _detail.text = action?.Detail ?? string.Empty;
                _root.EnableInClassList("cf-action--unavailable", action == null || action.State != SlotState.Ready);
            }
        }
    }
}
