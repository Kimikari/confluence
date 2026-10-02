using System;
using Confluence.Core.Content;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy
{
    /// <summary>
    /// Everything the economy owns for one player in one match: Crowns, the loadout, completed-duel feats, and the
    /// record of processed transaction requests. Crowns are match-only money for equipment (spec L193); the match grants
    /// scheduled income and winner bonuses through <see cref="GrantCrowns"/>. Operations from the shop and the draft
    /// lock <see cref="SyncRoot"/>, so concurrent requests for one player are applied one at a time.
    /// </summary>
    public sealed class PlayerEconomy
    {
        public PlayerEconomy(Catalogue catalogue, string wizardId, int playerSlot)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (playerSlot < 0) throw new ArgumentOutOfRangeException(nameof(playerSlot));
            Catalogue = catalogue;
            PlayerSlot = playerSlot;
            Loadout = new Loadout(catalogue, wizardId);
            Feats = new FeatLedger();
        }

        public Catalogue Catalogue { get; }

        /// <summary>Stable lobby index; selects this player's Offer and Draft random sub-streams.</summary>
        public int PlayerSlot { get; }

        public Loadout Loadout { get; }
        public FeatLedger Feats { get; }

        /// <summary>Current balance. Never negative.</summary>
        public int Crowns { get; private set; }

        public object SyncRoot { get; } = new object();

        internal RequestLedger Requests { get; } = new RequestLedger();

        /// <summary>Adds scheduled income or the winner bonus (spec L193). Amount must be non-negative.</summary>
        public void GrantCrowns(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Income cannot be negative.");
            lock (SyncRoot) Crowns = checked(Crowns + amount);
        }

        internal void Debit(int amount)
        {
            if (amount < 0 || amount > Crowns) throw new InvalidOperationException("Invalid debit of " + amount + " from " + Crowns + ".");
            Crowns -= amount;
        }

        internal void Credit(int amount)
        {
            if (amount < 0) throw new InvalidOperationException("Invalid credit.");
            Crowns = checked(Crowns + amount);
        }
    }
}
