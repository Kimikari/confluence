using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>Art-only dressing of one module instance (pipeline step 4, spec L319): never touches collision or state.</summary>
    public struct CosmeticVariant
    {
        public int ModuleIndex;
        public int ArtVariant;
        public int Palette;
    }

    /// <summary>A released arena: a validated layout (generated or prevalidated fallback) and its art dressing.</summary>
    public sealed class GeneratedArena
    {
        internal GeneratedArena(ArenaLayout layout, List<ArenaValidationReport> rejected, List<CosmeticVariant> cosmetics)
        {
            Layout = layout;
            RejectedAttempts = rejected;
            Cosmetics = cosmetics;
            LayoutHash = ArenaLayoutHash.Compute(layout);
        }

        public ArenaLayout Layout { get; }

        /// <summary>Fingerprint of the gameplay layout (equal requests give equal hashes).</summary>
        public ulong LayoutHash { get; }

        /// <summary>Seeds tried before release (1-8), or 8 when the fallback was used.</summary>
        public int Attempts => Layout.Report.AttemptsUsed;

        public bool UsedFallback => Layout.IsFallback;

        /// <summary>Validation reports of the rejected seeds, in attempt order.</summary>
        public IReadOnlyList<ArenaValidationReport> RejectedAttempts { get; }

        public IReadOnlyList<CosmeticVariant> Cosmetics { get; }
    }

    /// <summary>
    /// Server-authoritative arena generation (spec §7, L302-L335). Pipeline: pick size, biome, environment rule, and
    /// recorded seed from the match's Arena stream (L316); assemble the mirrored skeleton, modules, cover, biome
    /// sources, props, spawns, central destination, and lighting (L317-L320); validate (L321-L322); retry up to eight
    /// seeds and then release the prevalidated fallback for the size and biome (L323). Generation reads no player
    /// build or standing (L325). Thread-safe: concurrent calls share only immutable content and the fallback cache.
    /// </summary>
    public sealed class ArenaGenerator
    {
        private static readonly ulong FallbackSalt = SeedHash.Of("confluence.arena.fallback");
        private static readonly ulong CosmeticSalt = SeedHash.Of("confluence.arena.cosmetic");

        /// <summary>Fallback seeds searched per size and biome (tests prove the first valid one is found well before this).</summary>
        public const int FallbackSearchLimit = 64;

        private readonly ConcurrentDictionary<string, ArenaLayout> _fallbacks = new ConcurrentDictionary<string, ArenaLayout>(StringComparer.Ordinal);

        public ArenaGenerator(Catalogue catalogue)
            : this(new ArenaContent(catalogue))
        {
        }

        public ArenaGenerator(ArenaContent content)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public ArenaContent Content { get; }

        public GeneratedArena Generate(ArenaRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            int diameter = request.Diameter != 0 ? request.Diameter : ArenaSchedule.DiameterForRound(request.MatchSeed, request.Round);
            if (!Schedules.IsValidArenaDiameter(diameter))
            {
                throw new ArgumentException("Arena diameters are 24, 32, 40, or 48 m (spec L306).", nameof(request));
            }

            BiomeDef biome = request.BiomeId != null
                ? Content.Catalogue.Biome(request.BiomeId)
                : ArenaSchedule.BiomeForRound(Content.Catalogue, request.MatchSeed, request.Round, diameter, request.Biomes);
            if (biome == null)
            {
                throw new InvalidOperationException("No available biome supports a " + diameter + " m arena.");
            }

            var rejected = new List<ArenaValidationReport>();
            var validator = new ArenaValidator(Content.Modules) { FailFast = true, MinPropDefinitions = MinPropDefinitions(request.Props) };
            for (int attempt = 0; attempt < GlobalRules.Arena.MaxRetrySeeds; attempt++)
            {
                ulong seed = ArenaSchedule.AttemptSeed(request.MatchSeed, request.Round, attempt);
                ArenaLayout layout = Assemble(seed, diameter, biome, request.Props);
                layout.Round = request.Round;
                ArenaValidationReport report = validator.Validate(layout);
                report.AttemptsUsed = attempt + 1;
                layout.Report = report;
                if (report.Valid)
                {
                    return new GeneratedArena(layout, rejected, Dress(layout));
                }

                rejected.Add(report);
            }

            ArenaLayout fallback = Fallback(diameter, biome, request.Props);
            fallback.Round = request.Round;
            fallback.Report.AttemptsUsed = GlobalRules.Arena.MaxRetrySeeds;
            return new GeneratedArena(fallback, rejected, Dress(fallback));
        }

        /// <summary>One unvalidated attempt from a recorded seed (deterministic; for tools, tests, and replays).</summary>
        public ArenaLayout Assemble(ulong seed, int diameter, BiomeDef biome, IContentAvailability props = null, bool conservative = false, List<string> notes = null)
        {
            var assembler = new ArenaAssembler(Content, seed, diameter, biome, props ?? ContentAvailability.All, conservative);
            ArenaLayout layout = assembler.Build();
            notes?.AddRange(assembler.Notes);
            return layout;
        }

        /// <summary>Validates a layout with the full report (every check, no fail-fast).</summary>
        public ArenaValidationReport Validate(ArenaLayout layout)
        {
            return new ArenaValidator(Content.Modules).Validate(layout);
        }

        /// <summary>
        /// The prevalidated fallback layout for a size and biome (spec L323, L455 "fallback seeds"): the first
        /// conservative fallback seed that validates. Layouts for the full prop pool are cached; callers receive copies.
        /// </summary>
        public ArenaLayout Fallback(int diameter, BiomeDef biome, IContentAvailability props = null)
        {
            props = props ?? ContentAvailability.All;
            string key = diameter + "/" + biome.Id;
            if (ReferenceEquals(props, ContentAvailability.All) && _fallbacks.TryGetValue(key, out ArenaLayout cached))
            {
                return cached.Clone();
            }

            var validator = new ArenaValidator(Content.Modules) { MinPropDefinitions = MinPropDefinitions(props) };
            for (int k = 0; k < FallbackSearchLimit; k++)
            {
                ulong seed = FallbackSeed(diameter, biome.Id, k);
                ArenaLayout layout = Assemble(seed, diameter, biome, props, conservative: true);
                ArenaValidationReport report = validator.Validate(layout);
                if (!report.Valid)
                {
                    continue;
                }

                report.AttemptsUsed = GlobalRules.Arena.MaxRetrySeeds;
                layout.Report = report;
                layout.FallbackId = "fallback:" + diameter + ":" + biome.Id + ":" + k;
                if (ReferenceEquals(props, ContentAvailability.All))
                {
                    _fallbacks.TryAdd(key, layout);
                }

                return layout.Clone();
            }

            // Never release an invalid arena (spec L323). Tests prove every size and biome has a fallback.
            throw new InvalidOperationException("No valid fallback layout for " + diameter + " m " + biome.Id + ".");
        }

        /// <summary>Prop definitions an arena must hold: three, or all of them when the filter offers fewer (spec L662).</summary>
        public int MinPropDefinitions(IContentAvailability props)
        {
            props = props ?? ContentAvailability.All;
            int available = 0;
            for (int i = 0; i < Content.Props.Count; i++)
            {
                if (props.IsAvailable(Content.Props[i].Id))
                {
                    available++;
                }
            }

            return Math.Min(GlobalRules.Arena.PropDefinitionsMin, available);
        }

        /// <summary>Recorded seed of fallback k for a size and biome (independent of any match).</summary>
        public static ulong FallbackSeed(int diameter, string biomeId, int k)
        {
            return SeedHash.Combine(FallbackSalt, (ulong)diameter, SeedHash.Of(biomeId), (ulong)k);
        }

        /// <summary>
        /// Art variants per module instance, drawn from a sub-stream of the layout seed so dressing can never change
        /// a gameplay choice. Mirrored modules draw independently: visual symmetry is optional (spec L308).
        /// </summary>
        public static List<CosmeticVariant> Dress(ArenaLayout layout)
        {
            var rng = new Rng(SeedHash.Combine(layout.Seed, CosmeticSalt));
            var result = new List<CosmeticVariant>(layout.Modules.Count);
            for (int i = 0; i < layout.Modules.Count; i++)
            {
                result.Add(new CosmeticVariant { ModuleIndex = i, ArtVariant = rng.NextInt(4), Palette = rng.NextInt(3) });
            }

            return result;
        }
    }
}
