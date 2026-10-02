using Confluence.Core.Geometry;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Props
{
    public enum PropChange
    {
        /// <summary>A force impulse set a loose prop moving (spec L155, L173).</summary>
        Pushed,

        /// <summary>An anchored prop (or one without a collision body) only shook (spec L173).</summary>
        Shaken,

        /// <summary>A moving prop stopped: distance spent, blocked by a solid, or at an unsupported edge.</summary>
        Stopped,

        /// <summary>A moving prop struck a player (collision damage applies above the threshold, at most once per second).</summary>
        Collided,

        Damaged,
        Destroyed,
    }

    /// <summary>Prop state change (spec L433 PropState, L171-L173).</summary>
    public sealed class PropEvent : DuelEvent
    {
        public PropChange Change { get; internal set; }

        /// <summary>Placement id in the arena layout.</summary>
        public int PropId { get; internal set; }

        /// <summary>Catalogue id (PROP-xxx).</summary>
        public string DefinitionId { get; internal set; }

        public Vec3 Position { get; internal set; }

        /// <summary>Push distance, collision damage, or durability damage.</summary>
        public float Amount { get; internal set; }

        /// <summary>The player a collision struck, or -1.</summary>
        public int TargetPlayer { get; internal set; } = -1;

        public float DurabilityAfter { get; internal set; }

        public override DuelEventType Type => DuelEventType.Prop;
    }
}
