using System;
using System.Collections.Generic;
using System.Reflection;
using Confluence.Core.Content;

namespace Confluence.Sim.Content
{
    /// <summary>
    /// Every catalogue behavior found by scanning assemblies for classes marked <see cref="ContentBehaviorAttribute"/>
    /// (docs/ARCHITECTURE.md "Content model"). Each marked class must have a public parameterless constructor and
    /// implement the behavior interface matching its record type: SPL- <see cref="ISpellBehavior"/>; AUG- transformation
    /// <see cref="ISpellTransform"/> or shared <see cref="IAugmentBehavior"/>; WIZ- <see cref="IWizardKitBehavior"/>;
    /// EQP- <see cref="IEquipmentBehavior"/>; BIO- <see cref="IBiomeRuleBehavior"/>; PROP- <see cref="IPropBehavior"/>.
    /// One instance per record id, shared by all duels. <see cref="Availability"/> exposes the registered ids to the
    /// draft and shop pools, so only implemented records are offered (DECISIONS D-010).
    /// </summary>
    public sealed class ContentRegistry
    {
        private static readonly Lazy<ContentRegistry> DefaultRegistry =
            new Lazy<ContentRegistry>(() => Scan(typeof(ContentRegistry).Assembly));

        private readonly Dictionary<string, object> _byId = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly List<string> _ids = new List<string>();

        private ContentRegistry()
        {
            Availability = new RegistryAvailability(this);
        }

        /// <summary>The behaviors shipped in Confluence.Sim.</summary>
        public static ContentRegistry Default => DefaultRegistry.Value;

        /// <summary>A registry without behaviors (duels that run the bare rules).</summary>
        public static ContentRegistry Empty { get; } = new ContentRegistry();

        /// <summary>Registered record ids in ordinal order.</summary>
        public IReadOnlyList<string> Ids => _ids;

        /// <summary>Implemented records, for Economy and Arena pools (D-010).</summary>
        public IContentAvailability Availability { get; }

        public int Count => _ids.Count;

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);

        /// <summary>The implementing class of a record, or null.</summary>
        public Type TypeOf(string id) => id != null && _types.TryGetValue(id, out Type t) ? t : null;

        public bool TryGetSpell(string id, out ISpellBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetTransform(string id, out ISpellTransform behavior) => TryGet(id, out behavior);

        public bool TryGetKit(string id, out IWizardKitBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetAugment(string id, out IAugmentBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetEquipment(string id, out IEquipmentBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetBiomeRule(string id, out IBiomeRuleBehavior behavior) => TryGet(id, out behavior);

        public bool TryGetProp(string id, out IPropBehavior behavior) => TryGet(id, out behavior);

        /// <summary>Scans the assemblies (in the given order) and builds a registry; throws on an invalid declaration.</summary>
        public static ContentRegistry Scan(params Assembly[] assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            var registry = new ContentRegistry();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null) continue;
                var types = new List<Type>(assembly.GetTypes());
                types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                foreach (Type type in types)
                {
                    foreach (ContentBehaviorAttribute attribute in type.GetCustomAttributes<ContentBehaviorAttribute>(false))
                    {
                        registry.Register(attribute.Id, type);
                    }
                }
            }

            registry._ids.Sort(StringComparer.Ordinal);
            return registry;
        }

        /// <summary>
        /// A registry of explicit behavior instances keyed by record id (tests, tools, Laboratory variants). Each
        /// instance must implement the interface its id prefix expects, exactly as for scanned classes.
        /// </summary>
        public static ContentRegistry FromBehaviors(IEnumerable<KeyValuePair<string, object>> behaviors)
        {
            if (behaviors == null) throw new ArgumentNullException(nameof(behaviors));
            var registry = new ContentRegistry();
            foreach (KeyValuePair<string, object> entry in behaviors)
            {
                if (entry.Value == null) throw new ArgumentException("Behavior for " + entry.Key + " is null.", nameof(behaviors));
                registry.Add(entry.Key, entry.Value.GetType(), entry.Value);
            }

            registry._ids.Sort(StringComparer.Ordinal);
            return registry;
        }

        /// <summary>The interface a record id must implement (null when its prefix is not a behavior record type).</summary>
        public static Type[] ExpectedInterfaces(string id)
        {
            if (id == null) return null;
            if (id.StartsWith("SPL-", StringComparison.Ordinal)) return new[] { typeof(ISpellBehavior) };
            if (id.StartsWith("AUG-", StringComparison.Ordinal)) return new[] { typeof(ISpellTransform), typeof(IAugmentBehavior) };
            if (id.StartsWith("WIZ-", StringComparison.Ordinal)) return new[] { typeof(IWizardKitBehavior) };
            if (id.StartsWith("EQP-", StringComparison.Ordinal)) return new[] { typeof(IEquipmentBehavior) };
            if (id.StartsWith("BIO-", StringComparison.Ordinal)) return new[] { typeof(IBiomeRuleBehavior) };
            if (id.StartsWith("PROP-", StringComparison.Ordinal)) return new[] { typeof(IPropBehavior) };
            return null;
        }

        private void Register(string id, Type type)
        {
            if (ExpectedInterfaces(id) == null) return; // generator content (MOD-) is implemented by Arena without a Sim behavior
            if (type.IsAbstract || !type.IsClass)
            {
                throw new InvalidOperationException(type.FullName + " is marked [ContentBehavior(\"" + id + "\")] but is not a concrete class.");
            }

            ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                throw new InvalidOperationException(type.FullName + " (" + id + ") needs a public parameterless constructor.");
            }

            Add(id, type, null, ctor);
        }

        private void Add(string id, Type type, object instance, ConstructorInfo ctor = null)
        {
            Type[] expected = ExpectedInterfaces(id);
            if (expected == null)
            {
                throw new InvalidOperationException(id + " is not a record type with a Sim behavior.");
            }

            bool matches = false;
            foreach (Type i in expected)
            {
                if (i.IsAssignableFrom(type)) matches = true;
            }

            if (!matches)
            {
                throw new InvalidOperationException(type.FullName + " is marked [ContentBehavior(\"" + id + "\")] but does not implement " +
                    expected[0].Name + (expected.Length > 1 ? " or " + expected[1].Name : "") + ".");
            }

            if (_byId.ContainsKey(id))
            {
                throw new InvalidOperationException("Two behaviors claim " + id + ": " + _types[id].FullName + " and " + type.FullName + ".");
            }

            _byId.Add(id, instance ?? ctor.Invoke(null));
            _types.Add(id, type);
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

        private sealed class RegistryAvailability : IContentAvailability
        {
            private readonly ContentRegistry _registry;

            public RegistryAvailability(ContentRegistry registry)
            {
                _registry = registry;
            }

            public bool IsAvailable(string id) => _registry.Contains(id);
        }
    }
}
