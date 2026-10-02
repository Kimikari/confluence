using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Economics;
using Confluence.Match;

namespace Confluence.Hosting
{
    /// <summary>
    /// Something that shops and drafts for a player through the command API (bots, scripted test players). The host
    /// calls it once per tick, in player order, while that player's shop or draft is open.
    /// </summary>
    public interface IEconomyAgent
    {
        void Act(EconomyTurn turn);
    }

    /// <summary>
    /// What an economy agent may see and do on one tick: its own private shop or draft view (the same information the
    /// player's client receives) and <see cref="Submit"/>, which goes through the validated command gateway like any
    /// network request (spec L429). Agents never touch the economy objects directly.
    /// </summary>
    public sealed class EconomyTurn
    {
        private readonly MatchHost _host;
        private Loadout _loadout;

        internal EconomyTurn(MatchHost host, int player, EconomyPhase phase)
        {
            _host = host;
            Player = player;
            Phase = phase;
        }

        public int Player { get; }
        public EconomyPhase Phase { get; }
        public MatchMode Mode => _host.Flow.Mode;
        public int Round => _host.Flow.Round;
        public int BracketRound => _host.Flow.BracketRound;

        /// <summary>Match tick of this turn.</summary>
        public int Tick => _host.Flow.CurrentTick;

        /// <summary>Ticks since the shop or draft phase began.</summary>
        public int TicksInPhase => _host.Flow.CurrentTick - _host.Flow.PhaseStartTick;

        public int TicksRemaining => _host.Flow.PhaseTicksRemaining;

        public Catalogue Catalogue => _host.Catalogue;

        public int Crowns => _host.Economy.Player(Player).Crowns;

        /// <summary>The three shop cards, or an empty list outside the shop.</summary>
        public IReadOnlyList<ShopCard> ShopCards
        {
            get
            {
                EquipmentShop shop = _host.Economy.OpenShopOf(Player);
                return shop != null ? shop.Cards : (IReadOnlyList<ShopCard>)new ShopCard[0];
            }
        }

        /// <summary>The five draft cards, or an empty list outside an open draft.</summary>
        public IReadOnlyList<DraftCard> DraftCards
        {
            get
            {
                AugmentDraft draft = _host.Economy.OpenDraftOf(Player);
                return draft != null ? draft.Cards : (IReadOnlyList<DraftCard>)new DraftCard[0];
            }
        }

        /// <summary>A private copy of the player's live loadout (taken once per turn).</summary>
        public Loadout Loadout
        {
            get
            {
                if (_loadout != null) return _loadout;
                var economy = _host.Economy.Player(Player);
                lock (economy.SyncRoot) _loadout = economy.Loadout.Clone();
                return _loadout;
            }
        }

        public bool IsReady => _host.Economy.IsReady(Player);

        /// <summary>Previews a draft selection (replacement target and the augments that would become dormant, L231, L645).</summary>
        public SelectionPreview PreviewDraftSelection(int position, int? augmentSlot)
        {
            AugmentDraft draft = _host.Economy.OpenDraftOf(Player);
            return draft?.PreviewSelection(position, augmentSlot);
        }

        /// <summary>Submits a command for this player through the host's gateway.</summary>
        public CommandResult Submit(PlayerCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Player != Player) throw new ArgumentException("An agent may only act for its own player.", nameof(command));
            _loadout = null; // the loadout may change
            return _host.Submit(command);
        }
    }
}
