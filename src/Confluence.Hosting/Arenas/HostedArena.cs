using System;
using System.Threading;
using Confluence.Arena.Generation;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Match;
using MatchArenaRequest = Confluence.Match.ArenaRequest;

namespace Confluence.Hosting.Arenas
{
    /// <summary>
    /// One released round arena held by the host. Every pair of the pass fights in its own deep copy
    /// (<see cref="CreateCopy"/>), so destruction never leaks between copies (spec L34). The master layout is never
    /// handed out for mutation.
    /// </summary>
    public sealed class HostedArena
    {
        private readonly ArenaLayout _layout;
        private readonly Lazy<ArenaNav> _navigation;

        internal HostedArena(string arenaId, MatchArenaRequest request, ArenaRecipe recipe, GeneratedArena generated,
            ArenaPreview preview, ArenaPreviewInfo publicPreview)
        {
            ArenaId = arenaId;
            Request = request;
            Recipe = recipe;
            _layout = generated.Layout;
            LayoutHash = generated.LayoutHash;
            UsedFallback = generated.UsedFallback;
            Attempts = generated.Attempts;
            Preview = preview;
            PublicPreview = publicPreview;
            // Built once from the initial layout and shared read-only by every bot of the pass (queries are thread-safe).
            _navigation = new Lazy<ArenaNav>(() => ArenaNav.Build(_layout), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string ArenaId { get; }

        /// <summary>Match's request for the pass (round, attempt, diameter, recorded seed).</summary>
        public MatchArenaRequest Request { get; }

        /// <summary>What regenerates this layout exactly (replays, L427).</summary>
        public ArenaRecipe Recipe { get; }

        /// <summary>Gameplay fingerprint of the layout (ArenaLayoutHash).</summary>
        public ulong LayoutHash { get; }

        public bool UsedFallback { get; }
        public int Attempts { get; }

        /// <summary>The preview handed to Match (reveal events, L44).</summary>
        public ArenaPreview Preview { get; }

        /// <summary>The full public preview for clients (L329).</summary>
        public ArenaPreviewInfo PublicPreview { get; }

        public int Diameter => _layout.Diameter;
        public string BiomeId => _layout.BiomeId;

        /// <summary>Navigation over the initial layout with props as obstacles (bots, L381).</summary>
        public ArenaNav Navigation => _navigation.Value;

        /// <summary>A deep copy for one pair's duel (L34).</summary>
        public ArenaLayout CreateCopy() => _layout.Clone();

        /// <summary>Read-only access for previews and tests; never mutate it.</summary>
        internal ArenaLayout Master => _layout;
    }
}
