using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
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
        public void Debug_Lash_Steam()
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(BramKit.LateKit, BramKit.Aug("AUG-318")), BramKit.FlatWith(BramKit.Water(new Vec2(0f, -3f), 1f, 1f, 2)));
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            _out.WriteLine("before " + BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3f)).Water + " / " + BramKit.Reactions(w).UnitsAt(new Vec3(0.25f, 0f, -3.25f)).Water);
            BramKit.Cast(w, 0);
            for (int i = 0; i < 30; i++)
            {
                BramKit.Run(w, 1);
                _out.WriteLine(w.Tick + " water " + BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3f)).Water + " heat " + BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3f)).Heat + " vols " + BramKit.Reactions(w).Volumes.Count);
            }

            foreach (SpellEvent e in BramKit.Spell(w, SpellEventKind.Steamed)) _out.WriteLine(e + " " + e.Position + " " + e.Direction);
        }
    }
}
