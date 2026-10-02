using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Movement
{
    public class CharacterMotorTests
    {
        private static readonly CollisionWorld Flat = TestWorlds.Collision();

        [Fact]
        public void Walk_Reaches_5_5_Meters_Per_Second()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), Flat, 30);
            float z0 = s.Position.Z;
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), Flat, 60);
            Assert.Equal(5.5f, s.Position.Z - z0, 3);
            Assert.Equal(5.5f, s.Velocity.LengthXZ(), 3);
            Assert.True(s.Grounded);
            Assert.Equal(0f, s.Position.Y, 4);
        }

        [Fact]
        public void Sprint_Reaches_7_And_Drains_12_Stamina_Per_Second()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = TestWorlds.Forward();
            c.SprintHeld = true;
            c.Stamina = 100f;
            float spent = 0f;
            for (int i = 0; i < 60; i++)
            {
                CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out MotorReport r);
                spent += r.StaminaSpent;
                c.Stamina = 100f - spent;
            }

            Assert.Equal(12f, spent, 3);
            Assert.Equal(GlobalRules.Movement.SprintSpeed, s.Velocity.LengthXZ(), 3);
            Assert.True(s.Sprinting);
        }

        [Fact]
        public void Sprint_Needs_Stamina_For_The_Tick_And_Forward_Input()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = TestWorlds.Forward();
            c.SprintHeld = true;
            c.Stamina = 0.1f; // less than one tick of sprint (0.2)
            TestWorlds.StepMotor(ref s, c, Flat, 60);
            Assert.False(s.Sprinting);
            Assert.Equal(5.5f, s.Velocity.LengthXZ(), 3);

            MotorCommand back = TestWorlds.Forward(0f, -1f);
            back.SprintHeld = true;
            TestWorlds.StepMotor(ref s, back, Flat, 60);
            Assert.False(s.Sprinting);
        }

        [Fact]
        public void Jump_Costs_10_And_Rises_1_1_Meters_With_0_7_Second_Airtime()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = MotorCommand.Default;
            c.JumpPressed = true;
            c.Stamina = 100f;
            CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out MotorReport first);
            Assert.True(first.Jumped);
            Assert.Equal(GlobalRules.Movement.JumpStaminaCost, first.StaminaSpent);

            c.JumpPressed = false;
            float peak = s.Position.Y;
            int airTicks = 1;
            for (; airTicks < 120 && !s.Grounded; airTicks++)
            {
                CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out _);
                peak = Math.Max(peak, s.Position.Y);
            }

            Assert.InRange(peak, 1.05f, 1.12f);
            Assert.InRange(airTicks / 60f, 0.66f, 0.74f);
        }

        [Fact]
        public void Jump_Is_Refused_Without_Full_Cost_Or_In_Air()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = MotorCommand.Default;
            c.JumpPressed = true;
            c.Stamina = 9.9f;
            CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out MotorReport r);
            Assert.False(r.Jumped);

            c.Stamina = 100f;
            CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out r);
            Assert.True(r.Jumped);
            CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out r);
            Assert.False(r.Jumped); // no universal double jump (spec L80)
        }

        [Fact]
        public void Air_Steering_Cannot_Reverse_A_Running_Jump()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), Flat, 60);
            MotorCommand jump = TestWorlds.Forward(0f, -1f);
            jump.JumpPressed = true;
            CharacterMotor.Step(ref s, jump, Flat, Ticks.Dt, out MotorReport r);
            Assert.True(r.Jumped);
            jump.JumpPressed = false;
            int airTicks = 0;
            while (!s.Grounded)
            {
                CharacterMotor.Step(ref s, jump, Flat, Ticks.Dt, out _);
                airTicks++;
                if (airTicks == 21)
                {
                    Assert.True(s.Velocity.Z > 1.5f, "half-way through the jump the run should still carry forward: " + s.Velocity.Z);
                }
            }

            // One ground tick of braking on take-off plus 8 m/s^2 over the 0.7 s jump: at most ~6.4 m/s of change against
            // a 5.5 m/s run, so the landing speed backward stays far below walk speed (a grounded reversal reaches
            // -5.5 m/s within about 0.22 s).
            Assert.True(s.Velocity.Z > -1.0f, "reversed to " + s.Velocity.Z);
        }

        [Fact]
        public void Repeated_Jumps_Never_Exceed_Sprint_Speed()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = TestWorlds.Forward();
            c.SprintHeld = true;
            float maxSpeed = 0f;
            for (int i = 0; i < 600; i++)
            {
                c.JumpPressed = s.Grounded && i % 2 == 0;
                CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out _);
                maxSpeed = Math.Max(maxSpeed, s.Velocity.LengthXZ());
            }

            Assert.True(maxSpeed <= GlobalRules.Movement.SprintSpeed + 1e-3f, "bunny-hop gained speed: " + maxSpeed);
        }

        [Fact]
        public void Dodge_Travels_3_Meters_In_0_22_Seconds_Costs_25_And_Has_A_1_6_Second_Cooldown()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = TestWorlds.Forward(90f); // facing +X, moving forward
            c.DodgePressed = true;
            c.Stamina = 100f;
            Vec3 start = s.Position;
            CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out MotorReport r);
            Assert.True(r.DodgeStarted);
            Assert.Equal(GlobalRules.Movement.DodgeStaminaCost, r.StaminaSpent);
            Assert.Equal(13, CharacterMotor.DodgeTicks); // 0.22 s rounds to 13 ticks

            MotorCommand idle = MotorCommand.Default;
            idle.YawDeg = 90f;
            idle.DodgePressed = true; // still pressed: refused while cooling down
            for (int i = 1; i < CharacterMotor.DodgeTicks; i++)
            {
                CharacterMotor.Step(ref s, idle, Flat, Ticks.Dt, out r);
                Assert.False(r.DodgeStarted);
            }

            Assert.Equal(3f, s.Position.X - start.X, 3);
            Assert.False(s.IsDodging);
            Assert.True(s.Velocity.LengthXZ() <= GlobalRules.Movement.WalkSpeed + 1e-3f); // no slide stacking

            int cooldown = CharacterMotor.DodgeCooldownTicks;
            Assert.Equal(96, cooldown);
            for (int i = CharacterMotor.DodgeTicks; i < cooldown; i++)
            {
                CharacterMotor.Step(ref s, idle, Flat, Ticks.Dt, out r);
                Assert.False(r.DodgeStarted);
            }

            CharacterMotor.Step(ref s, idle, Flat, Ticks.Dt, out r);
            Assert.True(r.DodgeStarted); // exactly 1.6 s after the first dodge
        }

        [Fact]
        public void Dodge_Without_Input_Goes_Backward_And_Needs_Ground()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = MotorCommand.Default;
            c.DodgePressed = true;
            TestWorlds.StepMotor(ref s, c, Flat, CharacterMotor.DodgeTicks);
            Assert.Equal(-3f, s.Position.Z, 3);

            MotorState air = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand jump = MotorCommand.Default;
            jump.JumpPressed = true;
            CharacterMotor.Step(ref air, jump, Flat, Ticks.Dt, out _);
            CharacterMotor.Step(ref air, c, Flat, Ticks.Dt, out MotorReport r);
            Assert.False(r.DodgeStarted);
        }

        [Fact]
        public void Crouch_Moves_At_Half_Walk_And_Cannot_Stand_Under_A_Low_Ceiling()
        {
            Confluence.Core.Arena.Solid ceiling = TestWorlds.Box(2, new Vec3(0f, 1.4f, 5f), new Vec3(4f, 0.5f, 4f));
            CollisionWorld world = TestWorlds.Collision(ceiling);
            MotorState s = TestWorlds.Settled(world, Vec3.Zero);
            MotorCommand crouch = TestWorlds.Forward();
            crouch.CrouchHeld = true;
            TestWorlds.StepMotor(ref s, crouch, world, 30);
            Assert.True(s.Crouched);
            Assert.Equal(GlobalRules.Movement.CrouchSpeed, s.Velocity.LengthXZ(), 3);
            TestWorlds.StepMotor(ref s, crouch, world, 60);
            Assert.InRange(s.Position.Z, 4f, 6f); // under the 1.4 m ceiling

            MotorCommand stand = MotorCommand.Default;
            TestWorlds.StepMotor(ref s, stand, world, 5);
            Assert.True(s.Crouched); // no headroom

            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), world, 5);
            MotorCommand crawl = TestWorlds.Forward();
            crawl.CrouchHeld = true;
            TestWorlds.StepMotor(ref s, crawl, world, 90);
            TestWorlds.StepMotor(ref s, stand, world, 5);
            Assert.False(s.Crouched);
        }

        [Fact]
        public void Thirty_Degree_Ramp_Is_Walkable_And_Steep_Ramp_Is_Not()
        {
            float halfLength = 2f;
            float rise30 = 2f * halfLength * MathF.Tan(30f * CMath.Deg2Rad);
            CollisionWorld gentle = TestWorlds.Collision(
                TestWorlds.Ramp(2, new Vec3(0f, 0f, 3f), 1.5f, halfLength, rise30),
                TestWorlds.Box(3, new Vec3(0f, 0f, 6f), new Vec3(3f, rise30, 2f), SolidKind.Platform));
            MotorState s = TestWorlds.Settled(gentle, Vec3.Zero);
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), gentle, 70); // up the ramp and onto the 5-7 m platform
            Assert.True(s.Grounded);
            Assert.Equal(rise30, s.Position.Y, 2);
            Assert.True(s.Position.Z > 5.5f);

            float rise45 = 2f * halfLength;
            CollisionWorld steep = TestWorlds.Collision(TestWorlds.Ramp(2, new Vec3(0f, 0f, 3f), 1.5f, halfLength, rise45));
            MotorState t = TestWorlds.Settled(steep, Vec3.Zero);
            TestWorlds.StepMotor(ref t, TestWorlds.Forward(), steep, 150);
            Assert.True(t.Position.Y < 0.6f, "climbed a 45 degree ramp to " + t.Position.Y);
        }

        [Fact]
        public void Steps_Up_To_0_35_Meters_But_Not_Higher()
        {
            CollisionWorld low = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 3f), new Vec3(4f, 0.3f, 2f), SolidKind.Stair));
            MotorState s = TestWorlds.Settled(low, Vec3.Zero);
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(), low, 40); // onto the 2-4 m stair block
            Assert.Equal(0.3f, s.Position.Y, 3);
            Assert.True(s.Position.Z > 2.5f);

            CollisionWorld high = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 3f), new Vec3(4f, 0.5f, 2f)));
            MotorState t = TestWorlds.Settled(high, Vec3.Zero);
            TestWorlds.StepMotor(ref t, TestWorlds.Forward(), high, 60);
            Assert.Equal(0f, t.Position.Y, 3);
            Assert.True(t.Position.Z < 1.61f);
        }

        [Fact]
        public void Walls_Block_And_Slide()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 3f), new Vec3(40f, 3f, 1f)));
            MotorState s = TestWorlds.Settled(world, Vec3.Zero);
            TestWorlds.StepMotor(ref s, TestWorlds.Forward(0f, 1f, 1f), world, 120);
            Assert.InRange(s.Position.Z, 2.0f, 2.11f); // stops 0.4 m (radius) before the wall face at 2.5
            Assert.True(s.Position.X > 4f); // slides sideways along it
        }

        [Fact]
        public void Landing_Reports_Peak_To_Ground_Drop()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 0f), new Vec3(4f, 5f, 4f), SolidKind.Platform));
            MotorState s = TestWorlds.Settled(world, new Vec3(0f, 5f, 0f));
            Assert.True(s.Grounded);
            float drop = 0f;
            bool landed = false;
            for (int i = 0; i < 240 && !landed; i++)
            {
                CharacterMotor.Step(ref s, TestWorlds.Forward(), world, Ticks.Dt, out MotorReport r);
                if (r.Landed && s.Position.Y < 1f)
                {
                    landed = true;
                    drop = r.LandingDrop;
                }
            }

            Assert.True(landed);
            Assert.Equal(5f, drop, 2);
            Assert.Equal(20f, GlobalRules.Terrain.FallDamage(drop), 0);
        }

        [Fact]
        public void Ice_Reduces_Braking_Without_Removing_Control()
        {
            MotorState normal = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorState ice = normal;
            TestWorlds.StepMotor(ref normal, TestWorlds.Forward(), Flat, 60);
            TestWorlds.StepMotor(ref ice, TestWorlds.Forward(), Flat, 60);
            MotorCommand stop = MotorCommand.Default;
            MotorCommand stopIce = MotorCommand.Default;
            stopIce.Surface = new SurfaceInfo { Kind = SurfaceKind.Ice };
            float z0 = normal.Position.Z;
            TestWorlds.StepMotor(ref normal, stop, Flat, 60);
            TestWorlds.StepMotor(ref ice, stopIce, Flat, 60);
            float normalSlide = normal.Position.Z - z0;
            float iceSlide = ice.Position.Z - z0;
            Assert.True(iceSlide > normalSlide * 3f, "ice " + iceSlide + " vs " + normalSlide);
            Assert.Equal(0f, ice.Velocity.LengthXZ(), 3); // braking still works, only slower

            MotorState traction = TestWorlds.Settled(Flat, Vec3.Zero);
            TestWorlds.StepMotor(ref traction, TestWorlds.Forward(), Flat, 60);
            float zt = traction.Position.Z;
            MotorCommand grip = stopIce;
            grip.Traction = 1f;
            TestWorlds.StepMotor(ref traction, grip, Flat, 60);
            Assert.Equal(normalSlide, traction.Position.Z - zt, 3);
        }

        [Fact]
        public void Casting_Factor_And_Speed_Multiplier_Scale_Walk_Speed()
        {
            MotorCommand heavy = TestWorlds.Forward();
            heavy.CastSpeedFactor = GlobalRules.Casting.MoveSpeedFactorHeavyCast;
            Assert.Equal(3.85f, CharacterMotor.MaxSpeed(false, false, heavy), 4);
            MotorCommand quickSprint = TestWorlds.Forward();
            quickSprint.CastSpeedFactor = GlobalRules.Casting.MoveSpeedFactorQuickCast;
            Assert.Equal(5.5f * 0.85f, CharacterMotor.MaxSpeed(false, true, quickSprint), 4);
            MotorCommand stationary = TestWorlds.Forward();
            stationary.CastSpeedFactor = 0f;
            Assert.Equal(0f, CharacterMotor.MaxSpeed(false, false, stationary));
        }

        [Fact]
        public void Knockback_Displacement_Moves_Its_Distance_And_Locks_Input()
        {
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            CharacterMotor.BeginDisplacement(ref s, DisplacementKind.Knockback, new Vec3(12f, 0f, 0f), 20);
            MotorCommand fighting = TestWorlds.Forward(0f, -1f);
            float moved = 0f;
            for (int i = 0; i < 20; i++)
            {
                CharacterMotor.Step(ref s, fighting, Flat, Ticks.Dt, out MotorReport r);
                moved += r.SelfMovedDistance;
            }

            Assert.Equal(4f, s.Position.X, 3);
            Assert.Equal(0f, s.Position.Z, 3); // deliberate movement was prevented
            Assert.Equal(0f, moved); // forced displacement is not valid combat movement (FEAT-005)
            Assert.False(s.IsDisplaced);
        }

        [Fact]
        public void Displacement_Stops_At_The_Boundary_Edge()
        {
            var bounds = new CircleBounds(5f);
            MotorState s = TestWorlds.Settled(Flat, new Vec3(3f, 0f, 0f));
            CharacterMotor.BeginDisplacement(ref s, DisplacementKind.Knockback, new Vec3(12f, 0f, 0f), 20);
            MotorCommand c = MotorCommand.Default;
            c.DisplacementBounds = bounds;
            bool stopped = false;
            for (int i = 0; i < 20; i++)
            {
                CharacterMotor.Step(ref s, c, Flat, Ticks.Dt, out MotorReport r);
                stopped |= r.StoppedAtEdge;
            }

            Assert.True(stopped);
            Assert.InRange(s.Position.X, 4.95f, 5.0001f);
            Assert.False(s.IsDisplaced);
        }

        [Fact]
        public void Confinement_Keeps_The_Feet_In_The_Alcove()
        {
            var alcove = new YawBox(new Vec3(0f, 1.5f, 0f), new Vec3(2f, 1.5f, 2f), 0f);
            MotorState s = TestWorlds.Settled(Flat, Vec3.Zero);
            MotorCommand c = TestWorlds.Forward(0f, 1f, 1f);
            c.Confined = true;
            c.Confinement = alcove;
            TestWorlds.StepMotor(ref s, c, Flat, 300);
            Assert.InRange(s.Position.Z, 1.59f, 1.6001f);
            Assert.InRange(s.Position.X, 1.59f, 1.6001f);
        }

        [Fact]
        public void Kill_Plane_Detects_Outside_Map_Falls()
        {
            CollisionWorld world = TestWorlds.Collision();
            MotorState s = MotorState.At(new Vec3(100f, 0f, 0f), 0f); // beyond the floor
            bool fell = false;
            for (int i = 0; i < 120 && !fell; i++)
            {
                CharacterMotor.Step(ref s, MotorCommand.Default, world, Ticks.Dt, out MotorReport r);
                fell = r.FellOutOfMap;
            }

            Assert.True(fell);
            Assert.True(world.KillPlaneY <= -SimRules.Motor.KillPlaneDepth);
        }

        [Fact]
        public void Motor_Is_Deterministic()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(2f, 0f, 3f), new Vec3(1f, 2f, 1f), SolidKind.Pillar, 30f));
            MotorState a = TestWorlds.Settled(world, Vec3.Zero);
            MotorState b = a;
            var rng = new Confluence.Core.Random.Rng(7);
            for (int i = 0; i < 600; i++)
            {
                MotorCommand c = TestWorlds.Forward(rng.Range(-180f, 180f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                c.JumpPressed = rng.Chance(0.05f);
                c.DodgePressed = rng.Chance(0.02f);
                c.SprintHeld = rng.Chance(0.5f);
                CharacterMotor.Step(ref a, c, world, Ticks.Dt, out _);
                CharacterMotor.Step(ref b, c, world, Ticks.Dt, out _);
                Assert.Equal(a.Position, b.Position);
                Assert.Equal(a.Velocity, b.Velocity);
            }
        }

        private sealed class CircleBounds : IPositionConstraint
        {
            private readonly float _radius;

            public CircleBounds(float radius)
            {
                _radius = radius;
            }

            public bool Contains(Vec3 point) => point.LengthXZ() <= _radius;

            public Vec3 ClampInside(Vec3 point, float margin)
            {
                float r = point.LengthXZ();
                float max = _radius - margin;
                return r <= max ? point : new Vec3(point.X * max / r, point.Y, point.Z * max / r);
            }
        }
    }
}
