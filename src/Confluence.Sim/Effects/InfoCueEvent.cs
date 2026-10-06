using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Effects
{
    /// <summary>What an <see cref="InfoCueEvent"/> shows its owner.</summary>
    public enum InfoCueKind : byte
    {
        /// <summary>A fixed world point (an impact point, a cast origin, a movement origin); never follows anyone.</summary>
        Point,

        /// <summary>A direction only, from the owner toward a heard or seen event; carries no position or distance.</summary>
        Direction,

        /// <summary>A fixed path from <see cref="InfoCueEvent.Position"/> to <see cref="InfoCueEvent.To"/> (an incoming projectile path).</summary>
        Path,

        /// <summary>A fixed area outline (center and radius) such as a cleared cloud's former boundary.</summary>
        Boundary,

        /// <summary>A value about a visible object or the owner (cover health, field or summon life, damage channels).</summary>
        Value,
    }

    /// <summary>
    /// An owner-only information cue (augments-b Information and deception family, spec L120, L433): a HUD or world cue that
    /// only the record's owner receives (the network layer replicates it to its player only), with the stated timing. The
    /// server decides what it may carry: direction-only cues carry no position, fixed markers never track a player, and the
    /// emitting record checks normal sight or sound occlusion first, so a cue never carries data through walls.
    /// </summary>
    public sealed class InfoCueEvent : DuelEvent
    {
        public string SourceId { get; internal set; }

        /// <summary>The attached effect's key (source id and attach order).</summary>
        public string EffectKey { get; internal set; }

        /// <summary>The cue's presentation name (for example "impact-mark", "cast-direction").</summary>
        public string Cue { get; internal set; }

        public InfoCueKind Kind { get; internal set; }

        /// <summary>The fixed world point, path start, or boundary center (unset for a direction-only cue).</summary>
        public bool HasPosition { get; internal set; }

        public Vec3 Position { get; internal set; }

        /// <summary>The path end of a <see cref="InfoCueKind.Path"/> cue.</summary>
        public Vec3 To { get; internal set; }

        /// <summary>Unit horizontal direction of a <see cref="InfoCueKind.Direction"/> or path cue, or zero.</summary>
        public Vec3 Direction { get; internal set; }

        /// <summary>Boundary radius, or 0.</summary>
        public float Radius { get; internal set; }

        /// <summary>The object a value cue describes (solid, field or summon entity id), or -1.</summary>
        public int EntityId { get; internal set; } = -1;

        /// <summary>The value a value cue shows (health, remaining seconds), or 0.</summary>
        public float Amount { get; internal set; }

        /// <summary>A second value (a summon's remaining life), or 0.</summary>
        public float Amount2 { get; internal set; }

        /// <summary>Damage channels a value cue shows, most recent first (AUG-199), or empty.</summary>
        public DamageChannel[] Channels { get; internal set; } = new DamageChannel[0];

        public int DurationTicks { get; internal set; }

        /// <summary>The first tick at which the cue is gone.</summary>
        public int ExpireTick { get; internal set; }

        public bool VisibleAt(int tick) => tick >= Tick && tick < ExpireTick;

        public override DuelEventType Type => DuelEventType.Effect;

        public override string ToString() => base.ToString() + " info " + Kind + " " + Cue + " " + SourceId;
    }
}
