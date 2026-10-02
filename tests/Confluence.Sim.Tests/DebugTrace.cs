using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Movement;
using Confluence.Sim.World;
using Xunit.Abstractions;

namespace Confluence.Sim.Tests
{
    public class DebugTrace
    {
        private readonly ITestOutputHelper _out;

        public DebugTrace(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void Trace()
        {
            float halfLength = 2f;
            float rise30 = 2f * halfLength * System.MathF.Tan(30f * CMath.Deg2Rad);
            CollisionWorld world = TestWorlds.Collision(
                TestWorlds.Ramp(2, new Vec3(0f, 0f, 3f), 1.5f, halfLength, rise30),
                TestWorlds.Box(3, new Vec3(0f, 0f, 6f), new Vec3(3f, rise30, 2f), SolidKind.Platform));
            MotorState s = TestWorlds.Settled(world, Vec3.Zero);
            _out.WriteLine("settled " + s.Position + " g=" + s.Grounded);
            for (int i = 0; i < 40; i++)
            {
                CharacterMotor.Step(ref s, TestWorlds.Forward(0f, 1f, 0f), world, Ticks.Dt, out MotorReport r);
                _out.WriteLine(i + " " + s.Position + " v=" + s.Velocity + " g=" + s.Grounded + " n=" + s.GroundNormal);
            }

            var cap = Capsule.Vertical(new Vec3(0f, 0f, 2.2f), 1.8f, 0.4f);
            bool hit = world.DeepestPenetration(cap, out Penetration p, out int id);
            _out.WriteLine("pen " + hit + " " + p.Normal + " " + p.Depth + " id " + id);
        }
    }
}
