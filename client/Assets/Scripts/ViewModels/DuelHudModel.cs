using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Status;

namespace Confluence.Client.ViewModels
{
    /// <summary>Cooldown and casting state of a spell slot as the spell runtime reports it.</summary>
    public struct SpellSlotReading
    {
        public float CooldownSec;

        /// <summary>Charges where relevant, or -1.</summary>
        public int Charges;

        public bool Casting;
    }

    /// <summary>
    /// Reads per-slot cooldowns from whatever spell runtime the duel uses. The Sim has no cooldown query yet (its
    /// <see cref="ISpellRuntime"/> reports only <see cref="CastingStatus"/>); the casting integration provides one.
    /// </summary>
    public interface ISpellSlotReader
    {
        bool TryRead(DuelWorld world, int player, int slot, out SpellSlotReading reading);
    }

    /// <summary>
    /// <see cref="IHudViewModel"/> for one player of a Sim <see cref="DuelWorld"/>: the local authoritative world in
    /// the Laboratory, or the client's predicted/interpolated world once networking lands. Every value is read from
    /// the world (resources, shields, statuses, guard, melee, boundary tracker, timer judgement), never invented.
    /// Call <see cref="Refresh"/> after stepping the world.
    /// </summary>
    public sealed class DuelHudModel : IHudViewModel
    {
        private readonly Catalogue _catalogue;
        private readonly List<HudSlotView> _slots = new List<HudSlotView>();
        private readonly List<StatusIconView> _statuses = new List<StatusIconView>();

        public DuelHudModel(Catalogue catalogue, int player, ISpellSlotReader slotReader = null)
        {
            _catalogue = catalogue;
            PlayerIndex = player;
            SlotReader = slotReader;
            for (int i = 0; i < GlobalRules.Loadout.EquippedSpellSlots; i++)
            {
                _slots.Add(new HudSlotView { Number = i + 1, State = SlotState.Empty });
            }

            Signature = new HudActionView { Key = "Q" };
            ActiveItem = new HudActionView { Key = "E" };
            Consumable = new HudActionView { Key = "X" };
        }

        public event Action Changed;

        public int PlayerIndex { get; }
        public ISpellSlotReader SlotReader { get; set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float Shield { get; private set; }
        public float Mana { get; private set; }
        public float MaxMana { get; private set; }
        public float Stamina { get; private set; }
        public float MaxStamina { get; private set; }
        public int SelectedSlot { get; private set; }
        public IReadOnlyList<HudSlotView> Slots => _slots;
        public string SelectedSpellLine { get; private set; }
        public HudActionView Signature { get; }
        public HudActionView ActiveItem { get; }
        public HudActionView Consumable { get; }
        public IReadOnlyList<StatusIconView> Statuses => _statuses;
        public string TimerText { get; private set; }
        public string PhaseText { get; private set; }
        public BoundaryCue Boundary { get; private set; }
        public string BoundaryText { get; private set; }
        public bool JudgementVisible { get; private set; }
        public int Judgement { get; private set; }
        public string GuardText { get; private set; }
        public string MeleeText { get; private set; }
        public string Notice { get; private set; }

        public void Refresh(DuelWorld world)
        {
            if (world == null)
            {
                return;
            }

            DuelPlayer p = world.Player(PlayerIndex);
            int now = world.Tick;
            Health = p.Resources.Health;
            MaxHealth = p.Resources.MaxHealth;
            Shield = p.Shields.Total;
            Mana = p.Resources.Mana;
            MaxMana = p.Resources.MaxMana;
            Stamina = p.Resources.Stamina;
            MaxStamina = p.Resources.MaxStamina;
            SelectedSlot = p.SelectedSlot;

            CastPermissions permissions = world.CastPermissionsFor(p);
            RefreshSlots(world, p, permissions);
            RefreshActions(p, permissions);
            RefreshStatuses(p, now);
            RefreshTimer(world);
            RefreshBoundary(world, p);
            JudgementVisible = world.JudgementIndicatorVisible; // spec L53: final 15 s
            Judgement = world.JudgementIndicator(PlayerIndex);
            GuardText = GuardLine(p, now);
            MeleeText = p.Melee.Busy ? "Melee: " + p.Melee.Phase.ToString().ToLowerInvariant() + " (" + p.Melee.Profile.Name + ")" : null;
            Notice = world.Spells is NullSpellRuntime && SlotReader == null
                ? "No spell runtime in this build: casting is inert; movement, guard, and melee are live."
                : null;
            Changed?.Invoke();
        }

        private void RefreshSlots(DuelWorld world, DuelPlayer p, CastPermissions permissions)
        {
            IReadOnlyList<string> spells = p.Setup.EquippedSpellIds;
            for (int i = 0; i < _slots.Count; i++)
            {
                HudSlotView slot = _slots[i];
                slot.Selected = i == p.SelectedSlot;
                slot.SpellId = i < spells.Count ? spells[i] : null;
                SpellDef def = slot.SpellId != null && _catalogue != null && _catalogue.SpellsById.TryGetValue(slot.SpellId, out SpellDef d) ? d : null;
                slot.Name = def != null ? def.Name : slot.SpellId;
                slot.ManaCost = def != null ? (float)def.Mana : 0f;
                slot.CooldownSec = 0f;
                slot.Charges = -1;
                if (slot.SpellId == null)
                {
                    slot.State = SlotState.Empty;
                    continue;
                }

                SpellSlotReading reading = default;
                bool known = SlotReader != null && SlotReader.TryRead(world, p.Index, i, out reading);
                if (known)
                {
                    slot.CooldownSec = reading.CooldownSec;
                    slot.Charges = reading.Charges;
                }

                if (known && reading.Casting)
                {
                    slot.State = SlotState.Casting;
                }
                else if (!permissions.CanCast)
                {
                    slot.State = SlotState.Blocked;
                }
                else if (known && reading.CooldownSec > 0f)
                {
                    slot.State = SlotState.Cooldown;
                }
                else if (!p.Resources.CanAffordMana(p.Stats.EffectiveCost(slot.ManaCost)))
                {
                    slot.State = SlotState.NoMana; // spec L78: the full initial commitment must be affordable
                }
                else
                {
                    slot.State = SlotState.Ready;
                }
            }

            HudSlotView selected = p.SelectedSlot >= 0 && p.SelectedSlot < _slots.Count ? _slots[p.SelectedSlot] : null;
            SelectedSpellLine = selected == null || selected.SpellId == null ? "No spell" : selected.Name + ": " + SlotLine(selected, permissions);
        }

        private static string SlotLine(HudSlotView slot, CastPermissions permissions)
        {
            switch (slot.State)
            {
                case SlotState.Blocked: return "blocked (" + BlockText(permissions.Reason) + ")";
                case SlotState.Cooldown: return "cooldown " + slot.CooldownSec.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                case SlotState.NoMana: return "needs " + ContentFormat.Number(slot.ManaCost) + " mana";
                case SlotState.Casting: return "casting";
                default: return "ready, " + ContentFormat.Number(slot.ManaCost) + " mana";
            }
        }

        private void RefreshActions(DuelPlayer p, CastPermissions permissions)
        {
            WizardDef wizard = _catalogue != null && p.Setup.WizardId != null && _catalogue.WizardsById.TryGetValue(p.Setup.WizardId, out WizardDef w) ? w : null;
            Signature.Name = wizard != null ? LeadName(wizard.Signature) : "Signature";
            Signature.State = permissions.CanUseSignature ? SlotState.Ready : SlotState.Blocked;
            Signature.Detail = permissions.CanUseSignature ? "ready" : BlockText(permissions.Reason);

            string activeId = p.Setup.EnabledActiveItemId;
            ActiveItem.Name = activeId != null ? ItemName(activeId) : "No active";
            ActiveItem.State = activeId == null ? SlotState.Empty : permissions.CanUseItems ? SlotState.Ready : SlotState.Blocked;
            ActiveItem.Detail = activeId == null ? "none enabled" : permissions.CanUseItems ? "ready" : BlockText(permissions.Reason);

            EquippedItem consumable = p.Setup.ItemIn(EquipmentSlot.Consumable);
            Consumable.Name = consumable != null ? ItemName(consumable.Id) : "No consumable";
            Consumable.State = consumable == null ? SlotState.Empty : permissions.CanUseItems ? SlotState.Ready : SlotState.Blocked;
            Consumable.Detail = consumable == null ? "none" : permissions.CanUseItems ? "1 charge" : BlockText(permissions.Reason);
        }

        private void RefreshStatuses(DuelPlayer p, int now)
        {
            _statuses.Clear();
            if (p.Statuses.IsControlImmune(now))
            {
                _statuses.Add(new StatusIconView
                {
                    Kind = StatusIconKind.ControlImmunity,
                    Glyph = "IMM",
                    Label = "Control immune",
                    Seconds = (p.Statuses.ControlImmuneUntil - now) / (float)Ticks.PerSecond,
                });
            }

            IReadOnlyList<StatusInstance> active = p.Statuses.Active;
            for (int i = 0; i < active.Count; i++)
            {
                StatusInstance s = active[i];
                _statuses.Add(new StatusIconView
                {
                    Kind = IconKind(s),
                    Glyph = Glyph(s.Kind),
                    Label = s.Kind.ToString(),
                    Seconds = s.ExpireTick == int.MaxValue ? -1f : s.RemainingTicks(now) / (float)Ticks.PerSecond,
                    Value = s.Intensity > 0f ? ContentFormat.Number(s.Intensity) : null,
                });
            }

            // Spec L347: the remaining healing budget is always visible (120 per duel, spec L114).
            _statuses.Add(new StatusIconView
            {
                Kind = StatusIconKind.HealingBudget,
                Glyph = "HEAL",
                Label = "Healing budget",
                Value = ContentFormat.Number(Math.Round(p.Healing.RemainingDuel)) + "/" + ContentFormat.Number(GlobalRules.Healing.MaxPerDuel),
            });
        }

        private void RefreshTimer(DuelWorld world)
        {
            switch (world.Phase)
            {
                case DuelPhase.Alcove:
                    float alcove = Math.Max(0, world.AlcoveTicks - world.Tick) / (float)Ticks.PerSecond;
                    TimerText = alcove.ToString("0.0", CultureInfo.InvariantCulture);
                    PhaseText = "Alcove ward: no damage or casting until it opens (spec L46)";
                    break;
                case DuelPhase.Combat:
                    float left = Math.Max(0f, world.CombatTicks / (float)Ticks.PerSecond - world.CombatSeconds);
                    TimerText = Clock(left);
                    PhaseText = "Combat";
                    break;
                default:
                    TimerText = Clock(0f);
                    PhaseText = world.Result != null ? "Duel over" : "Ended";
                    break;
            }
        }

        private void RefreshBoundary(DuelWorld world, DuelPlayer p)
        {
            float t = world.CombatSeconds;
            if (world.Phase == DuelPhase.Combat && p.Boundary.Outside)
            {
                Boundary = BoundaryCue.Outside;
                float rate = GlobalRules.Boundary.DamagePerSecond(p.Boundary.EscalationSeconds);
                BoundaryText = "OUTSIDE THE BOUNDARY: " + ContentFormat.Number(rate) + " damage/s, return inside";
            }
            else if (world.Phase == DuelPhase.Combat && t >= GlobalRules.Boundary.ContractionStartSec)
            {
                Boundary = BoundaryCue.Contracting;
                BoundaryText = t < GlobalRules.Boundary.ContractionEndSec
                    ? "Boundary contracting: final 8 m floor at 85 s"
                    : "Final floor";
            }
            else
            {
                Boundary = BoundaryCue.Steady;
                BoundaryText = world.Phase == DuelPhase.Combat
                    ? "Boundary contracts in " + Clock(GlobalRules.Boundary.ContractionStartSec - t)
                    : null;
            }
        }

        private static string GuardLine(DuelPlayer p, int now)
        {
            if (p.Guard.Active)
            {
                return p.Guard.IsPerfect(now) ? "Guard: perfect window" : "Guard: holding";
            }

            if (p.Guard.InReleaseWindow(now))
            {
                return "Guard: release window (punishable)";
            }

            return p.Guard.OnCooldown(now)
                ? "Guard: cooldown " + ((p.Guard.CooldownUntil - now) / (float)Ticks.PerSecond).ToString("0.0", CultureInfo.InvariantCulture) + " s"
                : null;
        }

        private static StatusIconKind IconKind(StatusInstance s)
        {
            if (s.Kind == StatusKind.Wet)
            {
                return StatusIconKind.Wet;
            }

            if (s.Rule.DamageOverTime)
            {
                return StatusIconKind.DamageOverTime;
            }

            if (s.Rule.HardControl)
            {
                return StatusIconKind.HardControl;
            }

            return s.Kind == StatusKind.Slow || s.Kind == StatusKind.Chill ? StatusIconKind.Slow : StatusIconKind.Other;
        }

        private static string Glyph(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Burn: return "BRN";
                case StatusKind.Venom: return "VEN";
                case StatusKind.Chill: return "CHL";
                case StatusKind.Brittle: return "BRT";
                case StatusKind.Charge: return "CHG";
                case StatusKind.Marked: return "MRK";
                case StatusKind.Wet: return "WET";
                case StatusKind.Slow: return "SLW";
                case StatusKind.Root: return "RT";
                case StatusKind.Stun: return "STN";
                case StatusKind.Silence: return "SIL";
                case StatusKind.Displaced: return "KNK";
                case StatusKind.Reveal: return "REV";
                case StatusKind.Invisibility: return "INV";
                default: return kind.ToString();
            }
        }

        public static string BlockText(CastBlockReason reason)
        {
            switch (reason)
            {
                case CastBlockReason.AlcoveWard: return "alcove ward";
                case CastBlockReason.Dead: return "defeated";
                case CastBlockReason.Stunned: return "stunned";
                case CastBlockReason.Silenced: return "silenced";
                case CastBlockReason.GuardRelease: return "guard release";
                case CastBlockReason.Melee: return "melee";
                case CastBlockReason.Swapping: return "swapping";
                case CastBlockReason.Recalling: return "recall";
                case CastBlockReason.DuelEnded: return "duel over";
                default: return "ready";
            }
        }

        /// <summary>The name before the dash of a record's "Name — rules" text (signature, passive).</summary>
        public static string LeadName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int dash = text.IndexOf(" — ", StringComparison.Ordinal);
            if (dash < 0)
            {
                dash = text.IndexOf(" - ", StringComparison.Ordinal);
            }

            return dash > 0 ? text.Substring(0, dash) : text;
        }

        private string ItemName(string id)
        {
            return _catalogue != null && _catalogue.EquipmentById.TryGetValue(id, out EquipmentDef def) ? def.Name : id;
        }

        private static string Clock(float seconds)
        {
            int whole = (int)Math.Ceiling(Math.Max(0f, seconds));
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
