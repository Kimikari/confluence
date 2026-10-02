using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Confluence.Core.Content;

namespace Confluence.Tools.Ledger
{
    /// <summary>Finds classes marked [ContentBehavior("ID")] in the shipped gameplay assemblies.</summary>
    public static class BehaviorScanner
    {
        /// <summary>Assemblies that may implement catalogue records (all referenced by this tool's project).</summary>
        public static readonly IReadOnlyList<string> GameplayAssemblies = new[]
        {
            "Confluence.Core", "Confluence.Reactions", "Confluence.Casting", "Confluence.Arena",
            "Confluence.Economy", "Confluence.Sim", "Confluence.Match",
        };

        /// <summary>Catalogue ID -> implementing type names, sorted.</summary>
        public static SortedDictionary<string, List<string>> Scan(List<string> warnings)
        {
            var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var name in GameplayAssemblies)
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.Load(new AssemblyName(name));
                }
                catch (Exception e)
                {
                    warnings.Add("could not load " + name + ": " + e.Message);
                    continue;
                }
                foreach (var type in LoadableTypes(assembly, warnings))
                {
                    if (!type.IsClass) continue;
                    foreach (var attribute in type.GetCustomAttributes<ContentBehaviorAttribute>(false))
                    {
                        if (!result.TryGetValue(attribute.Id, out var types)) result.Add(attribute.Id, types = new List<string>());
                        types.Add(type.FullName);
                    }
                }
            }
            foreach (var list in result.Values) list.Sort(StringComparer.Ordinal);
            return result;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly, List<string> warnings)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                warnings.Add("some types in " + assembly.GetName().Name + " failed to load: " + e.LoaderExceptions.FirstOrDefault()?.Message);
                return e.Types.Where(t => t != null);
            }
        }
    }
}
