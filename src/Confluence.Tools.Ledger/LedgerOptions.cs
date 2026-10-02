using System;
using System.Collections.Generic;
using System.IO;

namespace Confluence.Tools.Ledger
{
    /// <summary>Command-line options. Relative paths resolve against the repository root.</summary>
    public sealed class LedgerOptions
    {
        public const string DefaultTestAssemblies = "tests/*/bin/**/Confluence.*.Tests.dll";

        public string RepoRoot { get; private set; }
        public string CataloguePath { get; private set; }
        public List<string> TestAssemblyPatterns { get; } = new List<string>();
        public List<string> TrxPaths { get; } = new List<string>();
        public string MarkdownPath { get; private set; }
        public string JsonPath { get; private set; }
        public bool ShowHelp { get; private set; }

        public static string Usage =>
            "Confluence.Tools.Ledger - generates docs/CONTENT_LEDGER.md and docs/content-ledger.json\n" +
            "  --root <dir>                 repository root (default: nearest ancestor containing Confluence.sln)\n" +
            "  --catalogue <file>           catalogue JSON (default: CONFLUENCE_CATALOGUE or docs/confluence/confluence-catalogue.json)\n" +
            "  --test-assemblies <glob|dir> test assemblies to scan for [Verifies] (repeatable; default " + DefaultTestAssemblies + ", newest build per name)\n" +
            "  --trx <dir|file>             TRX results to read (repeatable; without it verifying tests count as not run)\n" +
            "  --out-md <file>              Markdown output (default docs/CONTENT_LEDGER.md)\n" +
            "  --out-json <file>            JSON output (default docs/content-ledger.json)";

        public static LedgerOptions Parse(string[] args)
        {
            var options = new LedgerOptions();
            string root = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Value()
                {
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for " + arg);
                    return args[++i];
                }
                switch (arg)
                {
                    case "--root": root = Value(); break;
                    case "--catalogue": options.CataloguePath = Value(); break;
                    case "--test-assemblies": options.TestAssemblyPatterns.Add(Value()); break;
                    case "--trx": options.TrxPaths.Add(Value()); break;
                    case "--out-md": options.MarkdownPath = Value(); break;
                    case "--out-json": options.JsonPath = Value(); break;
                    case "-h":
                    case "--help": options.ShowHelp = true; break;
                    default: throw new ArgumentException("Unknown argument " + arg);
                }
            }

            options.RepoRoot = Path.GetFullPath(root ?? FindRepoRoot());
            options.CataloguePath = options.CataloguePath == null ? null : options.Resolve(options.CataloguePath);
            if (options.TestAssemblyPatterns.Count == 0) options.TestAssemblyPatterns.Add(DefaultTestAssemblies);
            options.MarkdownPath = options.Resolve(options.MarkdownPath ?? "docs/CONTENT_LEDGER.md");
            options.JsonPath = options.Resolve(options.JsonPath ?? "docs/content-ledger.json");
            return options;
        }

        public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(RepoRoot, path));

        private static string FindRepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "Confluence.sln"))) return dir.FullName;
            }
            throw new InvalidOperationException("Could not find Confluence.sln above the current directory; pass --root.");
        }
    }
}
