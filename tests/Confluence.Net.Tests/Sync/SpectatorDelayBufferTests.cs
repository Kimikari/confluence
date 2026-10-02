using System;
using System.Collections.Generic;
using Confluence.Core.Time;
using Confluence.Net.Sync;

namespace Confluence.Net.Tests.Sync
{
    public sealed class SpectatorDelayBufferTests
    {
        [Fact]
        public void Delay_Is_Never_Shorter_Than_Fifteen_Seconds()
        {
            // Spec L391: a minimum 15 second delay while a match remains active.
            Assert.Equal(15 * Ticks.PerSecond, NetRules.SpectatorDelayMinTicks);
            Assert.Equal(NetRules.SpectatorDelayMinTicks, new SpectatorDelayBuffer<int>(60).DelayTicks);
            Assert.Equal(NetRules.SpectatorDelayMinTicks, SpectatorDelayBuffer<int>.ForSeconds(2f).DelayTicks);

            // Private hosts may choose a longer delay.
            Assert.Equal(Ticks.FromSeconds(45f), SpectatorDelayBuffer<int>.ForSeconds(45f).DelayTicks);
        }

        [Fact]
        public void Items_Are_Released_Once_They_Are_The_Delay_Old_In_Order()
        {
            var buffer = new SpectatorDelayBuffer<string>(0);
            int delay = buffer.DelayTicks;
            buffer.Push(100, "cast");
            buffer.Push(100, "hit");
            buffer.Push(130, "reaction");

            var output = new List<string>();
            Assert.Equal(0, buffer.Release(100 + delay - 1, output));
            Assert.Equal(2, buffer.Release(100 + delay, output));
            Assert.Equal(new[] { "cast", "hit" }, output);
            Assert.Equal(1, buffer.Count);

            Assert.Equal(1, buffer.Release(130 + delay + 500, output));
            Assert.Equal(new[] { "cast", "hit", "reaction" }, output);
        }

        [Fact]
        public void Out_Of_Order_Pushes_Are_Refused_And_Match_End_Releases_Everything()
        {
            var buffer = new SpectatorDelayBuffer<int>(0);
            buffer.Push(10, 1);
            Assert.Throws<ArgumentException>(() => buffer.Push(9, 2));
            buffer.Push(11, 3);

            var output = new List<int>();
            Assert.Equal(2, buffer.ReleaseAll(output));
            Assert.Equal(new[] { 1, 3 }, output);
            Assert.Equal(0, buffer.Count);
        }
    }
}
