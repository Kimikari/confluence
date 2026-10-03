using System;
using System.Collections.Generic;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Economics;

namespace Confluence.Hosting.Commands
{
    /// <summary>What the command gateway needs from its host.</summary>
    public interface ICommandContext
    {
        int PlayerCount { get; }

        /// <summary>The match has started and has not finished.</summary>
        bool IsRunning { get; }

        int CurrentTick { get; }

        bool IsEliminated(int player);

        HostedEconomy Economy { get; }

        /// <summary>Surrenders the player's running duel; false when the player is not in one.</summary>
        bool TrySurrender(int player);
    }

    /// <summary>
    /// The player command API (spec L429): every shop, draft, and surrender request is validated against the host state
    /// and then executed by the server-side economy objects, which recheck money, offers, rerolls, slots, Unique groups,
    /// and eligibility atomically (L208, L229). Requests are idempotent by request id: each player's processed
    /// requests are stored for the whole match, failures included, and a repeat with the same command returns the stored
    /// result without reapplying it, while a different command under a used id is refused (D-073). The rate-limit hook
    /// runs before execution; refused requests are not recorded (D-116).
    /// </summary>
    public sealed class CommandGateway
    {
        private readonly ICommandContext _context;
        private readonly ICommandRateLimiter _limiter;
        private readonly Dictionary<long, Entry>[] _ledgers;

        public CommandGateway(ICommandContext context, ICommandRateLimiter limiter = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _limiter = limiter ?? UnlimitedCommands.Instance;
            _ledgers = new Dictionary<long, Entry>[context.PlayerCount];
            for (int i = 0; i < _ledgers.Length; i++) _ledgers[i] = new Dictionary<long, Entry>();
        }

        /// <summary>Raised after every executed (recorded) command, duplicates excluded.</summary>
        public event Action<PlayerCommand, CommandResult> Executed;

        /// <summary>Requests recorded for a player.</summary>
        public int RecordedCount(int player) => _ledgers[player].Count;

        public CommandResult Submit(PlayerCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Player < 0 || command.Player >= _ledgers.Length) return CommandResult.Of(CommandCode.UnknownPlayer);
            if (command.RequestId <= 0) return CommandResult.Of(CommandCode.InvalidRequestId);

            Dictionary<long, Entry> ledger = _ledgers[command.Player];
            string text = command.CanonicalText();
            if (ledger.TryGetValue(command.RequestId, out Entry previous))
            {
                return string.Equals(previous.Text, text, StringComparison.Ordinal)
                    ? previous.Result.AsDuplicate()
                    : CommandResult.Of(CommandCode.RequestIdConflict);
            }

            if (!_limiter.TryAcquire(command.Player, command.Kind, _context.CurrentTick)) return CommandResult.Of(CommandCode.RateLimited);

            CommandResult result = Execute(command);
            ledger.Add(command.RequestId, new Entry(text, result));
            Executed?.Invoke(command, result);
            return result;
        }

        private CommandResult Execute(PlayerCommand c)
        {
            if (!_context.IsRunning) return CommandResult.Of(CommandCode.MatchNotRunning);
            if (_context.IsEliminated(c.Player)) return CommandResult.Of(CommandCode.PlayerEliminated);
            if (c.IsShopCommand) return ExecuteShop(c);
            if (c.IsDraftCommand) return ExecuteDraft(c);
            return _context.TrySurrender(c.Player) ? CommandResult.Of(CommandCode.Success) : CommandResult.Of(CommandCode.NotInDuel);
        }

        private CommandResult ExecuteShop(PlayerCommand c)
        {
            HostedEconomy economy = _context.Economy;
            EquipmentShop shop = economy.OpenShopOf(c.Player);
            if (shop == null) return CommandResult.Of(CommandCode.ShopNotOpen);
            switch (c.Kind)
            {
                case CommandKind.ShopBuy:
                    Placement? destination = c.ToReserve ? Placement.Reserve : c.Slot.HasValue ? Placement.In(c.Slot.Value) : (Placement?)null;
                    return CommandResult.FromShop(shop.Buy(c.RequestId, c.Position, destination));
                case CommandKind.ShopSell:
                    return CommandResult.FromShop(shop.Sell(c.RequestId, c.InstanceId));
                case CommandKind.ShopReroll:
                    return CommandResult.FromShop(shop.Reroll(c.RequestId, c.Position));
                case CommandKind.ShopEquipFromReserve:
                    // An unknown instance falls through to the shop, which answers UnknownItem.
                    if (!c.Slot.HasValue || PlacementOf(economy.Player(c.Player), c.InstanceId) == ItemLocation.Slot)
                    {
                        return CommandResult.Of(CommandCode.InvalidArguments);
                    }

                    return CommandResult.FromShop(shop.Move(c.RequestId, c.InstanceId, Placement.In(c.Slot.Value)));
                case CommandKind.ShopMoveToReserve:
                    if (PlacementOf(economy.Player(c.Player), c.InstanceId) == ItemLocation.Reserve) return CommandResult.Of(CommandCode.InvalidArguments);
                    return CommandResult.FromShop(shop.Move(c.RequestId, c.InstanceId, Placement.Reserve));
                case CommandKind.ShopSetActive:
                    return CommandResult.FromShop(shop.SetEnabledActive(c.RequestId, c.ClearActive ? (int?)null : c.InstanceId));
                case CommandKind.ShopSelectSpells:
                    if (c.SpellIds == null) return CommandResult.Of(CommandCode.InvalidArguments);
                    return CommandResult.FromShop(shop.SetEquippedSpells(c.RequestId, c.SpellIds));
                case CommandKind.ShopReady:
                    return economy.MarkShopReady(c.Player) ? CommandResult.Of(CommandCode.Success) : CommandResult.Of(CommandCode.ShopNotOpen);
                default:
                    throw new InvalidOperationException("Unhandled shop command " + c.Kind + ".");
            }
        }

        private CommandResult ExecuteDraft(PlayerCommand c)
        {
            AugmentDraft draft = _context.Economy.OpenDraftOf(c.Player);
            if (draft == null) return CommandResult.Of(CommandCode.DraftNotOpen);
            switch (c.Kind)
            {
                case CommandKind.DraftReroll:
                    return CommandResult.FromDraft(draft.Reroll(c.RequestId, c.Position));
                case CommandKind.DraftSelect:
                    return CommandResult.FromDraft(draft.Select(c.RequestId, c.Position, c.AugmentSlot, c.ConfirmDormancy));
                case CommandKind.DraftPass:
                    return CommandResult.FromDraft(draft.Pass(c.RequestId));
                default:
                    throw new InvalidOperationException("Unhandled draft command " + c.Kind + ".");
            }
        }

        private enum ItemLocation
        {
            Missing,
            Slot,
            Reserve,
        }

        private static ItemLocation PlacementOf(PlayerEconomy player, int instanceId)
        {
            lock (player.SyncRoot)
            {
                OwnedEquipment item = player.Loadout.FindItem(instanceId, out Placement placement);
                if (item == null) return ItemLocation.Missing;
                return placement.IsReserve ? ItemLocation.Reserve : ItemLocation.Slot;
            }
        }

        private sealed class Entry
        {
            public Entry(string text, CommandResult result)
            {
                Text = text;
                Result = result;
            }

            public string Text { get; }
            public CommandResult Result { get; }
        }
    }
}
