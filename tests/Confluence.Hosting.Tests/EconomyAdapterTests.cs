using System.Linq;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Content;
using Confluence.Hosting.Messages;
using Confluence.Match;

namespace Confluence.Hosting.Tests
{
    /// <summary>The Match economy seam over Confluence.Economy, the match-start floors (D-010), and Laboratory labels.</summary>
    public sealed class EconomyAdapterTests
    {
        private static readonly string[] ProofPoolIds = { "AUG-145", "AUG-169", "AUG-175", "EQP-051", "EQP-066", "EQP-096" };

        [Fact]
        public void Standard_Rules_Refuse_To_Start_When_The_Implemented_Pools_Break_The_Floors()
        {
            // Only the six Stage 1b proof records (three tier-1 equipment, three Silver augments) carry [ContentBehavior],
            // far below the floors (D-010), so the implemented pools hold exactly those.
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, LaboratoryOverrides.None));
            // Content stages add records to the pools; the proof records stay in them (the equipment floor still fails).
            Assert.Subset(new System.Collections.Generic.HashSet<string>(host.Content.PoolIds), new System.Collections.Generic.HashSet<string>(ProofPoolIds));
            HostStartResult start = host.Start();
            Assert.Equal(HostStartCode.RefusedEconomyFloors, start.Code);
            Assert.False(host.IsStarted);
            Assert.NotEmpty(start.Gaps);
            Assert.False(start.Floors.Equipment.Meets);
            Assert.Contains(host.Outbox.Drain(), m => m is HostNoticeMessage n && n.Text.Contains("refused"));
            Assert.Equal(CommandCode.MatchNotRunning, host.Submit(PlayerCommand.Ready(0, 1)).Code);
        }

        [Fact]
        public void The_Floor_Bypass_Is_A_Labeled_Laboratory_Override_In_The_Fingerprint()
        {
            var bypass = new LaboratoryOverrides(bypassEconomyFloors: true);
            MatchHost standard = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, LaboratoryOverrides.None));
            MatchHost lab = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, bypass));
            MatchHost whole = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, HostFixture.WholeCatalogue));

            Assert.Equal(standard.MatchFingerprint, lab.MatchFingerprint); // same match rules
            Assert.NotEqual(standard.Fingerprint, lab.Fingerprint);         // but never the same host fingerprint (L71)
            Assert.NotEqual(lab.Fingerprint, whole.Fingerprint);
            Assert.Equal("laboratory:bypass-economy-floors", bypass.CanonicalText());
            Assert.Equal("standard", LaboratoryOverrides.None.CanonicalText());

            HostStartResult start = lab.Start();
            Assert.True(start.Started);
            Assert.NotEmpty(start.Gaps);
            Assert.Contains(lab.Log.Lines, l => l.StartsWith("warn:") && l.Contains("bypass-economy-floors"));
            Assert.False(lab.CountsForRating);

            // With the small implemented pools the shop offers only implemented equipment, else Save Crowns (L208).
            Assert.All(lab.Economy.OpenShopOf(0).Cards, c => Assert.True(c.State == ShopCardState.SaveCrowns
                || (c.Definition != null && ProofPoolIds.Contains(c.Definition.Id)), c.ToString()));
        }

        [Fact]
        public void Each_Player_Shops_And_Drafts_On_Their_Own_Offer_And_Draft_Streams()
        {
            ulong seed = HostFixture.SmallDuelSeed;
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(seed, wizard0: "WIZ-002", wizard1: "WIZ-002"));
            Assert.True(host.Start().Started);

            // The shop is the Economy's own object on the Offer stream of (round, player slot) (L435, D-084).
            for (int p = 0; p < 2; p++)
            {
                var fresh = new PlayerEconomy(HostFixture.Catalogue, "WIZ-002", p);
                fresh.GrantCrowns(GlobalRules.Economy.FirstShopIncome);
                var expected = new EquipmentShop(fresh, new RoundInfo(MatchMode.Duel, 1), seed, host.Content.Pools);
                Assert.Equal(expected.Cards.Select(c => c.Definition?.Id), host.Economy.OpenShopOf(p).Cards.Select(c => c.Definition?.Id));
            }

            Assert.NotEqual(host.Economy.OpenShopOf(0).Cards.Select(c => c.Definition?.Id), host.Economy.OpenShopOf(1).Cards.Select(c => c.Definition?.Id));

            host.Submit(PlayerCommand.Ready(0, 1));
            host.Submit(PlayerCommand.Ready(1, 1));
            host.Tick();
            Assert.Equal(MatchPhase.Draft, host.Flow.Phase);
            for (int p = 0; p < 2; p++)
            {
                var fresh = new PlayerEconomy(HostFixture.Catalogue, "WIZ-002", p);
                Assert.True(AugmentDraft.TryOpen(fresh, new RoundInfo(MatchMode.Duel, 1), seed, host.Content.Pools, out AugmentDraft expected));
                Assert.Equal(expected.Cards.Select(c => c.AugmentId), host.Economy.OpenDraftOf(p).Cards.Select(c => c.AugmentId));
                Assert.Equal(Rarity.Silver, host.Economy.OpenDraftOf(p).Rarity);
            }
        }

        [Fact]
        public void Finalized_Loadout_Is_The_Round_Snapshot_And_Draft_Timeouts_Choose_Nothing()
        {
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed));
            Assert.True(host.Start().Started);
            ShopCard card = host.Economy.OpenShopOf(0).Cards.First(c => c.IsPurchasable);
            Assert.True(host.Submit(PlayerCommand.Buy(0, 1, card.Position)).Succeeded);

            // Nobody readies: the shop and the draft run their full timers (L45), and the draft times out (L229).
            HostFixture.RunUntilPhase(host, MatchPhase.Draft, host.Flow.Timings.ShopTicks + 2);
            Assert.True(host.Economy.Player(0).Loadout.IsFrozen);
            HostFixture.RunUntilPhase(host, MatchPhase.Alcove, host.Flow.Timings.DraftTicks + 2);
            Assert.Equal(DraftOutcome.TimedOut, host.Economy.CurrentDraftOf(0).Outcome);
            Assert.Empty(host.Economy.Player(0).Loadout.Augments);
            Assert.False(host.Economy.Player(0).Loadout.IsFrozen);

            PublicLoadout finalized = host.Flow.Player(0).CurrentLoadout;
            Assert.Contains(card.Definition.Id, finalized.EquipmentIds);
            Assert.Equal(host.Economy.RoundLoadout(0).EquippedSpellIds, finalized.SpellIds);
            Assert.NotSame(host.Economy.Player(0).Loadout, host.Economy.RoundLoadout(0));
            Assert.NotNull(host.Economy.LastDormancy(0));

            // The duel was created from that snapshot (L46).
            Assert.Contains(host.Duels.Running.Single().World.Player(host.Duels.Running.Single().SideOf(0)).Setup.Equipment, e => e.Id == card.Definition.Id);
        }
    }
}
