using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Confluence.Core.Content;

namespace Confluence.Tools.Ledger
{
    /// <summary>
    /// Generates the content coverage ledger: which catalogue records are implemented ([ContentBehavior]) and which
    /// are verified ([Verifies] tests that ran and passed according to TRX results).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            LedgerOptions options;
            try
            {
                options = LedgerOptions.Parse(args);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(LedgerOptions.Usage);
                return 2;
            }
            if (options.ShowHelp)
            {
                Console.WriteLine(LedgerOptions.Usage);
                return 0;
            }

            try
            {
                Run(options);
                return 0;
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("ledger: " + e.Message);
                return 1;
            }
        }

        private static void Run(LedgerOptions options)
        {
            var warnings = new List<string>();
            string cataloguePath = options.CataloguePath ?? Catalogue.FindDefaultPath();
            var catalogue = Catalogue.Load(cataloguePath);

            var behaviors = BehaviorScanner.Scan(warnings);

            var assemblies = TestScanner.ResolveAssemblies(options.RepoRoot, options.TestAssemblyPatterns);
            if (assemblies.Count == 0) warnings.Add("no test assemblies matched " + string.Join(", ", options.TestAssemblyPatterns) + "; build the tests first");
            var tests = new List<VerifyingTest>();
            foreach (var path in assemblies) tests.AddRange(TestScanner.Scan(path));

            var results = options.TrxPaths.Count > 0 ? TrxResults.Load(options.RepoRoot, options.TrxPaths) : new TrxResults();
            if (options.TrxPaths.Count > 0 && results.Files.Count == 0) warnings.Add("no TRX files found under " + string.Join(", ", options.TrxPaths));

            var relativeAssemblies = assemblies.Select(a => Relative(options.RepoRoot, a)).ToList();
            var ledger = Ledger.Build(catalogue, behaviors.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                tests, results, relativeAssemblies, warnings);

            Write(options.MarkdownPath, LedgerWriter.Markdown(ledger));
            Write(options.JsonPath, LedgerWriter.Json(ledger));

            Console.WriteLine("Catalogue: " + Relative(options.RepoRoot, cataloguePath));
            Console.WriteLine("Behaviors: " + behaviors.Count + " IDs with [ContentBehavior]; tests: " + tests.Count + " methods with [Verifies] in "
                + assemblies.Count + " assemblies; TRX files: " + results.Files.Count + " (" + results.MethodCount + " test methods with results)");
            foreach (var status in Ledger.AllStatuses)
                Console.WriteLine("  " + Ledger.Slug(status).PadRight(26) + ledger.Count(null, status));
            Console.WriteLine("  " + "total".PadRight(26) + ledger.Entries.Count);
            if (ledger.UnknownIds.Count > 0) Console.WriteLine("warning: attributes name IDs missing from the catalogue: " + string.Join(", ", ledger.UnknownIds));
            if (ledger.OutOfScopeIds.Count > 0) Console.WriteLine("note: attribute IDs outside per-ID tracking: " + string.Join(", ", ledger.OutOfScopeIds));
            foreach (var warning in warnings) Console.WriteLine("warning: " + warning);
            Console.WriteLine("Wrote " + Relative(options.RepoRoot, options.MarkdownPath) + " and " + Relative(options.RepoRoot, options.JsonPath));
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
