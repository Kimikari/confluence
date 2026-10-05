using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    public sealed class ShopCardWire
    {
        public int Position;
        public ShopCardState State;
        public string DefinitionId;
        public int Price;
        public int Tier;
        public bool RerollAvailable;
        public List<EquipmentSlot> Slots = new List<EquipmentSlot>();
    }

    public sealed class OwnedItemWire
    {
        public int InstanceId;
        public string DefinitionId;

        /// <summary>The equipment slot, or -1 for the reserve.</summary>
        public int Slot = -1;

        public int Charges;
        public int SellValue;
        public bool IsActiveItem;
        public bool ActivationEnabled;
    }

    public sealed class OwnedAugmentWire
    {
        public int Slot;
        public string AugmentId;
        public int Rank;
        public bool Dormant;
    }

    /// <summary>
    /// A player's private shop view (spec L45, L204-L213, L353-L355): balance, ready flag, the three cards with their
    /// reroll indicators, the owned items with instance ids (slots and reserve), the kit and equipped spells, and the
    /// owned augments with ranks and dormancy. Sent only to its owner (hidden information, spec L44 "live purchases
    /// remain hidden").
    /// </summary>
    public sealed class ShopStateMessage : INetMessage
    {
        public int Round { get; set; }
        public bool Open { get; set; }
        public int Crowns { get; set; }
        public bool Ready { get; set; }
        public int HighestTier { get; set; }
        public List<ShopCardWire> Cards { get; } = new List<ShopCardWire>();
        public List<OwnedItemWire> Items { get; } = new List<OwnedItemWire>();
        public List<string> KitSpellIds { get; set; } = new List<string>();
        public List<string> EquippedSpellIds { get; set; } = new List<string>();
        public List<OwnedAugmentWire> Augments { get; } = new List<OwnedAugmentWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)Round);
            w.WriteBool(Open);
            w.WriteVarInt(Crowns);
            w.WriteBool(Ready);
            w.WriteByte((byte)HighestTier);
            w.WriteVarUInt((uint)Cards.Count);
            foreach (ShopCardWire c in Cards)
            {
                w.WriteByte((byte)c.Position);
                w.WriteByte((byte)c.State);
                w.WriteId(c.DefinitionId);
                w.WriteVarInt(c.Price);
                w.WriteByte((byte)c.Tier);
                w.WriteBool(c.RerollAvailable);
                w.WriteVarUInt((uint)c.Slots.Count);
                foreach (EquipmentSlot s in c.Slots) w.WriteByte((byte)s);
            }

            w.WriteVarUInt((uint)Items.Count);
            foreach (OwnedItemWire i in Items)
            {
                w.WriteVarUInt((uint)i.InstanceId);
                w.WriteId(i.DefinitionId);
                w.WriteVarInt(i.Slot);
                w.WriteVarInt(i.Charges);
                w.WriteVarInt(i.SellValue);
                w.WriteByte((byte)((i.IsActiveItem ? 1 : 0) | (i.ActivationEnabled ? 2 : 0)));
            }

            w.WriteIdList(KitSpellIds);
            w.WriteIdList(EquippedSpellIds);
            w.WriteVarUInt((uint)Augments.Count);
            foreach (OwnedAugmentWire a in Augments)
            {
                w.WriteByte((byte)a.Slot);
                w.WriteId(a.AugmentId);
                w.WriteByte((byte)a.Rank);
                w.WriteBool(a.Dormant);
            }
        }

        public void Deserialize(NetReader r)
        {
            Round = (int)r.ReadVarUInt();
            Open = r.ReadBool();
            Crowns = r.ReadVarIntInRange(0, 1000000);
            Ready = r.ReadBool();
            HighestTier = r.ReadByteInRange(0, 4);
            int cards = r.ReadCount(5, 6);
            for (int k = 0; k < cards; k++)
            {
                var c = new ShopCardWire
                {
                    Position = r.ReadByteInRange(0, 4),
                    State = (ShopCardState)r.ReadByteInRange(0, (byte)ShopCardState.SaveCrowns),
                    DefinitionId = r.ReadId(),
                    Price = r.ReadVarIntInRange(0, 1000000),
                    Tier = r.ReadByteInRange(0, 4),
                    RerollAvailable = r.ReadBool(),
                };
                int slots = r.ReadCount(8, 1);
                for (int s = 0; s < slots; s++) c.Slots.Add((EquipmentSlot)r.ReadByteInRange(0, (byte)EquipmentSlot.Consumable));
                Cards.Add(c);
            }

            int items = r.ReadCount(32, 5);
            for (int k = 0; k < items; k++)
            {
                var i = new OwnedItemWire
                {
                    InstanceId = (int)r.ReadVarUInt(),
                    DefinitionId = r.ReadId(),
                    Slot = r.ReadVarIntInRange(-1, (int)EquipmentSlot.Consumable),
                    Charges = r.ReadVarIntInRange(0, 1000),
                    SellValue = r.ReadVarIntInRange(0, 1000000),
                };
                byte bits = r.ReadByteInRange(0, 3);
                i.IsActiveItem = (bits & 1) != 0;
                i.ActivationEnabled = (bits & 2) != 0;
                Items.Add(i);
            }

            KitSpellIds = r.ReadIdList();
            EquippedSpellIds = r.ReadIdList();
            int augments = r.ReadCount(GlobalRules.Draft.AugmentSlots, 4);
            for (int k = 0; k < augments; k++)
            {
                Augments.Add(new OwnedAugmentWire
                {
                    Slot = r.ReadByteInRange(0, (byte)(GlobalRules.Draft.AugmentSlots - 1)),
                    AugmentId = r.ReadId(),
                    Rank = r.ReadByteInRange(1, 2),
                    Dormant = r.ReadBool(),
                });
            }
        }
    }

    public sealed class DraftCardWire
    {
        public int Position;
        public string AugmentId;
        public int Rank;
        public bool IsUpgrade;
        public Rarity Rarity;
        public string TargetSpellId;
        public bool RerollAvailable;
        public string EligibilityText;
    }

    /// <summary>A player's private draft view (spec L216-L233, L353): the five cards with reroll indicators and the outcome.</summary>
    public sealed class DraftStateMessage : INetMessage
    {
        public int Round { get; set; }
        public bool Open { get; set; }
        public Rarity Rarity { get; set; }
        public DraftOutcome Outcome { get; set; }
        public List<DraftCardWire> Cards { get; } = new List<DraftCardWire>();
        public string SelectedAugmentId { get; set; }
        public int SelectedRank { get; set; }

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)Round);
            w.WriteBool(Open);
            w.WriteByte((byte)Rarity);
            w.WriteByte((byte)Outcome);
            w.WriteVarUInt((uint)Cards.Count);
            foreach (DraftCardWire c in Cards)
            {
                w.WriteByte((byte)c.Position);
                w.WriteId(c.AugmentId);
                w.WriteByte((byte)c.Rank);
                w.WriteBool(c.IsUpgrade);
                w.WriteByte((byte)c.Rarity);
                w.WriteId(c.TargetSpellId);
                w.WriteBool(c.RerollAvailable);
                w.WriteText(c.EligibilityText);
            }

            w.WriteId(SelectedAugmentId);
            w.WriteByte((byte)SelectedRank);
        }

        public void Deserialize(NetReader r)
        {
            Round = (int)r.ReadVarUInt();
            Open = r.ReadBool();
            Rarity = (Rarity)r.ReadByteInRange(0, (byte)Rarity.Prismatic);
            Outcome = (DraftOutcome)r.ReadByteInRange(0, (byte)DraftOutcome.TimedOut);
            int n = r.ReadCount(GlobalRules.Draft.Candidates, 8);
            for (int k = 0; k < n; k++)
            {
                Cards.Add(new DraftCardWire
                {
                    Position = r.ReadByteInRange(0, (byte)(GlobalRules.Draft.Candidates - 1)),
                    AugmentId = r.ReadId(),
                    Rank = r.ReadByteInRange(1, 2),
                    IsUpgrade = r.ReadBool(),
                    Rarity = (Rarity)r.ReadByteInRange(0, (byte)Rarity.Prismatic),
                    TargetSpellId = r.ReadId(),
                    RerollAvailable = r.ReadBool(),
                    EligibilityText = r.ReadText(),
                });
            }

            SelectedAugmentId = r.ReadId();
            SelectedRank = r.ReadByteInRange(0, 2);
        }
    }

    /// <summary>
    /// One shop, draft, or duel command (spec L429) mapped one to one onto Hosting's <see cref="PlayerCommand"/>; the
    /// request envelope's id is the Hosting request id, so a resent request is answered from the ledgers without
    /// running again (duplicate purchases are prevented).
    /// </summary>
    public sealed class CommandRequest : INetMessage
    {
        public CommandKind Kind { get; set; }
        public int Position { get; set; } = -1;
        public int InstanceId { get; set; }
        public EquipmentSlot? Slot { get; set; }
        public bool ToReserve { get; set; }
        public bool ClearActive { get; set; }
        public List<string> SpellIds { get; set; }
        public int? AugmentSlot { get; set; }
        public bool ConfirmDormancy { get; set; }

        public void Serialize(NetWriter w)
        {
            w.WriteByte((byte)Kind);
            w.WriteVarInt(Position);
            w.WriteVarUInt((uint)InstanceId);
            w.WriteVarInt(Slot.HasValue ? (int)Slot.Value : -1);
            w.WriteByte((byte)((ToReserve ? 1 : 0) | (ClearActive ? 2 : 0) | (ConfirmDormancy ? 4 : 0) | (SpellIds != null ? 8 : 0)));
            if (SpellIds != null) w.WriteIdList(SpellIds);
            w.WriteVarInt(AugmentSlot ?? -1);
        }

        public void Deserialize(NetReader r)
        {
            Kind = (CommandKind)r.ReadByteInRange(0, (byte)CommandKind.Surrender);
            Position = r.ReadVarIntInRange(-1, 16);
            InstanceId = (int)r.ReadVarUInt();
            int slot = r.ReadVarIntInRange(-1, (int)EquipmentSlot.Consumable);
            Slot = slot < 0 ? (EquipmentSlot?)null : (EquipmentSlot)slot;
            byte flags = r.ReadByteInRange(0, 15);
            ToReserve = (flags & 1) != 0;
            ClearActive = (flags & 2) != 0;
            ConfirmDormancy = (flags & 4) != 0;
            SpellIds = (flags & 8) != 0 ? r.ReadIdList() : null;
            int augment = r.ReadVarIntInRange(-1, GlobalRules.Draft.AugmentSlots - 1);
            AugmentSlot = augment < 0 ? (int?)null : augment;
            if (ToReserve && Slot.HasValue) throw new NetReadException(NetReadError.InvalidValue, "a purchase goes to a slot or the reserve");
        }

        /// <summary>The Hosting command for a player and request id.</summary>
        public PlayerCommand ToCommand(int player, long requestId)
        {
            switch (Kind)
            {
                case CommandKind.ShopBuy: return PlayerCommand.Buy(player, requestId, Position, Slot, ToReserve);
                case CommandKind.ShopSell: return PlayerCommand.Sell(player, requestId, InstanceId);
                case CommandKind.ShopReroll: return PlayerCommand.Reroll(player, requestId, Position);
                case CommandKind.ShopEquipFromReserve: return PlayerCommand.EquipFromReserve(player, requestId, InstanceId, Slot ?? EquipmentSlot.MainFocus);
                case CommandKind.ShopMoveToReserve: return PlayerCommand.MoveToReserve(player, requestId, InstanceId);
                case CommandKind.ShopSetActive: return PlayerCommand.SetActive(player, requestId, ClearActive ? (int?)null : InstanceId);
                case CommandKind.ShopSelectSpells: return PlayerCommand.SelectSpells(player, requestId, SpellIds);
                case CommandKind.ShopReady: return PlayerCommand.Ready(player, requestId);
                case CommandKind.DraftReroll: return PlayerCommand.DraftReroll(player, requestId, Position);
                case CommandKind.DraftSelect: return PlayerCommand.DraftSelect(player, requestId, Position, AugmentSlot, ConfirmDormancy);
                case CommandKind.DraftPass: return PlayerCommand.DraftPass(player, requestId);
                default: return PlayerCommand.Surrender(player, requestId);
            }
        }

        public static CommandRequest Buy(int position, EquipmentSlot? slot = null, bool toReserve = false) =>
            new CommandRequest { Kind = CommandKind.ShopBuy, Position = position, Slot = slot, ToReserve = toReserve };

        public static CommandRequest Sell(int instanceId) => new CommandRequest { Kind = CommandKind.ShopSell, InstanceId = instanceId };

        public static CommandRequest Reroll(int position) => new CommandRequest { Kind = CommandKind.ShopReroll, Position = position };

        public static CommandRequest SelectSpells(IEnumerable<string> spells) =>
            new CommandRequest { Kind = CommandKind.ShopSelectSpells, SpellIds = new List<string>(spells) };

        public static CommandRequest Ready() => new CommandRequest { Kind = CommandKind.ShopReady };

        public static CommandRequest DraftReroll(int position) => new CommandRequest { Kind = CommandKind.DraftReroll, Position = position };

        public static CommandRequest DraftSelect(int position, int? augmentSlot = null, bool confirmDormancy = false) =>
            new CommandRequest { Kind = CommandKind.DraftSelect, Position = position, AugmentSlot = augmentSlot, ConfirmDormancy = confirmDormancy };

        public static CommandRequest DraftPass() => new CommandRequest { Kind = CommandKind.DraftPass };

        public static CommandRequest Surrender() => new CommandRequest { Kind = CommandKind.Surrender };
    }

    /// <summary>
    /// The reply to one command: Hosting's code and the economy's transaction code (spec L429 explicit outcomes), and
    /// whether the request id had already been processed (the stored result, nothing reapplied).
    /// </summary>
    public sealed class CommandResponse : INetMessage
    {
        public CommandCode Code { get; set; }
        public TransactionCode Transaction { get; set; }
        public bool Duplicate { get; set; }

        public bool Succeeded => Code == CommandCode.Success;

        public void Serialize(NetWriter w)
        {
            w.WriteByte((byte)Code);
            w.WriteVarUInt((uint)Transaction);
            w.WriteBool(Duplicate);
        }

        public void Deserialize(NetReader r)
        {
            Code = (CommandCode)r.ReadByteInRange(0, (byte)CommandCode.TransactionRejected);
            uint transaction = r.ReadVarUInt();
            if (transaction > 255) throw new NetReadException(NetReadError.InvalidValue, "transaction code out of range");
            Transaction = (TransactionCode)transaction;
            Duplicate = r.ReadBool();
        }

        public override string ToString() => Code + (Transaction != TransactionCode.Success ? "/" + Transaction : "") + (Duplicate ? " (duplicate)" : "");
    }
}
