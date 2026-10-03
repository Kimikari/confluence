using System;
using System.Collections.Generic;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Economics;

namespace Confluence.Hosting.Bots
{
    /// <summary>
    /// A bot's shop and draft play through the validated command API, one command per think interval (spec L429; the
    /// same gateway and rate limits as a client). Heuristics, in order:
    /// <list type="bullet">
    /// <item>Shop: buy the most expensive affordable card the loadout can take (first legal slot, else the reserve);
    /// otherwise use a card's single reroll on an unaffordable or unusable card while Crowns remain; enable an owned
    /// active item when none is enabled (L575); then ready (L45).</item>
    /// <item>Draft: take a rank-2 upgrade if offered, else a random card that can be taken; with all six augment slots
    /// full, replace the lowest-rank augment (L231); confirm the dormancy consequences the preview lists (L645). With
    /// nothing selectable, reroll one card once, then pass (L229).</item>
    /// </list>
    /// Random choices use the Bot stream for the round (L435).
    /// </summary>
    public sealed class BotEconomyAgent : IEconomyAgent
    {
        private readonly int _player;
        private readonly ulong _matchSeed;
        private readonly BotProfile _profile;
        private Rng _rng;
        private int _rngRound = -1;
        private int _nextActionTick;
        private int _phaseKey = -1;
        private long _nextRequestId = 1;
        private bool _draftRerolled;

        public BotEconomyAgent(int player, ulong matchSeed, BotProfile profile)
        {
            _player = player;
            _matchSeed = matchSeed;
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>Commands submitted so far (all accepted or rejected; tests count them).</summary>
        public int CommandsSubmitted { get; private set; }

        public int CommandsRejected { get; private set; }

        public void Act(EconomyTurn turn)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            int key = turn.Round * 4 + (int)turn.Phase + turn.BracketRound * 1000;
            if (key != _phaseKey)
            {
                _phaseKey = key;
                _nextActionTick = turn.Tick + _profile.EconomyThinkTicks;
                _draftRerolled = false;
            }

            if (_rngRound != turn.Round)
            {
                _rngRound = turn.Round;
                _rng = BotStreams.ForEconomy(_matchSeed, turn.Round, _player);
            }

            if (turn.Tick < _nextActionTick || turn.IsReady) return;
            _nextActionTick = turn.Tick + _profile.EconomyThinkTicks;
            if (turn.Phase == EconomyPhase.Shop) ShopStep(turn);
            else if (turn.Phase == EconomyPhase.Draft) DraftStep(turn);
        }

        // ---- Shop ----------------------------------------------------------------------------------------------

        private void ShopStep(EconomyTurn turn)
        {
            Loadout loadout = turn.Loadout;
            int crowns = turn.Crowns;
            IReadOnlyList<ShopCard> cards = turn.ShopCards;

            ShopCard best = null;
            foreach (ShopCard card in cards)
            {
                if (!card.IsPurchasable || card.Price > crowns) continue;
                if (loadout.CheckAdd(card.Definition, null, out _) != TransactionCode.Success) continue;
                if (best == null || card.Price > best.Price) best = card;
            }

            if (best != null)
            {
                Send(turn, PlayerCommand.Buy(_player, NextId(), best.Position));
                return;
            }

            foreach (ShopCard card in cards)
            {
                bool useless = card.State == ShopCardState.OutsideBudget ||
                    (card.IsPurchasable && loadout.CheckAdd(card.Definition, null, out _) != TransactionCode.Success);
                if (card.RerollAvailable && useless && crowns > 0)
                {
                    Send(turn, PlayerCommand.Reroll(_player, NextId(), card.Position));
                    return;
                }
            }

            if (loadout.EnabledActive == null)
            {
                foreach (OwnedEquipment item in loadout.EquippedItems)
                {
                    if (!item.IsActive || item.IsSpent) continue;
                    loadout.FindItem(item.InstanceId, out Placement placement);
                    if (!placement.IsSlot || !SlotRules.SlotAllowsActivation(placement.Slot)) continue;
                    Send(turn, PlayerCommand.SetActive(_player, NextId(), item.InstanceId));
                    return;
                }
            }

            Send(turn, PlayerCommand.Ready(_player, NextId()));
        }

        // ---- Draft ---------------------------------------------------------------------------------------------

        private void DraftStep(EconomyTurn turn)
        {
            IReadOnlyList<DraftCard> cards = turn.DraftCards;
            var options = new List<Choice>();
            foreach (DraftCard card in cards)
            {
                if (card == null) continue;
                Choice choice;
                if (TryChoose(turn, card, out choice)) options.Add(choice);
            }

            if (options.Count > 0)
            {
                Choice pick = options[0];
                bool upgrade = false;
                foreach (Choice c in options)
                {
                    if (c.Card.IsUpgrade && !upgrade)
                    {
                        pick = c;
                        upgrade = true;
                    }
                }

                if (!upgrade) pick = options[_rng.NextInt(options.Count)];
                Send(turn, PlayerCommand.DraftSelect(_player, NextId(), pick.Card.Position, pick.Slot, pick.NeedsConfirmation));
                return;
            }

            if (!_draftRerolled)
            {
                foreach (DraftCard card in cards)
                {
                    if (card == null || !card.RerollAvailable) continue;
                    _draftRerolled = true;
                    Send(turn, PlayerCommand.DraftReroll(_player, NextId(), card.Position));
                    return;
                }
            }

            Send(turn, PlayerCommand.DraftPass(_player, NextId()));
        }

        private bool TryChoose(EconomyTurn turn, DraftCard card, out Choice choice)
        {
            choice = default;
            SelectionPreview preview = turn.PreviewDraftSelection(card.Position, null);
            if (preview == null) return false;
            if (preview.Code == TransactionCode.Success)
            {
                choice = new Choice { Card = card, Slot = null, NeedsConfirmation = preview.NeedsConfirmation };
                return true;
            }

            if (preview.Code != TransactionCode.ReplacementRequired && preview.Code != TransactionCode.AugmentSlotsFull) return false;

            // All six slots are full (L231): replace the lowest-rank augment whose replacement is legal.
            IReadOnlyList<OwnedAugment> slots = turn.Loadout.AugmentSlots;
            int bestSlot = -1;
            int bestRank = int.MaxValue;
            bool confirm = false;
            for (int s = 0; s < slots.Count; s++)
            {
                if (slots[s] == null || slots[s].Rank >= bestRank) continue;
                SelectionPreview p = turn.PreviewDraftSelection(card.Position, s);
                if (p == null || p.Code != TransactionCode.Success) continue;
                bestSlot = s;
                bestRank = slots[s].Rank;
                confirm = p.NeedsConfirmation;
            }

            if (bestSlot < 0) return false;
            choice = new Choice { Card = card, Slot = bestSlot, NeedsConfirmation = confirm };
            return true;
        }

        private void Send(EconomyTurn turn, PlayerCommand command)
        {
            CommandResult result = turn.Submit(command);
            CommandsSubmitted++;
            if (!result.Succeeded) CommandsRejected++;
        }

        private long NextId() => _nextRequestId++;

        private struct Choice
        {
            public DraftCard Card;
            public int? Slot;
            public bool NeedsConfirmation;
        }
    }

    /// <summary>A seated bot: its duel input source and its economy agent (L381).</summary>
    public sealed class BotPlayer
    {
        public BotPlayer(int player, ulong matchSeed, BotProfile profile)
        {
            Player = player;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Input = new BotInputSource(matchSeed, profile);
            Economy = new BotEconomyAgent(player, matchSeed, profile);
        }

        public int Player { get; }
        public BotProfile Profile { get; }
        public BotInputSource Input { get; }
        public BotEconomyAgent Economy { get; }
    }
}
