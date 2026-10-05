using Confluence.Core.Geometry;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// A public, inert world marker with geometry that a content record leaves for both players to read (spec L433 cue,
    /// L447 counterplay readability): a glowing path, a telegraphed exit, a marked spot. It has no rule effect and no
    /// collision; it is an authoritative event both clients receive, so each can draw the marker from
    /// <see cref="From"/> to <see cref="To"/> until <see cref="ExpireTick"/>. Emitted with <see cref="EffectHook"/>'s
    /// <c>EmitMarker</c>.
    /// </summary>
    public sealed class MarkerEvent : DuelEvent
    {
        /// <summary>The record that left the marker.</summary>
        public string SourceId { get; internal set; }

        /// <summary>The attached effect's key (source id and attach order).</summary>
        public string EffectKey { get; internal set; }

        /// <summary>The marker's presentation name (for example "afterimage-trail", "wall-exit").</summary>
        public string Cue { get; internal set; }

        /// <summary>Where the marker starts (a path's origin, a motion's start).</summary>
        public Vec3 From { get; internal set; }

        /// <summary>Where the marker ends (a path's end point, a motion's predicted destination).</summary>
        public Vec3 To { get; internal set; }

        /// <summary>Unit direction the marker shows (a casting direction, a motion direction), or zero.</summary>
        public Vec3 Direction { get; internal set; }

        /// <summary>How long the marker stays visible, in ticks.</summary>
        public int DurationTicks { get; internal set; }

        /// <summary>The first tick at which the marker is gone.</summary>
        public int ExpireTick { get; internal set; }

        /// <summary>A record-specific magnitude (distance, mana), or 0.</summary>
        public float Amount { get; internal set; }

        /// <summary>Whether the marker is visible at tick <paramref name="tick"/>.</summary>
        public bool VisibleAt(int tick) => tick >= Tick && tick < ExpireTick;

        public override DuelEventType Type => DuelEventType.Effect;

        public override string ToString() => base.ToString() + " marker " + Cue + " " + SourceId;
    }
}
