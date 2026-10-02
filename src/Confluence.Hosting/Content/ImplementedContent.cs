using System;
using System.Collections.Generic;
using System.Reflection;
using Confluence.Core.Content;

namespace Confluence.Hosting.Content
{
    /// <summary>
    /// The content registry the host reads: every catalogue record whose behavior class carries
    /// <see cref="ContentBehaviorAttribute"/> in a shipped rule assembly. Only these records may appear in shop, draft,
    /// or arena pools (D-010), so a menu entry is never an empty handler.
    /// </summary>
    public sealed class ImplementedContent : IContentAvailability
    {
        private readonly HashSet<string> _ids;
        private readonly List<string> _sorted;

        private ImplementedContent(IEnumerable<string> ids)
        {
            _ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (!string.IsNullOrEmpty(id)) _ids.Add(id);
            _sorted = new List<string>(_ids);
            _sorted.Sort(StringComparer.Ordinal);
        }

        /// <summary>Every implemented record id, in ordinal order.</summary>
        public IReadOnlyList<string> Ids => _sorted.AsReadOnly();

        public bool IsAvailable(string id) => id != null && _ids.Contains(id);

        /// <summary>Implemented records of one type, in ordinal order.</summary>
        public List<string> IdsOf(ContentType type)
        {
            var result = new List<string>();
            foreach (string id in _sorted)
                if (ContentIds.TryParse(id, out ContentType t, out _) && t == type) result.Add(id);
            return result;
        }

        public bool HasAny(ContentType type) => IdsOf(type).Count > 0;

        /// <summary>A registry with exactly these ids (tests, tools).</summary>
        public static ImplementedContent FromIds(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            return new ImplementedContent(ids);
        }

        /// <summary>Scans the shipped rule assemblies the host links: Core, Reactions, Casting, Arena, Economy, Sim, Match, Hosting.</summary>
        public static ImplementedContent FromShippedAssemblies()
        {
            return Scan(new[]
            {
                typeof(Catalogue).Assembly,
                typeof(Confluence.Reactions.ReactionWorld).Assembly,
                typeof(Confluence.Casting.CastMachine).Assembly,
                typeof(Confluence.Arena.Generation.ArenaGenerator).Assembly,
                typeof(Confluence.Economy.PlayerEconomy).Assembly,
                typeof(Confluence.Sim.Duel.DuelWorld).Assembly,
                typeof(Confluence.Match.MatchFlow).Assembly,
                typeof(ImplementedContent).Assembly,
            });
        }

        /// <summary>Collects [ContentBehavior] ids from the classes of the given assemblies.</summary>
        public static ImplementedContent Scan(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            var ids = new List<string>();
            var seen = new HashSet<Assembly>();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null || !seen.Add(assembly)) continue;
                foreach (Type type in LoadableTypes(assembly))
                {
                    if (type == null || !type.IsClass) continue;
                    foreach (ContentBehaviorAttribute attribute in type.GetCustomAttributes<ContentBehaviorAttribute>(false))
                        ids.Add(attribute.Id);
                }
            }

            return new ImplementedContent(ids);
        }

        private static Type[] LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types;
            }
        }
    }
}
