using System.Linq;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Xunit.Abstractions;

namespace Confluence.Sim.Tests.Content.Halen
{
    public class Probe
    {
        private readonly ITestOutputHelper _o;
        public Probe(ITestOutputHelper o) { _o = o; }

        [Fact]
        public void Run()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(PoolDivide.Id, 1)));
            d.Signature();
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            d.AimAt(new Vec3(0f, 0f, 3f));
            d.Cast(WaterPoolTests.Slot);
            d.Run(40);
            foreach (var e in d.World.EventLog)
            {
                if (e is CastDuelEvent c && c.Player == 0) _o.WriteLine(c.Tick + " cast " + c.Cast.Kind + " " + c.Cast.SpellId + " " + c.Cast.Reject);
                if (e is SpellEvent s) _o.WriteLine(s.Tick + " spell " + s.Kind + " " + s.SpellId + " " + s.Position);
            }
        }
    }
}
