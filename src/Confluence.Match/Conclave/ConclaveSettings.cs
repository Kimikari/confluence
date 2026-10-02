using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Confluence.Core.Content;
using Confluence.Core.Json;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Match
{
    /// <summary>
    /// Lobby rules of a Conclave (spec L66-L67, L71). Ranked and Unranked use the fixed standard rules; Custom hosts may
    /// set player count 2-8, shop timer, starting Crowns, allowed catalogue entries, spectator delay, and an arena seed
    /// rotation. The settings fingerprint is shown before joining, and only standard ranked settings produce rating.
    /// </summary>
    public sealed class ConclaveSettings
    {
        private const string FingerprintPrefix = "confluence.conclave.settings.v1";

        public ConclaveSettings(ConclavePreset preset, int playerCount, float shopSec, int startingCrowns, float spectatorDelaySec,
            IEnumerable<string> allowedContentIds = null, IEnumerable<ulong> arenaSeedRotation = null)
        {
            Preset = preset;
            PlayerCount = playerCount;
            ShopSec = shopSec;
            StartingCrowns = startingCrowns;
            SpectatorDelaySec = spectatorDelaySec;
            if (allowedContentIds != null)
            {
                var ids = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string id in allowedContentIds)
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                AllowedContentIds = new ReadOnlyCollection<string>(new List<string>(ids));
            }

            ArenaSeedRotation = new ReadOnlyCollection<ulong>(arenaSeedRotation == null ? new List<ulong>() : new List<ulong>(arenaSeedRotation));
        }

        /// <summary>The fixed competition rules of Ranked Conclave (spec L67): eight players, 35 s shop, 600 Crowns, 15 s delay, all content.</summary>
        public static ConclaveSettings Ranked { get; } = Standard(ConclavePreset.Ranked);

        /// <summary>The complete main rules without ladder impact (spec L66).</summary>
        public static ConclaveSettings Unranked { get; } = Standard(ConclavePreset.Unranked);

        public static ConclaveSettings Custom(int playerCount, float shopSec = GlobalRules.Round.ShopSec, int startingCrowns = GlobalRules.Economy.FirstShopIncome,
            float spectatorDelaySec = GlobalRules.Match.SpectatorDelayMinSec, IEnumerable<string> allowedContentIds = null, IEnumerable<ulong> arenaSeedRotation = null) =>
            new ConclaveSettings(ConclavePreset.Custom, playerCount, shopSec, startingCrowns, spectatorDelaySec, allowedContentIds, arenaSeedRotation);

        public ConclavePreset Preset { get; }

        /// <summary>Lobby size: 8 for Ranked and Unranked (L34), 2-8 for Custom (L71).</summary>
        public int PlayerCount { get; }

        public float ShopSec { get; }

        /// <summary>Income before the first shop; the standard is 600 (L193).</summary>
        public int StartingCrowns { get; }

        /// <summary>Spectator delay while the match is active; at least 15 s (L391).</summary>
        public float SpectatorDelaySec { get; }

        /// <summary>Allowed catalogue IDs in ordinal order, or null when every gameplay definition is allowed (L67).</summary>
        public IReadOnlyList<string> AllowedContentIds { get; }

        /// <summary>
        /// Host arena seed rotation: when non-empty, round r generates from entry (r-1) mod count, and replays derive a new
        /// seed from it. Empty means the standard per-round seeds derived from the match seed (L34 "the seed changes between rounds").
        /// </summary>
        public IReadOnlyList<ulong> ArenaSeedRotation { get; }

        /// <summary>True when every rule equals the standard main rules.</summary>
        public bool UsesStandardRules =>
            PlayerCount == GlobalRules.Match.LobbySize && ShopSec == GlobalRules.Round.ShopSec &&
            StartingCrowns == GlobalRules.Economy.FirstShopIncome && SpectatorDelaySec == GlobalRules.Match.SpectatorDelayMinSec &&
            AllowedContentIds == null && ArenaSeedRotation.Count == 0;

        /// <summary>Custom rules never appear as standard ranked results (L71).</summary>
        public bool CountsForRating => Preset == ConclavePreset.Ranked && UsesStandardRules;

        public MatchTimings Timings => MatchTimings.WithShopSeconds(ShopSec);

        public int SpectatorDelayTicks => Ticks.FromSeconds(SpectatorDelaySec);

        /// <summary>Content gate for the shop and draft pools; intersect it with implemented content (D-010).</summary>
        public IContentAvailability Availability() =>
            AllowedContentIds == null ? ContentAvailability.All : ContentAvailability.FromIds(AllowedContentIds);

        /// <summary>Canonical text the fingerprint hashes; every rule-relevant setting appears in a fixed order.</summary>
        public string CanonicalText()
        {
            var sb = new StringBuilder(FingerprintPrefix);
            sb.Append(";preset=").Append(Preset.ToString().ToLowerInvariant());
            sb.Append(";players=").Append(PlayerCount);
            sb.Append(";shop=").Append(JsonWriter.FormatNumber(ShopSec));
            sb.Append(";crowns=").Append(StartingCrowns);
            sb.Append(";spectator=").Append(JsonWriter.FormatNumber(SpectatorDelaySec));
            sb.Append(";content=").Append(AllowedContentIds == null ? "all" : string.Join(",", AllowedContentIds));
            sb.Append(";seeds=");
            if (ArenaSeedRotation.Count == 0) sb.Append("derived");
            for (int i = 0; i < ArenaSeedRotation.Count; i++) sb.Append(i == 0 ? "" : ",").Append(ArenaSeedRotation[i].ToString("X16"));
            return sb.ToString();
        }

        /// <summary>Settings fingerprint shown before joining (L71): 16 hex digits of a stable hash.</summary>
        public string Fingerprint => SeedHash.Of(CanonicalText()).ToString("X16");

        /// <summary>Rule problems, empty when valid. With a catalogue, allowed IDs must name real records.</summary>
        public IReadOnlyList<string> Validate(Catalogue catalogue = null)
        {
            var errors = new List<string>();
            if (Preset != ConclavePreset.Custom && !UsesStandardRules)
            {
                errors.Add(Preset + " Conclave uses the fixed standard rules (L66-L67).");
            }

            if (PlayerCount < GlobalRules.Match.CustomMinPlayers || PlayerCount > GlobalRules.Match.CustomMaxPlayers)
            {
                errors.Add("Player count must be " + GlobalRules.Match.CustomMinPlayers + "-" + GlobalRules.Match.CustomMaxPlayers + " (L71).");
            }

            if (!(ShopSec >= MatchRules.CustomShopSecMin && ShopSec <= MatchRules.CustomShopSecMax))
            {
                errors.Add("Shop timer must be " + MatchRules.CustomShopSecMin + "-" + MatchRules.CustomShopSecMax + " s.");
            }

            if (StartingCrowns < 0 || StartingCrowns > MatchRules.CustomStartingCrownsMax)
            {
                errors.Add("Starting Crowns must be 0-" + MatchRules.CustomStartingCrownsMax + ".");
            }

            if (!(SpectatorDelaySec >= GlobalRules.Match.SpectatorDelayMinSec && SpectatorDelaySec <= MatchRules.SpectatorDelayMaxSec))
            {
                errors.Add("Spectator delay must be at least " + GlobalRules.Match.SpectatorDelayMinSec + " s (L391) and at most " + MatchRules.SpectatorDelayMaxSec + " s.");
            }

            if (AllowedContentIds != null)
            {
                if (AllowedContentIds.Count == 0) errors.Add("An allowed-content list cannot be empty.");
                if (catalogue != null)
                {
                    foreach (string id in AllowedContentIds)
                        if (!catalogue.TryGet(id, out _)) errors.Add("Allowed content " + id + " is not a catalogue record.");
                }
            }

            return errors;
        }

        public void ThrowIfInvalid(Catalogue catalogue = null)
        {
            var errors = Validate(catalogue);
            if (errors.Count > 0) throw new ArgumentException("Invalid Conclave settings: " + string.Join(" ", errors));
        }

        private static ConclaveSettings Standard(ConclavePreset preset) => new ConclaveSettings(preset, GlobalRules.Match.LobbySize,
            GlobalRules.Round.ShopSec, GlobalRules.Economy.FirstShopIncome, GlobalRules.Match.SpectatorDelayMinSec);
    }
}
