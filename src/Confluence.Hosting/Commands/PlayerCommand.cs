using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Confluence.Core.Rules;

namespace Confluence.Hosting.Commands
{
    /// <summary>Player actions the host accepts outside the per-tick input stream (spec L45, L53, L204-L233).</summary>
    public enum CommandKind
    {
        ShopBuy,
        ShopSell,
        ShopReroll,
        ShopEquipFromReserve,
        ShopMoveToReserve,
        ShopSetActive,
        ShopSelectSpells,
        ShopReady,
        DraftReroll,
        DraftSelect,
        DraftPass,
        Surrender,
    }

    /// <summary>
    /// One reliable request from a player (spec L429): a client-assigned positive request id that is unique for the
    /// player for the whole match, the command, and its arguments. Built only through the factory methods, so every
    /// kind carries exactly the arguments it needs. Immutable.
    /// </summary>
    public sealed class PlayerCommand
    {
        private PlayerCommand(int player, long requestId, CommandKind kind)
        {
            Player = player;
            RequestId = requestId;
            Kind = kind;
            Position = -1;
            InstanceId = 0;
        }

        public int Player { get; }
        public long RequestId { get; }
        public CommandKind Kind { get; }

        /// <summary>Shop card (0-2) or draft card (0-4) position.</summary>
        public int Position { get; private set; }

        /// <summary>Owned equipment instance for sell, move, and set-active.</summary>
        public int InstanceId { get; private set; }

        /// <summary>Target equipment slot (buy into a slot, equip from reserve); null = automatic for a purchase.</summary>
        public EquipmentSlot? Slot { get; private set; }

        /// <summary>A purchase goes straight to the reserve.</summary>
        public bool ToReserve { get; private set; }

        /// <summary>Set-active: clear the enabled active instead of choosing one (L575).</summary>
        public bool ClearActive { get; private set; }

        /// <summary>Six spells from the wizard's kit in slot order (L292).</summary>
        public IReadOnlyList<string> SpellIds { get; private set; }

        /// <summary>Draft selection: augment slot (0-5) the new definition takes or replaces; null = first free slot (L231).</summary>
        public int? AugmentSlot { get; private set; }

        /// <summary>Draft selection: the player confirmed that the listed augments become dormant (L231, L645).</summary>
        public bool ConfirmDormancy { get; private set; }

        public bool IsShopCommand => Kind <= CommandKind.ShopReady;
        public bool IsDraftCommand => Kind >= CommandKind.DraftReroll && Kind <= CommandKind.DraftPass;

        // ---- Shop ----------------------------------------------------------------------------------------------------

        /// <summary>Buy a card into a slot, automatically (first empty legal slot, else reserve), or into the reserve.</summary>
        public static PlayerCommand Buy(int player, long requestId, int position, EquipmentSlot? slot = null, bool toReserve = false)
        {
            if (slot.HasValue && toReserve) throw new ArgumentException("A purchase goes to a slot or the reserve, not both.");
            return new PlayerCommand(player, requestId, CommandKind.ShopBuy) { Position = position, Slot = slot, ToReserve = toReserve };
        }

        public static PlayerCommand Sell(int player, long requestId, int instanceId) =>
            new PlayerCommand(player, requestId, CommandKind.ShopSell) { InstanceId = instanceId };

        public static PlayerCommand Reroll(int player, long requestId, int position) =>
            new PlayerCommand(player, requestId, CommandKind.ShopReroll) { Position = position };

        public static PlayerCommand EquipFromReserve(int player, long requestId, int instanceId, EquipmentSlot slot) =>
            new PlayerCommand(player, requestId, CommandKind.ShopEquipFromReserve) { InstanceId = instanceId, Slot = slot };

        public static PlayerCommand MoveToReserve(int player, long requestId, int instanceId) =>
            new PlayerCommand(player, requestId, CommandKind.ShopMoveToReserve) { InstanceId = instanceId };

        /// <summary>Choose the enabled active item, or pass null to clear the choice (L575).</summary>
        public static PlayerCommand SetActive(int player, long requestId, int? instanceId) =>
            new PlayerCommand(player, requestId, CommandKind.ShopSetActive) { InstanceId = instanceId ?? 0, ClearActive = !instanceId.HasValue };

        public static PlayerCommand SelectSpells(int player, long requestId, IEnumerable<string> spellIds)
        {
            var list = spellIds == null ? null : new List<string>(spellIds).AsReadOnly();
            return new PlayerCommand(player, requestId, CommandKind.ShopSelectSpells) { SpellIds = list };
        }

        public static PlayerCommand Ready(int player, long requestId) => new PlayerCommand(player, requestId, CommandKind.ShopReady);

        // ---- Draft ---------------------------------------------------------------------------------------------------

        public static PlayerCommand DraftReroll(int player, long requestId, int position) =>
            new PlayerCommand(player, requestId, CommandKind.DraftReroll) { Position = position };

        /// <summary>Select a card, ending the draft (L229); optionally naming the augment slot to fill or replace.</summary>
        public static PlayerCommand DraftSelect(int player, long requestId, int position, int? augmentSlot = null, bool confirmDormancy = false) =>
            new PlayerCommand(player, requestId, CommandKind.DraftSelect) { Position = position, AugmentSlot = augmentSlot, ConfirmDormancy = confirmDormancy };

        public static PlayerCommand DraftPass(int player, long requestId) => new PlayerCommand(player, requestId, CommandKind.DraftPass);

        // ---- Duel ----------------------------------------------------------------------------------------------------

        /// <summary>A surrender is a normal loss (L53).</summary>
        public static PlayerCommand Surrender(int player, long requestId) => new PlayerCommand(player, requestId, CommandKind.Surrender);

        /// <summary>
        /// Canonical text of the operation and its arguments (without the request id): a repeated id must carry the same
        /// text to count as a duplicate (D-073).
        /// </summary>
        public string CanonicalText()
        {
            var sb = new StringBuilder(Kind.ToString());
            switch (Kind)
            {
                case CommandKind.ShopBuy:
                    sb.Append(':').Append(Position).Append(':').Append(ToReserve ? "reserve" : Slot.HasValue ? Slot.Value.ToString() : "auto");
                    break;
                case CommandKind.ShopSell:
                case CommandKind.ShopMoveToReserve:
                    sb.Append(':').Append(InstanceId.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommandKind.ShopEquipFromReserve:
                    sb.Append(':').Append(InstanceId.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Slot);
                    break;
                case CommandKind.ShopSetActive:
                    sb.Append(':').Append(ClearActive ? "none" : InstanceId.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommandKind.ShopSelectSpells:
                    sb.Append(':').Append(SpellIds == null ? "null" : string.Join(",", SpellIds));
                    break;
                case CommandKind.ShopReroll:
                case CommandKind.DraftReroll:
                    sb.Append(':').Append(Position.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommandKind.DraftSelect:
                    sb.Append(':').Append(Position.ToString(CultureInfo.InvariantCulture)).Append(':')
                        .Append(AugmentSlot.HasValue ? AugmentSlot.Value.ToString(CultureInfo.InvariantCulture) : "auto")
                        .Append(':').Append(ConfirmDormancy ? "confirm" : "ask");
                    break;
            }

            return sb.ToString();
        }

        public override string ToString() => "p" + Player + " #" + RequestId + " " + CanonicalText();
    }
}
