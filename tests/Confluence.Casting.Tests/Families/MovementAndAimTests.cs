using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Geometry;

namespace Confluence.Casting.Tests.Families
{
    public class MovementTrackerTests
    {
        private static CastInputFrame Body(int tick, Vec3 position, Vec3 velocity = default, float aimYaw = 0f, InputButtons held = InputButtons.None,
            params CombatEventKind[] events)
        {
            var frame = new CastInputFrame { Tick = tick, Position = position, Velocity = velocity, AimYawDeg = aimYaw, Held = held, Grounded = true };
            if (events.Length > 0)
            {
                var list = new List<CombatEvent>();
                foreach (var kind in events) list.Add(new CombatEvent(kind, tick));
                frame.CombatEvents = list;
            }

            return frame;
        }

        [Fact]
        public void Real_Travel_Builds_Ordered_Legs_Relative_To_Aim()
        {
            var tracker = new MovementTracker(minLegMeters: 0.5f);
            var p = Vec3.Zero;
            tracker.Reset(Body(0, p));
            for (int t = 1; t <= 10; t++) tracker.Sample(Body(t, p = p + new Vec3(0f, 0f, 0.1f)));   // forward (+Z at yaw 0)
            for (int t = 11; t <= 20; t++) tracker.Sample(Body(t, p = p + new Vec3(0.1f, 0f, 0f)));  // right (+X)
            Assert.Equal(2f, tracker.TravelMeters, 4);
            Assert.Equal(new[] { RelativeDirection.Forward, RelativeDirection.Right }, tracker.Legs);
            Assert.Equal(1.41421f, tracker.DisplacementMeters, 4);

            // The same world motion with the camera turned 90 degrees right reads as forward then left.
            var turned = new MovementTracker(0.5f);
            p = Vec3.Zero;
            turned.Reset(Body(0, p, aimYaw: 90f));
            for (int t = 1; t <= 10; t++) turned.Sample(Body(t, p = p + new Vec3(0.1f, 0f, 0f), aimYaw: 90f));
            for (int t = 11; t <= 20; t++) turned.Sample(Body(t, p = p + new Vec3(0f, 0f, 0.1f), aimYaw: 90f));
            Assert.Equal(new[] { RelativeDirection.Forward, RelativeDirection.Left }, turned.Legs);
        }

        [Fact]
        public void Shaking_Keys_Against_A_Wall_Produces_No_Travel()
        {
            // Spec L482: movement grammars "cannot be activated by shaking keys against a wall".
            var tracker = new MovementTracker();
            var wall = new Vec3(3f, 0f, 3f);
            tracker.Reset(Body(0, wall));
            for (int t = 1; t <= 60; t++) tracker.Sample(Body(t, wall, held: t % 2 == 0 ? InputButtons.Sprint : InputButtons.None));
            Assert.Equal(0f, tracker.TravelMeters);
            Assert.Empty(tracker.Legs);
        }

        [Fact]
        public void Forced_Displacement_And_Teleports_Add_No_Travel()
        {
            var tracker = new MovementTracker();
            tracker.Reset(Body(0, Vec3.Zero));
            tracker.Sample(Body(1, new Vec3(0f, 0f, 3f), events: CombatEventKind.ForcedDisplacement));
            tracker.Sample(Body(2, new Vec3(0f, 0f, 9f), events: CombatEventKind.Teleported));
            Assert.Equal(0f, tracker.TravelMeters);
            Assert.True(tracker.Displaced);
            Assert.Empty(tracker.Legs);
            Assert.False(tracker.Collided);
            tracker.Sample(Body(3, new Vec3(0f, 0f, 9f), events: CombatEventKind.TerrainCollision));
            Assert.True(tracker.Collided);
        }

        [Fact]
        public void Declared_Velocity_Counts_Consecutive_Aligned_Ticks()
        {
            // WIZ-029 shape: at least 4 m/s within 20 degrees of the declared vector.
            var tracker = new MovementTracker();
            var p = Vec3.Zero;
            tracker.Reset(Body(0, p));
            tracker.Declare(0f, 4f, 20f);
            var along = new Vec3(0f, 0f, 5f);
            for (int t = 1; t <= 3; t++) tracker.Sample(Body(t, p = p + along / 60f, along));
            Assert.Equal(3, tracker.AlignedTicks);

            var angled = new Vec3(5f * 0.5f, 0f, 5f * 0.866f);                      // 30 degrees off
            tracker.Sample(Body(4, p = p + angled / 60f, angled));
            Assert.Equal(0, tracker.AlignedTicks);
            tracker.Sample(Body(5, p = p + along / 60f, along));
            var slow = new Vec3(0f, 0f, 3f);
            tracker.Sample(Body(6, p = p + slow / 60f, slow));
            Assert.Equal(0, tracker.AlignedTicks);
        }
    }

    public class AimTrackerTests
    {
        private static CastInputFrame Aim(int tick, float yaw, float pitch = 0f) =>
            new CastInputFrame { Tick = tick, AimYawDeg = yaw, AimPitchDeg = pitch };

        [Fact]
        public void Steadiness_Covers_Exactly_The_Final_Window()
        {
            // WIZ-001 shape: within 2 degrees for the final 0.25 s (15 ticks).
            var tracker = new AimTracker(15);
            tracker.Reset(Aim(0, 10f));
            Assert.Equal(float.PositiveInfinity, tracker.SteadinessDeg(15));
            for (int t = 1; t <= 5; t++) tracker.Sample(Aim(t, 10f + t));               // sweeping
            for (int t = 6; t <= 20; t++) tracker.Sample(Aim(t, 16f + (t % 2) * 0.5f)); // steady within 0.5 degrees
            Assert.True(tracker.SteadinessDeg(15) < 0.55f); // float acos resolves about 0.02 degrees near zero
            Assert.True(tracker.SteadinessDeg(15) > 0.45f);
            tracker.Sample(Aim(21, 19f));
            Assert.True(tracker.SteadinessDeg(15) > 2f);
        }

        [Fact]
        public void Checkpoints_Count_Only_In_Order_And_Never_Reference_A_Target()
        {
            // WIZ-025 shape: three checkpoints around the origin aim, then finish near the initial aim.
            var tracker = new AimTracker(4);
            tracker.Reset(Aim(0, 30f, 5f));
            tracker.SetCheckpoints(new[] { new AimOffset(-4f, 0f), new AimOffset(0f, 4f), new AimOffset(4f, 0f) }, 1f);
            tracker.Sample(Aim(1, 26f, 5f));
            tracker.Sample(Aim(2, 30f, 9f));
            tracker.Sample(Aim(3, 34.5f, 5f));
            Assert.Equal(3, tracker.CheckpointsPassed);
            Assert.Equal(3, tracker.CheckpointsCompletedTick);
            Assert.False(tracker.CheckpointOrderBroken);
            tracker.Sample(Aim(4, 30.5f, 5f));
            Assert.True(tracker.AngleFromOriginDeg < 1.5f);

            var skipped = new AimTracker(4);
            skipped.Reset(Aim(0, 30f, 5f));
            skipped.SetCheckpoints(new[] { new AimOffset(-4f, 0f), new AimOffset(0f, 4f), new AimOffset(4f, 0f) }, 1f);
            skipped.Sample(Aim(1, 34f, 5f));
            Assert.True(skipped.CheckpointOrderBroken);
        }

        [Fact]
        public void Socket_Angle_Is_Measured_From_The_Recorded_Aim()
        {
            // WIZ-028 shape: finish within 1.5 degrees of a chosen offset socket from the recorded vector.
            var tracker = new AimTracker(1);
            tracker.Reset(Aim(0, -170f, 0f));
            tracker.Sample(Aim(24, 175f, 2f)); // wraps through 180: a 15 degree turn left of -170
            Assert.True(tracker.AngleFromDeg(new AimOffset(-15f, 2f)) < 0.1f);
            Assert.True(tracker.AngleFromDeg(new AimOffset(15f, 2f)) > 1.5f);
        }
    }
}
