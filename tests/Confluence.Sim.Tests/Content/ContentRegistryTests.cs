using System;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Sim.Content;
using Confluence.Sim.Content.SpellBehaviors;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content
{
    /// <summary>The content behavior registry, availability for the pools (D-010), and prose parameters (D-011).</summary>
    public class ContentRegistryTests
    {
        [Fact]
        public void The_Default_Registry_Finds_The_Shipped_Spell_Behaviors()
        {
            ContentRegistry registry = ContentRegistry.Default;
            Assert.Contains("SPL-009", registry.Ids);
            Assert.Contains("SPL-010", registry.Ids);
            Assert.Equal(typeof(Fireball), registry.TypeOf("SPL-009"));
            Assert.True(registry.TryGetSpell("SPL-010", out ISpellBehavior sweep));
            Assert.IsType<CinderSweep>(sweep);
            Assert.False(registry.TryGetTransform("SPL-010", out _));
            Assert.Equal(registry.Ids.OrderBy(i => i, StringComparer.Ordinal), registry.Ids);
        }

        [Fact]
        public void Every_Registered_Id_Is_A_Catalogue_Record_Of_The_Matching_Type()
        {
            Catalogue catalogue = SpellTestKit.Catalogue;
            foreach (string id in ContentRegistry.Default.Ids)
            {
                Assert.True(catalogue.TryGet(id, out ContentDef def), id + " is not a catalogue record");
                Type[] expected = ContentRegistry.ExpectedInterfaces(id);
                Assert.Contains(expected, i => i.IsAssignableFrom(ContentRegistry.Default.TypeOf(id)));
                Assert.NotNull(def);
            }
        }

        [Fact]
        public void Availability_Offers_Only_Implemented_Records()
        {
            IContentAvailability available = ContentRegistry.Default.Availability;
            Assert.True(available.IsAvailable("SPL-009"));
            Assert.True(available.IsAvailable("SPL-010"));
            Assert.False(available.IsAvailable("SPL-011")); // D-010: no empty handlers in the pools
            Assert.False(available.IsAvailable(null));
        }

        [Fact]
        public void Each_Record_Type_Maps_To_Its_Behavior_Interface()
        {
            Assert.Equal(new[] { typeof(ISpellBehavior) }, ContentRegistry.ExpectedInterfaces("SPL-001"));
            Assert.Equal(new[] { typeof(ISpellTransform), typeof(IAugmentBehavior) }, ContentRegistry.ExpectedInterfaces("AUG-001"));
            Assert.Equal(new[] { typeof(IWizardKitBehavior) }, ContentRegistry.ExpectedInterfaces("WIZ-001"));
            Assert.Equal(new[] { typeof(IEquipmentBehavior) }, ContentRegistry.ExpectedInterfaces("EQP-001"));
            Assert.Equal(new[] { typeof(IBiomeRuleBehavior) }, ContentRegistry.ExpectedInterfaces("BIO-001"));
            Assert.Equal(new[] { typeof(IPropBehavior) }, ContentRegistry.ExpectedInterfaces("PROP-001"));
            Assert.Null(ContentRegistry.ExpectedInterfaces("MOD-001"));
        }

        [Fact]
        public void Every_Prose_Parameter_Quotes_Its_Record_And_Contains_Its_Number()
        {
            // D-011: numbers that exist only in prose are declared with the exact phrase they come from.
            Catalogue catalogue = SpellTestKit.Catalogue;
            int checkedCount = 0;
            foreach (string id in ContentRegistry.Default.Ids)
            {
                if (!ContentRegistry.Default.TryGetSpell(id, out ISpellBehavior behavior)) continue;
                SpellDef def = catalogue.Spell(id);
                string text = string.Join(" | ", def.Damage, def.Effect, def.WorldInteraction, def.Counterplay);
                foreach (ProseParameter p in behavior.Parameters)
                {
                    Assert.True(text.Contains(p.Quote, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" is not in the record");
                    Assert.True(p.Quote.Contains(p.WrittenText, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" does not contain " + p.WrittenText);
                    checkedCount++;
                }
            }

            Assert.True(checkedCount >= 9);
        }

        [Fact]
        public void Every_Registered_Spell_Builds_A_Form_From_Its_Record()
        {
            Catalogue catalogue = SpellTestKit.Catalogue;
            foreach (string id in ContentRegistry.Default.Ids.Where(i => i.StartsWith("SPL-", StringComparison.Ordinal)))
            {
                ContentRegistry.Default.TryGetSpell(id, out ISpellBehavior behavior);
                SpellForm form = SpellFormResolver.Resolve(catalogue.Spell(id), behavior);
                Assert.Equal(id, form.SpellId);
                Assert.Equal((float)catalogue.Spell(id).Mana, form.Castable.Mana);
                Assert.NotNull(form.Delivery);
            }
        }

        [Fact]
        public void A_Catalogue_Loadout_Uses_The_Wizard_Grammar_And_Leaves_Unimplemented_Slots_Empty()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            SpellLoadout loadout = w.Player(0).Caster.Loadout;
            Assert.IsType<Confluence.Casting.Grammars.PressureReleaseGrammar>(loadout.Grammar);
            Assert.Equal(6, loadout.Spells.Count);
            Assert.Equal("SPL-009", loadout.Spells[0].SpellId);
            Assert.Equal("SPL-010", loadout.Spells[1].SpellId);
            Assert.All(loadout.Spells.Skip(2), Assert.Null);
            Assert.Null(loadout.Signature);
        }
    }
}
