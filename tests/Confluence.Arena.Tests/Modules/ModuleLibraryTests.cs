using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Core.Content;

namespace Confluence.Arena.Tests.Modules
{
    public class ModuleLibraryTests
    {
        public static IEnumerable<object[]> ModuleIds() => ArenaFixture.Catalogue.Modules.Select(m => new object[] { m.Id });

        [Fact]
        public void EveryCatalogueModuleHasAuthoredGeometry()
        {
            Assert.Equal(72, ArenaFixture.Catalogue.Modules.Count);
            Assert.Equal(72, ModuleLibrary.BlueprintCount);
            foreach (ModuleDef def in ArenaFixture.Catalogue.Modules)
            {
                Assert.True(ArenaFixture.Modules.TryGet(def.Id, out ModuleGeometry _), def.Id + " has no blueprint");
            }
        }

        [Theory]
        [MemberData(nameof(ModuleIds))]
        public void ModulePassesAuthoringChecks(string id)
        {
            ModuleAssert.Authored(id);
        }

        [Fact]
        public void LibraryListsModulesInIdOrder()
        {
            IReadOnlyList<ModuleGeometry> all = ArenaFixture.Modules.All;
            for (int i = 1; i < all.Count; i++)
            {
                Assert.True(string.CompareOrdinal(all[i - 1].ModuleId, all[i].ModuleId) < 0);
            }
        }

        [Fact]
        public void DurabilityTextParsesLabeledValues()
        {
            var d = new ModuleDurability("Low wood 60; thin panels 45; stone slab 120; prism 75; metal or column 90; porous wall 100.");
            Assert.Equal(60f, d.Get("low wood"));
            Assert.Equal(45f, d.Get("thin panels"));
            Assert.Equal(90f, d.Get("column"));
            Assert.Equal(90f, d.Get("metal"));
            Assert.False(d.TryGet("hedge", out float _));
            var e = new ModuleDurability("Load-bearing floor, stairs, and ramps anchored. Separate edge cover has 80 durability.");
            Assert.Equal(80f, e.Get("edge cover"));
            Assert.True(e.DeclaresAnchoredTerrain);
        }
    }
}
