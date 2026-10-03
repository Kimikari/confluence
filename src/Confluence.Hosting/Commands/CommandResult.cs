using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Shop;

namespace Confluence.Hosting.Commands
{
    /// <summary>Host-level outcome of a command. Anything but <see cref="Success"/> means nothing changed.</summary>
    public enum CommandCode
    {
        Success = 0,

        /// <summary>Request ids must be positive (D-073).</summary>
        InvalidRequestId,

        /// <summary>The id was already used by this player for a different command; nothing happened (D-073).</summary>
        RequestIdConflict,

        /// <summary>The rate-limit hook refused it; not recorded, so the same id may be retried (L429, D-116).</summary>
        RateLimited,

        UnknownPlayer,
        MatchNotRunning,
        PlayerEliminated,

        /// <summary>A shop command while this player's shop is not open.</summary>
        ShopNotOpen,

        /// <summary>A draft command while this player's draft is not open.</summary>
        DraftNotOpen,

        /// <summary>A surrender while the player is not in a running duel.</summary>
        NotInDuel,

        InvalidArguments,

        /// <summary>The server-side economy object rejected it; see <see cref="CommandResult.Transaction"/> (L429).</summary>
        TransactionRejected,
    }

    /// <summary>Outcome of one command with its explicit codes and the economy result when there is one.</summary>
    public sealed class CommandResult
    {
        internal CommandResult(CommandCode code, TransactionCode transaction = TransactionCode.Success, ShopResult shop = null,
            DraftResult draft = null, bool isDuplicate = false)
        {
            Code = code;
            Transaction = transaction;
            Shop = shop;
            Draft = draft;
            IsDuplicate = isDuplicate;
        }

        public CommandCode Code { get; }

        /// <summary>The economy's code (Success when the command never reached it).</summary>
        public TransactionCode Transaction { get; }

        public ShopResult Shop { get; }
        public DraftResult Draft { get; }

        /// <summary>The request id was already processed; this is the stored result and nothing was reapplied (L429).</summary>
        public bool IsDuplicate { get; }

        public bool Succeeded => Code == CommandCode.Success;

        internal CommandResult AsDuplicate() => new CommandResult(Code, Transaction, Shop, Draft, true);

        internal static CommandResult Of(CommandCode code) => new CommandResult(code);

        internal static CommandResult FromShop(ShopResult result)
        {
            // Save Crowns is a legal outcome that leaves the card unchanged and keeps its reroll (L208).
            bool ok = result.Code == TransactionCode.Success || result.Code == TransactionCode.SaveCrowns;
            return new CommandResult(ok ? CommandCode.Success : CommandCode.TransactionRejected, result.Code, result);
        }

        internal static CommandResult FromDraft(DraftResult result) =>
            new CommandResult(result.Succeeded ? CommandCode.Success : CommandCode.TransactionRejected, result.Code, null, result);

        public override string ToString() =>
            Code + (Code == CommandCode.TransactionRejected || Transaction != TransactionCode.Success ? "/" + Transaction : "") + (IsDuplicate ? " (duplicate)" : "");
    }
}
