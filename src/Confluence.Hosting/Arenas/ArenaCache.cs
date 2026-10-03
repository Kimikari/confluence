using System;
using System.Collections.Concurrent;
using Confluence.Arena.Generation;

namespace Confluence.Hosting.Arenas
{
    /// <summary>
    /// Released arenas keyed by their generator request. Generation is deterministic (equal requests give identical
    /// layouts), so hosts on one server, replays, and tests may share released layouts instead of regenerating them.
    /// Thread-safe; entries are never mutated (consumers receive copies through <see cref="HostedArena"/>).
    /// </summary>
    public sealed class ArenaCache
    {
        private readonly ConcurrentDictionary<string, Lazy<GeneratedArena>> _entries =
            new ConcurrentDictionary<string, Lazy<GeneratedArena>>(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public GeneratedArena GetOrGenerate(string key, Func<GeneratedArena> generate)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (generate == null) throw new ArgumentNullException(nameof(generate));
            return _entries.GetOrAdd(key, _ => new Lazy<GeneratedArena>(generate)).Value;
        }
    }
}
