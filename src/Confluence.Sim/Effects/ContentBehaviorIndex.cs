using System;
using System.Collections.Generic;
using System.Reflection;
using Confluence.Core.Content;

namespace Confluence.Sim.Effects
{
    /// <summary>Where a duel finds the behaviors of its loadout and biome (catalogue id to behavior instance).</summary>
    public interface IEffectContentSource
    {
        bool TryGetAugment(string id, out IAugmentBehavior behavior);

        bool TryGetEquipment(string id, out IEquipmentBehavior behavior);

        bool TryGetBiomeRule(string id, out IBiomeRuleBehavior behavior);
    }

    /// <summary>
    /// The augment, equipment, and biome behaviors found by scanning assemblies for <see cref="ContentBehaviorAttribute"/>
    /// classes that implement the matching contract (ordinal type order, so the result never depends on reflection
    /// order). Classes marked for other record types are ignored here; two classes claiming one id are an error.
    /// </summary>
    public sealed class ContentBehaviorIndex : IEffectContentSource
    {
        private static readonly Lazy<ContentBehaviorIndex> DefaultIndex =
            new Lazy<ContentBehaviorIndex>(() => Scan(typeof(ContentBehaviorIndex).Assembly));

        private readonly Dictionary<string, object> _byId = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<string> _ids = new List<string>();

        private ContentBehaviorIndex()
        {
        }

        /// <summary>The behaviors shipped in Confluence.Sim.</summary>
        public static ContentBehaviorIndex Default => DefaultIndex.Value;

        /// <summary>An index without behaviors (duels that must run the bare rules).</summary>
        public static ContentBehaviorIndex Empty => new ContentBehaviorIndex();

        /// <summary>Indexed record ids in ordinal order.</summary>
        public IReadOnlyList<string> Ids => _ids;

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);

        public static ContentBehaviorIndex Scan(params Assembly[] assemblies)
        {
            if (assemblies == null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            var index = new ContentBehaviorIndex();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                var types = new List<Type>(assembly.GetTypes());
                types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                foreach (Type type in types)
                {
                    foreach (ContentBehaviorAttribute attribute in type.GetCustomAttributes<ContentBehaviorAttribute>(false))
                    {
                        index.Register(attribute.Id, type);
                    }
                }
            }

            index._ids.Sort(StringComparer.Ordinal);
            return index;
        }

        public bool TryGetAugment(string id, out IAugmentBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetEquipment(string id, out IEquipmentBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetBiomeRule(string id, out IBiomeRuleBehavior behavior) => TryGet(id, out behavior);

        private void Register(string id, Type type)
        {
            if (!typeof(IAugmentBehavior).IsAssignableFrom(type) && !typeof(IEquipmentBehavior).IsAssignableFrom(type)
                && !typeof(IBiomeRuleBehavior).IsAssignableFrom(type))
            {
                return; // spells, transformations, kits, props: indexed by the content registry of their stage
            }

            if (type.IsAbstract || !type.IsClass)
            {
                throw new InvalidOperationException(type.FullName + " is marked [ContentBehavior(\"" + id + "\")] but is not a concrete class.");
            }

            if (_byId.ContainsKey(id))
            {
                throw new InvalidOperationException("Two behaviors claim " + id + ": " + _byId[id].GetType().FullName + " and " + type.FullName + ".");
            }

            ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException(type.FullName + " (" + id + ") needs a public parameterless constructor.");
            _byId.Add(id, ctor.Invoke(null));
            _ids.Add(id);
        }

        private bool TryGet<T>(string id, out T behavior) where T : class
        {
            if (id != null && _byId.TryGetValue(id, out object found) && found is T typed)
            {
                behavior = typed;
                return true;
            }

            behavior = null;
            return false;
        }
    }
}
