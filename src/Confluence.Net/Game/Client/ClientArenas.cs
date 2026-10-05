using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// different float runtime or build; the regenerated layout is then not used and the exact layout is downloaded from the
    /// server instead (<see cref="ArenaLayoutMessage"/>, research §2.7.1). Only when the download fails too does the client
    /// show the unsupported-version state (L427). Layouts are cached by recipe within the process (identical inputs give
    /// identical layouts, so clients in one process may share them).
    /// </summary>
    public sealed class ClientArenas
    {
        private static readonly ConcurrentDictionary<string, Task<ArenaLayout>> SharedCache = new ConcurrentDictionary<string, Task<ArenaLayout>>();

        private readonly Catalogue _catalogue;
        private readonly ArenaGenerator _generator;
        private readonly ConcurrentDictionary<string, Task<ArenaLayout>> _byArena = new ConcurrentDictionary<string, Task<ArenaLayout>>();
        private readonly ConcurrentDictionary<string, ulong> _expected = new ConcurrentDictionary<string, ulong>();
        private readonly ConcurrentDictionary<string, bool> _requested = new ConcurrentDictionary<string, bool>();

        public ClientArenas(Catalogue catalogue, ArenaGenerator generator)
        {
            _catalogue = catalogue;
            _generator = generator;
        }

        /// <summary>Layout hash mismatches seen (incompatible builds).</summary>
        public int HashMismatches { get; private set; }

        public bool CanGenerate => _catalogue != null && _generator != null;

        /// <summary>Always download layouts instead of regenerating them (a client without the generator).</summary>
        public bool AlwaysDownload { get; set; }

        /// <summary>Layouts received from the server and verified.</summary>
        public int Downloads { get; private set; }

        /// <summary>Downloaded layouts that failed to decode or verify (the unsupported-version state, L427).</summary>
        public int DownloadFailures { get; private set; }

        /// <summary>Starts regenerating the arena of <paramref name="message"/> (no-op when already known).</summary>
        public void Prepare(ArenaMessage message)
        {
            if (message == null || message.ArenaId == null) return;
            _expected[message.ArenaId] = message.LayoutHash;
            if (!CanGenerate || AlwaysDownload) return; // DownloadsNeeded lists it
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

        /// <summary>
        /// Announced arenas this client has no verified layout for and will not get by regeneration (a hash mismatch, no
        /// generator, or <see cref="AlwaysDownload"/>), not yet requested; each id is listed once.
        /// </summary>
        public List<string> TakeDownloadsNeeded()
        {
            var ids = new List<string>();
            foreach (KeyValuePair<string, ulong> e in _expected)
            {
                if (_requested.ContainsKey(e.Key)) continue;
                bool needed;
                if (!_byArena.TryGetValue(e.Key, out Task<ArenaLayout> task)) needed = true;
                else needed = task.IsCompleted && (task.Status != TaskStatus.RanToCompletion || task.Result == null);
                if (!needed) continue;
                _requested[e.Key] = true;
                ids.Add(e.Key);
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Accepts a downloaded layout after verifying it against the announced hash.</summary>
        public bool Accept(ArenaLayoutMessage message)
        {
            if (message?.ArenaId == null || !_expected.TryGetValue(message.ArenaId, out ulong expected)) return false;
            ArenaLayout layout;
            try
            {
                layout = LayoutCodec.Decode(message.Layout);
            }
            catch (Confluence.Net.Serialization.NetReadException)
            {
                DownloadFailures++;
                return false;
            }

            if (message.LayoutHash != expected || ArenaLayoutHash.Compute(layout) != expected)
            {
                DownloadFailures++;
                return false;
            }

            _byArena[message.ArenaId] = Task.FromResult(layout);
            Downloads++;
            return true;
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
