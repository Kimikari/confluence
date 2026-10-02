using System;
using Confluence.Core.Content;

namespace Confluence.Casting.Tests.Support
{
    /// <summary>The supplied catalogue, loaded once per test run.</summary>
    internal static class TestCatalogue
    {
        private static readonly Lazy<Catalogue> Lazy = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));

        public static Catalogue Real => Lazy.Value;

        public static CastableSpell Spell(string id) => CastableSpell.FromDef(Real.Spell(id));
    }
}
