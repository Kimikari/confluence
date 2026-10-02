using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>The five-card augment draft (spec L214-L233, L594, L645).</summary>
    public class DraftTests
    {
        private static readonly RoundInfo Conclave7 = new RoundInfo(MatchMode.Conclave, 7);

        private static AugmentDraft Open(PlayerEconomy p, RoundInfo round, ulong seed, IContentAvailability availability = null)
        {
            Assert.True(AugmentDraft.TryOpen(p, round, seed, availability ?? ContentAvailability.All, out var draft, NoChanges));
            return draft;
        }

        private static IContentAvailability Only(params string[] ids) => ContentAvailability.FromIds(ids);

        [Fact]
        public void OpeningShowsFiveDistinctEligibleCardsWithTheGuarantees()
        {
            foreach (var wizard in Cat.Wizards)
            {
                foreach (var round in new[] { Conclave1, Conclave5, Conclave9 })
                {
                    round.TryGetDraftRarity(out var rarity);
                    var pool = AugmentPool.Eligible(Loadout(wizard.Id), round, rarity, null, ContentAvailability.All, NoChanges)
                        .Select(o => o.Id).ToHashSet();
                    for (ulong seed = 1; seed <= 3; seed++)
                    {
                        var draft = Open(Player(wizard.Id), round, seed * 7919 + (ulong)round.Round);
                        var cards = draft.Cards;
                        Assert.Equal(5, cards.Count);
                        Assert.Equal(5, cards.Select(c => c.AugmentId).Distinct().Count());
                        Assert.All(cards, c => Assert.Contains(c.AugmentId, pool));
                        Assert.All(cards, c => Assert.Equal(rarity, c.Rarity));          // new definitions match the draft rarity
                        Assert.All(cards, c => Assert.Equal(1, c.Rank));
                        Assert.All(cards, c => Assert.False(string.IsNullOrEmpty(c.EligibilityText)));
                        Assert.True(draft.TransformationEligible);
                        Assert.True(draft.TransformationGuaranteeMet);
                        Assert.Contains(cards, c => c.Definition.IsTransformation && c.Definition.OwnerWizardId == wizard.Id);
                        Assert.True(cards.Count(c => AugmentClassification.IsUniversalOrBroad(c.Definition)) >= 2);
                        Assert.All(cards, c => Assert.True(!c.Definition.IsTransformation || c.Definition.OwnerWizardId == wizard.Id));
                        Assert.True(draft.MeetsFloor);
                    }
                }
            }
        }

        [Fact]
        public void WithoutAnEligibleTransformationAnOrdinaryChoiceReplacesTheGuarantee()
        {
            var shared = ContentAvailability.FromIds(Cat.SharedAugments.Select(a => a.Id));
            var draft = Open(Player("WIZ-002"), Conclave1, 4, shared);
            Assert.False(draft.TransformationEligible);
            Assert.False(draft.TransformationGuaranteeMet);
            Assert.NotNull(draft.GuaranteeNote);
            Assert.Equal(5, draft.Cards.Count);
            Assert.True(draft.Cards.Count(c => AugmentClassification.IsUniversalOrBroad(c.Definition)) >= 2);
        }

        [Fact]
        public void EachCardHasOneRerollThatNeverShowsAnyDefinitionShownBefore()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var draft = Open(Player("WIZ-010"), Conclave5, seed);
                var shown = draft.Cards.Select(c => c.AugmentId).ToList();
                for (int position = 0; position < 5; position++)
                {
                    var before = draft.Cards.Select(c => c.AugmentId).ToList();
                    var result = draft.Reroll(100 + position, position);
                    Assert.Equal(TransactionCode.Success, result.Code);
                    Assert.DoesNotContain(result.Card.AugmentId, shown);  // excludes every definition shown in this draft
                    Assert.False(result.Card.RerollAvailable);
                    var after = draft.Cards.Select(c => c.AugmentId).ToList();
                    for (int i = 0; i < 5; i++)
                        if (i != position) Assert.Equal(before[i], after[i]); // only that card changes
                    shown.Add(result.Card.AugmentId);
                    Assert.Equal(TransactionCode.RerollAlreadyUsed, draft.Reroll(200 + position, position).Code);
                }
                Assert.Equal(10, shown.Distinct().Count()); // five opening cards and five distinct rerolls (spec L233)
                Assert.Equal(shown, draft.ShownDefinitions);
            }
        }

        [Fact]
        public void RerollWithNoUnshownAlternativeKeepsTheReroll()
        {
            var five = Only("AUG-149", "AUG-073", "AUG-074", "AUG-075", "AUG-076");
            var draft = Open(Player("WIZ-001"), Conclave1, 2, five);
            Assert.Equal(5, draft.Cards.Count);
            Assert.False(draft.MeetsFloor);
            var result = draft.Reroll(1, 3);
            Assert.Equal(TransactionCode.NoAlternative, result.Code);
            Assert.True(draft.Cards[3].RerollAvailable);
        }

        [Fact]
        [Verifies("AUG-149")]
        public void OwnedRankOneReturnsAsRankTwoAtEqualOrLaterRarityAndRankTwoNeverReturns()
        {
            var p = Player("WIZ-001");
            Grant(p.Loadout, "AUG-149"); // Silver, rank 1, slot 0
            var gold = Only("AUG-149", "AUG-012", "AUG-083", "AUG-084", "AUG-085");
            var draft = Open(p, Conclave5, 3, gold);
            var upgrade = draft.Cards.Single(c => c.AugmentId == "AUG-149");
            Assert.True(upgrade.IsUpgrade);
            Assert.Equal(2, upgrade.Rank);
            Assert.Equal(Rarity.Silver, upgrade.Rarity); // the original rarity; offered in a Gold draft

            var result = draft.Select(1, upgrade.Position);
            Assert.Equal(TransactionCode.Success, result.Code);
            Assert.Equal(2, p.Loadout.FindAugment("AUG-149").Rank);
            Assert.Equal(0, p.Loadout.AugmentSlotOf("AUG-149")); // same slot
            Assert.Single(p.Loadout.Augments);

            var next = Open(p, Conclave7, 4, gold);
            Assert.DoesNotContain(next.Cards, c => c.AugmentId == "AUG-149"); // an owned rank 2 never appears

            // A Gold rank 1 is not offered as rank 2 in an earlier-rarity (Silver) draft.
            var l = Loadout("WIZ-001");
            Grant(l, "AUG-012", Conclave5);
            DormancyResolver.Resolve(l, NoChanges);
            Assert.DoesNotContain(AugmentPool.Eligible(l, Conclave3, Rarity.Silver, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-012");
            Assert.Contains(AugmentPool.Eligible(l, Conclave9, Rarity.Prismatic, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-012" && o.Rank == 2);
        }

        [Fact]
        [Verifies("AUG-001")]
        public void ADormantRankOneIsNotOfferedAsRankTwo()
        {
            var l = Loadout("WIZ-001");
            Grant(l, "AUG-001");
            l.SetEquippedSpells(new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-007" });
            DormancyResolver.Resolve(l, NoChanges);
            Assert.DoesNotContain(AugmentPool.Eligible(l, Conclave5, Rarity.Gold, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-001");
        }

        [Fact]
        [Verifies("AUG-289", "AUG-290", "AUG-032", "AUG-033")]
        public void ExclusiveGroupConflictsAreIneligible()
        {
            var l = Loadout("WIZ-001");
            DormancyResolver.Resolve(l, NoChanges);
            Assert.Contains(AugmentPool.Eligible(l, Conclave5, Rarity.Gold, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-290");
            Grant(l, "AUG-289"); // SPL-001-delivery
            Assert.DoesNotContain(AugmentPool.Eligible(l, Conclave5, Rarity.Gold, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-290");
            Assert.Equal(TransactionCode.ExclusiveGroupConflict, AugmentAcquisition.AcquireNew(l, Cat.Augment("AUG-290"), Conclave5, null, null, NoChanges).Code);

            var s = Loadout("WIZ-006");     // projectile kit
            Give(s, "EQP-031");             // melee
            Give(s, "EQP-001", Placement.In(EquipmentSlot.Sidearm)); // precision
            DormancyResolver.Resolve(s, NoChanges);
            Assert.Contains(AugmentPool.Eligible(s, Conclave5, Rarity.Gold, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-033");
            Grant(s, "AUG-032");            // range_specialization
            Assert.DoesNotContain(AugmentPool.Eligible(s, Conclave5, Rarity.Gold, null, ContentAvailability.All, NoChanges), o => o.Id == "AUG-033");
        }

        [Fact]
        public void OtherWizardsTransformationsAreNeverOffered()
        {
            var l = Loadout("WIZ-002");
            DormancyResolver.Resolve(l, NoChanges);
            var pool = AugmentPool.Eligible(l, Conclave1, Rarity.Silver, null, ContentAvailability.All, NoChanges);
            Assert.All(pool.Where(o => o.IsTransformation), o => Assert.Equal("WIZ-002", o.Definition.OwnerWizardId));
            Assert.Equal(5, pool.Count(o => o.IsTransformation)); // five Silver transformations per wizard (spec L596)
            Assert.Equal(TransactionCode.WrongWizard, AugmentAcquisition.AcquireNew(l, Cat.Augment("AUG-289"), Conclave1, null, null, NoChanges).Code);
        }

        [Fact]
        public void SelectingOneCardEndsTheDraft()
        {
            var p = Player("WIZ-003", 300);
            var draft = Open(p, Conclave1, 9);
            Assert.True(p.Loadout.IsFrozen);
            var card = draft.Cards[2];
            var result = draft.Select(1, 2);
            Assert.Equal(TransactionCode.Success, result.Code);
            Assert.Equal(DraftOutcome.Selected, draft.Outcome);
            Assert.False(p.Loadout.IsFrozen);
            Assert.Equal(new[] { card.AugmentId }, Ids(p.Loadout.Augments));
            Assert.Equal(Conclave1, p.Loadout.FindAugment(card.AugmentId).AcquiredAt);
            Assert.Equal(300, p.Crowns); // free (spec L594)

            Assert.Equal(TransactionCode.DraftClosed, draft.Select(2, 0).Code); // cannot take a second card
            Assert.Equal(TransactionCode.DraftClosed, draft.Reroll(3, 0).Code);
            Assert.Equal(TransactionCode.DraftClosed, draft.Pass(4).Code);
            Assert.Single(p.Loadout.Augments);
        }

        [Fact]
        public void PassingAndTimingOutChooseNothingAndBankNothing()
        {
            var p = Player("WIZ-003");
            var first = Open(p, Conclave1, 1);
            Assert.Equal(TransactionCode.Success, first.Pass(1).Code);
            Assert.Equal(DraftOutcome.Passed, first.Outcome);
            Assert.Empty(p.Loadout.Augments);

            var second = Open(p, Conclave3, 2);
            Assert.Equal(5, second.Cards.Count); // no extra cards or choices
            second.Timeout();
            Assert.Equal(DraftOutcome.TimedOut, second.Outcome);
            Assert.Empty(p.Loadout.Augments);
            Assert.False(p.Loadout.IsFrozen);

            var third = Open(p, Conclave5, 3);
            Assert.Equal(TransactionCode.Success, third.Select(3, 0).Code);
            Assert.Equal(TransactionCode.DraftClosed, third.Select(4, 1).Code);
            Assert.Single(p.Loadout.Augments);
        }

        [Fact]
        [Verifies("AUG-079", "AUG-093")]
        public void ReplacingAParentNeedsASlotChoiceAndConfirmationOfDormantDependents()
        {
            var p = Player("WIZ-006", 500);
            var l = p.Loadout;
            Give(l, "EQP-038");
            Give(l, "EQP-031");
            foreach (var id in new[] { "AUG-149", "AUG-079", "AUG-073", "AUG-074", "AUG-075" }) Grant(l, id);
            Grant(l, "AUG-093", Conclave9);
            Assert.Equal(-1, l.FirstFreeAugmentSlot);

            var prismatic = Only("AUG-019", "AUG-020", "AUG-089", "AUG-090", "AUG-092");
            var draft = Open(p, Conclave9, 5, prismatic);
            Assert.Equal(TransactionCode.ReplacementRequired, draft.Select(1, 0).Code);
            Assert.True(draft.IsOpen);

            int parentSlot = l.AugmentSlotOf("AUG-079");
            var preview = draft.PreviewSelection(0, parentSlot);
            Assert.Equal("AUG-079", preview.Replaced.Id);
            Assert.Equal(new[] { "AUG-093" }, preview.BecomeDormant);

            var unconfirmed = draft.Select(2, 0, parentSlot);
            Assert.Equal(TransactionCode.ConfirmationRequired, unconfirmed.Code);
            Assert.Equal(new[] { "AUG-093" }, unconfirmed.Preview.BecomeDormant);
            Assert.True(draft.IsOpen);
            Assert.NotNull(l.FindAugment("AUG-079"));

            var chosen = draft.Cards[0].AugmentId;
            var confirmed = draft.Select(3, 0, parentSlot, confirmDormancy: true);
            Assert.Equal(TransactionCode.Success, confirmed.Code);
            Assert.Equal("AUG-079", confirmed.Acquisition.Replaced.Id);
            Assert.Null(l.FindAugment("AUG-079"));
            Assert.Equal(chosen, l.AugmentSlots[parentSlot].Id);
            Assert.True(l.FindAugment("AUG-093").IsDormant);
            Assert.Equal(500, p.Crowns); // replacement gives no Crowns
            Assert.Equal(6, l.Augments.Count);
            Assert.False(draft.IsOpen); // and no further choice
        }

        [Fact]
        public void ReplacingAnAugmentWithoutDependentsNeedsNoConfirmation()
        {
            var p = Player("WIZ-001");
            foreach (var id in new[] { "AUG-149", "AUG-073", "AUG-074", "AUG-075", "AUG-076", "AUG-077" }) Grant(p.Loadout, id);
            var draft = Open(p, Conclave5, 6);
            var preview = draft.PreviewSelection(1, 0);
            Assert.False(preview.NeedsConfirmation);
            Assert.Equal(TransactionCode.Success, draft.Select(1, 1, 0).Code);
            Assert.Null(p.Loadout.FindAugment("AUG-149"));
        }

        [Fact]
        public void DraftRequestIdsAreIdempotent()
        {
            var p = Player("WIZ-001");
            var draft = Open(p, Conclave1, 8);
            var first = draft.Select(5, 0);
            var again = draft.Select(5, 0);
            Assert.True(first.Succeeded);
            Assert.True(again.IsDuplicate);
            Assert.Equal(first.Code, again.Code);
            Assert.Single(p.Loadout.Augments);
            Assert.Equal(TransactionCode.RequestIdConflict, draft.Reroll(5, 1).Code);
            Assert.Equal(TransactionCode.InvalidRequestId, draft.Pass(0).Code);
        }

        [Fact]
        public void DraftUsesOnlyTheDraftStream()
        {
            string Cards(AugmentDraft d) => string.Join(",", d.Cards.Select(c => c.AugmentId));
            var plain = Open(Player("WIZ-007", 600), Conclave1, 77);

            // Shopping first consumes the Offer stream; the draft cards are unchanged.
            var shopper = Player("WIZ-007", 600);
            var shop = new EquipmentShop(shopper, Conclave1, 77, ContentAvailability.All, NoChanges);
            shop.Reroll(1, 0);
            shop.Reroll(2, 1);
            shop.Close();
            var after = Open(shopper, Conclave1, 77);
            Assert.Equal(Cards(plain), Cards(after));

            Assert.Equal(Cards(plain), Cards(Open(Player("WIZ-007"), Conclave1, 77)));
            var others = Enumerable.Range(1, 6).Select(s => Cards(Open(Player("WIZ-007"), Conclave1, (ulong)s))).Distinct().Count();
            Assert.True(others > 1);
        }

        [Fact]
        public void DraftsOpenOnlyInScheduledRounds()
        {
            var p = Player("WIZ-001");
            Assert.False(AugmentDraft.TryOpen(p, new RoundInfo(MatchMode.Conclave, 2), 1, ContentAvailability.All, out _));
            Assert.False(AugmentDraft.TryOpen(p, new RoundInfo(MatchMode.Conclave, 13, true), 1, ContentAvailability.All, out _));
            Assert.False(AugmentDraft.TryOpen(p, new RoundInfo(MatchMode.Duel, 3, true), 1, ContentAvailability.All, out _)); // tied-round replay
            Assert.False(AugmentDraft.TryOpen(p, new RoundInfo(MatchMode.Duel, 7), 1, ContentAvailability.All, out _));
            var duel = Open(p, new RoundInfo(MatchMode.Duel, 3), 1);
            Assert.Equal(Rarity.Gold, duel.Rarity);
            duel.Pass(1);
            Assert.Equal(Rarity.Prismatic, Open(p, new RoundInfo(MatchMode.Duel, 6), 1).Rarity);
        }
    }
}
