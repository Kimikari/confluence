using System.Collections.Generic;

namespace Confluence.Match.Tests.Fakes
{
    /// <summary>Returns a preview echoing the request, and records every request.</summary>
    internal sealed class FakeArenaProvider : IArenaProvider
    {
        public List<ArenaRequest> Requests { get; } = new List<ArenaRequest>();

        public ArenaPreview RequestArena(ArenaRequest request)
        {
            Requests.Add(request);
            string id = "arena-r" + request.Round + "-a" + request.Attempt + "-" + request.ArenaSeed.ToString("X16");
            return new ArenaPreview(id, request.ArenaSeed, request.Diameter, "BIO-001", "Rain pools refill",
                new[] { "MOD-001", "MOD-002" }, new[] { "PROP-001", "PROP-001" });
        }
    }
}
