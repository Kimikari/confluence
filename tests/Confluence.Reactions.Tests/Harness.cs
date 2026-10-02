using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>A bare 24 m world centered on the origin with a tick counter and body list.</summary>
    internal sealed class Harness
    {
        public const int PlayerA = 0;
        public const int PlayerB = 1;

        public Harness(float diameter = 24f)
        {
            World = new ReactionWorld(CellGrid.ForDiameter(Vec2.Zero, diameter));
        }

        public Harness(ReactionWorld world)
        {
            World = world;
            Tick = world.CombatStartTick;
        }

        public ReactionWorld World { get; }
        public CellGrid Grid => World.Grid;
        public int Tick { get; private set; }
        public List<ReactionBody> Bodies { get; } = new List<ReactionBody>();

        /// <summary>Steps one tick and returns the result (valid until the next step).</summary>
        public StepResult Step() => World.Step(Tick++, Bodies);

        /// <summary>Steps n ticks, collecting every result's damage and events.</summary>
        public StepLog StepFor(int ticks)
        {
            var log = new StepLog();
            for (int i = 0; i < ticks; i++)
            {
                log.Add(Step());
            }

            return log;
        }

        public StepLog StepSeconds(float seconds) => StepFor(Ticks.FromSeconds(seconds));

        /// <summary>World center of cell (x, z) at height 0.</summary>
        public Vec3 CellPoint(int x, int z)
        {
            Vec2 c = Grid.CellCenter(x, z);
            return new Vec3(c.X, 0f, c.Y);
        }

        public int Index(Vec3 p)
        {
            Grid.TryGetIndex(p, out int i);
            return i;
        }

        public void Field(Material m, Footprint footprint, int strength, float lifetimeSec, int owner = PlayerA, int source = 1, int fieldId = 0)
        {
            World.Deposit(MaterialDeposit.Field(m, footprint, 0f, lifetimeSec, owner, source, strength, fieldId));
        }

        public void Point(Material m, Vec3 at, int strength, int lifetimeTicks = 0, int owner = PlayerA, int source = 1)
        {
            MaterialDeposit d = MaterialDeposit.QuickProjectile(m, at, owner, source, lifetimeTicks);
            d.Strength = strength;
            World.Deposit(d);
        }

        public ObscurationVolume OnlyVolume()
        {
            Xunit.Assert.Single(World.Volumes);
            return World.Volumes[0];
        }
    }

    /// <summary>Accumulated results across several steps.</summary>
    internal sealed class StepLog
    {
        public List<ReactionEvent> Events { get; } = new List<ReactionEvent>();
        public List<ContactDamage> Damage { get; } = new List<ContactDamage>();
        public List<PropImpulse> Impulses { get; } = new List<PropImpulse>();
        public List<VolumeEvent> VolumeEvents { get; } = new List<VolumeEvent>();

        public void Add(StepResult result)
        {
            Events.AddRange(result.Events);
            Damage.AddRange(result.Damage);
            Impulses.AddRange(result.PropImpulses);
            VolumeEvents.AddRange(result.VolumeEvents);
        }

        public void Add(StepLog other)
        {
            Events.AddRange(other.Events);
            Damage.AddRange(other.Damage);
            Impulses.AddRange(other.Impulses);
            VolumeEvents.AddRange(other.VolumeEvents);
        }

        public int Count(ReactionKind kind)
        {
            int n = 0;
            foreach (ReactionEvent e in Events)
            {
                if (e.Kind == kind) n++;
            }

            return n;
        }

        public float DamageTo(int body)
        {
            float total = 0f;
            foreach (ContactDamage d in Damage)
            {
                if (d.BodyId == body) total += d.Amount;
            }

            return total;
        }
    }
}
