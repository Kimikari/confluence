using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Messages;
using Confluence.Match;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;

namespace Confluence.Hosting.Tests
{
    /// <summary>
    /// The player command API (spec L429): validation, atomicity, idempotent request ids, one reroll per card, the
    /// rate-limit hook, and the draft ending on selection (L229). Two network-fed players in a Duel; round 1 has a shop
    /// and a Silver draft.
    /// </summary>
    public sealed class CommandGatewayTests
    {
        private static MatchHost StartedDuel(ICommandRateLimiter limiter = null)
        {
            MatchHostSettings settings = HostFixture.DuelSettings(HostFixture.SmallDuelSeed);
            settings.RateLimiter = limiter ?? UnlimitedCommands.Instance;
            MatchHost host = HostFixture.Host(settings);
            Assert.True(host.Start().Started);
            Assert.Equal(MatchPhase.Shop, host.Flow.Phase);
            return host;
        }

        private static ShopCard Offered(MatchHost host, int player) => host.Economy.OpenShopOf(player).Cards.First(c => c.IsPurchasable);

        [Fact]
        public void Commands_Before_The_Match_Or_With_Bad_Ids_Are_Refused()
        {
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed));
            Assert.Equal(CommandCode.MatchNotRunning, host.Submit(PlayerCommand.Ready(0, 1)).Code);
            Assert.Equal(CommandCode.InvalidRequestId, host.Submit(PlayerCommand.Ready(0, 0)).Code);
            Assert.Equal(CommandCode.UnknownPlayer, host.Submit(PlayerCommand.Ready(7, 1)).Code);
        }

        [Fact]
        public void Purchases_Are_Validated_By_The_Server_Shop()
        {
            MatchHost host = StartedDuel();
            PlayerEconomy economy = host.Economy.Player(0);
            Assert.Equal(GlobalRules.Economy.FirstShopIncome, economy.Crowns); // L193: Match's income, credited at the open

            CommandResult badPosition = host.Submit(PlayerCommand.Buy(0, 1, 7));
            Assert.Equal(CommandCode.TransactionRejected, badPosition.Code);
            Assert.Equal(TransactionCode.InvalidCardPosition, badPosition.Transaction);

            ShopCard card = Offered(host, 0);
            EquipmentSlot wrongSlot = SlotRules.AllSlots.First(s => !card.Slots.Contains(s));
            CommandResult illegalSlot = host.Submit(PlayerCommand.Buy(0, 2, card.Position, wrongSlot));
            Assert.Equal(CommandCode.TransactionRejected, illegalSlot.Code);
            Assert.Equal(GlobalRules.Economy.FirstShopIncome, economy.Crowns); // atomic: nothing changed

            CommandResult bought = host.Submit(PlayerCommand.Buy(0, 3, card.Position));
            Assert.True(bought.Succeeded, bought.ToString());
            Assert.Equal(GlobalRules.Economy.FirstShopIncome - card.Price, economy.Crowns);

            CommandResult again = host.Submit(PlayerCommand.Buy(0, 4, card.Position));
            Assert.Equal(TransactionCode.CardNotOffered, again.Transaction); // a bought position stays empty (L206)

            CommandResult unknownSale = host.Submit(PlayerCommand.Sell(0, 5, 999));
            Assert.Equal(TransactionCode.UnknownItem, unknownSale.Transaction);

            CommandResult notInKit = host.Submit(PlayerCommand.SelectSpells(0, 6, new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-999" }));
            Assert.Equal(CommandCode.TransactionRejected, notInKit.Code);

            // Player 1's shop is a different server object: player 0's purchase never touched it.
            Assert.Equal(GlobalRules.Economy.FirstShopIncome, host.Economy.Player(1).Crowns);
        }

        [Fact]
        public void Duplicate_Request_Ids_Are_Idempotent_And_Conflicts_Are_Refused()
        {
            MatchHost host = StartedDuel();
            PlayerEconomy economy = host.Economy.Player(0);
            ShopCard card = Offered(host, 0);
            CommandResult first = host.Submit(PlayerCommand.Buy(0, 42, card.Position));
            Assert.True(first.Succeeded);
            int crowns = economy.Crowns;
            int items = economy.Loadout.AllItems.Count;

            CommandResult repeat = host.Submit(PlayerCommand.Buy(0, 42, card.Position));
            Assert.True(repeat.IsDuplicate);
            Assert.Equal(first.Code, repeat.Code);
            Assert.Equal(crowns, economy.Crowns);                     // not charged twice
            Assert.Equal(items, economy.Loadout.AllItems.Count);       // not granted twice

            CommandResult conflict = host.Submit(PlayerCommand.Reroll(0, 42, 1));
            Assert.Equal(CommandCode.RequestIdConflict, conflict.Code);

            // A rejected request is also stored: repeating it returns the same rejection (D-073).
            CommandResult rejected = host.Submit(PlayerCommand.Buy(0, 43, 9));
            CommandResult rejectedAgain = host.Submit(PlayerCommand.Buy(0, 43, 9));
            Assert.True(rejectedAgain.IsDuplicate);
            Assert.Equal(rejected.Transaction, rejectedAgain.Transaction);

            // Request ids are per player: player 1 may use 42 for its own purchase.
            Assert.True(host.Submit(PlayerCommand.Buy(1, 42, Offered(host, 1).Position)).Succeeded);
        }

        [Fact]
        public void Each_Shop_Card_Has_One_Reroll()
        {
            MatchHost host = StartedDuel();
            CommandResult first = host.Submit(PlayerCommand.Reroll(0, 1, 0));
            Assert.True(first.Succeeded, first.ToString());
            Assert.False(host.Economy.OpenShopOf(0).Cards[0].RerollAvailable);
            CommandResult second = host.Submit(PlayerCommand.Reroll(0, 2, 0));
            Assert.Equal(CommandCode.TransactionRejected, second.Code);
            Assert.Equal(TransactionCode.RerollAlreadyUsed, second.Transaction);
            Assert.True(host.Submit(PlayerCommand.Reroll(0, 3, 1)).Succeeded); // other cards keep their own reroll
        }

        [Fact]
        public void Reserve_Moves_Spell_Selection_And_The_Enabled_Active_Go_Through_The_Shop()
        {
            MatchHost host = StartedDuel();
            PlayerEconomy economy = host.Economy.Player(0);
            ShopCard card = Offered(host, 0);
            Assert.True(host.Submit(PlayerCommand.Buy(0, 1, card.Position, toReserve: true)).Succeeded);
            OwnedEquipment item = economy.Loadout.Reserve.Single(i => i != null);

            Assert.Equal(CommandCode.InvalidArguments, host.Submit(PlayerCommand.MoveToReserve(0, 2, item.InstanceId)).Code); // already there
            CommandResult equip = host.Submit(PlayerCommand.EquipFromReserve(0, 3, item.InstanceId, card.Slots[0]));
            Assert.True(equip.Succeeded, equip.ToString());
            economy.Loadout.FindItem(item.InstanceId, out Placement placement);
            Assert.True(placement.IsSlot);
            Assert.Equal(CommandCode.InvalidArguments, host.Submit(PlayerCommand.EquipFromReserve(0, 4, item.InstanceId, card.Slots[0])).Code);
            Assert.True(host.Submit(PlayerCommand.MoveToReserve(0, 5, item.InstanceId)).Succeeded);

            CommandResult active = host.Submit(PlayerCommand.SetActive(0, 6, item.InstanceId));
            Assert.Equal(CommandCode.TransactionRejected, active.Code); // only an equipped active item can be enabled (L575)
            Assert.True(host.Submit(PlayerCommand.SetActive(0, 7, null)).Succeeded);

            IReadOnlyList<string> kit = economy.Loadout.KitSpellIds;
            var reversed = kit.Take(6).Reverse().ToList();
            Assert.True(host.Submit(PlayerCommand.SelectSpells(0, 8, reversed)).Succeeded);
            Assert.Equal(reversed, economy.Loadout.EquippedSpellIds);

            // Every accepted shop command sends the player's private state, never to the opponent (L429).
            List<HostMessage> pending = host.Outbox.Drain();
            Assert.Contains(pending, m => m is ShopStateMessage s && s.Player == 0 && s.Loadout.SpellIds.SequenceEqual(reversed));
            Assert.All(pending.OfType<ShopStateMessage>(), s => Assert.Equal(s.Player, s.Audience));
        }

        [Fact]
        public void Ready_Ends_The_Shop_Early_And_A_Draft_Selection_Ends_The_Draft()
        {
            MatchHost host = StartedDuel();
            Assert.True(host.Submit(PlayerCommand.Ready(0, 1)).Succeeded);
            host.Tick();
            Assert.Equal(MatchPhase.Shop, host.Flow.Phase); // player 1 has not readied (L45)
            Assert.True(host.Submit(PlayerCommand.Ready(1, 1)).Succeeded);
            host.Tick();
            Assert.Equal(MatchPhase.Draft, host.Flow.Phase); // Duel round 1 drafts Silver (L225)

            // A shop command now is refused; the draft is open and the loadout frozen (L227).
            Assert.Equal(CommandCode.ShopNotOpen, host.Submit(PlayerCommand.Buy(0, 2, 0)).Code);
            Assert.True(host.Economy.Player(0).Loadout.IsFrozen);

            AugmentDraft draft = host.Economy.OpenDraftOf(0);
            Assert.NotNull(draft);
            DraftCard pick = draft.Cards.First(c => draft.PreviewSelection(c.Position, 3).Code == TransactionCode.Success);
            CommandResult selected = host.Submit(PlayerCommand.DraftSelect(0, 3, pick.Position, augmentSlot: 3, confirmDormancy: true));
            Assert.True(selected.Succeeded, selected.ToString());
            Assert.Equal(pick.AugmentId, host.Economy.Player(0).Loadout.AugmentSlots[3].Id); // the named target slot (L231)
            Assert.True(host.Economy.IsReady(0));
            Assert.False(host.Economy.Player(0).Loadout.IsFrozen);
            Assert.Equal(CommandCode.DraftNotOpen, host.Submit(PlayerCommand.DraftReroll(0, 4, 0)).Code);
            Assert.Equal(CommandCode.DraftNotOpen, host.Submit(PlayerCommand.DraftSelect(0, 5, 1)).Code);

            Assert.True(host.Submit(PlayerCommand.DraftPass(1, 2)).Succeeded);
            host.Tick();
            Assert.Equal(MatchPhase.Alcove, host.Flow.Phase);

            // The finalized loadout is public from the alcove on (L46) and carries the drafted augment.
            PublicLoadout finalized = host.Flow.Player(0).CurrentLoadout;
            Assert.Contains(pick.AugmentId, finalized.AugmentIds);
            Assert.Equal(DraftOutcome.Passed, host.Economy.CurrentDraftOf(1).Outcome);
        }

        [Fact]
        public void Draft_Rerolls_Are_One_Per_Card()
        {
            MatchHost host = StartedDuel();
            host.Submit(PlayerCommand.Ready(0, 1));
            host.Submit(PlayerCommand.Ready(1, 1));
            host.Tick();
            Assert.Equal(MatchPhase.Draft, host.Flow.Phase);
            Assert.True(host.Submit(PlayerCommand.DraftReroll(0, 2, 4)).Succeeded);
            CommandResult again = host.Submit(PlayerCommand.DraftReroll(0, 3, 4));
            Assert.Equal(CommandCode.TransactionRejected, again.Code);
            Assert.Equal(TransactionCode.RerollAlreadyUsed, again.Transaction);
        }

        [Fact]
        public void Surrender_Is_A_Normal_Loss_Resolved_By_The_Duel()
        {
            MatchHost host = StartedDuel();
            Assert.Equal(CommandCode.NotInDuel, host.Submit(PlayerCommand.Surrender(1, 1)).Code);
            host.Submit(PlayerCommand.Ready(0, 1));
            host.Submit(PlayerCommand.Ready(1, 2));
            host.Tick();
            host.Submit(PlayerCommand.DraftPass(0, 2));
            host.Submit(PlayerCommand.DraftPass(1, 3));
            HostFixture.RunUntilPhase(host, MatchPhase.Combat);
            for (int i = 0; i < 30; i++) host.Tick();

            Assert.True(host.Submit(PlayerCommand.Surrender(1, 4)).Succeeded);
            host.Tick();
            DuelResolvedEvent resolved = host.Flow.Events.OfType<DuelResolvedEvent>().Single();
            Assert.Equal(MatchDuelEndReason.Surrender, resolved.Outcome.Reason); // L53: a surrender is a normal loss
            Assert.Equal(0, resolved.Outcome.Winner);
            Assert.Equal(CommandCode.NotInDuel, host.Submit(PlayerCommand.Surrender(1, 5)).Code);
            Assert.Contains(host.Duels.Reports.Single().Replay.Actions, a => a.Kind == ReplayActionKind.Surrender && a.Side == resolved.Setup.SideOf(1));
        }

        [Fact]
        public void Rate_Limited_Commands_Are_Not_Recorded_And_May_Be_Retried()
        {
            var limiter = new TickRateLimiter();
            MatchHost host = StartedDuel(limiter);
            for (int i = 0; i < TickRateLimiter.RerollBurst; i++)
            {
                Assert.True(host.Submit(PlayerCommand.Reroll(0, i + 1, i)).Succeeded);
            }

            CommandResult limited = host.Submit(PlayerCommand.Reroll(0, 10, 0));
            Assert.Equal(CommandCode.RateLimited, limited.Code);
            Assert.Equal(TickRateLimiter.RerollBurst, host.Commands.RecordedCount(0));

            for (int i = 0; i < TickRateLimiter.RerollRefillTicks; i++) host.Tick();
            CommandResult retried = host.Submit(PlayerCommand.Reroll(0, 10, 0));
            Assert.False(retried.IsDuplicate); // the refused attempt was never recorded
            Assert.Equal(TransactionCode.RerollAlreadyUsed, retried.Transaction);
        }
    }
}
