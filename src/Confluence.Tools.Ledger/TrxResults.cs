using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Confluence.Tools.Ledger
{
    public enum TestOutcome
    {
        NotRun,
        Passed,
        Failed,
    }

    /// <summary>
    /// Per-method outcomes from Visual Studio TRX files. A method (all theory rows together) passes only when every
    /// executed result passed and at least one ran; any failed, error, timeout, or aborted result fails it.
    /// </summary>
    public sealed class TrxResults
    {
        private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        private readonly Dictionary<string, (int Passed, int Failed, int NotRun)> _byMethod =
            new Dictionary<string, (int Passed, int Failed, int NotRun)>(StringComparer.Ordinal);

        public List<string> Files { get; } = new List<string>();

        public int MethodCount => _byMethod.Count;

        public static TrxResults Load(string root, IEnumerable<string> paths)
        {
            var results = new TrxResults();
            foreach (var path in paths)
            {
                string full = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
                IEnumerable<string> files;
                if (Directory.Exists(full)) files = Directory.EnumerateFiles(full, "*.trx", SearchOption.AllDirectories);
                else if (File.Exists(full)) files = new[] { full };
                else throw new FileNotFoundException("TRX path not found", full);
                foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal)) results.Read(file);
            }
            return results;
        }

        public TestOutcome OutcomeOf(string className, string method)
        {
            if (!_byMethod.TryGetValue(className + "." + method, out var counts)) return TestOutcome.NotRun;
            if (counts.Failed > 0) return TestOutcome.Failed;
            return counts.Passed > 0 && counts.NotRun == 0 ? TestOutcome.Passed : TestOutcome.NotRun;
        }

        private void Read(string file)
        {
            var doc = XDocument.Load(file);
            var methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var unitTest in doc.Descendants(Ns + "UnitTest"))
            {
                var testMethod = unitTest.Element(Ns + "TestMethod");
                string id = (string)unitTest.Attribute("id");
                if (id == null || testMethod == null) continue;
                string className = (string)testMethod.Attribute("className");
                string name = StripArguments((string)testMethod.Attribute("name"));
                if (className == null || name == null) continue;
                // Some adapters report the fully qualified method in "name".
                if (name.StartsWith(className + ".", StringComparison.Ordinal)) name = name.Substring(className.Length + 1);
                methods[id] = className + "." + name;
            }
            foreach (var result in doc.Descendants(Ns + "UnitTestResult"))
            {
                string testId = (string)result.Attribute("testId");
                if (testId == null || !methods.TryGetValue(testId, out var key)) continue;
                string outcome = (string)result.Attribute("outcome") ?? "";
                _byMethod.TryGetValue(key, out var counts);
                switch (outcome)
                {
                    case "Passed":
                        counts.Passed++;
                        break;
                    case "Failed":
                    case "Error":
                    case "Timeout":
                    case "Aborted":
                        counts.Failed++;
                        break;
                    default: // NotExecuted, Inconclusive, Pending, ...
                        counts.NotRun++;
                        break;
                }
                _byMethod[key] = counts;
            }
            Files.Add(file);
        }

        private static string StripArguments(string name)
        {
            if (name == null) return null;
            int paren = name.IndexOf('(');
            return paren < 0 ? name : name.Substring(0, paren);
        }
    }
}
