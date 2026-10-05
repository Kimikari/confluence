using System.Collections.Generic;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>DuelWorld.RemoveEventObserver: short-lived observers unregister; a removal during delivery is deferred.</summary>
    public class EventObserverRemovalTests
    {
        private sealed class ProbeEvent : DuelEvent
        {
            public override DuelEventType Type => DuelEventType.Spell;
        }

        private sealed class Recorder : IDuelEventObserver
        {
            public readonly List<int> Seen = new List<int>();
            public IDuelEventObserver RemoveOnEvent;

            public void OnEvent(DuelWorld world, DuelEvent e)
            {
                Seen.Add(e.Id);
                if (RemoveOnEvent != null) world.RemoveEventObserver(RemoveOnEvent);
            }
        }

        [Fact]
        public void A_Removed_Observer_Sees_No_Further_Events()
        {
            DuelWorld world = TestWorlds.Duel();
            int baseline = world.EventObserverCount;
            var a = new Recorder();
            world.AddEventObserver(a);
            world.RecordEvent(new ProbeEvent(), -1);
            Assert.True(world.RemoveEventObserver(a));
            Assert.False(world.RemoveEventObserver(a));
            world.RecordEvent(new ProbeEvent(), -1);
            Assert.Single(a.Seen);
            Assert.Equal(baseline, world.EventObserverCount);
        }

        [Fact]
        public void A_Removal_During_Delivery_Takes_Effect_After_It_And_Skips_No_Other_Observer()
        {
            DuelWorld world = TestWorlds.Duel();
            int baseline = world.EventObserverCount;
            var first = new Recorder();
            var second = new Recorder();
            var third = new Recorder();
            first.RemoveOnEvent = first; // removes itself while the event is delivered
            world.AddEventObserver(first);
            world.AddEventObserver(second);
            world.AddEventObserver(third);
            ProbeEvent e = world.RecordEvent(new ProbeEvent(), -1);
            Assert.Equal(new[] { e.Id }, first.Seen);
            Assert.Equal(new[] { e.Id }, second.Seen);
            Assert.Equal(new[] { e.Id }, third.Seen);
            Assert.Equal(baseline + 2, world.EventObserverCount);
            world.RecordEvent(new ProbeEvent(), -1);
            Assert.Single(first.Seen);
            Assert.Equal(2, second.Seen.Count);
            Assert.Equal(2, third.Seen.Count);
        }
    }
}
