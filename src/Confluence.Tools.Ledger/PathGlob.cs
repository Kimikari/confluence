using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Confluence.Tools.Ledger
{
    /// <summary>Minimal glob expansion: '*' within a segment, '**' across segments, '?' for one character.</summary>
    public static class PathGlob
    {
        public static IEnumerable<string> Expand(string root, string pattern)
        {
            string full = (Path.IsPathRooted(pattern) ? pattern : Path.Combine(root, pattern)).Replace('\\', '/');
            if (File.Exists(full)) return new[] { Path.GetFullPath(full) };
            if (Directory.Exists(full))
                return Directory.EnumerateFiles(full, "Confluence.*.Tests.dll", SearchOption.AllDirectories)
                    .Where(p => !IsUnderObj(p))
                    .Select(Path.GetFullPath);

            var segments = full.Split('/');
            int firstWild = Array.FindIndex(segments, s => s.IndexOfAny(new[] { '*', '?' }) >= 0);
            if (firstWild < 0) return Enumerable.Empty<string>();
            string baseDir = string.Join("/", segments.Take(firstWild));
            if (baseDir.Length == 0) baseDir = "/";
            if (!Directory.Exists(baseDir)) return Enumerable.Empty<string>();

            var regex = new Regex("^" + ToRegex(segments.Skip(firstWild)) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string prefix = Path.GetFullPath(baseDir).Replace('\\', '/').TrimEnd('/') + "/";
            return Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetFullPath(p))
                .Where(p => regex.IsMatch(p.Replace('\\', '/').Substring(prefix.Length)))
                .ToList();
        }

        private static string ToRegex(IEnumerable<string> segments)
        {
            var list = segments.ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                bool last = i == list.Count - 1;
                if (list[i] == "**")
                {
                    sb.Append(last ? ".*" : "(?:[^/]+/)*"); // zero or more directories
                    continue;
                }
                foreach (char c in list[i])
                {
                    if (c == '*') sb.Append("[^/]*");
                    else if (c == '?') sb.Append("[^/]");
                    else sb.Append(Regex.Escape(c.ToString()));
                }
                if (!last) sb.Append('/');
            }
            return sb.ToString();
        }

        private static bool IsUnderObj(string path)
        {
            return path.Replace('\\', '/').Split('/').Any(s => s.Equals("obj", StringComparison.OrdinalIgnoreCase));
        }
    }
}
