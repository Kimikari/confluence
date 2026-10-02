using System.IO;
using System.Linq;
using Confluence.Core.Content;

namespace Confluence.Casting.Tests.Support
{
    /// <summary>Reads single lines of the read-only design document next to the catalogue.</summary>
    internal static class SpecText
    {
        public static string Line(int number)
        {
            string path = Path.Combine(Path.GetDirectoryName(Catalogue.FindDefaultPath()), "confluence-design.md");
            return File.ReadLines(path).Skip(number - 1).First();
        }
    }
}
