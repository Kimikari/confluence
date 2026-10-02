using System;
using Confluence.Core.Time;

namespace Confluence.Core.Tests.Time
{
    public class TicksTests
    {
        [Fact]
        public void Tick_Rate_Is_Sixty()
        {
            Assert.Equal(60, Ticks.PerSecond);
            Assert.Equal(1f / 60f, Ticks.Dt);
            Assert.Equal(1.0 / 60.0, Ticks.DtDouble);
        }

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(-1.0, 0)]
        [InlineData(-0.001, 0)]
        [InlineData(0.0001, 1)]      // positive durations last at least one tick
        [InlineData(0.008, 1)]       // 0.48 ticks
        [InlineData(1.0 / 120.0, 1)] // exactly half a tick rounds up
        [InlineData(1.0, 60)]
        [InlineData(0.15, 9)]        // re-cast lock (spec L90)
        [InlineData(0.12, 7)]        // perfect guard window 7.2 ticks (spec L104)
        [InlineData(0.18, 11)]       // butt strike windup 10.8 ticks (spec L102)
        [InlineData(0.22, 13)]       // dodge 13.2 ticks (spec L82)
        [InlineData(0.175, 11)]      // 10.5 ticks rounds up despite binary representation
        [InlineData(0.6, 36)]
        [InlineData(1.5, 90)]
        [InlineData(90.0, 5400)]
        public void FromSeconds_Rounds_To_Nearest_Tick(double seconds, int expected)
        {
            Assert.Equal(expected, Ticks.FromSeconds(seconds));
        }

        [Fact]
        public void FromSeconds_Float_Overload_Matches_Decimal_Intent()
        {
            Assert.Equal(9, Ticks.FromSeconds(0.15f));
            Assert.Equal(11, Ticks.FromSeconds(0.175f));
            Assert.Equal(15, Ticks.FromSeconds(0.25f));
            Assert.Equal(13, Ticks.FromSeconds(0.22f));
            Assert.Equal(0, Ticks.FromSeconds(0f));
            Assert.Equal(0, Ticks.FromSeconds(float.NaN));
            Assert.Equal(1, Ticks.FromSeconds(1e-6f));
        }

        [Fact]
        public void FromSeconds_Handles_NaN_And_Saturates()
        {
            Assert.Equal(0, Ticks.FromSeconds(double.NaN));
            Assert.Equal(0, Ticks.FromSeconds(double.NegativeInfinity));
            Assert.Equal(int.MaxValue, Ticks.FromSeconds(double.PositiveInfinity));
            Assert.Equal(int.MaxValue, Ticks.FromSeconds(1e12));
        }

        [Fact]
        public void ToSeconds_Is_Exact_For_Whole_Seconds()
        {
            Assert.Equal(1f, Ticks.ToSeconds(60));
            Assert.Equal(90f, Ticks.ToSeconds(5400));
            Assert.Equal(55f, Ticks.ToSeconds(3300));
            Assert.Equal(0.5f, Ticks.ToSeconds(30));
            Assert.Equal(0f, Ticks.ToSeconds(0));
            Assert.Equal(85.0, Ticks.ToSecondsDouble(5100));
        }

        [Fact]
        public void Milliseconds_Convert_Through_Seconds()
        {
            Assert.Equal(9, Ticks.FromMilliseconds(150)); // lag compensation window (spec L423)
            Assert.Equal(6, Ticks.FromMilliseconds(100));
            Assert.Equal(0, Ticks.FromMilliseconds(0));
            Assert.Equal(1, Ticks.FromMilliseconds(1));
        }

        [Fact]
        public void Round_Trip_Is_Stable_For_Tick_Multiples()
        {
            for (int t = 1; t < 6000; t += 7)
            {
                Assert.Equal(t, Ticks.FromSeconds(Ticks.ToSecondsDouble(t)));
                Assert.Equal(t, Ticks.FromSeconds(Ticks.ToSeconds(t)));
            }
        }
    }
}
