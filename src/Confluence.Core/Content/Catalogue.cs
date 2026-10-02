using System;
using System.Collections.Generic;
using System.IO;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// The complete immutable content catalogue loaded from docs/confluence/confluence-catalogue.json.
    /// Loading checks structure (kinds, required and unknown fields, predicate grammar); semantic consistency is
    /// checked separately by <see cref="CatalogueValidator"/>.
    /// </summary>
    public sealed class Catalogue
    {
        public const string EnvironmentVariable = "CONFLUENCE_CATALOGUE";
        public const string RelativePath = "docs/confluence/confluence-catalogue.json";

        private static readonly string[] TopLevelKeys = { "title", "design_version", "date", "counts", "rules", "content" };

        private readonly Dictionary<string, ContentDef> _all = new Dictionary<string, ContentDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SpellDef>> _spellsByWizard = new Dictionary<string, List<SpellDef>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AugmentDef>> _transformsByWizard = new Dictionary<string, List<AugmentDef>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AugmentDef>> _transformsBySpell = new Dictionary<string, List<AugmentDef>>(StringComparer.Ordinal);
        private readonly List<string> _duplicateIds = new List<string>();

        private Catalogue(JsonValue root)
        {
            if (root == null || !root.IsObject) throw new CatalogueFormatException(null, "catalogue root must be a JSON object");
            foreach (var key in root.Keys)
                if (Array.IndexOf(TopLevelKeys, key) < 0) throw new CatalogueFormatException(null, "unknown top-level field '" + key + "'");

            Title = RequireString(root, "title");
            DesignVersion = RequireString(root, "design_version");
            Date = RequireString(root, "date");
            if (!root.TryGet("counts", out var counts)) throw new CatalogueFormatException(null, "missing 'counts'");
            Counts = new CatalogueCounts(counts);
            if (!root.TryGet("rules", out var rules)) throw new CatalogueFormatException(null, "missing 'rules'");
            RuleFlags = new CatalogueRules(rules);
            if (!root.TryGet("content", out var content) || !content.IsObject) throw new CatalogueFormatException(null, "missing 'content' object");
            foreach (var key in content.Keys)
            {
                bool known = false;
                foreach (var type in ContentIds.AllTypes)
                    if (ContentIds.JsonKey(type) == key) known = true;
                if (!known) throw new CatalogueFormatException(null, "unknown content list '" + key + "'");
            }

            Wizards = ReadList(content, ContentType.Wizard, r => new WizardDef(r), out var wizardsById);
            Spells = ReadList(content, ContentType.Spell, r => new SpellDef(r), out var spellsById);
            EquipmentItems = ReadList(content, ContentType.Equipment, r => new EquipmentDef(r), out var equipmentById);
            Augments = ReadList(content, ContentType.Augment, r => new AugmentDef(r), out var augmentsById);
            Biomes = ReadList(content, ContentType.Biome, r => new BiomeDef(r), out var biomesById);
            Modules = ReadList(content, ContentType.Module, r => new ModuleDef(r), out var modulesById);
            Props = ReadList(content, ContentType.Prop, r => new PropDef(r), out var propsById);
            Builds = ReadList(content, ContentType.Build, r => new BuildDef(r), out var buildsById);
            WizardsById = wizardsById;
            SpellsById = spellsById;
            EquipmentById = equipmentById;
            AugmentsById = augmentsById;
            BiomesById = biomesById;
            ModulesById = modulesById;
            PropsById = propsById;
            BuildsById = buildsById;

            foreach (var spell in Spells)
            {
                string owner = spell.WizardId;
                if (owner == null) continue;
                Bucket(_spellsByWizard, owner).Add(spell);
            }
            var shared = new List<AugmentDef>();
            var transformations = new List<AugmentDef>();
            foreach (var augment in Augments)
            {
                if (!augment.IsTransformation)
                {
                    shared.Add(augment);
                    continue;
                }
                transformations.Add(augment);
                foreach (var wizardId in augment.EligibleWizards) Bucket(_transformsByWizard, wizardId).Add(augment);
                foreach (var spellId in augment.TargetSpellIds) Bucket(_transformsBySpell, spellId).Add(augment);
            }
            SharedAugments = shared.AsReadOnly();
            Transformations = transformations.AsReadOnly();
            DuplicateIds = _duplicateIds.AsReadOnly();
        }

        public string Title { get; }
        public string DesignVersion { get; }
        public string Date { get; }
        public CatalogueCounts Counts { get; }

        /// <summary>The catalogue's "rules" block.</summary>
        public CatalogueRules RuleFlags { get; }

        public IReadOnlyList<WizardDef> Wizards { get; }
        public IReadOnlyList<SpellDef> Spells { get; }
        public IReadOnlyList<EquipmentDef> EquipmentItems { get; }
        public IReadOnlyList<AugmentDef> Augments { get; }
        public IReadOnlyList<BiomeDef> Biomes { get; }
        public IReadOnlyList<ModuleDef> Modules { get; }
        public IReadOnlyList<PropDef> Props { get; }
        public IReadOnlyList<BuildDef> Builds { get; }

        public IReadOnlyDictionary<string, WizardDef> WizardsById { get; }
        public IReadOnlyDictionary<string, SpellDef> SpellsById { get; }
        public IReadOnlyDictionary<string, EquipmentDef> EquipmentById { get; }
        public IReadOnlyDictionary<string, AugmentDef> AugmentsById { get; }
        public IReadOnlyDictionary<string, BiomeDef> BiomesById { get; }
        public IReadOnlyDictionary<string, ModuleDef> ModulesById { get; }
        public IReadOnlyDictionary<string, PropDef> PropsById { get; }
        public IReadOnlyDictionary<string, BuildDef> BuildsById { get; }

        /// <summary>The 288 shared (non-transformation) augments in catalogue order.</summary>
        public IReadOnlyList<AugmentDef> SharedAugments { get; }

        /// <summary>The 576 wizard spell transformations in catalogue order.</summary>
        public IReadOnlyList<AugmentDef> Transformations { get; }

        /// <summary>IDs that appeared more than once; lookups resolve to the first occurrence.</summary>
        public IReadOnlyList<string> DuplicateIds { get; }

        // ---- Loading -----------------------------------------------------------------------

        public static Catalogue Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string text = File.ReadAllText(path);
            try
            {
                return LoadFromJson(text);
            }
            catch (JsonParseException e)
            {
                throw new CatalogueFormatException(null, "failed to parse catalogue '" + path + "': " + e.Message, e);
            }
        }

        public static Catalogue LoadFromJson(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return FromJson(JsonParser.Parse(text));
        }

        public static Catalogue FromJson(JsonValue root) => new Catalogue(root);

        /// <summary>Loads the catalogue found by <see cref="FindDefaultPath"/>.</summary>
        public static Catalogue LoadDefault() => Load(FindDefaultPath());

        /// <summary>
        /// The catalogue path from the CONFLUENCE_CATALOGUE environment variable, else the first
        /// docs/confluence/confluence-catalogue.json found walking upward from AppContext.BaseDirectory and then
        /// from the current directory. Throws <see cref="FileNotFoundException"/> when nothing is found.
        /// </summary>
        public static string FindDefaultPath()
        {
            string fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv))
            {
                if (File.Exists(fromEnv)) return Path.GetFullPath(fromEnv);
                throw new FileNotFoundException(EnvironmentVariable + " points to a missing file.", fromEnv);
            }
            if (TryFindDefaultPath(out string path)) return path;
            throw new FileNotFoundException(
                "Could not find " + RelativePath + " above '" + AppContext.BaseDirectory + "' or '" + Directory.GetCurrentDirectory()
                + "'. Set " + EnvironmentVariable + " to the catalogue path.");
        }

        public static bool TryFindDefaultPath(out string path)
        {
            string fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
            {
                path = Path.GetFullPath(fromEnv);
                return true;
            }
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start)) continue;
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "docs", "confluence", "confluence-catalogue.json");
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                    dir = dir.Parent;
                }
            }
            path = null;
            return false;
        }

        // ---- Typed lookup ------------------------------------------------------------------

        public WizardDef Wizard(string id) => Lookup(WizardsById, id, "wizard");
        public SpellDef Spell(string id) => Lookup(SpellsById, id, "spell");
        public EquipmentDef Equipment(string id) => Lookup(EquipmentById, id, "equipment");
        public AugmentDef Augment(string id) => Lookup(AugmentsById, id, "augment");
        public BiomeDef Biome(string id) => Lookup(BiomesById, id, "biome");
        public ModuleDef Module(string id) => Lookup(ModulesById, id, "module");
        public PropDef Prop(string id) => Lookup(PropsById, id, "prop");
        public BuildDef Build(string id) => Lookup(BuildsById, id, "build");

        public bool TryGetWizard(string id, out WizardDef def) => TryLookup(WizardsById, id, out def);
        public bool TryGetSpell(string id, out SpellDef def) => TryLookup(SpellsById, id, out def);
        public bool TryGetEquipment(string id, out EquipmentDef def) => TryLookup(EquipmentById, id, out def);
        public bool TryGetAugment(string id, out AugmentDef def) => TryLookup(AugmentsById, id, out def);
        public bool TryGetBiome(string id, out BiomeDef def) => TryLookup(BiomesById, id, out def);
        public bool TryGetModule(string id, out ModuleDef def) => TryLookup(ModulesById, id, out def);
        public bool TryGetProp(string id, out PropDef def) => TryLookup(PropsById, id, out def);
        public bool TryGetBuild(string id, out BuildDef def) => TryLookup(BuildsById, id, out def);

        /// <summary>Any catalogue record by ID.</summary>
        public bool TryGet(string id, out ContentDef def)
        {
            if (id != null && _all.TryGetValue(id, out def)) return true;
            def = null;
            return false;
        }

        public ContentDef Get(string id)
        {
            if (!TryGet(id, out var def)) throw new KeyNotFoundException("No catalogue record with ID '" + id + "'.");
            return def;
        }

        /// <summary>True when a record of any content type has this ID (feats are rule data; see <see cref="Feats"/>).</summary>
        public bool Exists(string id) => id != null && _all.ContainsKey(id);

        /// <summary>Display name for a catalogue ID or feat ID; null when unknown.</summary>
        public string NameOf(string id)
        {
            if (TryGet(id, out var def)) return def.Name;
            return Feats.TryGet(id, out var feat) ? feat.Description : null;
        }

        public IReadOnlyList<ContentDef> RecordsOf(ContentType type)
        {
            switch (type)
            {
                case ContentType.Wizard: return Wizards;
                case ContentType.Spell: return Spells;
                case ContentType.Equipment: return EquipmentItems;
                case ContentType.Augment: return Augments;
                case ContentType.Biome: return Biomes;
                case ContentType.Module: return Modules;
                case ContentType.Prop: return Props;
                case ContentType.Build: return Builds;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>Every record in type order, then catalogue order.</summary>
        public IEnumerable<ContentDef> AllRecords()
        {
            foreach (var type in ContentIds.AllTypes)
                foreach (var record in RecordsOf(type))
                    yield return record;
        }

        // ---- Relationships -----------------------------------------------------------------

        /// <summary>The wizard's spells in catalogue order (by each spell's owner).</summary>
        public IReadOnlyList<SpellDef> SpellsOf(string wizardId) => Group(_spellsByWizard, wizardId);

        /// <summary>The transformations whose eligible wizard is this wizard.</summary>
        public IReadOnlyList<AugmentDef> TransformationsOf(string wizardId) => Group(_transformsByWizard, wizardId);

        /// <summary>The transformations targeting this spell.</summary>
        public IReadOnlyList<AugmentDef> TransformationsFor(string spellId) => Group(_transformsBySpell, spellId);

        /// <summary>Plain-language eligibility text with record names resolved.</summary>
        public string DescribeEligibility(Predicate predicate) => PredicateDescriber.Describe(predicate, NameOf);

        // ---- Helpers -----------------------------------------------------------------------

        private IReadOnlyList<T> ReadList<T>(JsonValue content, ContentType type, Func<RecordReader, T> create, out IReadOnlyDictionary<string, T> byId)
            where T : ContentDef
        {
            string key = ContentIds.JsonKey(type);
            if (!content.TryGet(key, out var array)) throw new CatalogueFormatException(null, "missing content list '" + key + "'");
            if (!array.IsArray) throw new CatalogueFormatException(null, "content." + key + " must be an array");
            var items = array.AsArray();
            var list = new List<T>(items.Count);
            var index = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var reader = new RecordReader(items[i], "content." + key + "[" + i + "]");
                T def;
                try
                {
                    def = create(reader);
                }
                catch (JsonAccessException e)
                {
                    throw reader.Fail(e.Message, e);
                }
                list.Add(def);
                if (index.ContainsKey(def.Id) || _all.ContainsKey(def.Id))
                {
                    _duplicateIds.Add(def.Id);
                    continue;
                }
                index.Add(def.Id, def);
                _all.Add(def.Id, def);
            }
            byId = index;
            return list.AsReadOnly();
        }

        private static string RequireString(JsonValue root, string key)
        {
            if (!root.TryGet(key, out var value) || !value.IsString)
                throw new CatalogueFormatException(null, "'" + key + "' must be a string");
            return value.AsString();
        }

        private static List<T> Bucket<T>(Dictionary<string, List<T>> map, string key)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<T>();
                map.Add(key, list);
            }
            return list;
        }

        private static IReadOnlyList<T> Group<T>(Dictionary<string, List<T>> map, string key)
        {
            if (key != null && map.TryGetValue(key, out var list)) return list.AsReadOnly();
            return Array.AsReadOnly(new T[0]);
        }

        private static T Lookup<T>(IReadOnlyDictionary<string, T> map, string id, string kind)
        {
            if (id != null && map.TryGetValue(id, out var def)) return def;
            throw new KeyNotFoundException("No " + kind + " with ID '" + id + "' in the catalogue.");
        }

        private static bool TryLookup<T>(IReadOnlyDictionary<string, T> map, string id, out T def) where T : class
        {
            if (id != null && map.TryGetValue(id, out def)) return true;
            def = null;
            return false;
        }
    }
}
