using Confluence.Core.Content;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// The structural tag changes of WIZ-002's transformations (spec L243, L537, L543, L637; DECISIONS D-012), declared
    /// once in Core's process-wide <see cref="StructuralDeltas"/> registry so Economy's structural profile and the Sim's
    /// form resolver read the same tags. Each transformation behavior registers them when it is constructed (the content
    /// registry scan instantiates every behavior), so any process that loads the Sim content sees them. Only changes to
    /// the spell's structural vocabulary are declared; the other twelve WIZ-002 transformations keep their base tags.
    /// <list type="bullet">
    /// <item>AUG-307 Sweep Wake: SPL-010 gains <c>field</c> ("field allocation required").</item>
    /// <item>AUG-310 Haze Reservoir: SPL-011 gains <c>cone</c> (its recast vent is a forward cone delivery).</item>
    /// <item>AUG-316 Boiler Mine: SPL-014 gains <c>trap</c> and <c>field</c> (a proximity mine consumes a field
    /// allocation, spec L510, L543).</item>
    /// <item>AUG-317 Lash Knot: SPL-015 loses <c>burn</c> ("Burn removed").</item>
    /// </list>
    /// </summary>
    public static class Wiz002StructuralDeltas
    {
        private static readonly object Gate = new object();
        private static bool _registered;

        public static void EnsureRegistered()
        {
            lock (Gate)
            {
                if (_registered && StructuralDeltas.TryGet(SweepWake.Id, out _)) return;
                StructuralDeltas.Register(SweepWake.Id, "SPL-010", new[] { "field" }, null);
                StructuralDeltas.Register(HazeReservoir.Id, "SPL-011", new[] { "cone" }, null);
                StructuralDeltas.Register(BoilerMine.Id, "SPL-014", new[] { "trap", "field" }, null);
                StructuralDeltas.Register(LashKnot.Id, "SPL-015", null, new[] { "burn" });
                _registered = true;
            }
        }
    }
}
