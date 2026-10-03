using System;
using System.Collections.Generic;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// One player's resolved casting kit for a duel: the wizard grammar, the six equipped spell forms in slot order,
    /// and the signature technique. Forms are resolved once (base, transformations, caps) and stay immutable.
    /// A slot whose record has no implemented behavior holds null and cannot be cast (D-010 pools never offer it).
    /// </summary>
    public sealed class SpellLoadout
    {
        public SpellLoadout(IGrammar grammar, IReadOnlyList<SpellForm> spells, SpellForm signature = null)
        {
            Grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
            if (spells == null) throw new ArgumentNullException(nameof(spells));
            if (spells.Count > GlobalRules.Loadout.EquippedSpellSlots)
            {
                throw new ArgumentException("At most " + GlobalRules.Loadout.EquippedSpellSlots + " equipped spells (spec L292).", nameof(spells));
            }

            if (signature != null && !signature.IsSignature) throw new ArgumentException("Not a signature form.", nameof(signature));
            var copy = new SpellForm[spells.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = spells[i];
            Spells = Array.AsReadOnly(copy);
            Signature = signature;
        }

        public IGrammar Grammar { get; }

        /// <summary>Equipped forms by slot (0-5); null where the record is not implemented.</summary>
        public IReadOnlyList<SpellForm> Spells { get; }

        public SpellForm Signature { get; }

        public SpellForm FormFor(string spellId)
        {
            if (spellId == null) return null;
            if (Signature != null && string.Equals(Signature.SpellId, spellId, StringComparison.Ordinal)) return Signature;
            for (int i = 0; i < Spells.Count; i++)
            {
                if (Spells[i] != null && string.Equals(Spells[i].SpellId, spellId, StringComparison.Ordinal)) return Spells[i];
            }

            return null;
        }
    }

    /// <summary>Supplies each player's loadout when the spell runtime attaches to a duel.</summary>
    public interface ISpellLoadoutSource
    {
        SpellLoadout Build(DuelWorld world, int player);
    }

    /// <summary>Fixed loadouts (tests, the training room, tools).</summary>
    public sealed class FixedLoadouts : ISpellLoadoutSource
    {
        private readonly SpellLoadout[] _loadouts;

        public FixedLoadouts(SpellLoadout player0, SpellLoadout player1)
        {
            _loadouts = new[] { player0, player1 };
        }

        public SpellLoadout Build(DuelWorld world, int player) => _loadouts[player];
    }

    /// <summary>
    /// Loadouts from catalogue records and registered behaviors: the player's wizard grammar (the executable grammar,
    /// else the guided normal route, spec L470), each equipped spell resolved through its <see cref="ISpellBehavior"/>
    /// with the player's active transformations of that spell (spec L286, L510, L541), and the wizard's signature
    /// when its kit behavior is registered.
    /// </summary>
    public sealed class CatalogueLoadouts : ISpellLoadoutSource
    {
        private readonly Catalogue _catalogue;
        private readonly ContentRegistry _registry;
        private readonly Func<string, IGrammar> _grammarFactory;

        public CatalogueLoadouts(Catalogue catalogue, ContentRegistry registry = null, Func<string, IGrammar> grammarFactory = null)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _registry = registry ?? ContentRegistry.Default;
            _grammarFactory = grammarFactory;
        }

        public SpellLoadout Build(DuelWorld world, int player)
        {
            DuelPlayerSetup setup = world.Player(player).Setup;
            string wizardId = setup.WizardId;
            IGrammar grammar = _grammarFactory != null ? _grammarFactory(wizardId) : DefaultGrammar(wizardId);
            if (grammar == null) return null; // no casting route yet: the player duels without spells (D-010 offers none)
            var forms = new List<SpellForm>();
            for (int i = 0; i < setup.EquippedSpellIds.Count && i < GlobalRules.Loadout.EquippedSpellSlots; i++)
            {
                forms.Add(Resolve(setup, setup.EquippedSpellIds[i]));
            }

            SpellForm signature = null;
            if (wizardId != null && _registry.TryGetKit(wizardId, out IWizardKitBehavior kit))
            {
                SpellFormBuilder b = SpellFormBuilder.ForSignature(wizardId);
                b.Parameters.AddRange(kit.Parameters);
                kit.BuildSignature(b);
                signature = b.Build();
            }

            return new SpellLoadout(grammar, forms, signature);
        }

        /// <summary>
        /// The executable wizard grammar, or the guided normal route for wizards still on it (spec L470), or null when the
        /// wizard has neither yet (a record that states no fixed normal preparation, such as WIZ-005's token syntax).
        /// </summary>
        public static IGrammar DefaultGrammar(string wizardId)
        {
            if (!GrammarRegistry.TryDescribe(wizardId, out GrammarDescriptor descriptor)) return null;
            if (descriptor.Executable) return GrammarRegistry.Create(wizardId);
            return descriptor.NormalPreparationTicks.HasValue ? GrammarRegistry.CreateGuided(wizardId) : null;
        }

        private SpellForm Resolve(DuelPlayerSetup setup, string spellId)
        {
            if (!_registry.TryGetSpell(spellId, out ISpellBehavior behavior)) return null;
            SpellDef def = _catalogue.Spell(spellId);
            return SpellFormResolver.Resolve(def, behavior, TransformsOf(setup, spellId));
        }

        private List<ActiveTransform> TransformsOf(DuelPlayerSetup setup, string spellId)
        {
            var result = new List<ActiveTransform>();
            for (int i = 0; i < setup.Augments.Count; i++)
            {
                ActiveAugment a = setup.Augments[i];
                if (!_catalogue.AugmentsById.TryGetValue(a.Id ?? string.Empty, out AugmentDef def) || !def.IsTransformation) continue;
                if (!string.Equals(def.TargetSpellId, spellId, StringComparison.Ordinal)) continue;
                if (!_registry.TryGetTransform(a.Id, out ISpellTransform transform)) continue;
                result.Add(new ActiveTransform(a.Id, Math.Max(1, Math.Min(2, a.Rank)), transform, def.ExclusiveGroup));
            }

            return result;
        }
    }
}
