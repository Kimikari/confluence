using System;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Ranked, Unranked, and Custom Conclave settings with their fingerprint (spec L66-L67, L71, L391).</summary>
    public class ConclaveSettingsTests
    {
        private static readonly Lazy<Catalogue> Catalogue = new Lazy<Catalogue>(() => Core.Content.Catalogue.Load(Core.Content.Catalogue.FindDefaultPath()));

        [Fact]
        public void Ranked_And_Unranked_Use_The_Fixed_Standard_Rules()
        {
            foreach (var settings in new[] { ConclaveSettings.Ranked, ConclaveSettings.Unranked })
            {
                Assert.Empty(settings.Validate());
                Assert.True(settings.UsesStandardRules);
                Assert.Equal(8, settings.PlayerCount);
                Assert.Equal(35f, settings.ShopSec);
                Assert.Equal(600, settings.StartingCrowns);
                Assert.Equal(15f, settings.SpectatorDelaySec);
                Assert.Null(settings.AllowedContentIds); // L67: all gameplay content
            }

            Assert.True(ConclaveSettings.Ranked.CountsForRating);
            Assert.False(ConclaveSettings.Unranked.CountsForRating); // L66: no ladder impact

            // L67: ranked rules are fixed; a ranked lobby with other rules is rejected.
            var tampered = new ConclaveSettings(ConclavePreset.Ranked, 8, 50f, 600, 15f);
            Assert.NotEmpty(tampered.Validate());
            Assert.Throws<ArgumentException>(() => new ConclaveMatch(tampered, 1, new FakeDuelRunner(FakeDuelRunner.LowerIdWins), new FakeEconomy(), new FakeArenaProvider()));
        }

        [Fact]
        public void Custom_Settings_Validate_Their_Ranges()
        {
            Assert.Empty(ConclaveSettings.Custom(2).Validate());
            Assert.Empty(ConclaveSettings.Custom(8, shopSec: 60f, startingCrowns: 2000, spectatorDelaySec: 120f).Validate());
            Assert.NotEmpty(ConclaveSettings.Custom(1).Validate());
            Assert.NotEmpty(ConclaveSettings.Custom(9).Validate());
            Assert.NotEmpty(ConclaveSettings.Custom(4, shopSec: 0f).Validate());
            Assert.NotEmpty(ConclaveSettings.Custom(4, startingCrowns: -1).Validate());
            Assert.NotEmpty(ConclaveSettings.Custom(4, spectatorDelaySec: 10f).Validate()); // L391: at least 15 s
            Assert.NotEmpty(ConclaveSettings.Custom(4, allowedContentIds: new string[0]).Validate());

            // Allowed entries must be catalogue records.
            Assert.Empty(ConclaveSettings.Custom(4, allowedContentIds: new[] { "EQP-001", "AUG-001" }).Validate(Catalogue.Value));
            Assert.NotEmpty(ConclaveSettings.Custom(4, allowedContentIds: new[] { "EQP-001", "EQP-999" }).Validate(Catalogue.Value));
            var gate = ConclaveSettings.Custom(4, allowedContentIds: new[] { "EQP-001" }).Availability();
            Assert.True(gate.IsAvailable("EQP-001"));
            Assert.False(gate.IsAvailable("EQP-002"));
            Assert.True(ConclaveSettings.Unranked.Availability().IsAvailable("EQP-002"));
        }

        [Fact]
        public void The_Fingerprint_Covers_Every_Rule_And_Is_Order_Independent()
        {
            var baseline = ConclaveSettings.Custom(6);
            Assert.Equal(16, baseline.Fingerprint.Length);
            Assert.Equal(baseline.Fingerprint, ConclaveSettings.Custom(6).Fingerprint);

            var variants = new[]
            {
                ConclaveSettings.Custom(5),
                ConclaveSettings.Custom(6, shopSec: 40f),
                ConclaveSettings.Custom(6, startingCrowns: 700),
                ConclaveSettings.Custom(6, spectatorDelaySec: 30f),
                ConclaveSettings.Custom(6, allowedContentIds: new[] { "EQP-001" }),
                ConclaveSettings.Custom(6, arenaSeedRotation: new[] { 5UL }),
                ConclaveSettings.Unranked,
                ConclaveSettings.Ranked,
            };
            var all = variants.Select(v => v.Fingerprint).Concat(new[] { baseline.Fingerprint }).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());

            // Allowed content is a set: listing order and duplicates do not change the rules.
            Assert.Equal(
                ConclaveSettings.Custom(6, allowedContentIds: new[] { "EQP-002", "EQP-001", "EQP-001" }).Fingerprint,
                ConclaveSettings.Custom(6, allowedContentIds: new[] { "EQP-001", "EQP-002" }).Fingerprint);

            // L71: custom rules never count as standard ranked results, even when they equal the standard numbers.
            Assert.False(ConclaveSettings.Custom(8).CountsForRating);
            Assert.True(ConclaveSettings.Custom(8).UsesStandardRules);
        }

        [Fact]
        public void The_Match_Records_The_Fingerprint_And_Applies_Custom_Rules()
        {
            var settings = ConclaveSettings.Custom(3, shopSec: 20f, startingCrowns: 900, arenaSeedRotation: new[] { 11UL, 22UL });
            var h = Harness.ForConclave(settings, 3, FakeDuelRunner.LowerIdWins, readyAfter: null);
            h.RunToEnd();

            Assert.Equal(settings.Fingerprint, h.Match.Events.OfType<MatchStartedEvent>().Single().SettingsFingerprint);
            Assert.Equal(settings.Fingerprint, h.Match.Result.SettingsFingerprint);
            Assert.False(h.Match.Result.CountsForRating);
            Assert.Equal(3, h.Match.Players.Count);

            // Custom shop timer and starting Crowns (L71).
            var shop = h.Phases(1).Single(e => e.Phase == MatchPhase.Shop);
            Assert.Equal(20 * 60, shop.EndTick - shop.Tick);
            Assert.All(h.ShopsIn(1), s => Assert.Equal(900, s.BaselineIncome));

            // Seed rotation: round r generates from entry (r-1) mod count.
            Assert.Equal(11UL, h.Arenas.Requests.Single(r => r.Round == 1 && r.Attempt == 0).ArenaSeed);
            Assert.Equal(22UL, h.Arenas.Requests.Single(r => r.Round == 2 && r.Attempt == 0).ArenaSeed);
            Assert.Equal(11UL, h.Arenas.Requests.Single(r => r.Round == 3 && r.Attempt == 0).ArenaSeed);

            Assert.Equal(15 * 60, settings.SpectatorDelayTicks);
        }

        [Fact]
        public void A_Two_Player_Custom_Conclave_Plays_To_A_Result()
        {
            var h = Harness.ForConclave(2, 4, s => DuelScript.Win(s.Round % 3 == 0 ? 1 : 0));
            h.RunToEnd();
            Assert.All(h.Match.RoundResults, r => Assert.Equal(2, r.Entries.Count));
            Assert.NotEqual(MatchRules.NoPlayer, h.Match.Result.Champion);
            Assert.Equal(new[] { 1.0, 2.0 }, h.Match.Result.Placements.Select(p => p.Position));
        }
    }
}
