using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>Equipment shop: offers, rerolls, placeholders, purchases, sales, and request handling (spec L191-L213, L429).</summary>
    public class ShopTests
    {
        private static EquipmentShop Open(PlayerEconomy p, RoundInfo round, ulong seed, IContentAvailability availability = null) =>
            new EquipmentShop(p, round, seed, availability ?? ContentAvailability.All, NoChanges);

        private static IEnumerable<EquipmentDef> Offered(EquipmentShop shop) =>
            shop.Cards.Where(c => c.State == ShopCardState.Offered).Select(c => c.Definition);

        [Theory]
        [InlineData(MatchMode.Conclave, 1, 600, 1)]
        [InlineData(MatchMode.Conclave, 3, 600, 2)]
        [InlineData(MatchMode.Conclave, 5, 2000, 2)]
        [InlineData(MatchMode.Conclave, 6, 2000, 3)]
        [InlineData(MatchMode.Conclave, 9, 2000, 4)]
        [InlineData(MatchMode.Conclave, 13, 2000, 4)]
        [InlineData(MatchMode.Duel, 2, 2000, 2)]
        [InlineData(MatchMode.Duel, 4, 2000, 3)]
        [InlineData(MatchMode.Duel, 6, 2000, 4)]
        [InlineData(MatchMode.Conclave, 9, 500, 2)]
        public void OffersComeOnlyFromUnlockedAffordableTiers(MatchMode mode, int round, int crowns, int highestTier)
        {
            var seenTiers = new HashSet<int>();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var p = Player("WIZ-003", crowns);
                var shop = Open(p, new RoundInfo(mode, round), seed);
                var defs = Offered(shop).ToList();
                Assert.Equal(3, defs.Count);
                Assert.Equal(3, defs.Select(d => d.Id).Distinct().Count());
                foreach (var def in defs)
                {
                    Assert.True(def.Tier <= highestTier, def.Id + " tier " + def.Tier);
                    Assert.True(def.Price <= crowns);
                    Assert.Contains(def, Cat.EquipmentItems); // only shared equipment, never spells or augments
                    seenTiers.Add(def.Tier);
                }
                Assert.All(shop.Cards, c => Assert.True(c.RerollAvailable));
            }
            Assert.Contains(highestTier, seenTiers); // tiers are drawn uniformly first (D-020), so the top tier shows up
        }

        [Fact]
        public void OffersAreDeterministicPerMatchSeedRoundAndPlayer()
        {
            string Key(EquipmentShop s) => string.Join(",", s.Cards.Select(c => c.Definition.Id));
            var a = Open(Player("WIZ-001", 600, slot: 2), Conclave1, 99);
            var b = Open(Player("WIZ-020", 600, slot: 2), Conclave1, 99);
            Assert.Equal(Key(a), Key(b)); // same Offer stream; owning a wizard is never an item condition (spec L206)
            var distinct = new HashSet<string>();
            for (int slot = 0; slot < 8; slot++) distinct.Add(Key(Open(Player("WIZ-001", 600, slot), Conclave1, 99)));
            Assert.True(distinct.Count > 1);
        }

        [Fact]
        public void RerollNeverReturnsTheReplacedOrAnyVisibleDefinitionAndIsSingleUse()
        {
            for (ulong seed = 1; seed <= 80; seed++)
            {
                var p = Player("WIZ-002", 600);
                var shop = Open(p, Conclave3, seed);
                int position = (int)(seed % 3);
                var before = shop.Cards.Select(c => c.Definition.Id).ToList();
                var result = shop.Reroll((long)seed, position);
                Assert.Equal(TransactionCode.Success, result.Code);
                var after = shop.Cards;
                Assert.DoesNotContain(after[position].Definition.Id, before);
                Assert.True(after[position].Definition.Price <= p.Crowns);
                Assert.False(after[position].RerollAvailable);
                for (int i = 0; i < 3; i++)
                    if (i != position) Assert.Equal(before[i], after[i].Definition.Id); // only that card changes
                Assert.Equal(TransactionCode.RerollAlreadyUsed, shop.Reroll(1000 + (long)seed, position).Code);
                Assert.Equal(600, p.Crowns); // rerolls are free
            }
        }

        [Fact]
        public void RerollWithNoAlternativeKeepsTheCardAndTheReroll()
        {
            var three = ContentAvailability.FromIds(new[] { "EQP-001", "EQP-003", "EQP-006" });
            var shop = Open(Player("WIZ-001", 600), Conclave1, 5, three);
            var before = shop.Cards.Select(c => c.Definition.Id).ToList();
            var result = shop.Reroll(1, 0);
            Assert.Equal(TransactionCode.RerollPoolEmpty, result.Code);
            Assert.Equal(before, shop.Cards.Select(c => c.Definition.Id));
            Assert.True(shop.Cards[0].RerollAvailable);
        }

        [Fact]
        public void EmptyAffordablePoolShowsSaveCrownsWithoutConsumingRerolls()
        {
            var p = Player("WIZ-001", 150); // below the cheapest tier
            var shop = Open(p, Conclave1, 3);
            Assert.All(shop.Cards, c => Assert.Equal(ShopCardState.SaveCrowns, c.State));
            var result = shop.Reroll(1, 1);
            Assert.Equal(TransactionCode.SaveCrowns, result.Code);
            Assert.True(shop.Cards[1].RerollAvailable);
            Assert.Equal(TransactionCode.CardNotOffered, shop.Buy(2, 1).Code);
        }

        [Fact]
        public void PurchaseChargesPriceEmptiesThePositionAndPlacesTheItem()
        {
            var p = Player("WIZ-004", 600);
            var shop = Open(p, Conclave1, 11);
            var card = shop.Cards[0];
            var result = shop.Buy(1, 0);
            Assert.Equal(TransactionCode.Success, result.Code);
            Assert.Equal(600 - card.Price, p.Crowns);
            Assert.Equal(-card.Price, result.CrownsDelta);
            Assert.Equal(card.Definition.Id, result.Item.Id);
            Assert.Equal(card.Price, result.Item.PaidPrice);
            Assert.Contains(result.Item, p.Loadout.AllItems);
            Assert.Equal(ShopCardState.Purchased, shop.Cards[0].State);
            Assert.Null(shop.Cards[0].Definition); // empty until the next shop (spec L206)
            Assert.Equal(TransactionCode.CardNotOffered, shop.Buy(2, 0).Code);
            Assert.Equal(TransactionCode.CardNotRerollable, shop.Reroll(3, 0).Code);
            Assert.Equal(TransactionCode.InvalidCardPosition, shop.Buy(4, 3).Code);
        }

        [Fact]
        public void BuyingAnySubsetIsLegalWithinMoney()
        {
            var p = Player("WIZ-004", 600);
            var shop = Open(p, Conclave1, 12); // three tier 1 cards at 200
            Assert.Equal(TransactionCode.Success, shop.Buy(1, 0).Code);
            Assert.Equal(TransactionCode.Success, shop.Buy(2, 2).Code);
            Assert.Equal(TransactionCode.Success, shop.Buy(3, 1).Code);
            Assert.Equal(0, p.Crowns);
            Assert.Equal(3, p.Loadout.AllItems.Count);
        }

        [Fact]
        public void UnaffordableCardsFoldIntoPlaceholdersWhoseRerollDrawsFromTheNewPool()
        {
            // Find a tier 2 + tier 1 mix at Conclave round 3 with 600 Crowns.
            for (ulong seed = 1; seed < 500; seed++)
            {
                var p = Player("WIZ-007", 600);
                var shop = Open(p, Conclave3, seed);
                var cards = shop.Cards;
                int expensive = Array.FindIndex(cards.ToArray(), c => c.Price == 400);
                int otherExpensive = Array.FindLastIndex(cards.ToArray(), c => c.Price == 400);
                int cheap = Array.FindIndex(cards.ToArray(), c => c.Price == 200);
                if (expensive < 0 || otherExpensive == expensive || cheap < 0) continue;

                var folded = cards[otherExpensive].Definition.Id;
                Assert.Equal(TransactionCode.Success, shop.Buy(1, expensive).Code);
                Assert.Equal(200, p.Crowns);
                var after = shop.Cards;
                Assert.Equal(ShopCardState.OutsideBudget, after[otherExpensive].State); // folds, no free redraw
                Assert.Equal(folded, after[otherExpensive].Definition.Id);
                Assert.Equal(ShopCardState.Offered, after[cheap].State);               // still affordable
                Assert.Equal(TransactionCode.CardNotOffered, shop.Buy(2, otherExpensive).Code);

                var reroll = shop.Reroll(3, otherExpensive);
                Assert.Equal(TransactionCode.Success, reroll.Code);
                Assert.Equal(ShopCardState.Offered, reroll.Card.State);
                Assert.Equal(200, reroll.Card.Price);                                // the new affordable pool
                Assert.NotEqual(folded, reroll.Card.Definition.Id);
                Assert.NotEqual(after[cheap].Definition.Id, reroll.Card.Definition.Id);
                return;
            }
            Assert.Fail("No seed produced the needed card mix.");
        }

        [Fact]
        public void FoldedPlaceholderWithAnEmptyPoolShowsSaveCrownsAndSellingNeverRefills()
        {
            var tierTwoWands = ContentAvailability.FromIds(new[] { "EQP-002", "EQP-004", "EQP-005" });
            var p = Player("WIZ-007", 600);
            var shop = Open(p, Conclave3, 21, tierTwoWands);
            Assert.All(shop.Cards, c => Assert.Equal(400, c.Price));
            var bought = shop.Buy(1, 0);
            Assert.Equal(TransactionCode.Success, bought.Code);
            Assert.Equal(ShopCardState.OutsideBudget, shop.Cards[1].State);
            Assert.Equal(ShopCardState.OutsideBudget, shop.Cards[2].State);

            var save = shop.Reroll(2, 1);
            Assert.Equal(TransactionCode.SaveCrowns, save.Code);         // nothing affordable: Save Crowns
            Assert.Equal(ShopCardState.SaveCrowns, shop.Cards[1].State);
            Assert.True(shop.Cards[1].RerollAvailable);                  // the reroll was not consumed

            var sold = shop.Sell(3, bought.Item.InstanceId);
            Assert.Equal(TransactionCode.Success, sold.Code);
            Assert.Equal(200, sold.CrownsDelta);                         // 50% of 400
            Assert.Equal(400, p.Crowns);
            // Selling does not refill cards or restore rerolls (spec L208).
            Assert.Equal(ShopCardState.Purchased, shop.Cards[0].State);
            Assert.Equal(ShopCardState.SaveCrowns, shop.Cards[1].State);
            Assert.Equal(ShopCardState.OutsideBudget, shop.Cards[2].State);

            // The kept reroll can now draw from the new affordable pool, never the visible placeholder's definition.
            var later = shop.Reroll(4, 1);
            Assert.Equal(TransactionCode.Success, later.Code);
            Assert.NotEqual(shop.Cards[2].Definition.Id, later.Card.Definition.Id);
            Assert.False(shop.Cards[1].RerollAvailable);
        }

        [Fact]
        public void SellRefundsHalfThePaidPriceAndUsedConsumablesSellForZero()
        {
            var p = Player("WIZ-002", 2000);
            var shop = Open(p, Conclave9, 4);
            var staff = Give(p.Loadout, "EQP-028");       // tier 4 staff, paid 800
            var rain = Give(p.Loadout, "EQP-116");        // tier 2 consumable, paid 400
            var spare = Give(p.Loadout, "EQP-113");       // tier 1 consumable to reserve
            Assert.Equal(400, shop.Sell(1, staff.InstanceId).CrownsDelta);
            Assert.Equal(TransactionCode.Success, p.Loadout.SpendConsumableCharge()); // the duel spent Pocket Rain
            var usedSale = shop.Sell(2, rain.InstanceId);
            Assert.Equal(TransactionCode.Success, usedSale.Code);
            Assert.Equal(0, usedSale.CrownsDelta);
            Assert.DoesNotContain(rain, p.Loadout.AllItems);                          // disappears
            Assert.Equal(100, shop.Sell(3, spare.InstanceId).CrownsDelta);            // unused consumable: 50%
            Assert.Equal(2500, p.Crowns);
            Assert.Equal(TransactionCode.UnknownItem, shop.Sell(4, staff.InstanceId).Code);
        }

        [Fact]
        public void PurchaseIntoAConflictingPlacementFailsWithoutCharging()
        {
            var p = Player("WIZ-001", 600);
            var shop = Open(p, Conclave1, 8);
            var def = shop.Cards[0].Definition;
            var slot = SlotRules.SlotsFor(def)[0];
            Give(p.Loadout, def.Id, Placement.In(slot));
            var other = SlotRules.SlotsFor(def).Skip(1).Cast<EquipmentSlot?>().FirstOrDefault();
            var code = shop.Buy(1, 0, Placement.In(other ?? slot)).Code;
            Assert.True(code == TransactionCode.DuplicateEquippedDefinition || code == TransactionCode.SlotOccupied, code.ToString());
            Assert.Equal(600, p.Crowns);
            Assert.Equal(ShopCardState.Offered, shop.Cards[0].State);
            Assert.Equal(TransactionCode.Success, shop.Buy(2, 0, Placement.Reserve).Code); // a second copy may sit in reserve
        }

        [Fact]
        public void PurchaseWithNoSpaceFailsWithoutCharging()
        {
            var p = Player("WIZ-001", 600);
            foreach (var id in new[] { "EQP-001", "EQP-003", "EQP-051", "EQP-066", "EQP-081", "EQP-096", "EQP-097", "EQP-111", "EQP-098", "EQP-006", "EQP-010" })
                Give(p.Loadout, id);
            Assert.Equal(3, p.Loadout.Reserve.Count);
            var shop = Open(p, Conclave1, 2);
            Assert.Equal(TransactionCode.NoFreeSpace, shop.Buy(1, 0).Code);
            Assert.Equal(600, p.Crowns);
        }

        [Fact]
        public void DuplicateRequestIdsAreIdempotentAndReusedIdsAreRejected()
        {
            var p = Player("WIZ-001", 600);
            var shop = Open(p, Conclave1, 30);
            var first = shop.Buy(7, 0);
            var again = shop.Buy(7, 0);
            Assert.Equal(TransactionCode.Success, first.Code);
            Assert.False(first.IsDuplicate);
            Assert.True(again.IsDuplicate);
            Assert.Equal(first.Code, again.Code);
            Assert.Same(first.Item, again.Item);
            Assert.Single(p.Loadout.AllItems);
            Assert.Equal(400, p.Crowns);

            Assert.Equal(TransactionCode.RequestIdConflict, shop.Sell(7, first.Item.InstanceId).Code);
            Assert.Equal(TransactionCode.RequestIdConflict, shop.Buy(7, 1).Code);
            Assert.Equal(TransactionCode.InvalidRequestId, shop.Buy(0, 1).Code);
            Assert.Equal(TransactionCode.InvalidRequestId, shop.Buy(-3, 1).Code);

            // A failed request is also stored: retransmitting it does not retry against the new state.
            var failed = shop.Buy(8, 0);
            Assert.Equal(TransactionCode.CardNotOffered, failed.Code);
            Assert.True(shop.Buy(8, 0).IsDuplicate);
        }

        [Fact]
        public void ConcurrentDuplicateRequestsApplyOnce()
        {
            var p = Player("WIZ-001", 600);
            var shop = Open(p, Conclave1, 31);
            var results = new ShopResult[32];
            Parallel.For(0, results.Length, i => results[i] = shop.Buy(55, 1));
            Assert.All(results, r => Assert.Equal(TransactionCode.Success, r.Code));
            Assert.Equal(1, results.Count(r => !r.IsDuplicate));
            Assert.Single(p.Loadout.AllItems);
            Assert.Equal(400, p.Crowns);
        }

        [Fact]
        public void ConcurrentPurchasesRecheckMoneyAtomically()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var p = Player("WIZ-001", 400); // two of the three 200-Crown cards
                var shop = Open(p, Conclave1, seed);
                var results = new ShopResult[3];
                Parallel.For(0, 3, i => results[i] = shop.Buy(100 + i, i));
                Assert.Equal(2, results.Count(r => r.Succeeded));
                Assert.Equal(0, p.Crowns);
                Assert.Equal(2, p.Loadout.AllItems.Count);
                var loser = results.Single(r => !r.Succeeded).Code;
                Assert.True(loser == TransactionCode.CardNotOffered || loser == TransactionCode.InsufficientCrowns, loser.ToString());
            }
        }

        [Fact]
        public void SpellsAreNeverPurchasable()
        {
            var p = Player("WIZ-002", 600);
            var shop = Open(p, Conclave1, 1);
            Assert.Equal(TransactionCode.SpellsAreNotPurchasable, shop.BuyById(1, "SPL-009").Code); // own spell
            Assert.Equal(TransactionCode.SpellsAreNotPurchasable, shop.BuyById(2, "SPL-001").Code); // another wizard's spell
            Assert.Equal(600, p.Crowns);
            Assert.Equal(TransactionCode.CardNotOffered, shop.BuyById(3, "AUG-149").Code);
            var offered = shop.Cards[2].Definition.Id;
            Assert.Equal(TransactionCode.Success, shop.BuyById(4, offered).Code);
            Assert.True(shop.BuyById(4, offered).IsDuplicate);

            var pool = new EquipmentPool(Cat, ContentAvailability.All);
            int total = Enumerable.Range(1, 4).Sum(t => pool.Tier(t).Count);
            Assert.Equal(120, total); // exactly the shared equipment catalogue
        }

        [Fact]
        public void SpellSelectionHappensInTheShopFromTheOwnKitOnly()
        {
            var p = Player("WIZ-002", 600);
            var shop = Open(p, Conclave1, 1);
            Assert.Equal(TransactionCode.SpellNotInKit, shop.SetEquippedSpells(1, new[] { "SPL-073", "SPL-010", "SPL-011", "SPL-012", "SPL-013", "SPL-014" }).Code);
            Assert.Equal(TransactionCode.Success, shop.SetEquippedSpells(2, new[] { "SPL-015", "SPL-016", "SPL-011", "SPL-012", "SPL-013", "SPL-014" }).Code);
            Assert.Equal("SPL-015", p.Loadout.EquippedSpellIds[0]);
        }

        [Fact]
        public void ClosedShopRejectsRequestsAndUnavailableItemsAreNeverOffered()
        {
            var availability = ContentAvailability.FromIds(new[] { "EQP-001", "EQP-031", "EQP-051", "EQP-096", "EQP-111", "EQP-081" });
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var shop = Open(Player("WIZ-001", 600), Conclave1, seed, availability);
                Assert.All(shop.Cards, c => Assert.True(availability.IsAvailable(c.Definition.Id)));
            }
            var p = Player("WIZ-001", 600);
            var closing = Open(p, Conclave1, 1);
            closing.Close();
            Assert.Equal(TransactionCode.ShopClosed, closing.Buy(1, 0).Code);
            Assert.Equal(TransactionCode.ShopClosed, closing.Reroll(2, 0).Code);
            Assert.Equal(600, p.Crowns);
        }

        [Fact]
        public void EnablingAnActiveGoesThroughTheShop()
        {
            var p = Player("WIZ-001", 0);
            var shop = Open(p, Conclave1, 1);
            var forked = Give(p.Loadout, "EQP-007");
            var boots = Give(p.Loadout, "EQP-094");
            Assert.Same(forked, p.Loadout.EnabledActive);
            Assert.Equal(TransactionCode.Success, shop.SetEnabledActive(1, boots.InstanceId).Code);
            Assert.Same(boots, p.Loadout.EnabledActive);
            Assert.Equal(TransactionCode.Success, shop.SetEnabledActive(2, null).Code);
            Assert.Null(p.Loadout.EnabledActive);
            Assert.Equal(TransactionCode.Success, shop.Move(3, forked.InstanceId, Placement.Reserve).Code);
            Assert.Contains(forked, p.Loadout.Reserve);
        }
    }
}
