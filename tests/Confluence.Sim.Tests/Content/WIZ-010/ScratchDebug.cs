using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Xunit.Abstractions;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    public class ScratchDebug
    {
        private readonly ITestOutputHelper _out;

        public ScratchDebug(ITestOutputHelper o)
        {
            _out = o;
        }

        [Fact]
        public void Debug()
        {
            HalenDuel d = BubbleBucklerTests.Raised(HalenTestKit.Duel(other: HalenTestKit.Bram()));
            Vec3 ground = new Vec3(0f, 0f, -4.5f);
            Vec3 aim = ground - d.P1.Eye;
            PlayerInput bram = TestWorlds.B.Facing(CMath.YawOf(aim), CMath.PitchOf(aim));
            d.Other = bram;
            for (int i = 0; i < 15; i++) d.Step(InputButtons.None, InputButtons.Primary);
            d.Step();
            d.Run(90);
            foreach (var e in d.World.EventLog)
            {
                if (e is CastDuelEvent c && c.Player == 1) _out.WriteLine(c.Tick + " cast " + c.Cast.Kind + " " + c.Cast.SpellId + " " + c.Cast.Reject);
                if (e is SpellEvent s) _out.WriteLine(s.Tick + " spell " + s.Kind + " " + s.SpellId + " " + s.Position + " t" + s.TargetPlayer);
                if (e is DamageEvent dm) _out.WriteLine(dm.Tick + " dmg " + dm.SourceId + " " + dm.Kind + " " + dm.Dealt + " to " + dm.Target);
            }
        }
    }
}
