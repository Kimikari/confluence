using System;

namespace Confluence.Core.Random
{
    /// <summary>
    /// Independent random streams. Offer, arena, cosmetic, and bot randomness are separate (spec L435), so for
    /// example rendering an extra particle can never change a later shop offer.
    /// </summary>
    public enum RngStream
    {
        Offer,
        Draft,
        Arena,
        Cosmetic,
        Bot,
        Pairing,
        Bracket,
    }

    public static class RngStreams
    {
        /// <summary>
        /// Seed of a stream for one match, round, and sub-index (for example a player slot or card position).
        /// Streams are keyed by a fixed name rather than the enum's ordinal, so reordering the enum never
        /// changes recorded seeds.
        /// </summary>
        public static ulong SeedFor(ulong matchSeed, RngStream stream, int round, int sub = 0)
        {
            unchecked
            {
                return SeedHash.Combine(matchSeed, StreamKey(stream), (ulong)(long)round, (ulong)(long)sub);
            }
        }

        /// <summary>New generator for a stream; equal arguments always produce the same sequence.</summary>
        public static Rng For(ulong matchSeed, RngStream stream, int round, int sub = 0)
        {
            return new Rng(SeedFor(matchSeed, stream, round, sub));
        }

        /// <summary>Stable per-stream key: SeedHash.Of("confluence.rng." + lowercase stream name).</summary>
        public static ulong StreamKey(RngStream stream)
        {
            switch (stream)
            {
                case RngStream.Offer: return OfferKey;
                case RngStream.Draft: return DraftKey;
                case RngStream.Arena: return ArenaKey;
                case RngStream.Cosmetic: return CosmeticKey;
                case RngStream.Bot: return BotKey;
                case RngStream.Pairing: return PairingKey;
                case RngStream.Bracket: return BracketKey;
                default: throw new ArgumentOutOfRangeException(nameof(stream), stream, "Unknown random stream.");
            }
        }

        private static readonly ulong OfferKey = SeedHash.Of("confluence.rng.offer");
        private static readonly ulong DraftKey = SeedHash.Of("confluence.rng.draft");
        private static readonly ulong ArenaKey = SeedHash.Of("confluence.rng.arena");
        private static readonly ulong CosmeticKey = SeedHash.Of("confluence.rng.cosmetic");
        private static readonly ulong BotKey = SeedHash.Of("confluence.rng.bot");
        private static readonly ulong PairingKey = SeedHash.Of("confluence.rng.pairing");
        private static readonly ulong BracketKey = SeedHash.Of("confluence.rng.bracket");
    }
}
