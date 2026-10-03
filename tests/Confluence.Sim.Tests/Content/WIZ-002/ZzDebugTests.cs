using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Spells;
using Xunit.Abstractions;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    public class ZzDebugTests
    {
        private readonly ITestOutputHelper _out;

        public ZzDebugTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void Debug_Wake_Cool()
        {
            var wet = BramKit.FlatWith(BramKit.Water(new Vec2(0f, -3.17f), 4f, 1f, 3));
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-307")), wet);
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            w.Step(TestWorlds.A.Selecting(1), TestWorlds.B);
            BramKit.Cast(w, 1, 15, TestWorlds.A.Selecting(1));
            for (int t = 0; t < 60; t++)
            {
                BramKit.Run(w, 1, TestWorlds.A.Selecting(1));
                var s = BramKit.Entity<HeatStripEntity>(w);
                var u = BramKit.Reactions(w).UnitsAt(new Vec3(1f, 0f, -3.17f));
                _out.WriteLine(w.Tick + ": strip=" + (s == null ? "null" : s.Alive.ToString()) + " owned=" + w.Owned.Count(0, OwnedEntityKind.Field) + " heat=" + u.Heat + " water=" + u.Water + " fields=" + string.Join(",", w.Owned.Entities.Select(o => o.DefinitionId + "/" + o.Owner + "/" + o.Kind)));
            }
        }
    }
}
