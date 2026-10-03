using System;
using System.Collections.Generic;
using Confluence.Core.Content;

namespace Confluence.Hosting.Content
{
    /// <summary>
    /// Which catalogue records one match may use (D-010): biomes and props the arena generator may place, and the
    /// equipment and augments the shop and draft may offer. Pools hold implemented records only. While no biome or prop
    /// carries a behavior class yet, the arena falls back to an explicit configured set (by default every catalogue
    /// record, whose geometry the Arena library builds, D-069) and says so in the log. A custom lobby's allowed-content
    /// list (L71) restricts each record type it names; types it does not name stay unrestricted. Laboratory overrides
    /// may widen the economy pools (L69).
    /// </summary>
    public sealed class HostContentPolicy
    {
        private readonly IdSet _biomes;
        private readonly IdSet _props;
        private readonly IdSet _pools;

        private HostContentPolicy(IdSet biomes, IdSet props, IdSet pools, IReadOnlyList<string> notes)
        {
            _biomes = biomes;
            _props = props;
            _pools = pools;
            Notes = notes;
        }

        /// <summary>Biomes the generator may choose (ordinal order).</summary>
        public IReadOnlyList<string> BiomeIds => _biomes.Ids;

        /// <summary>Props the generator may place (ordinal order).</summary>
        public IReadOnlyList<string> PropIds => _props.Ids;

        /// <summary>Equipment and augments the shop and draft may offer (ordinal order).</summary>
        public IReadOnlyList<string> PoolIds => _pools.Ids;

        public IContentAvailability Biomes => _biomes;
        public IContentAvailability Props => _props;

        /// <summary>Shop (EQP) and draft (AUG) availability; the two id ranges never overlap.</summary>
        public IContentAvailability Pools => _pools;

        /// <summary>Plain explanations of every fallback and override applied, also written to the host log.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>
        /// Builds the policy. <paramref name="configuredBiomes"/> and <paramref name="configuredProps"/> are the explicit
        /// arena sets used while the registry has no behavior of that type (null = every catalogue record of the type);
        /// <paramref name="allowedIds"/> is a custom lobby's allowed content (null = everything).
        /// </summary>
        public static HostContentPolicy Build(Catalogue catalogue, ImplementedContent implemented, LaboratoryOverrides overrides,
            IEnumerable<string> configuredBiomes = null, IEnumerable<string> configuredProps = null,
            IReadOnlyCollection<string> allowedIds = null, HostLog log = null)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (implemented == null) throw new ArgumentNullException(nameof(implemented));
            overrides = overrides ?? LaboratoryOverrides.None;
            var allowed = new AllowedContent(allowedIds);
            var notes = new List<string>();

            IdSet biomes = ArenaSet(catalogue, implemented, ContentType.Biome, configuredBiomes, allowed, notes);
            IdSet props = ArenaSet(catalogue, implemented, ContentType.Prop, configuredProps, allowed, notes);

            var poolIds = new List<string>();
            if (overrides.PoolsUseWholeCatalogue)
            {
                foreach (EquipmentDef def in catalogue.EquipmentItems) poolIds.Add(def.Id);
                foreach (AugmentDef def in catalogue.Augments) poolIds.Add(def.Id);
                notes.Add("Laboratory override: shop and draft pools use the whole catalogue (" + poolIds.Count +
                    " definitions); unimplemented definitions change the build but have no duel effect (L69).");
            }
            else
            {
                poolIds.AddRange(implemented.IdsOf(ContentType.Equipment));
                poolIds.AddRange(implemented.IdsOf(ContentType.Augment));
                notes.Add("Shop and draft pools hold the " + poolIds.Count + " implemented equipment and augment definitions (D-010).");
            }

            var pools = new IdSet(allowed.Filter(poolIds));
            if (pools.Ids.Count != poolIds.Count)
            {
                notes.Add("The lobby's allowed content leaves " + pools.Ids.Count + " pool definitions (L71).");
            }

            if (log != null)
                foreach (string note in notes) log.Info(note);

            return new HostContentPolicy(biomes, props, pools, notes.AsReadOnly());
        }

        /// <summary>Canonical text recorded in the host settings fingerprint.</summary>
        public string CanonicalText() =>
            "biomes=" + string.Join(",", BiomeIds) + ";props=" + string.Join(",", PropIds) + ";pools=" + string.Join(",", PoolIds);

        private static IdSet ArenaSet(Catalogue catalogue, ImplementedContent implemented, ContentType type,
            IEnumerable<string> configured, AllowedContent allowed, List<string> notes)
        {
            string label = type == ContentType.Biome ? "biome" : "prop";
            List<string> registered = implemented.IdsOf(type);
            var chosen = new List<string>();
            if (registered.Count > 0)
            {
                chosen.AddRange(registered);
                notes.Add("Arena " + label + "s: " + registered.Count + " implemented records from the content registry (D-010).");
            }
            else
            {
                if (configured != null)
                {
                    foreach (string id in configured)
                    {
                        if (!ContentIds.TryParse(id, out ContentType t, out _) || t != type || !catalogue.Exists(id))
                        {
                            throw new ArgumentException("Configured " + label + " " + id + " is not a catalogue " + label + ".");
                        }

                        chosen.Add(id);
                    }
                }
                else
                {
                    foreach (ContentDef def in catalogue.RecordsOf(type)) chosen.Add(def.Id);
                }

                notes.Add("No " + ContentIds.Prefix(type) + " record carries [ContentBehavior] yet; arena " + label + "s use the " +
                    (configured == null ? "default configured set: every catalogue " + label + " (" + chosen.Count + "), whose geometry the generator builds (D-069)."
                        : "configured set of " + chosen.Count + "."));
            }

            var set = new IdSet(allowed.Filter(chosen));
            if (set.Ids.Count == 0) throw new InvalidOperationException("No " + label + " is available for arena generation.");
            return set;
        }

        /// <summary>A lobby allow-list: restricts only the record types it names.</summary>
        private sealed class AllowedContent
        {
            private readonly HashSet<string> _ids;
            private readonly HashSet<ContentType> _types = new HashSet<ContentType>();

            public AllowedContent(IReadOnlyCollection<string> ids)
            {
                if (ids == null) return;
                _ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    _ids.Add(id);
                    if (ContentIds.TryParse(id, out ContentType type, out _)) _types.Add(type);
                }
            }

            public List<string> Filter(IEnumerable<string> ids)
            {
                var result = new List<string>();
                foreach (string id in ids)
                    if (Allows(id)) result.Add(id);
                return result;
            }

            private bool Allows(string id)
            {
                if (_ids == null || !ContentIds.TryParse(id, out ContentType type, out _) || !_types.Contains(type)) return true;
                return _ids.Contains(id);
            }
        }

        private sealed class IdSet : IContentAvailability
        {
            private readonly HashSet<string> _ids;

            public IdSet(IEnumerable<string> ids)
            {
                var sorted = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string id in ids)
                    if (!string.IsNullOrEmpty(id)) sorted.Add(id);
                Ids = new List<string>(sorted).AsReadOnly();
                _ids = new HashSet<string>(sorted, StringComparer.Ordinal);
            }

            public IReadOnlyList<string> Ids { get; }

            public bool IsAvailable(string id) => id != null && _ids.Contains(id);
        }
    }
}
