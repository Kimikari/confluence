using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Confluence.Arena;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Hosting.Arenas;
using Confluence.Net.Game.Wire;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// Rebuilds a pass's public arena layout from the recipe in its <see cref="ArenaMessage"/> (generation is
    /// deterministic, spec L316, L427) on a worker thread, and verifies the server's layout hash; a mismatch means an
    /// incompatible build (the unsupported-version state, L427) and the layout is not used. Layouts are cached by recipe
    /// within the process (identical inputs give identical layouts, so clients in one process may share them).
    /// </summary>
    public sealed class ClientArenas
    {
        private static readonly ConcurrentDictionary<string, Task<ArenaLayout>> SharedCache = new ConcurrentDictionary<string, Task<ArenaLayout>>();

        private readonly Catalogue _catalogue;
        private readonly ArenaGenerator _generator;
        private readonly ConcurrentDictionary<string, Task<ArenaLayout>> _byArena = new ConcurrentDictionary<string, Task<ArenaLayout>>();

        public ClientArenas(Catalogue catalogue, ArenaGenerator generator)
        {
            _catalogue = catalogue;
            _generator = generator;
        }

        /// <summary>Layout hash mismatches seen (incompatible builds).</summary>
        public int HashMismatches { get; private set; }

        public bool CanGenerate => _catalogue != null && _generator != null;

        /// <summary>Starts regenerating the arena of <paramref name="message"/> (no-op when already known).</summary>
        public void Prepare(ArenaMessage message)
        {
            if (!CanGenerate || message == null || message.ArenaId == null) return;
            var recipe = new ArenaRecipe(message.RecipeSeed, message.Round, message.Diameter, message.BiomeId, message.AvailablePropIds);
            string key = recipe.CanonicalText();
            ulong expected = message.LayoutHash;
            Task<ArenaLayout> task = SharedCache.GetOrAdd(key, _ => Task.Run(() => _generator.Generate(recipe.ToGeneratorRequest(_catalogue)).Layout));
            _byArena[message.ArenaId] = task.ContinueWith(t =>
            {
                ArenaLayout layout = t.Result;
                if (ArenaLayoutHash.Compute(layout) != expected)
                {
                    HashMismatches++;
                    return null;
                }

                return layout;
            }, TaskScheduler.Default);
        }

        /// <summary>The verified layout, or null while it is still being generated (or failed verification).</summary>
        public ArenaLayout TryGet(string arenaId)
        {
            if (arenaId == null || !_byArena.TryGetValue(arenaId, out Task<ArenaLayout> task)) return null;
            if (!task.IsCompleted) return null;
            return task.Status == TaskStatus.RanToCompletion ? task.Result : null;
        }

        /// <summary>Waits for a layout (tests and tools only; the client loop never blocks on it).</summary>
        public ArenaLayout Wait(string arenaId, TimeSpan timeout)
        {
            if (arenaId == null || !_byArena.TryGetValue(arenaId, out Task<ArenaLayout> task)) return null;
            return task.Wait(timeout) && task.Status == TaskStatus.RanToCompletion ? task.Result : null;
        }
    }
}
